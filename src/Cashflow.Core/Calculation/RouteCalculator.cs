using System;
using System.Collections.Generic;
using System.Linq;
using Cashflow.Core.Models;

namespace Cashflow.Core.Calculation
{
    public sealed class RouteCalculator
    {
        private const int MaximumPaths = 10000;

        public IReadOnlyList<RouteResult> Calculate(
            CashflowScenario scenario,
            string sourceNodeId,
            string destinationNodeId,
            decimal initialAmount)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (initialAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(initialAmount), "El monto debe ser mayor que cero.");
            if (sourceNodeId == destinationNodeId) throw new ArgumentException("El origen y el destino deben ser distintos.");

            var nodes = scenario.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            if (!nodes.ContainsKey(sourceNodeId)) throw new ArgumentException("El nodo de origen no existe.", nameof(sourceNodeId));
            if (!nodes.ContainsKey(destinationNodeId)) throw new ArgumentException("El nodo de destino no existe.", nameof(destinationNodeId));

            var outgoing = scenario.Routes
                .Where(route => route.Enabled && nodes.ContainsKey(route.FromNodeId) && nodes.ContainsKey(route.ToNodeId))
                .GroupBy(route => route.FromNodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var results = new List<RouteResult>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { sourceNodeId };
            var currentSteps = new List<RouteStepResult>();

            Explore(sourceNodeId, initialAmount);

            return results
                .OrderByDescending(result => result.FinalAmount)
                .ThenBy(result => result.SourceDebitedAmount)
                .ThenBy(result => result.Steps.Count)
                .ToArray();

            void Explore(string currentNodeId, decimal amount)
            {
                if (results.Count >= MaximumPaths)
                {
                    return;
                }

                if (currentNodeId == destinationNodeId)
                {
                    results.Add(new RouteResult
                    {
                        Steps = currentSteps.ToArray(),
                        FinalAmount = amount,
                        DestinationCurrency = nodes[destinationNodeId].Currency,
                        SourceBudgetAmount = initialAmount,
                        SourceDebitedAmount = currentSteps.Count > 0 ? currentSteps[0].DebitedAmount : 0m
                    });
                    return;
                }

                if (!outgoing.TryGetValue(currentNodeId, out var availableRoutes))
                {
                    return;
                }

                foreach (var route in availableRoutes)
                {
                    if (visited.Contains(route.ToNodeId) ||
                        !TryApplyRoute(route, nodes[currentNodeId], nodes[route.ToNodeId], amount, out var step))
                    {
                        continue;
                    }

                    currentSteps.Add(step);
                    visited.Add(route.ToNodeId);
                    Explore(route.ToNodeId, step.OutputAmount);
                    visited.Remove(route.ToNodeId);
                    currentSteps.RemoveAt(currentSteps.Count - 1);
                }
            }
        }

        private static decimal ApplyPercentageFeeBounds(decimal fee, decimal? minimum, decimal? maximum)
        {
            if (minimum.HasValue && fee < minimum.Value)
            {
                fee = minimum.Value;
            }

            if (maximum.HasValue && fee > maximum.Value)
            {
                fee = maximum.Value;
            }

            return fee;
        }

        public static decimal RoundDownToStep(decimal amount, decimal? step)
        {
            if (!step.HasValue || step.Value <= 0m)
            {
                return amount;
            }

            return Math.Floor(amount / step.Value) * step.Value;
        }

        public static decimal CalculateTransferAmountWithinBudget(TransferRoute route, decimal budget)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (budget <= 0m || !IsConfigurationValid(route))
            {
                return 0m;
            }

            if (route.FeeApplication != FeeApplicationMode.ChargeSeparately)
            {
                return RoundDownToStep(budget, route.InputAmountStep);
            }

            var fullBudgetTransfer = RoundDownToStep(budget, route.InputAmountStep);
            if (fullBudgetTransfer + CalculateInputFee(route, fullBudgetTransfer) <= budget)
            {
                return fullBudgetTransfer;
            }

            var low = 0m;
            var high = budget;
            for (var iteration = 0; iteration < 96; iteration++)
            {
                var middle = low + (high - low) / 2m;
                if (middle == low || middle == high)
                {
                    break;
                }

                if (middle + CalculateInputFee(route, middle) <= budget)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return RoundDownToStep(low, route.InputAmountStep);
        }

