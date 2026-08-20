using System;
using System.Linq;
using Cashflow.Core.Calculation;
using Cashflow.Core.Input;
using Cashflow.Core.Models;

namespace Cashflow.Core.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("La mejor ruta cambia segun el monto", BestRouteChangesWithAmount);
            Run("Las rutas deshabilitadas se ignoran", DisabledRoutesAreIgnored);
            Run("Los ciclos no se recorren", CyclesAreNotTraversed);
            Run("Una comision que consume el monto descarta la ruta", ConsumedAmountIsDiscarded);
            Run("Los datos de entrada invalidos se rechazan", InvalidInputIsRejected);
            Run("Acepta decimales con coma o punto", DecimalSeparatorsAreAccepted);
            Run("ACH respeta minimo y maximo de comision", AchFeeBoundsAreApplied);
            Run("USDC suma porcentaje y costo de blockchain", UsdcFeeIsApplied);
            Run("USDT suma porcentaje y costo de blockchain", UsdtFeeIsApplied);
            Run("La cotizacion manual se aplica despues del fijo", ManualExchangeRateIsApplied);
            Run("Una cotizacion pendiente no participa", PendingExchangeRateIsIgnored);
            Run("Topes de comision inconsistentes se rechazan", InvalidFeeBoundsAreIgnored);
            Run("Las comisiones aparte quedan dentro del presupuesto total", SeparateFeesStayWithinBudget);
            Run("El ranking no premia rutas que exceden el presupuesto", OverBudgetRouteCannotWinRanking);
            Run("Binance descuenta su comision del monto vendido", BinanceFeeIsDeducted);
            Run("El fee de trade y el cargo de salida se aplican en secuencia", TradingAndOutputFeesAreSequential);
            Run("El paso de orden deja visible el remanente", OrderStepLeavesRemainder);
            Run("El minimo recibido filtra operaciones pequenas", MinimumOutputFiltersSmallTrades);
            Run("Los limites de monto filtran ofertas", AmountLimitsFilterRoutes);
            Run("El libro calcula precio promedio segun profundidad", OrderBookDepthAffectsRate);
            Run("El calculo inverso incluye el cargo aparte del origen", TargetFundingIncludesSeparateFee);
            Run("El calculo inverso suma cargos aparte intermedios", TargetFundingIncludesIntermediateSeparateFee);
            Run("El calculo inverso ordena la ruta de menor debito", TargetFundingSortsBySourceDebit);

            Console.WriteLine();
            Console.WriteLine($"Resultado: {_passed} correctas, {_failed} fallidas.");
            return _failed == 0 ? 0 : 1;
        }

        private static void BestRouteChangesWithAmount()
        {
            var scenario = CreateScenario();
            var calculator = new RouteCalculator();

            var small = calculator.Calculate(scenario, "source", "destination", 100m);
            Equal("Directo", small.First().Steps.First().Route.Label);
            Equal(97000m, small.First().FinalAmount);

            var large = calculator.Calculate(scenario, "source", "destination", 1000m);
            Equal("Entrada al proveedor", large.First().Steps.First().Route.Label);
            Equal(985050m, large.First().FinalAmount);
        }

        private static void DisabledRoutesAreIgnored()
        {
            var scenario = CreateScenario();
            scenario.Routes.Add(new TransferRoute
            {
                Id = "disabled",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Imposible",
                ExchangeRate = 9999m,
                Enabled = false
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 100m);
            True(result.All(route => route.Steps.All(step => step.Route.Id != "disabled")));
        }

        private static void CyclesAreNotTraversed()
        {
            var scenario = CreateScenario();
            scenario.Routes.Add(new TransferRoute
            {
                Id = "cycle",
                FromNodeId = "provider",
                ToNodeId = "source",
                Label = "Vuelta",
                ExchangeRate = 2m
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 100m);
            Equal(2, result.Count);
            True(result.All(route => route.Steps.Select(step => step.From.Id).Distinct().Count() == route.Steps.Count));
        }

        private static void ConsumedAmountIsDiscarded()
        {
            var scenario = CreateScenario();
            scenario.Routes.Clear();
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                FixedFee = 100m,
                ExchangeRate = 1000m
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 100m);
            Equal(0, result.Count);
        }

        private static void InvalidInputIsRejected()
        {
            var scenario = CreateScenario();
            Throws<ArgumentOutOfRangeException>(() => new RouteCalculator().Calculate(scenario, "source", "destination", 0m));
            Throws<ArgumentException>(() => new RouteCalculator().Calculate(scenario, "source", "source", 100m));
        }

        private static void DecimalSeparatorsAreAccepted()
        {
            True(DecimalInputParser.TryParse("1,5", out var comma));
            Equal(1.5m, comma);
            True(DecimalInputParser.TryParse("1.5", out var dot));
            Equal(1.5m, dot);
            True(DecimalInputParser.TryParse("1.234,56", out var spanish));
            Equal(1234.56m, spanish);
            True(DecimalInputParser.TryParse("1,234.56", out var english));
            Equal(1234.56m, english);
        }

        private static void AchFeeBoundsAreApplied()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "ach",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "ACH",
                PercentageFee = 0.3m,
                PercentageFeeMinimum = 1m,
                PercentageFeeMaximum = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1m
            });
            var calculator = new RouteCalculator();

            var minimum = calculator.Calculate(scenario, "source", "destination", 100m).Single();
            Equal(99m, minimum.FinalAmount);
            Equal(1m, minimum.Steps.Single().FeeAmount);
            Equal(100m, minimum.Steps.Single().DebitedAmount);

            var normal = calculator.Calculate(scenario, "source", "destination", 1000m).Single();
            Near(997.008973m, normal.FinalAmount, 0.000001m);
            Near(2.991027m, normal.Steps.Single().FeeAmount, 0.000001m);
            Near(1000m, normal.Steps.Single().DebitedAmount, 0.000001m);

            var maximum = calculator.Calculate(scenario, "source", "destination", 10000m).Single();
            Equal(9995m, maximum.FinalAmount);
            Equal(5m, maximum.Steps.Single().FeeAmount);
        }

        private static void UsdcFeeIsApplied()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "usdc",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "USDC",
                PercentageFee = 0.5m,
                FixedFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1m
            });

            var step = new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Single().Steps.Single();
            Near(5.970149m, step.FeeAmount, 0.000001m);
            Near(1000m, step.DebitedAmount, 0.000001m);
            Near(994.029851m, step.OutputAmount, 0.000001m);
        }

        private static void UsdtFeeIsApplied()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "usdt",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "USDT",
                PercentageFee = 0.6m,
                FixedFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1m
            });

            var step = new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Single().Steps.Single();
            Near(6.958251m, step.FeeAmount, 0.000001m);
            Near(1000m, step.DebitedAmount, 0.000001m);
            Near(993.041749m, step.OutputAmount, 0.000001m);
        }

        private static void ManualExchangeRateIsApplied()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "ars",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Retiro directo",
                FixedFee = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1200m,
                ExchangeRateConfigured = true
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Single();
            Equal(1194000m, result.FinalAmount);
            Equal(1000m, result.Steps.Single().DebitedAmount);
        }

        private static void PendingExchangeRateIsIgnored()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "pending",
                FromNodeId = "source",
                ToNodeId = "destination",
                ExchangeRate = 1m,
                ExchangeRateConfigured = false
            });

            Equal(0, new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Count);
        }

        private static void InvalidFeeBoundsAreIgnored()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "invalid-bounds",
                FromNodeId = "source",
                ToNodeId = "destination",
                PercentageFee = 0.3m,
                PercentageFeeMinimum = 5m,
                PercentageFeeMaximum = 1m,
                ExchangeRate = 1m
            });

            Equal(0, new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Count);
        }

        private static void SeparateFeesStayWithinBudget()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                PercentageFee = 0.5m,
                PercentageFeeMinimum = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Single();
            Equal(995m, result.FinalAmount);
            Equal(5m, result.Steps.Single().FeeAmount);
            Equal(1000m, result.Steps.Single().DebitedAmount);
            Equal(0m, result.SourceRemainder);
        }

        private static void OverBudgetRouteCannotWinRanking()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "charged",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Con cargo aparte",
                FixedFee = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1000m
            });
            scenario.Routes.Add(new TransferRoute
            {
                Id = "free",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Sin cargo",
                ExchangeRate = 1000m
            });

            var results = new RouteCalculator().Calculate(scenario, "source", "destination", 2500m);
            Equal("Sin cargo", results.First().Steps.Single().Route.Label);
            True(results.All(result => result.SourceDebitedAmount <= 2500m));
            Equal(2495000m, results.Single(result => result.Steps.Single().Route.Id == "charged").FinalAmount);
        }

        private static void BinanceFeeIsDeducted()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                PercentageFee = 1m,
                FeeApplication = FeeApplicationMode.DeductFromAmount
            });

            var result = new RouteCalculator().Calculate(scenario, "source", "destination", 1000m).Single();
            Equal(990m, result.FinalAmount);
            Equal(1000m, result.Steps.Single().DebitedAmount);
        }

        private static void TradingAndOutputFeesAreSequential()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                ExchangeRate = 1000m,
                TradingFeePercentage = 0.1m,
                OutputPercentageFee = 1m
            });

            var step = new RouteCalculator().Calculate(scenario, "source", "destination", 100m).Single().Steps.Single();
            Equal(100000m, step.GrossOutputAmount);
            Equal(100m, step.TradingFeeAmount);
            Equal(999m, step.OutputFeeAmount);
            Equal(98901m, step.OutputAmount);
        }

        private static void OrderStepLeavesRemainder()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                InputAmountStep = 1m
            });

            var step = new RouteCalculator().Calculate(scenario, "source", "destination", 1553.25m).Single().Steps.Single();
            Equal(1553m, step.TradeableInputAmount);
            Equal(0.25m, step.InputRemainder);
            Equal(1553m, step.DebitedAmount);
            Equal(1553m, step.OutputAmount);
        }

        private static void MinimumOutputFiltersSmallTrades()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                ExchangeRate = 1000m,
                MinimumOutputAmount = 2000m
            });
            var calculator = new RouteCalculator();

            Equal(0, calculator.Calculate(scenario, "source", "destination", 1m).Count);
            Equal(1, calculator.Calculate(scenario, "source", "destination", 2m).Count);
        }

        private static void AmountLimitsFilterRoutes()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                MinimumInputAmount = 100m,
                MaximumInputAmount = 200m
            });
            var calculator = new RouteCalculator();

            Equal(0, calculator.Calculate(scenario, "source", "destination", 99m).Count);
            Equal(1, calculator.Calculate(scenario, "source", "destination", 150m).Count);
            Equal(0, calculator.Calculate(scenario, "source", "destination", 201m).Count);
        }

        private static void OrderBookDepthAffectsRate()
        {
            var bids = new[] { (1m, 100m), (0.99m, 100m) };
            var asks = new[] { (1m, 100m), (1.01m, 100m) };

            Equal(149.5m / 150m, OrderBookQuoteCalculator.RateForSellingBase(bids, 150m));
            Equal((100m + 50m / 1.01m) / 150m, OrderBookQuoteCalculator.RateForBuyingBase(asks, 150m));
            Throws<InvalidOperationException>(() => OrderBookQuoteCalculator.RateForSellingBase(bids, 201m));
        }

        private static void TargetFundingIncludesSeparateFee()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "destination",
                FixedFee = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1000m
            });

            var result = new TargetFundingCalculator().Calculate(scenario, "source", "destination", 400000m).Single();
            Near(405m, result.RequiredInputAmount, 0.000001m);
            Near(405m, result.SourceDebitAmount, 0.000001m);
        }

        private static void TargetFundingSortsBySourceDebit()
        {
            var scenario = CreateSingleRouteScenario(new TransferRoute
            {
                Id = "expensive",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Cara",
                ExchangeRate = 900m
            });
            scenario.Routes.Add(new TransferRoute
            {
                Id = "efficient",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Eficiente",
                ExchangeRate = 1000m
            });

            var results = new TargetFundingCalculator().Calculate(scenario, "source", "destination", 90000m);
            Equal("Eficiente", results.First().Route.Steps.Single().Route.Label);
            Near(90m, results.First().SourceDebitAmount, 0.000001m);
        }

        private static void TargetFundingIncludesIntermediateSeparateFee()
        {
            var scenario = new CashflowScenario();
            scenario.Nodes.Add(new PlatformNode { Id = "source", Name = "Origen", Currency = "USD" });
            scenario.Nodes.Add(new PlatformNode { Id = "middle", Name = "Intermedio", Currency = "USDT" });
            scenario.Nodes.Add(new PlatformNode { Id = "destination", Name = "Destino", Currency = "USD" });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = "source",
                ToNodeId = "middle",
                FixedFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = "middle",
                ToNodeId = "destination",
                PercentageFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });

            var result = new TargetFundingCalculator().Calculate(scenario, "source", "destination", 400m).Single();
            Near(405m, result.RequiredInputAmount, 0.000001m);
            Near(405m, result.SourceDebitAmount, 0.000001m);
        }

        private static CashflowScenario CreateSingleRouteScenario(TransferRoute route)
        {
            var scenario = new CashflowScenario();
            scenario.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
            scenario.Nodes.Add(new PlatformNode { Id = "destination", Name = "Destino", Currency = "USD", Kind = NodeKind.Destination });
            scenario.Routes.Add(route);
            return scenario;
        }

        private static CashflowScenario CreateScenario()
        {
            var scenario = new CashflowScenario();
            scenario.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
            scenario.Nodes.Add(new PlatformNode { Id = "provider", Name = "Proveedor", Currency = "USD", Kind = NodeKind.Intermediate });
            scenario.Nodes.Add(new PlatformNode { Id = "destination", Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
            scenario.Routes.Add(new TransferRoute
            {
                Id = "direct",
                FromNodeId = "source",
                ToNodeId = "destination",
                Label = "Directo",
                PercentageFee = 3m,
                ExchangeRate = 1000m
            });
            scenario.Routes.Add(new TransferRoute
            {
                Id = "provider-in",
                FromNodeId = "source",
                ToNodeId = "provider",
                Label = "Entrada al proveedor",
                FixedFee = 5m,
                ExchangeRate = 1m
            });
            scenario.Routes.Add(new TransferRoute
            {
                Id = "provider-out",
                FromNodeId = "provider",
                ToNodeId = "destination",
                Label = "Salida del proveedor",
                PercentageFee = 1m,
                ExchangeRate = 1000m
            });
            return scenario;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine($"[OK] {name}");
            }
            catch (Exception exception)
            {
                _failed++;
                Console.WriteLine($"[ERROR] {name}: {exception.Message}");
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException($"Esperado: {expected}. Obtenido: {actual}.");
            }
        }

        private static void True(bool condition)
        {
            if (!condition) throw new InvalidOperationException("La condicion esperada no se cumplio.");
        }

        private static void Near(decimal expected, decimal actual, decimal tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException($"Esperado cerca de: {expected}. Obtenido: {actual}.");
            }
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException($"Se esperaba una excepcion {typeof(T).Name}.");
        }
    }
}
