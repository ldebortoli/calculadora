using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                CalculatesAllThreeMethodsFromGrabrFiOnly();
                MissingManualBinanceFeesAreVisible();
                SeparatesBankedAndNonArsScenarios();
                OfficialPurchaseRequiresExplicitConfirmation();
                StarterManualRatesAreMarked();
                ManualRatesPropagateAcrossScenarios();
                ScenariosCanBeDeletedWithoutRemovingTheLastOne();
                StarterRoutesRespectTotalBudget();
                MusicGraphRendersGrabrFiOrigin();
                StarterUsesOnlyUsdcToUsdt();
                MigrationRemovesUsdtToUsdc();
                RouteDetailsModalBuildsEveryStep();
                OppositeRoutesUseSeparateLanes();
                ManualRatesWindowBuildsWithApplicationResources();
                RetirementSettingsMigrateAllocationToStockTarget();
                RetirementProratesAnnualVacationExpense();
                RetirementFundsStocksBeforeBonds();
                RetirementCalculatesSixtyYearSustainableExpense();
                RetirementInflationModeChangesRunway();
                Console.WriteLine("Resultado: 19 correctas, 0 fallidas.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.WriteLine("[ERROR] " + exception.Message);
                return 1;
            }
        }

        private static void CalculatesAllThreeMethodsFromGrabrFiOnly()
        {
            var document = CreateReadyDocument();
            document.MusicSession.BinanceUsdcTransferFee = 0m;
            document.MusicSession.BinanceUsdtTransferFee = 0m;

            var calculation = new MusicSessionCalculator().Calculate(document);
            Equal(3, calculation.Options.Count);
            Equal(1, calculation.Options.Count(option => option.Method == "Efectivo vía stablecoin"));
            Equal(1, calculation.Options.Count(option => option.Method == "Pago directo en ARS"));
            Equal(1, calculation.Options.Count(option => option.Method == "Recompra al oficial"));
            True(calculation.Options.All(option => option.Source == "GrabrFi"));
            True(calculation.Options.First().SourceDebitAmount < 400m);
        }

        private static void MissingManualBinanceFeesAreVisible()
        {
            var calculation = new MusicSessionCalculator().Calculate(CreateReadyDocument());
            True(calculation.Pending.Any(message => message.Contains("envío USDC desde Binance", StringComparison.Ordinal)));
            True(calculation.Pending.Any(message => message.Contains("envío USDT desde Binance", StringComparison.Ordinal)));
            Equal(3, calculation.Options.Count);
        }

        private static void SeparatesBankedAndNonArsScenarios()
        {
            var document = CreateReadyDocument();
            document.MusicSession.BinanceUsdcTransferFee = 0m;
            document.MusicSession.BinanceUsdtTransferFee = 0m;

            var calculation = new MusicSessionCalculator().Calculate(document);
            Equal(1, calculation.Options.Count(option => option.Category == MusicSessionCategory.WithoutArs));
            Equal(2, calculation.Options.Count(option => option.Category == MusicSessionCategory.BankedArs));
            True(calculation.Options.Where(option => option.Category == MusicSessionCategory.WithoutArs).All(option => !option.RequiredArs.HasValue));
            True(calculation.Options.Where(option => option.Category == MusicSessionCategory.BankedArs).All(option => option.RequiredArs.HasValue));
        }

        private static void OfficialPurchaseRequiresExplicitConfirmation()
        {
            var document = CreateReadyDocument();
            document.MusicSession.OfficialPurchaseAvailable = false;

            var calculation = new MusicSessionCalculator().Calculate(document);
            Equal(0, calculation.Options.Count(option => option.Method == "Recompra al oficial"));
            True(calculation.Pending.Any(message => message.Contains("90 días", StringComparison.Ordinal)));
        }

        private static void StarterManualRatesAreMarked()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            Equal(9, document.Version);
            var directRates = document.Scenarios
                .SelectMany(scenario => scenario.Routes)
                .Where(route => route.Label.Contains("cotización manual", StringComparison.Ordinal))
                .ToArray();
            Equal(4, directRates.Length);
            True(directRates.All(route => route.ExchangeRateIsManual));
            True(directRates.All(route => !string.IsNullOrWhiteSpace(route.ManualExchangeRateKey)));
            Equal(2, document.ManualExchangeRates.Count);
        }

        private static void ManualRatesPropagateAcrossScenarios()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            var key = ManualExchangeRateSynchronizer.CreateKey("GrabrFi", "USD", "ARS");
            var reviewedAt = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.FromHours(-3));
            ManualExchangeRateSynchronizer.Apply(document, key, "GrabrFi", "USD", "ARS", 1600.25m, reviewedAt);

            var grabrRates = document.Scenarios
                .SelectMany(scenario => scenario.Routes)
                .Where(route => route.ManualExchangeRateKey == key)
                .ToArray();
            Equal(2, grabrRates.Length);
            True(grabrRates.All(route => route.ExchangeRate == 1600.25m && route.ManualExchangeRateUpdatedAt == reviewedAt));

            document.Scenarios.Add(StarterScenarioFactory.CreateGrabrFiTemplate("Nuevo"));
            ManualExchangeRateSynchronizer.EnsureSynchronized(document);
            Equal(3, document.Scenarios.SelectMany(scenario => scenario.Routes).Count(route => route.ManualExchangeRateKey == key && route.ExchangeRate == 1600.25m));

            var empty = StarterScenarioFactory.CreateEmpty("Pendiente");
            empty.Routes.Add(new TransferRoute
            {
                FromNodeId = empty.Nodes[0].Id,
                ToNodeId = empty.Nodes[1].Id,
                ExchangeRate = 1m,
                ExchangeRateConfigured = false,
                ExchangeRateIsManual = true
            });
            var pendingDocument = new ScenarioDocument { Scenarios = { empty } };
            ManualExchangeRateSynchronizer.EnsureSynchronized(pendingDocument);
            Equal(0, pendingDocument.ManualExchangeRates.Count);
            True(!empty.Routes[0].ExchangeRateConfigured);
        }

        private static void ScenariosCanBeDeletedWithoutRemovingTheLastOne()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            var removed = document.Scenarios[0];
            True(ScenarioDocumentEditor.TryDeleteScenario(document, removed.Id, out var next));
            Equal(1, document.Scenarios.Count);
            True(next != null && document.ActiveScenarioId == next.Id);
            True(!ScenarioDocumentEditor.TryDeleteScenario(document, next!.Id, out _));
            Equal(1, document.Scenarios.Count);
        }

        private static void StarterRoutesRespectTotalBudget()
        {
            var document = CreateReadyDocument();
            foreach (var scenario in document.Scenarios)
            {
                var source = scenario.Nodes.Single(node => node.Kind == NodeKind.Source);
                var destination = scenario.Nodes.Single(node => node.Kind == NodeKind.Destination);
                var results = new RouteCalculator().Calculate(scenario, source.Id, destination.Id, 2500m);
                True(results.Count > 0);
                True(results.All(result => result.SourceDebitedAmount <= 2500m));
                True(results.SelectMany(result => result.Steps).All(step => step.DebitedAmount <= step.InputAmount));
            }
        }

        private static void MusicGraphRendersGrabrFiOrigin()
        {
            Exception? renderError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var document = CreateReadyDocument();
                    document.MusicSession.BinanceUsdcTransferFee = 0m;
                    document.MusicSession.BinanceUsdtTransferFee = 0m;
                    var calculation = new MusicSessionCalculator().Calculate(document);
                    var graph = new MusicSessionGraphCanvas();
                    graph.ShowCalculation(calculation, document.MusicSession.TargetUsd);
                    graph.Measure(new Size(900, 350));
                    graph.Arrange(new Rect(0, 0, 900, 350));
                    var bitmap = new RenderTargetBitmap(900, 350, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(graph);
                    True(bitmap.PixelWidth == 900 && bitmap.PixelHeight == 350);
                }
                catch (Exception exception)
                {
                    renderError = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (renderError != null)
            {
                throw new InvalidOperationException("El grafo musical no pudo renderizarse.", renderError);
            }
        }

        private static void StarterUsesOnlyUsdcToUsdt()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            foreach (var scenario in document.Scenarios)
            {
                Equal(9, scenario.Routes.Count);
                Equal(1, scenario.Routes.Count(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt));
                Equal(0, scenario.Routes.Count(route => route.Label == "Binance Spot · USDT → USDC"));
            }
        }

        private static void MigrationRemovesUsdtToUsdc()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.Version = 7;
            foreach (var scenario in document.Scenarios)
            {
                var usdc = scenario.Nodes.Single(node => node.Name == "Binance · USDC");
                var usdt = scenario.Nodes.Single(node => node.Name == "Binance · USDT");
                scenario.Routes.Add(new TransferRoute
                {
                    FromNodeId = usdt.Id,
                    ToNodeId = usdc.Id,
                    Label = "Binance Spot · USDT → USDC",
                    LiveQuoteKey = "binance-spot-sell-usdt-usdc"
                });
            }

            True(StarterScenarioFactory.UpgradeStarterTemplates(document));
            Equal(9, document.Version);
            True(document.Scenarios.All(scenario => scenario.Routes.Count == 9));
            True(document.Scenarios.SelectMany(scenario => scenario.Routes).All(route => route.Label != "Binance Spot · USDT → USDC"));
        }

        private static void RouteDetailsModalBuildsEveryStep()
        {
            Exception? renderError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var document = CreateReadyDocument();
                    var scenario = document.Scenarios.First();
                    var source = scenario.Nodes.Single(node => node.Kind == NodeKind.Source);
                    var destination = scenario.Nodes.Single(node => node.Kind == NodeKind.Destination);
                    var result = new RouteCalculator().Calculate(scenario, source.Id, destination.Id, 2500m).First(route => route.Steps.Count > 1);
                    var window = new RouteDetailsWindow(result);
                    var stepsPanel = window.FindName("StepsPanel") as StackPanel;
                    True(stepsPanel != null);
                    Equal(result.Steps.Count, stepsPanel!.Children.Count);
                    True(window.FindName("PathText") is TextBlock path && path.Text == result.PathLabel);
                }
                catch (Exception exception)
                {
                    renderError = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (renderError != null)
            {
                throw new InvalidOperationException("El modal de detalle no pudo construirse.", renderError);
            }
        }

        private static void OppositeRoutesUseSeparateLanes()
        {
            Exception? laneError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var from = new PlatformNode { Id = "from", Name = "A", X = 20, Y = 100 };
                    var to = new PlatformNode { Id = "to", Name = "B", X = 500, Y = 100 };
                    var forward = new TransferRoute { Id = "forward", FromNodeId = from.Id, ToNodeId = to.Id };
                    var reverse = new TransferRoute { Id = "reverse", FromNodeId = to.Id, ToNodeId = from.Id };
                    var scenario = new CashflowScenario();
                    scenario.Nodes.Add(from);
                    scenario.Nodes.Add(to);
                    scenario.Routes.Add(forward);
                    scenario.Routes.Add(reverse);
                    var graph = new GraphCanvas { Scenario = scenario };
                    var method = typeof(GraphCanvas).GetMethod("TryGetRouteSegment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    True(method != null);

                    var forwardArguments = new object[] { forward, from, to, default(Point), default(Point), default(Vector) };
                    var reverseArguments = new object[] { reverse, to, from, default(Point), default(Point), default(Vector) };
                    True((bool)method!.Invoke(graph, forwardArguments)!);
                    True((bool)method.Invoke(graph, reverseArguments)!);

                    var forwardStart = (Point)forwardArguments[3];
                    var forwardEnd = (Point)forwardArguments[4];
                    var reverseStart = (Point)reverseArguments[3];
                    var reverseEnd = (Point)reverseArguments[4];
                    True(Math.Abs(forwardStart.Y - reverseEnd.Y) >= 20d);
                    True(Math.Abs(forwardEnd.Y - reverseStart.Y) >= 20d);
                }
                catch (Exception exception)
                {
                    laneError = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (laneError != null)
            {
                throw new InvalidOperationException("Las aristas opuestas no pudieron separarse.", laneError);
            }
        }

        private static void ManualRatesWindowBuildsWithApplicationResources()
        {
            Exception? windowError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = new App();
                    app.InitializeComponent();
                    var window = new ManualExchangeRatesWindow(StarterScenarioFactory.CreateStarterDocument(), new ScenarioStore());
                    True(window.FindName("RatesPanel") is StackPanel panel && panel.Children.Count == 2);
                    True(window.FindName("StatusText") is TextBlock);
                    window.Close();
                }
                catch (Exception exception)
                {
                    windowError = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (windowError != null)
            {
                throw new InvalidOperationException("El editor global de cotizaciones no pudo construirse: " + windowError);
            }
        }

        private static void RetirementSettingsMigrateAllocationToStockTarget()
        {
            var settings = new RetirementSettings
            {
                TargetInvestedCents = 50000000,
                StockAllocationPercentage = 80m,
                MonthlyIncomeCents = 250000
            };

            True(settings.EnsurePlanningCollections());
            Equal(40000000L, settings.TargetStocksCents!.Value);
            Equal(250000L, settings.MonthlyIncomes.Single().MonthlyAmountCents);
            Equal(3, settings.Reserves.Count);
            True(settings.Reserves.Any(reserve => reserve.Kind == "emergency"));
        }

        private static void RetirementProratesAnnualVacationExpense()
        {
            var settings = CreateRetirementSettings();
            settings.MonthlyIncomes[0].MonthlyAmountCents = 20000;
            settings.OrdinaryMonthlyExpensesCents = 5000;
            settings.AnnualVacationExpensesCents = 120000;

            var projection = new RetirementCalculator().Calculate(settings);
            Near(100d, projection.MonthlyVacationProrationUsd);
            Near(50d, projection.MonthlySurplusAfterExtraExpenses);
            Near(150d, projection.Runway.InitialMonthlyExpenseUsd);
        }

        private static void RetirementFundsStocksBeforeBonds()
        {
            var settings = CreateRetirementSettings();
            settings.MonthlyIncomes[0].MonthlyAmountCents = 20000;
            settings.AnnualVacationExpensesCents = 120000;
            settings.TargetInvestedCents = 1000000;
            settings.TargetStocksCents = 100000;

            var projection = new RetirementCalculator().Calculate(settings);
            Equal(100, projection.MonthsToTarget!.Value);
            Near(1000d, projection.FinalStocksRealUsd);
            Near(9000d, projection.FinalBondsRealUsd);
        }

        private static void RetirementCalculatesSixtyYearSustainableExpense()
        {
            var settings = CreateRetirementSettings();
            settings.InitialBondsCents = 7200000;
            settings.OrdinaryMonthlyExpensesCents = 20000;
            settings.TargetInvestedCents = 100000000;
            settings.TargetStocksCents = 0;
            settings.EmergencyRunwayTargetYears = 60;

            var runway = new RetirementCalculator().Calculate(settings).Runway;
            Equal(360, runway.MonthsCovered);
            Equal(60, runway.TargetYears);
            Near(100d, runway.SustainableMonthlyExpenseUsd, 0.02d);
            Near(100d, runway.RequiredMonthlyReductionUsd, 0.02d);
        }

        private static void RetirementInflationModeChangesRunway()
        {
            var settings = CreateRetirementSettings();
            settings.InitialBondsCents = 120000;
            settings.OrdinaryMonthlyExpensesCents = 10000;
            settings.TargetInvestedCents = 100000000;
            settings.BondAnnualReturnPercentage = 0m;
            settings.UsInflationPercentage = 12m;
            settings.UseInflationAdjustment = false;

            var nominal = new RetirementCalculator().Calculate(settings);
            settings.UseInflationAdjustment = true;
            var adjusted = new RetirementCalculator().Calculate(settings);

            True(!nominal.UsesInflationAdjustment);
            True(adjusted.UsesInflationAdjustment);
            True(!nominal.Runway.UsesInflationAdjustment);
            True(adjusted.Runway.UsesInflationAdjustment);
            Equal(12, nominal.Runway.MonthsCovered);
            True(adjusted.Runway.MonthsCovered < nominal.Runway.MonthsCovered);
        }

        private static RetirementSettings CreateRetirementSettings()
        {
            var settings = new RetirementSettings
            {
                TargetInvestedCents = 10000000,
                TargetStocksCents = 0,
                StockAnnualReturnPercentage = 0m,
                BondAnnualReturnPercentage = 0m,
                UsInflationPercentage = 0m,
                EmergencyRunwayTargetYears = 60
            };
            settings.EnsurePlanningCollections();
            foreach (var reserve in settings.Reserves)
            {
                reserve.CurrentCents = 0;
                reserve.TargetCents = 0;
                reserve.MonthlyCapCents = 0;
                reserve.StartAfterMonths = 0;
            }
            settings.MonthlyIncomes[0].MonthlyAmountCents = 0;
            return settings;
        }

        private static ScenarioDocument CreateReadyDocument()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.TargetUsd = 400m;
            document.MusicSession.BlueBuy = 1525m;
            document.MusicSession.BlueSell = 1545m;
            document.MusicSession.OfficialBuy = 1460m;
            document.MusicSession.OfficialSell = 1510m;
            document.MusicSession.OfficialPurchaseAvailable = true;
            foreach (var route in document.Scenarios.SelectMany(scenario => scenario.Routes))
            {
                if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt)
                {
                    route.ExchangeRate = 1m;
                    route.ExchangeRateConfigured = true;
                }
                else if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs)
                {
                    route.ExchangeRate = 1500m;
                    route.ExchangeRateConfigured = true;
                }
            }

            return document;
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
            if (!condition)
            {
                throw new InvalidOperationException("La condición esperada no se cumplió.");
            }
        }

        private static void Near(double expected, double actual, double tolerance = 0.001d)
        {
            if (Math.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException($"Esperado: {expected}. Obtenido: {actual}.");
            }
        }
    }
}