        internal static bool TryApplyRoute(
            TransferRoute route,
            PlatformNode from,
            PlatformNode to,
            decimal amount,
            out RouteStepResult step)
        {
            step = null!;
            if (!IsConfigurationValid(route))
            {
                return false;
            }

            var tradeableInput = CalculateTransferAmountWithinBudget(route, amount);
            if (tradeableInput <= 0m || !IsAmountValid(route, tradeableInput))
            {
                return false;
            }

            var totalFee = CalculateInputFee(route, tradeableInput);
            var debitedAmount = route.FeeApplication == FeeApplicationMode.ChargeSeparately
                ? tradeableInput + totalFee
                : tradeableInput;
            if (debitedAmount > amount)
            {
                return false;
            }

            var amountAfterFee = route.FeeApplication == FeeApplicationMode.ChargeSeparately
                ? tradeableInput
                : tradeableInput - totalFee;
            if (amountAfterFee <= 0m)
            {
                return false;
            }

            var grossOutputAmount = amountAfterFee * route.ExchangeRate;
            if (grossOutputAmount <= 0m ||
                route.MinimumOutputAmount.HasValue && grossOutputAmount < route.MinimumOutputAmount.Value)
            {
                return false;
            }

            var tradingFeeAmount = grossOutputAmount * route.TradingFeePercentage / 100m;
            var afterTradingFee = grossOutputAmount - tradingFeeAmount;
            var outputFeeAmount = afterTradingFee * route.OutputPercentageFee / 100m;
            var outputAmount = afterTradingFee - outputFeeAmount;
            if (outputAmount <= 0m)
            {
                return false;
            }

            step = new RouteStepResult
            {
                From = from,
                To = to,
                Route = route,
                InputAmount = amount,
                TradeableInputAmount = tradeableInput,
                InputRemainder = amount - debitedAmount,
                FeeAmount = totalFee,
                DebitedAmount = debitedAmount,
                GrossOutputAmount = grossOutputAmount,
                TradingFeeAmount = tradingFeeAmount,
                OutputFeeAmount = outputFeeAmount,
                OutputAmount = outputAmount
            };
            return true;
        }

        public static decimal CalculateInputFee(TransferRoute route, decimal transferAmount) =>
            ApplyPercentageFeeBounds(
                transferAmount * route.PercentageFee / 100m,
                route.PercentageFeeMinimum,
                route.PercentageFeeMaximum) + route.FixedFee;

        private static bool IsConfigurationValid(TransferRoute route) =>
            route.PercentageFee >= 0m &&
            route.PercentageFee <= 100m &&
            route.TradingFeePercentage >= 0m &&
            route.TradingFeePercentage <= 100m &&
            route.OutputPercentageFee >= 0m &&
            route.OutputPercentageFee <= 100m &&
            (!route.PercentageFeeMinimum.HasValue || route.PercentageFeeMinimum.Value >= 0m) &&
            (!route.PercentageFeeMaximum.HasValue || route.PercentageFeeMaximum.Value >= 0m) &&
            (!route.PercentageFeeMinimum.HasValue || !route.PercentageFeeMaximum.HasValue ||
                route.PercentageFeeMinimum.Value <= route.PercentageFeeMaximum.Value) &&
            route.FixedFee >= 0m &&
            (!route.InputAmountStep.HasValue || route.InputAmountStep.Value > 0m) &&
            (!route.MinimumInputAmount.HasValue || route.MinimumInputAmount.Value >= 0m) &&
            (!route.MaximumInputAmount.HasValue || route.MaximumInputAmount.Value > 0m) &&
            (!route.MinimumOutputAmount.HasValue || route.MinimumOutputAmount.Value >= 0m) &&
            (!route.MinimumInputAmount.HasValue || !route.MaximumInputAmount.HasValue ||
                route.MinimumInputAmount.Value <= route.MaximumInputAmount.Value) &&
            route.ExchangeRateConfigured &&
            route.ExchangeRate > 0m;

        private static bool IsAmountValid(TransferRoute route, decimal amount) =>
            (!route.MinimumInputAmount.HasValue || amount >= route.MinimumInputAmount.Value) &&
            (!route.MaximumInputAmount.HasValue || amount <= route.MaximumInputAmount.Value);
    }
}
