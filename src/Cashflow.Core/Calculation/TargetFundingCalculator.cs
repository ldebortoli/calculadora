using System;
using System.Collections.Generic;
using System.Linq;
using Cashflow.Core.Models;

namespace Cashflow.Core.Calculation
{
    public sealed class TargetFundingCalculator
    {
        private const int MaximumPaths = 10000;
        private const decimal Precision = 0.00000001m;

        public IReadOnlyList<TargetFundingResult> Calculate(
            CashflowScenario scenario,
            string sourceNodeId,
            string destinationNodeId,
            decimal targetAmount)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (targetAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(targetAmount));
            if (sourceNodeId == destinationNodeId) throw new ArgumentException("El origen y el destino deben ser distintos.");

            var nodes = scenario.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            if (!nodes.ContainsKey(sourceNodeId)) throw new ArgumentException("El nodo de origen no existe.", nameof(sourceNodeId));
            if (!nodes.ContainsKey(destinationNodeId)) throw new ArgumentException("El nodo de destino no existe.", nameof(destinationNodeId));

            var paths = EnumeratePaths(scenario, sourceNodeId, destinationNodeId, nodes);
            var results = new List<TargetFundingResult>();
            foreach (var path in paths)
            {
                if (path.Any(route => !route.ExchangeRateConfigured || route.ExchangeRate <= 0m))
                {
                    continue;
                }

                var low = 0m;
                var high = Math.Max(1m, targetAmount);
                RouteResult? highResult = null;
                for (var attempt = 0; attempt < 64; attempt++)
                {
                    if (TryEvaluate(path, nodes, sourceNodeId, destinationNodeId, high, out highResult) &&
                        highResult.FinalAmount >= targetAmount)
                    {
                        break;
                    }

                    highResult = null;
                    if (high > decimal.MaxValue / 2m)
                    {
                        break;
                    }

                    high *= 2m;
                }

                if (highResult == null)
                {
                    continue;
                }

                for (var iteration = 0; iteration < 96 && high - low > Precision; iteration++)
                {
                    var middle = low + (high - low) / 2m;
                    if (TryEvaluate(path, nodes, sourceNodeId, destinationNodeId, middle, out var middleResult) &&
                        middleResult.FinalAmount >= targetAmount)
                    {
                        high = middle;
                        highResult = middleResult;
                    }
                    else
                    {
                        low = middle;
                    }
                }

                if (!TryEvaluate(path, nodes, sourceNodeId, destinationNodeId, high, out var finalResult) ||
                    finalResult.FinalAmount < targetAmount || finalResult.Steps.Count == 0)
                {
                    continue;
                }

                results.Add(new TargetFundingResult
                {
                    RequiredInputAmount = high,
                    SourceDebitAmount = high,
                    Route = finalResult
                });
            }

            return results
                .OrderBy(result => result.SourceDebitAmount)
                .ThenBy(result => result.Route.Steps.Count)
                .ToArray();
        }

        private static IReadOnlyList<IReadOnlyList<TransferRoute>> EnumeratePaths(
            CashflowScenario scenario,
            string sourceNodeId,
            string destinationNodeId,
            IReadOnlyDictionary<string, PlatformNode> nodes)
        {
            var outgoing = scenario.Routes
                .Where(route => route.Enabled && nodes.ContainsKey(route.FromNodeId) && nodes.ContainsKey(route.ToNodeId))
                .GroupBy(route => route.FromNodeId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            var paths = new List<IReadOnlyList<TransferRoute>>();
            var current = new List<TransferRoute>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { sourceNodeId };

            Explore(sourceNodeId);
            return paths;

            void Explore(string nodeId)
            {
                if (paths.Count >= MaximumPaths)
                {
                    return;
                }

                if (nodeId == destinationNodeId)
                {
                    paths.Add(current.ToArray());
                    return;
                }

                if (!outgoing.TryGetValue(nodeId, out var routes))
                {
                    return;
                }

                foreach (var route in routes)
                {
                    if (!visited.Add(route.ToNodeId))
                    {
                        continue;
                    }

                    current.Add(route);
                    Explore(route.ToNodeId);
                    current.RemoveAt(current.Count - 1);
                    visited.Remove(route.ToNodeId);
                }
            }
        }

        private static bool TryEvaluate(
            IReadOnlyList<TransferRoute> path,
            IReadOnlyDictionary<string, PlatformNode> nodes,
            string sourceNodeId,
            string destinationNodeId,
            decimal inputAmount,
            out RouteResult result)
        {
            var amount = inputAmount;
            var currentNodeId = sourceNodeId;
            var steps = new List<RouteStepResult>();
            foreach (var route in path)
            {
                if (route.FromNodeId != currentNodeId ||
                    !RouteCalculator.TryApplyRoute(route, nodes[route.FromNodeId], nodes[route.ToNodeId], amount, out var step))
                {
                    result = null!;
                    return false;
                }

                steps.Add(step);
                amount = step.OutputAmount;
                currentNodeId = route.ToNodeId;
            }

            if (currentNodeId != destinationNodeId)
            {
                result = null!;
                return false;
            }

            result = new RouteResult
            {
                Steps = steps,
                FinalAmount = amount,
                DestinationCurrency = nodes[destinationNodeId].Currency,
                SourceBudgetAmount = inputAmount,
                SourceDebitedAmount = steps.Count > 0 ? steps[0].DebitedAmount : 0m
            };
            return true;
        }
    }
}
