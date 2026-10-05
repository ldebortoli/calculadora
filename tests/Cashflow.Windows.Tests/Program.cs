using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Contains("--presentation")) return RunPresentationOnly();
            try
            {
                CalculatesAllThreeMethodsFromGrabrFiOnly();
                MissingManualBinanceFeesAreVisible();
                SeparatesBankedAndNonArsScenarios();
                OfficialPurchaseRequiresExplicitConfirmation();
                MissingMusicMarketDataAndUnreachablePathsAreExplained();
                StarterManualRatesAreMarked();
                ManualRatesPropagateAcrossScenarios();
                ScenariosCanBeDeletedWithoutRemovingTheLastOne();
                ScenarioEditorRejectsUnknownIdsAndNodeLabelsAreReadable();
                StarterRoutesRespectTotalBudget();
                MusicGraphRendersGrabrFiOrigin();
                StarterUsesOnlyUsdcToUsdt();
                MigrationRemovesUsdtToUsdc();
                LegacyTemplatesUpgradeWithoutDuplicatingScenarios();
                HistoricalStarterShapesUpgradeWithoutLosingTheirIds();
                MigrationBoundaryTests.Run();
                ManualRateEdgeTests.Run();
                BinanceArsMigrationHandlesMissingNodesAndRoutes();
                GlobalFeesUpgradeOnlyUntouchedDefaults();
                ScenarioStoreRecoversInvalidAndMigratesLegacyDocuments();
                RouteDetailsModalBuildsEveryStep();
                OppositeRoutesUseSeparateLanes();
                ManualRatesWindowBuildsWithApplicationResources();
                RetirementSettingsMigrateAllocationToStockTarget();
                RetirementSettingsNormalizeLegacyAndPartialCollections();
                RetirementProratesAnnualVacationExpense();
                RetirementFundsStocksBeforeBonds();
                RetirementCalculatesSixtyYearSustainableExpense();
                RetirementInflationModeChangesRunway();
                RetirementCompletesDeferredReservesAfterGoal();
                RetirementHiddenReservesDoNotAffectCalculations();
                RetirementReserveVisibilityPersistsAndLegacyDefaultsToShown();
                RetirementRejectsInvalidAssumptionsAndSpendsLowerReturnFirst();
                RetirementChartsCaptureWheelAtMinimumZoom();
                MarketServiceTests.Run();
                Console.WriteLine("Resultado: suites Windows, WPF y mercados correctas; 0 fallidas.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.WriteLine("[ERROR] " + exception);
                return 1;
            }
        }

        private static int RunPresentationOnly()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var app = new App();
                    app.InitializeComponent();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    WindowPresentationTests.Run();
                }
                catch (Exception exception) { error = exception; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Console.WriteLine(error == null ? "Presentación: correcta." : "[ERROR] " + error);
            return error == null ? 0 : 1;
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

        private static void MissingMusicMarketDataAndUnreachablePathsAreExplained()
        {
            var document = CreateReadyDocument();
            document.MusicSession.BlueBuy = null;
            document.MusicSession.OfficialSell = null;
            var missingMarket = new MusicSessionCalculator().Calculate(document);
            True(missingMarket.Pending.Any(message => message.Contains("cotización blue", StringComparison.Ordinal)));
            True(missingMarket.Pending.Any(message => message.Contains("cotización oficial", StringComparison.Ordinal)));
            Equal(0, missingMarket.Options.Count(option => option.Category == MusicSessionCategory.BankedArs));

            document = CreateReadyDocument();
            var grabr = document.Scenarios.First(scenario => scenario.Nodes.Any(node => node.Name == "GrabrFi"));
            document.Scenarios.Clear();
            document.Scenarios.Add(grabr);
            document.MusicSession.PersonFeePercentage = 100m;
            var noCash = new MusicSessionCalculator().Calculate(document);
            True(noCash.Pending.Any(message => message.Contains("no hay un camino completo a efectivo", StringComparison.Ordinal)));
            Equal(0, noCash.Options.Count(option => option.Category == MusicSessionCategory.WithoutArs));

            document.MusicSession.PersonFeePercentage = 0m;
            foreach (var route in grabr.Routes)
            {
                if (grabr.Nodes.Any(node => node.Id == route.ToNodeId && node.Currency == "ARS"))
                {
                    route.ExchangeRateConfigured = false;
                }
            }
            var noArs = new MusicSessionCalculator().Calculate(document);
            True(noArs.Pending.Any(message => message.Contains("faltan cotizaciones de una ruta a ARS", StringComparison.Ordinal)));
            Equal(0, noArs.Options.Count(option => option.Category == MusicSessionCategory.BankedArs));

            document = CreateReadyDocument();
            foreach (var scenario in document.Scenarios)
            {
                scenario.Nodes.RemoveAll(node => node.Kind == NodeKind.Destination && node.Currency == "ARS");
            }
            var withoutArsDestinations = new MusicSessionCalculator().Calculate(document);
            Equal(0, withoutArsDestinations.Options.Count(option => option.Category == MusicSessionCategory.BankedArs));

            document = CreateReadyDocument();
            document.MusicSession.CashUsdPerUsdc = 0m;
            document.MusicSession.CashUsdPerUsdt = 0m;
            var withoutCashRates = new MusicSessionCalculator().Calculate(document);
            True(withoutCashRates.Pending.Any(message => message.Contains("no hay un camino completo a efectivo", StringComparison.Ordinal)));
            try
            {
                new MusicSessionCalculator().Calculate(null!);
                throw new InvalidOperationException("Se esperaba rechazar un documento nulo.");
            }
            catch (ArgumentNullException)
            {
            }
        }

        private static void ScenarioEditorRejectsUnknownIdsAndNodeLabelsAreReadable()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            var originalCount = document.Scenarios.Count;
            True(!ScenarioDocumentEditor.TryDeleteScenario(document, "no-existe", out var nextScenario));
            True(nextScenario == null);
            Equal(originalCount, document.Scenarios.Count);
            var node = new PlatformNode { Name = "Cuenta", Currency = "USD" };
            Equal("Cuenta · USD", node.ToString());
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

        private static void LegacyTemplatesUpgradeWithoutDuplicatingScenarios()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.Version = 1;
            var originalIds = document.Scenarios.Select(scenario => scenario.Id).ToArray();
            document.Scenarios[0].Routes.Add(new TransferRoute
            {
                Label = "Binance P2P · oferta anterior",
                FromNodeId = document.Scenarios[0].Nodes[0].Id,
                ToNodeId = document.Scenarios[0].Nodes[1].Id
            });

            True(StarterScenarioFactory.UpgradeStarterTemplates(document));
            Equal(StarterScenarioFactory.CurrentDocumentVersion, document.Version);
            Equal(2, document.Scenarios.Count);
            True(originalIds.SequenceEqual(document.Scenarios.Select(scenario => scenario.Id)));
            True(document.Scenarios.SelectMany(scenario => scenario.Routes)
                .All(route => !route.Label.StartsWith("Binance P2P ·", StringComparison.Ordinal)));
            var spot = document.Scenarios[0].Routes.Single(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs);
            Equal(0.1m, spot.TradingFeePercentage);
            Equal(1m, spot.OutputPercentageFee);
            True(!StarterScenarioFactory.UpgradeStarterTemplates(document));

            var empty = new ScenarioDocument { Version = 1 };
            True(StarterScenarioFactory.UpgradeStarterTemplates(empty));
            Equal(2, empty.Scenarios.Count);
            True(empty.Scenarios.Any(scenario => scenario.Nodes.Any(node => node.Name == "GrabrFi" && node.Kind == NodeKind.Source)));
            True(empty.Scenarios.Any(scenario => scenario.Nodes.Any(node => node.Name == "Wallbit Pro" && node.Kind == NodeKind.Source)));
        }

        private static void GlobalFeesUpgradeOnlyUntouchedDefaults()
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.GrabrFiGlobalFeesApplied = false;
            var first = document.Scenarios[0];
            first.Name = "GrabrFi · circuito completo";
            var oldAch = first.Routes.Single(route => route.Label == "GrabrFi → Wallbit Pro · ACH");
            oldAch.PercentageFee = 0.3m;
            oldAch.PercentageFeeMaximum = 5m;
            var oldUsdc = first.Routes.Single(route => route.Label == "GrabrFi → Binance · USDC");
            oldUsdc.PercentageFee = 0.5m;
            var oldUsdt = first.Routes.Single(route => route.Label == "GrabrFi → Binance · USDT");
            oldUsdt.PercentageFee = 0.6m;
            var customized = document.Scenarios[1].Routes.Single(route => route.Label == "GrabrFi → Binance · USDC");
            customized.PercentageFee = 0.8m;

            True(StarterScenarioFactory.UpgradeGrabrFiGlobalFees(document));
            Equal("GrabrFi Global · circuito completo", first.Name);
            Equal(0.5m, oldAch.PercentageFee);
            Equal(10m, oldAch.PercentageFeeMaximum!.Value);
            Equal(1m, oldUsdc.PercentageFee);
            Equal(1.1m, oldUsdt.PercentageFee);
            Equal(0.8m, customized.PercentageFee);
            True(!StarterScenarioFactory.UpgradeGrabrFiGlobalFees(document));
        }

        private static void HistoricalStarterShapesUpgradeWithoutLosingTheirIds()
        {
            var previous = new CashflowScenario { Name = "GrabrFi · tarifas configuradas" };
            previous.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
            previous.Nodes.Add(new PlatformNode { Id = "ach", Name = "Cuenta bancaria (ACH)", Currency = "USD" });
            previous.Nodes.Add(new PlatformNode { Id = "usdc", Name = "Wallet USDC", Currency = "USDC" });
            previous.Nodes.Add(new PlatformNode { Id = "usdt", Name = "Wallet USDT", Currency = "USDT" });
            previous.Nodes.Add(new PlatformNode { Id = "ars", Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
            foreach (var label in new[] { "Retiro directo a pesos · cotización manual", "Transferencia ACH", "Transferencia en USDC", "Transferencia en USDT" })
            {
                previous.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "ars", Label = label });
            }
            var previousId = previous.Id;
            var previousDocument = new ScenarioDocument { Version = 1 };
            previousDocument.Scenarios.Add(previous);
            True(StarterScenarioFactory.UpgradeStarterTemplates(previousDocument));
            Equal(previousId, previousDocument.Scenarios[0].Id);
            True(previousDocument.Scenarios[0].Routes.Count >= 9);

            var legacy = new CashflowScenario { Name = "GrabrFi · ejemplo editable" };
            legacy.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
            legacy.Nodes.Add(new PlatformNode { Id = "provider", Name = "Proveedor intermedio", Currency = "USD", Kind = NodeKind.Intermediate });
            legacy.Nodes.Add(new PlatformNode { Id = "ars", Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
            legacy.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "ars", Label = "Ejemplo directo", PercentageFee = 4m, ExchangeRate = 1000m });
            legacy.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "provider", Label = "Entrada al proveedor", PercentageFee = 1m, FixedFee = 1m, ExchangeRate = 1m });
            legacy.Routes.Add(new TransferRoute { FromNodeId = "provider", ToNodeId = "ars", Label = "Salida a cuenta local", PercentageFee = 1m, FixedFee = 2m, ExchangeRate = 1000m });
            var legacyId = legacy.Id;
            var legacyDocument = new ScenarioDocument { Version = 1 };
            legacyDocument.Scenarios.Add(legacy);
            True(StarterScenarioFactory.UpgradeStarterTemplates(legacyDocument));
            Equal(legacyId, legacyDocument.Scenarios[0].Id);
            True(legacyDocument.Scenarios[0].Routes.Count >= 9);

            var editedLegacy = new CashflowScenario { Name = "GrabrFi · ejemplo editable" };
            editedLegacy.Nodes.Add(new PlatformNode { Name = "Otra cuenta", Currency = "USD", Kind = NodeKind.Source });
            editedLegacy.Nodes.Add(new PlatformNode { Name = "Proveedor intermedio", Currency = "USD", Kind = NodeKind.Intermediate });
            editedLegacy.Nodes.Add(new PlatformNode { Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
            for (var index = 0; index < 3; index++) editedLegacy.Routes.Add(new TransferRoute());
            var editedId = editedLegacy.Id;
            var editedDocument = new ScenarioDocument { Version = 1 };
            editedDocument.Scenarios.Add(editedLegacy);
            True(StarterScenarioFactory.UpgradeStarterTemplates(editedDocument));
            Equal(editedId, editedDocument.Scenarios[0].Id);
            Equal("Otra cuenta", editedDocument.Scenarios[0].Nodes[0].Name);
        }

        private static void BinanceArsMigrationHandlesMissingNodesAndRoutes()
        {
            var noBinance = new ScenarioDocument { Version = 2 };
            var unrelated = StarterScenarioFactory.CreateEmpty("Sin Binance");
            noBinance.Scenarios.Add(unrelated);
            True(StarterScenarioFactory.UpgradeStarterTemplates(noBinance));
            True(unrelated.Routes.All(route => route.LiveQuoteKey != MarketQuoteKeys.BinanceSellUsdtForArs));

            var needsRoute = new ScenarioDocument { Version = 2 };
            var scenario = StarterScenarioFactory.CreateEmpty("Binance incompleto");
            scenario.Nodes.Add(new PlatformNode { Name = "Binance · USDT", Currency = "USDT", Kind = NodeKind.Intermediate });
            scenario.Nodes.Add(new PlatformNode { Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
            needsRoute.Scenarios.Add(scenario);
            True(StarterScenarioFactory.UpgradeStarterTemplates(needsRoute));
            Equal(1, scenario.Routes.Count(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs));
        }

        private static void ScenarioStoreRecoversInvalidAndMigratesLegacyDocuments()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "cashflow-store-test-" + Guid.NewGuid().ToString("N") + ".json");
            var store = new ScenarioStore(filePath);
            try
            {
                Equal(2, store.Load().Scenarios.Count);
                File.WriteAllText(filePath, "{ archivo inválido");
                Equal(2, store.Load().Scenarios.Count);
                var backups = Directory.GetFiles(Path.GetDirectoryName(filePath)!, Path.GetFileName(filePath) + ".invalid-*");
                Equal(1, backups.Length);
                True(File.ReadAllText(backups[0]).Contains("archivo inválido", StringComparison.Ordinal));
                File.Delete(backups[0]);

                var legacy = StarterScenarioFactory.CreateStarterDocument();
                legacy.Version = 7;
                legacy.GrabrFiGlobalFeesApplied = false;
                legacy.BinanceUsdtWalletRouteInitialized = false;
                legacy.Retirement.InitialInvestedUsd = 100m;
                legacy.Retirement.StockAllocationPercentage = 80m;
                store.Save(legacy);
                var restored = store.Load();
                Equal(9, restored.Version);
                Equal(8000L, restored.Retirement.InitialStocksCents);
                Equal(2000L, restored.Retirement.InitialBondsCents);
                True(restored.GrabrFiGlobalFeesApplied);
                True(restored.BinanceUsdtWalletRouteInitialized);
                True(File.ReadAllText(filePath).Contains("\"InitialStocksCents\": 8000", StringComparison.Ordinal));

                using (var locked = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Equal(2, store.Load().Scenarios.Count);
                }

                legacy.MusicSession = null!;
                legacy.Retirement = null!;
                legacy.ManualExchangeRates = null!;
                File.WriteAllText(filePath, JsonSerializer.Serialize(legacy));
                var normalized = store.Load();
                True(normalized.MusicSession != null && normalized.Retirement != null && normalized.ManualExchangeRates != null);

                legacy = StarterScenarioFactory.CreateStarterDocument();
                legacy.Version = 7;
                legacy.GrabrFiGlobalFeesApplied = false;
                legacy.BinanceUsdtWalletRouteInitialized = false;
                File.WriteAllText(filePath, JsonSerializer.Serialize(legacy));
                var temporaryPath = filePath + ".tmp";
                using (var lockedTemporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    Equal(9, store.Load().Version);
                }
                File.Delete(temporaryPath);
                Directory.CreateDirectory(temporaryPath);
                Equal(9, store.Load().Version);
                Directory.Delete(temporaryPath);

                File.WriteAllText(filePath, "{ archivo inválido");
                var blockedBackups = new[] { -1, 0, 1 }
                    .Select(offset => filePath + ".invalid-" + DateTime.Now.AddSeconds(offset).ToString("yyyyMMdd-HHmmss"))
                    .Distinct().ToArray();
                foreach (var backup in blockedBackups) Directory.CreateDirectory(backup);
                try
                {
                    Equal(2, store.Load().Scenarios.Count);
                    True(File.Exists(filePath));
                }
                finally
                {
                    foreach (var backup in blockedBackups) Directory.Delete(backup);
                }
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
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
                    var sampleStep = result.Steps[0];
                    var liveStep = new RouteStepResult
                    {
                        From = sampleStep.From,
                        To = sampleStep.To,
                        Route = new TransferRoute
                        {
                            Label = "Operación de mercado",
                            LiveQuoteKey = MarketQuoteKeys.BinanceSellUsdcForUsdt,
                            FeeApplication = FeeApplicationMode.ChargeSeparately,
                            ExchangeRate = 1m,
                            PercentageFee = 1m,
                            FixedFee = 1m
                        },
                        InputAmount = 100m,
                        TradeableInputAmount = 98m,
                        FeeAmount = 2m,
                        DebitedAmount = 100m,
                        InputRemainder = 1m,
                        GrossOutputAmount = 98m,
                        TradingFeeAmount = 1m,
                        OutputFeeAmount = 1m,
                        OutputAmount = 96m
                    };
                    var liveDetails = new RouteDetailsWindow(new RouteResult
                    {
                        Steps = new[] { liveStep },
                        FinalAmount = 96m,
                        DestinationCurrency = liveStep.To.Currency,
                        SourceBudgetAmount = 101m,
                        SourceDebitedAmount = 100m
                    });
                    var liveLines = ((StackPanel)((Border)((StackPanel)liveDetails.FindName("StepsPanel")).Children[0]).Child)
                        .Children.OfType<Grid>().SelectMany(grid => grid.Children.OfType<TextBlock>()).Select(text => text.Text).ToArray();
                    True(liveLines.Any(line => line.Contains("Saldo no utilizado", StringComparison.Ordinal)));
                    True(liveLines.Any(line => line.Contains("Fee de trade", StringComparison.Ordinal)));
                    True(liveLines.Any(line => line.Contains("Cargo sobre la salida", StringComparison.Ordinal)));
                    var feeRule = typeof(RouteDetailsWindow).GetMethod("BuildFeeRule",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                    var rule = new TransferRoute { PercentageFee = 1m, FixedFee = 2m };
                    True(((string)feeRule.Invoke(null, new object[] { rule })!).Contains("fijo", StringComparison.Ordinal));
                    rule.PercentageFeeMinimum = 1m;
                    True(((string)feeRule.Invoke(null, new object[] { rule })!).Contains("mínimo", StringComparison.Ordinal));
                    rule.PercentageFeeMinimum = null;
                    rule.PercentageFeeMaximum = 5m;
                    True(((string)feeRule.Invoke(null, new object[] { rule })!).Contains("máximo", StringComparison.Ordinal));
                    rule.PercentageFeeMinimum = 1m;
                    True(((string)feeRule.Invoke(null, new object[] { rule })!).Contains("mínimo", StringComparison.Ordinal));

                    liveStep.Route.FeeApplication = FeeApplicationMode.DeductFromAmount;
                    _ = new RouteDetailsWindow(new RouteResult { Steps = new[] { liveStep }, DestinationCurrency = liveStep.To.Currency });
                    liveStep.Route.ExchangeRateIsManual = true;
                    liveStep.Route.LiveQuoteKey = null;
                    liveStep.Route.ManualExchangeRateUpdatedAt = null;
                    _ = new RouteDetailsWindow(new RouteResult { Steps = new[] { liveStep }, DestinationCurrency = liveStep.To.Currency });
                    liveStep.Route.ManualExchangeRateUpdatedAt = DateTimeOffset.Now;
                    _ = new RouteDetailsWindow(new RouteResult { Steps = new[] { liveStep }, DestinationCurrency = liveStep.To.Currency });
                    liveDetails.Show();
                    typeof(RouteDetailsWindow).GetMethod("Close_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(liveDetails, new object[] { liveDetails, new RoutedEventArgs() });
                    True(!liveDetails.IsVisible);
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
                    VerifyRetirementReserveToggleInUi();
                    RetirementCardActionsTests.Run();
                    CoastFireTests.Run();
                    VerifyMainWindowFlowInUi();
                    VerifyGraphGeometryAndStylesInUi();
                    VerifyRetirementInflationRefreshInUi();
                    VerifyMusicMarketFailureStatesInUi();
                    MainWindowEdgeTests.Run();
                    RetirementEdgeTests.Run();
                    MusicEdgeTests.Run();
                    ConstructorContractTests.Run();
                    GraphEdgeTests.Run();
                    MarketFailureTypesTests.Run();
                    VerifyDialogsInUi();
                    VerifyManualRatesSaveInUi();
                    VerifyManualRatesInvalidAndEmptyStatesInUi();
                    WindowPresentationTests.Run();
                    var splash = new SplashWindow();
                    splash.Show();
                    var closeButton = splash.FindName("CloseSplashButton") as Button;
                    True(closeButton != null);
                    closeButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    True(!splash.IsVisible);
                    var keyboardSplash = new SplashWindow();
                    keyboardSplash.Show();
                    var splashSource = PresentationSource.FromVisual(keyboardSplash)!;
                    var keyHandler = typeof(SplashWindow).GetMethod("SplashWindow_KeyDown",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    var enter = new KeyEventArgs(Keyboard.PrimaryDevice, splashSource, Environment.TickCount, Key.Enter)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };
                    keyHandler.Invoke(keyboardSplash, new object[] { keyboardSplash, enter });
                    True(keyboardSplash.IsVisible && !enter.Handled);
                    var escape = new KeyEventArgs(Keyboard.PrimaryDevice, splashSource, Environment.TickCount, Key.Escape)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };
                    keyHandler.Invoke(keyboardSplash, new object[] { keyboardSplash, escape });
                    True(!keyboardSplash.IsVisible && escape.Handled);
                    window.Close();
                    AppStartupTests.Run(app);
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

        private static void VerifyGraphGeometryAndStylesInUi()
        {
            var scenario = new CashflowScenario();
            var source = new PlatformNode { Id = "source", Name = "Origen con un nombre deliberadamente muy largo", Currency = "USD", Kind = NodeKind.Source, X = 35, Y = 65 };
            var destination = new PlatformNode { Id = "destination", Name = "Destino", Currency = "ARS", Kind = NodeKind.Destination, X = 390, Y = 65 };
            var intermediary = new PlatformNode { Id = "intermediary", Name = "Intermedio", Currency = "USDT", Kind = NodeKind.Intermediate, X = 390, Y = 225 };
            scenario.Nodes.AddRange(new[] { source, destination, intermediary });
            var route = new TransferRoute
            {
                Id = "forward",
                FromNodeId = source.Id,
                ToNodeId = destination.Id,
                PercentageFee = 1m,
                PercentageFeeMinimum = 1m,
                PercentageFeeMaximum = 10m,
                FixedFee = 2m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                TradingFeePercentage = 0.1m,
                OutputPercentageFee = 1m,
                MinimumInputAmount = 5m,
                MaximumInputAmount = 1000m,
                InputAmountStep = 1m,
                ExchangeRateIsManual = true,
                ExchangeRate = 1500m
            };
            var reverse = new TransferRoute { Id = "reverse", FromNodeId = destination.Id, ToNodeId = source.Id, Enabled = false };
            var pending = new TransferRoute { Id = "pending", FromNodeId = source.Id, ToNodeId = intermediary.Id, ExchangeRateConfigured = false };
            scenario.Routes.AddRange(new[] { route, reverse, pending });
            var graph = new GraphCanvas { Scenario = scenario };
            var initial = RenderVisual(graph, 900, 460);
            graph.HighlightedRouteIds = new[] { route.Id };
            True(RenderVisual(graph, 900, 460) != initial);
            graph.SelectedRouteId = reverse.Id;
            True(RenderVisual(graph, 900, 460) != initial);
            graph.SelectedNodeId = intermediary.Id;
            True(RenderVisual(graph, 900, 460) != initial);

            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var summary = typeof(GraphCanvas).GetMethod("BuildRouteSummary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            string Label(TransferRoute value) => (string)summary.Invoke(null, new object[] { value })!;
            var fullLabel = Label(route);
            True(fullLabel.Contains("trade", StringComparison.Ordinal));
            True(fullLabel.Contains("salida", StringComparison.Ordinal));
            True(fullLabel.Contains("paso", StringComparison.Ordinal));
            True(fullLabel.Contains("manual", StringComparison.Ordinal));
            True(fullLabel.Contains("aparte", StringComparison.Ordinal));
            True(Label(pending).Contains("cotización pendiente", StringComparison.Ordinal));
            True(Label(reverse).Contains("sin comisión", StringComparison.Ordinal));
            route.PercentageFeeMinimum = null;
            True(Label(route).Contains("0–10", StringComparison.Ordinal));
            route.PercentageFeeMaximum = null;
            route.MinimumInputAmount = null;
            True(Label(route).Contains("0–1000", StringComparison.Ordinal));
            route.MaximumInputAmount = null;
            route.MinimumInputAmount = 5m;
            True(Label(route).Contains("∞", StringComparison.Ordinal));

            var segment = typeof(GraphCanvas).GetMethod("TryGetRouteSegment", flags)!;
            object[] arguments = { route, source, destination, new Point(), new Point(), new Vector() };
            True((bool)segment.Invoke(graph, arguments)!);
            var start = (Point)arguments[3];
            var end = (Point)arguments[4];
            var midpoint = new Point((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
            var findRoute = typeof(GraphCanvas).GetMethod("FindRoute", flags)!;
            scenario.Routes.Add(new TransferRoute { FromNodeId = "missing", ToNodeId = source.Id });
            Equal(route, findRoute.Invoke(graph, new object[] { midpoint }));
            True(findRoute.Invoke(graph, new object[] { new Point(850, 430) }) == null);

            var distance = typeof(GraphCanvas).GetMethod("DistanceToSegment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Near(5d, (double)distance.Invoke(null, new object[] { new Point(3, 4), new Point(0, 0), new Point(0, 0) })!);
            Near(2d, (double)distance.Invoke(null, new object[] { new Point(-2, 0), new Point(0, 0), new Point(10, 0) })!);
            Near(2d, (double)distance.Invoke(null, new object[] { new Point(12, 0), new Point(0, 0), new Point(10, 0) })!);

            destination.X = source.X;
            destination.Y = source.Y;
            True(!(bool)segment.Invoke(graph, arguments)!);
            RenderVisual(graph, 900, 460);
            graph.Scenario = null;
            True(findRoute.Invoke(graph, new object[] { midpoint }) == null);
            RenderVisual(graph, 900, 460);

            void MouseButton(string handler, System.Windows.Input.MouseButton button)
            {
                var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
                {
                    RoutedEvent = button == System.Windows.Input.MouseButton.Right
                        ? UIElement.MouseRightButtonDownEvent
                        : UIElement.MouseLeftButtonDownEvent
                };
                typeof(GraphCanvas).GetMethod(handler, flags)!.Invoke(graph, new object[] { args });
            }
            MouseButton("OnMouseLeftButtonDown", System.Windows.Input.MouseButton.Left);
            MouseButton("OnMouseRightButtonDown", System.Windows.Input.MouseButton.Right);
            graph.Scenario = new CashflowScenario();
            MouseButton("OnMouseLeftButtonDown", System.Windows.Input.MouseButton.Left);
            MouseButton("OnMouseRightButtonDown", System.Windows.Input.MouseButton.Right);
            var panStart = (Point)typeof(GraphCanvas).GetField("_panStart", flags)!.GetValue(graph)!;
            typeof(GraphCanvas).GetMethod("UpdatePointer", flags)!
                .Invoke(graph, new object[] { panStart + new Vector(30, 25), false, true });
            var pan = (Vector)typeof(GraphCanvas).GetField("_panOffset", flags)!.GetValue(graph)!;
            Near(30d, pan.X);
            Near(25d, pan.Y);
            MouseButton("OnMouseRightButtonUp", System.Windows.Input.MouseButton.Right);
            MouseButton("OnMouseRightButtonUp", System.Windows.Input.MouseButton.Right);
            MouseButton("OnMouseLeftButtonUp", System.Windows.Input.MouseButton.Left);
            var motion = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount);
            typeof(GraphCanvas).GetMethod("OnMouseMove", flags)!.Invoke(graph, new object[] { motion });
            typeof(GraphCanvas).GetMethod("OnLostMouseCapture", flags)!.Invoke(graph, new object[] { motion });
            Zoom(graph, 120);
            Zoom(graph, -120);

            var pointerArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left);
            var pointer = pointerArgs.GetPosition(graph);
            var selectableNode = new PlatformNode { Id = "selectable", Name = "Seleccionable", Currency = "USD", X = pointer.X - 20, Y = pointer.Y - 20 };
            var nodeScenario = new CashflowScenario();
            nodeScenario.Nodes.Add(selectableNode);
            graph.Scenario = nodeScenario;
            var nodeSelections = 0;
            var changes = 0;
            graph.NodeSelected += selected => { Equal(selectableNode, selected); nodeSelections++; };
            graph.GraphChanged += () => changes++;
            MouseButton("OnMouseLeftButtonDown", System.Windows.Input.MouseButton.Left);
            Equal(1, nodeSelections);
            typeof(GraphCanvas).GetMethod("UpdatePointer", flags)!
                .Invoke(graph, new object[] { new Point(10000, 10000), true, false });
            Near(710d, selectableNode.X);
            Near(372d, selectableNode.Y);
            MouseButton("OnMouseLeftButtonUp", System.Windows.Input.MouseButton.Left);
            Equal(1, changes);
            selectableNode.X = pointer.X - 20;
            selectableNode.Y = pointer.Y - 20;
            MouseButton("OnMouseRightButtonDown", System.Windows.Input.MouseButton.Right);
            True(!(bool)typeof(GraphCanvas).GetField("_isPanning", flags)!.GetValue(graph)!);

            var fromNode = new PlatformNode { Id = "from", Name = "Desde", Currency = "USD", X = pointer.X - 180 - 89, Y = pointer.Y - 38 };
            var toNode = new PlatformNode { Id = "to", Name = "Hasta", Currency = "ARS", X = pointer.X + 180 - 89, Y = pointer.Y - 38 };
            var selectableRoute = new TransferRoute { Id = "selectable-route", FromNodeId = fromNode.Id, ToNodeId = toNode.Id };
            var routeScenario = new CashflowScenario();
            routeScenario.Nodes.AddRange(new[] { fromNode, toNode });
            routeScenario.Routes.Add(selectableRoute);
            graph.Scenario = routeScenario;
            var routeSelections = 0;
            graph.RouteSelected += selected => { Equal(selectableRoute, selected); routeSelections++; };
            MouseButton("OnMouseLeftButtonDown", System.Windows.Input.MouseButton.Left);
            Equal(1, routeSelections);
            MouseButton("OnMouseRightButtonDown", System.Windows.Input.MouseButton.Right);
            True(!(bool)typeof(GraphCanvas).GetField("_isPanning", flags)!.GetValue(graph)!);

            var zeroWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 0);
            typeof(GraphCanvas).GetMethod("OnMouseWheel", flags)!.Invoke(graph, new object[] { zeroWheel });
            True(!zeroWheel.Handled);
            for (var index = 0; index < 40; index++) Zoom(graph, 120);
            Zoom(graph, 120);
        }

        private static void VerifyRetirementInflationRefreshInUi()
        {
            var path = Path.Combine(Path.GetTempPath(), "cashflow-inflation-ui-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var year = DateTime.UtcNow.Year;
                var json = "{\"status\":\"REQUEST_SUCCEEDED\",\"Results\":{\"series\":[{\"data\":[" +
                    $"{{\"year\":\"{year}\",\"period\":\"M08\",\"value\":\"120\"}}," +
                    $"{{\"year\":\"{year - 1}\",\"period\":\"M08\",\"value\":\"100\"}}" +
                    "]}]}}";
                using var successClient = MarketServiceTests.CreateClient(_ => MarketServiceTests.Json(json));
                var document = StarterScenarioFactory.CreateStarterDocument();
                var view = new RetirementView(document, new ScenarioStore(path), new UsInflationService(successClient));
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var refresh = typeof(RetirementView).GetMethod("RefreshInflationAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                ((Task)refresh.Invoke(view, null)!).GetAwaiter().GetResult();
                Equal(20m, document.Retirement.UsInflationPercentage);
                typeof(RetirementView).GetMethod("RefreshInflation_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(view, new object[] { view, new RoutedEventArgs() });
                True(((Button)view.FindName("RefreshInflationButton")).IsEnabled);
                True(File.Exists(path));

                typeof(RetirementView).GetField("_refreshing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(view, true);
                ((Task)refresh.Invoke(view, null)!).GetAwaiter().GetResult();
                typeof(RetirementView).GetField("_refreshing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(view, false);

                using var failingClient = MarketServiceTests.CreateClient(_ =>
                    new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
                var failedView = new RetirementView(document, new ScenarioStore(path), new UsInflationService(failingClient));
                failedView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                ((Task)refresh.Invoke(failedView, null)!).GetAwaiter().GetResult();
                True(((TextBlock)failedView.FindName("InflationStatusText")).Text.Contains("No se pudo", StringComparison.Ordinal));
                Equal(20m, document.Retirement.UsInflationPercentage);
                True(((Button)failedView.FindName("RefreshInflationButton")).IsEnabled);

                void VerifyBlsFailure(Func<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage> response)
                {
                    using var failing = MarketServiceTests.CreateClient(response);
                    var viewWithFailure = new RetirementView(document, new ScenarioStore(path), new UsInflationService(failing));
                    viewWithFailure.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    ((Task)refresh.Invoke(viewWithFailure, null)!).GetAwaiter().GetResult();
                    True(((TextBlock)viewWithFailure.FindName("InflationStatusText")).Text.Contains("No se pudo", StringComparison.Ordinal));
                    Equal(20m, document.Retirement.UsInflationPercentage);
                }
                VerifyBlsFailure(_ => MarketServiceTests.Json("{json inválido"));
                VerifyBlsFailure(_ => MarketServiceTests.Json("{\"status\":\"REQUEST_FAILED\"}"));
                VerifyBlsFailure(_ => throw new TaskCanceledException("Tiempo de espera simulado"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void VerifyMusicMarketFailureStatesInUi()
        {
            var path = Path.Combine(Path.GetTempPath(), "cashflow-music-fail-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                void CheckFailure(Func<System.Net.Http.HttpRequestMessage, bool> fail, string expected)
                {
                    using var client = MarketServiceTests.CreateFixtureClient(fail);
                    var document = CreateReadyDocument();
                    document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
                    document.MusicSession.AutoRefreshEnabled = false;
                    var view = new MusicSessionWindow(document, new ScenarioStore(path),
                        new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                        new ArgentinaExchangeRateService(client));
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    var refresh = typeof(MusicSessionWindow).GetMethod("RefreshMarketsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    ((Task)refresh.Invoke(view, new object[] { true })!).GetAwaiter().GetResult();
                    True(((TextBlock)view.FindName("RefreshStatusText")).Text.Contains(expected, StringComparison.Ordinal));
                    True(((Button)view.FindName("RefreshButton")).IsEnabled);
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                }

                CheckFailure(request => request.RequestUri!.AbsolutePath.StartsWith("/v1/dolares/", StringComparison.Ordinal), "Binance actualizado");
                CheckFailure(request => request.RequestUri!.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal), "Blue y oficial actualizados");
                CheckFailure(_ => true, "No se pudo actualizar internet");

                using var client = MarketServiceTests.CreateFixtureClient();
                var pendingDocument = CreateReadyDocument();
                pendingDocument.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
                pendingDocument.MusicSession.AutoRefreshEnabled = false;
                pendingDocument.MusicSession.BlueBuy = null;
                pendingDocument.MusicSession.OfficialSell = null;
                pendingDocument.MusicSession.PersonFeePercentage = 100m;
                var pendingView = new MusicSessionWindow(pendingDocument, new ScenarioStore(path),
                    new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                    new ArgentinaExchangeRateService(client));
                pendingView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                True(((TextBlock)pendingView.FindName("BestBankedMethodText")).Text.Contains("Faltan", StringComparison.Ordinal));
                True(((TextBlock)pendingView.FindName("BestWithoutMethodText")).Text.Contains("Faltan", StringComparison.Ordinal));
                var saveInputs = typeof(MusicSessionWindow).GetMethod("TrySaveInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                ((TextBox)pendingView.FindName("PersonFeeBox")).Text = "0";
                ((TextBox)pendingView.FindName("BinanceUsdcFeeBox")).Text = "2";
                True((bool)saveInputs.Invoke(pendingView, new object[] { false })!);
                Equal(2m, pendingDocument.MusicSession.BinanceUsdcTransferFee!.Value);
                typeof(MusicSessionWindow).GetMethod("Calculate_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(pendingView, new object[] { pendingView, new RoutedEventArgs() });
                pendingView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void VerifyRetirementReserveToggleInUi()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "cashflow-retirement-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var document = StarterScenarioFactory.CreateStarterDocument();
                document.Retirement.EnsurePlanningCollections();
                var reserve = document.Retirement.Reserves[0];
                reserve.Name = "Casa";
                reserve.TargetCents = 2000000;
                var view = new RetirementView(document, new ScenarioStore(filePath));
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var cards = view.FindName("ReserveRowsPanel") as StackPanel;
                True(cards != null);
                Equal(3, cards!.Children.Count);

                Button ToggleButton()
                {
                    var root = (StackPanel)((Border)cards.Children[0]).Child;
                    var actions = (StackPanel)((DockPanel)root.Children[0]).Children[0];
                    return (Button)actions.Children[0];
                }

                Equal("Ocultar", ToggleButton().Content);
                ToggleButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                True(!reserve.IsIncluded);
                Equal(3, cards.Children.Count);
                Equal("Mostrar", ToggleButton().Content);
                True(File.Exists(filePath));
                var saved = JsonSerializer.Deserialize<ScenarioDocument>(File.ReadAllText(filePath))!;
                True(!saved.Retirement.Reserves[0].IsIncluded);
                Equal(2000000L, saved.Retirement.Reserves[0].TargetCents);

                ToggleButton().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                True(reserve.IsIncluded);
                Equal("Ocultar", ToggleButton().Content);
                saved = JsonSerializer.Deserialize<ScenarioDocument>(File.ReadAllText(filePath))!;
                True(saved.Retirement.Reserves[0].IsIncluded);
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        private static void VerifyMainWindowFlowInUi()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "cashflow-window-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var document = CreateReadyDocument();
                document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
                document.MusicSession.AutoRefreshEnabled = false;
                document.Retirement.EnsurePlanningCollections();
                document.Retirement.MonthlyIncomes[0].MonthlyAmountCents = 200000;
                document.Retirement.OrdinaryMonthlyExpensesCents = 80000;
                document.Retirement.InitialStocksCents = 1000000;
                document.Retirement.InitialBondsCents = 500000;
                document.Retirement.TargetInvestedCents = 5000000;
                document.Retirement.TargetStocksCents = 3000000;
                document.Retirement.Reserves[0].CurrentCents = 100000;
                document.Retirement.Reserves[0].TargetCents = 500000;
                document.Retirement.Reserves[1].TargetCents = 300000;
                var store = new ScenarioStore(filePath);
                store.Save(document);

                using var marketClient = MarketServiceTests.CreateFixtureClient();
                var marketUpdater = new ScenarioMarketUpdater(new BinanceSpotQuoteService(marketClient));
                var argentinaRates = new ArgentinaExchangeRateService(marketClient);
                var window = new MainWindow(store, marketUpdater, argentinaRates);
                window.Show();
                window.Hide();
                var scenarios = window.FindName("ScenarioCombo") as ComboBox;
                var results = window.FindName("ResultsPanel") as StackPanel;
                var amount = window.FindName("AmountBox") as TextBox;
                True(scenarios?.SelectedItem is CashflowScenario);
                True(results != null && amount != null);

                amount!.Text = "2500";
                var calculate = typeof(MainWindow).GetMethod("Calculate_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                True(calculate != null);
                calculate!.Invoke(window, new object[] { window, new RoutedEventArgs() });
                True(results!.Children.Count > 0);
                var graph = window.FindName("Graph") as GraphCanvas;
                True(graph != null);
                var graphImage = RenderVisual(graph!, 1100, 680);
                graph!.SelectedNodeId = graph.Scenario!.Nodes[0].Id;
                True(RenderVisual(graph, 1100, 680) != graphImage);
                graph.SelectedNodeId = null;
                graph.SelectedRouteId = graph.Scenario.Routes[0].Id;
                True(RenderVisual(graph, 1100, 680) != graphImage);
                graph.SelectedRouteId = null;

                var music = window.FindName("MusicSessionHost") as ContentControl;
                True(music?.Content is MusicSessionWindow);
                var musicView = (MusicSessionWindow)music!.Content;
                musicView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var refreshMusic = typeof(MusicSessionWindow).GetMethod("RefreshMarketsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                True(refreshMusic != null);
                ((Task)refreshMusic!.Invoke(musicView, new object[] { true })!).GetAwaiter().GetResult();
                var refreshMain = typeof(MainWindow).GetMethod("RefreshInternetMarketsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                True(refreshMain != null);
                ((Task)refreshMain!.Invoke(window, new object[] { 2500m, true })!).GetAwaiter().GetResult();
                True(((TextBlock)window.FindName("MarketStatusText")).Text.Contains("actualizados", StringComparison.Ordinal));
                var options = musicView.FindName("OptionsPanel") as StackPanel;
                True(options != null && options.Children.Count > 0);
                VerifyInputValidation(musicView,
                    ("TargetUsdBox", "0"),
                    ("RefreshMinutesBox", "0"),
                    ("CashUsdcRateBox", "0"),
                    ("PersonFeeBox", "100"),
                    ("BinanceUsdcFeeBox", "-1"),
                    ("OfficialExtraBox", "101"));
                var musicGraph = musicView.FindName("MusicGraph") as MusicSessionGraphCanvas;
                True(musicGraph != null);
                var musicImage = RenderVisual(musicGraph!, 1000, 350);
                Zoom(musicGraph!, 120);
                True(RenderVisual(musicGraph!, 1000, 350) != musicImage);
                var statusMethod = typeof(MusicSessionWindow).GetMethod("BuildRefreshStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                string Status(bool rates, bool binance, bool requested) =>
                    (string)statusMethod.Invoke(null, new object[] { rates, binance, requested })!;
                True(Status(true, false, true).Contains("Blue y oficial", StringComparison.Ordinal));
                True(Status(false, true, true).Contains("Binance actualizado", StringComparison.Ordinal));
                True(Status(false, false, true).Contains("No se pudo", StringComparison.Ordinal));
                True(Status(false, false, false).Contains("Sin conexión nueva", StringComparison.Ordinal));
                var quoteCards = typeof(MusicSessionWindow).GetMethod("UpdateQuoteCards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var renderMusic = typeof(MusicSessionWindow).GetMethod("RenderCalculation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var musicDocument = (ScenarioDocument)typeof(MusicSessionWindow)
                    .GetField("_document", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(musicView)!;
                var savedBlueBuy = musicDocument.MusicSession.BlueBuy;
                var savedOfficialBuy = musicDocument.MusicSession.OfficialBuy;
                musicDocument.MusicSession.BlueBuy = null;
                musicDocument.MusicSession.OfficialBuy = null;
                quoteCards.Invoke(musicView, null);
                True(((TextBlock)musicView.FindName("BlueQuoteText")).Text.Contains("Pendiente", StringComparison.Ordinal));
                True(((TextBlock)musicView.FindName("OfficialQuoteText")).Text.Contains("Pendiente", StringComparison.Ordinal));
                renderMusic.Invoke(musicView, null);
                True(((TextBlock)musicView.FindName("BestBankedMethodText")).Text.Length > 0);
                musicDocument.MusicSession.BlueBuy = savedBlueBuy;
                musicDocument.MusicSession.OfficialBuy = savedOfficialBuy;
                var savedTarget = musicDocument.MusicSession.TargetUsd;
                musicDocument.MusicSession.TargetUsd = 0m;
                renderMusic.Invoke(musicView, null);
                True(((TextBlock)musicView.FindName("PendingText")).Text.Length > 0);
                musicDocument.MusicSession.TargetUsd = savedTarget;
                quoteCards.Invoke(musicView, null);
                renderMusic.Invoke(musicView, null);
                musicDocument.MusicSession.AutoRefreshEnabled = true;
                typeof(MusicSessionWindow).GetMethod("ConfigureTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(musicView, null);
                musicView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                musicDocument.MusicSession.AutoRefreshEnabled = false;

                var retirement = window.FindName("RetirementHost") as ContentControl;
                True(retirement?.Content is RetirementView);
                var retirementView = (RetirementView)retirement!.Content;
                retirementView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                var projectionChart = retirementView.FindName("ProjectionChart") as RetirementProjectionChart;
                var reserveChart = retirementView.FindName("ReserveGoalsChart") as RetirementReserveTimelineChart;
                var runwayChart = retirementView.FindName("RunwayChart") as RetirementRunwayChart;
                True(projectionChart != null && reserveChart != null && runwayChart != null);
                var projectionImage = RenderVisual(projectionChart!, 850, 340);
                var reserveImage = RenderVisual(reserveChart!, 850, 230);
                var runwayImage = RenderVisual(runwayChart!, 850, 310);
                var emptyGraph = new GraphCanvas { Scenario = new CashflowScenario() };
                RenderVisual(emptyGraph, 480, 320);
                RenderVisual(new RetirementProjectionChart(), 480, 240);
                RenderVisual(new RetirementReserveTimelineChart(), 480, 220);
                RenderVisual(new RetirementRunwayChart(), 480, 220);
                var emptyMusicGraph = new MusicSessionGraphCanvas();
                emptyMusicGraph.ShowCalculation(new MusicSessionCalculation(), 400m);
                RenderVisual(emptyMusicGraph, 800, 300);
                var projection = (RetirementProjection)typeof(RetirementProjectionChart)
                    .GetField("_projection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(projectionChart)!;
                typeof(RetirementProjectionChart)
                    .GetField("_selectedPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(projectionChart, projection.Points[0]);
                True(RenderVisual(projectionChart!, 850, 340) != projectionImage);
                var reserveGoal = projection.ReserveGoals.First(goal => goal.TargetUsd > 0d);
                typeof(RetirementReserveTimelineChart)
                    .GetField("_selectedGoal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(reserveChart, reserveGoal);
                True(RenderVisual(reserveChart!, 850, 230) != reserveImage);
                typeof(RetirementRunwayChart)
                    .GetField("_selectedPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(runwayChart, projection.Runway.Points[0]);
                True(RenderVisual(runwayChart!, 850, 310) != runwayImage);
                Zoom(projectionChart!, -120);
                Zoom(reserveChart!, -120);
                Zoom(runwayChart!, -120);
                Zoom(projectionChart!, 120);
                Zoom(reserveChart!, 120);
                Zoom(runwayChart!, 120);
                ChartInteractionTests.Run(projectionChart!, reserveChart!, runwayChart!, musicGraph!, projection);
                VerifyInputValidation(retirementView,
                    ("TargetInvestedBox", "0"),
                    ("InitialStocksBox", "-1"),
                    ("StockTargetBox", "999999999"),
                    ("ExtraMonthsBox", "1201"),
                    ("RunwayTargetYearsBox", "0"),
                    ("StockReturnBox", "101"),
                    ("InflationBox", "101"),
                    ("WithdrawalRateBox", "101"));
                VerifyRetirementDynamicInputValidation(retirementView);

                void InvokeView(string handler, object sender)
                {
                    var method = typeof(RetirementView).GetMethod(handler, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    True(method != null);
                    method!.Invoke(retirementView, new[] { sender, new RoutedEventArgs() });
                }
                var liveDocument = (ScenarioDocument)typeof(RetirementView)
                    .GetField("_document", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(retirementView)!;
                var retirementSettings = liveDocument.Retirement;
                var incomeCount = retirementSettings.MonthlyIncomes.Count;
                InvokeView("AddIncome_Click", retirementView);
                Equal(incomeCount + 1, retirementSettings.MonthlyIncomes.Count);
                InvokeView("RemoveIncome_Click", new Button { Tag = retirementSettings.MonthlyIncomes.Last() });
                Equal(incomeCount, retirementSettings.MonthlyIncomes.Count);
                var reserveCount = retirementSettings.Reserves.Count;
                InvokeView("AddReserve_Click", retirementView);
                Equal(reserveCount + 1, retirementSettings.Reserves.Count);
                InvokeView("RemoveReserve_Click", new Button { Tag = retirementSettings.Reserves.Last() });
                Equal(reserveCount, retirementSettings.Reserves.Count);
                var adjusted = retirementSettings.UseInflationAdjustment;
                InvokeView("InflationMode_Click", retirementView);
                True(retirementSettings.UseInflationAdjustment != adjusted);
                InvokeView("Calculate_Click", retirementView);

                void Click(string handler)
                {
                    var method = typeof(MainWindow).GetMethod(handler, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    True(method != null);
                    method!.Invoke(window, new object[] { window, new RoutedEventArgs() });
                }
                void ChooseNextDialog(string buttonName)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ContextIdle,
                        new Action(() =>
                        {
                            var dialog = Application.Current.Windows.OfType<AppDialogWindow>()
                                .Single(candidate => candidate.IsVisible);
                            ((Button)dialog.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }));
                }

                var originalScenarioCount = scenarios!.Items.Count;
                Click("NewScenario_Click");
                Equal(originalScenarioCount + 1, scenarios.Items.Count);
                var currentScenario = (CashflowScenario)scenarios.SelectedItem;
                Click("AddNode_Click");
                var nodeName = (TextBox)window.FindName("NodeNameBox");
                var nodeCurrency = (TextBox)window.FindName("NodeCurrencyBox");
                nodeName.Text = "Cuenta de prueba";
                nodeCurrency.Text = "USD";
                Click("ApplyNode_Click");
                True(currentScenario.Nodes.Any(node => node.Name == "Cuenta de prueba"));

                Click("AddRoute_Click");
                var routeLabel = (TextBox)window.FindName("RouteLabelBox");
                var routeRate = (TextBox)window.FindName("RouteRateBox");
                var pendingRoute = currentScenario.Routes.Last();
                routeLabel.Text = "Transferencia de prueba";
                routeRate.Text = "1";
                void ExpectRouteValidation(string expectedMessage)
                {
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.ContextIdle,
                        new Action(() =>
                        {
                            var dialog = Application.Current.Windows.OfType<AppDialogWindow>()
                                .Single(candidate => candidate.IsVisible);
                            True(((TextBlock)dialog.FindName("MessageText")).Text.Contains(expectedMessage, StringComparison.Ordinal));
                            ((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }));
                    Click("ApplyRoute_Click");
                    True(pendingRoute.Label != "Transferencia de prueba");
                }

                foreach (var invalid in new (string Field, string Value, string Message)[]
                {
                    ("RoutePercentageBox", "-1", "comisión porcentual"),
                    ("RouteFixedBox", "-1", "comisión fija"),
                    ("RouteMinimumFeeBox", "-1", "mínimo de la comisión"),
                    ("RouteMaximumFeeBox", "-1", "máximo de la comisión"),
                    ("RouteTradingFeeBox", "101", "fee de trade"),
                    ("RouteOutputFeeBox", "101", "cargo sobre la salida"),
                    ("RouteInputStepBox", "0", "paso de la orden"),
                    ("RouteMinimumAmountBox", "-1", "monto mínimo"),
                    ("RouteMaximumAmountBox", "0", "monto máximo"),
                    ("RouteMinimumOutputBox", "-1", "mínimo recibido"),
                    ("RouteRateBox", "0", "tipo de cambio")
                })
                {
                    var field = (TextBox)window.FindName(invalid.Field);
                    var original = field.Text;
                    field.Text = invalid.Value;
                    ExpectRouteValidation(invalid.Message);
                    field.Text = original;
                }

                var minimumFeeBox = (TextBox)window.FindName("RouteMinimumFeeBox");
                var maximumFeeBox = (TextBox)window.FindName("RouteMaximumFeeBox");
                minimumFeeBox.Text = "2";
                maximumFeeBox.Text = "1";
                ExpectRouteValidation("mínimo de comisión no puede");
                minimumFeeBox.Text = maximumFeeBox.Text = string.Empty;

                var minimumAmountBox = (TextBox)window.FindName("RouteMinimumAmountBox");
                var maximumAmountBox = (TextBox)window.FindName("RouteMaximumAmountBox");
                minimumAmountBox.Text = "2";
                maximumAmountBox.Text = "1";
                ExpectRouteValidation("monto mínimo no puede");
                minimumAmountBox.Text = maximumAmountBox.Text = string.Empty;

                Click("ApplyRoute_Click");
                True(currentScenario.Routes.Any(route => route.Label == "Transferencia de prueba"));
                Click("DeleteRoute_Click");
                True(currentScenario.Routes.All(route => route.Label != "Transferencia de prueba"));

                var addedNode = currentScenario.Nodes.Single(node => node.Name == "Cuenta de prueba");
                typeof(MainWindow).GetMethod("SelectNode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(window, new object[] { addedNode });
                var nodeCount = currentScenario.Nodes.Count;
                ChooseNextDialog("SecondaryButton");
                Click("DeleteNode_Click");
                Equal(nodeCount, currentScenario.Nodes.Count);
                ChooseNextDialog("AcceptButton");
                Click("DeleteNode_Click");
                Equal(nodeCount - 1, currentScenario.Nodes.Count);

                ((TextBox)window.FindName("ScenarioNameBox")).Text = "Escenario de prueba";
                Click("Save_Click");
                Equal("Escenario de prueba", currentScenario.Name);
                Click("NewGrabrFiScenario_Click");
                Click("NewWallbitScenario_Click");
                Click("NewGlobalComparison_Click");
                Equal(originalScenarioCount + 4, scenarios.Items.Count);

                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() => Application.Current.Windows.OfType<ManualExchangeRatesWindow>()
                        .Single(candidate => candidate.IsVisible).Close()));
                Click("OpenManualRates_Click");
                var scenarioCount = scenarios.Items.Count;
                ChooseNextDialog("SecondaryButton");
                Click("DeleteScenario_Click");
                Equal(scenarioCount, scenarios.Items.Count);
                ChooseNextDialog("AcceptButton");
                Click("DeleteScenario_Click");
                Equal(scenarioCount - 1, scenarios.Items.Count);

                window.Close();
                var saved = JsonSerializer.Deserialize<ScenarioDocument>(File.ReadAllText(filePath))!;
                True(saved.Scenarios.Count >= 2);
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        private static string RenderVisual(FrameworkElement element, int width, int height)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
            element.InvalidateVisual();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            var colors = new System.Collections.Generic.HashSet<uint>();
            for (var offset = 0; offset < pixels.Length; offset += 4 * 17)
            {
                colors.Add(BitConverter.ToUInt32(pixels, offset));
            }
            True(colors.Count >= 3);
            return Convert.ToHexString(SHA256.HashData(pixels));
        }

        private static void Zoom(FrameworkElement chart, int delta)
        {
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.MouseWheelEvent
            };
            chart.RaiseEvent(wheel);
            True(wheel.Handled);
        }

        private static void VerifyDialogsInUi()
        {
            void Choose(string buttonName)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(window => window.IsVisible);
                        var button = dialog.FindName(buttonName) as Button;
                        True(button != null);
                        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
            }

            Choose("AcceptButton");
            AppDialogWindow.ShowInfo(null, "Mensaje de prueba", "Aviso");
            Choose("SecondaryButton");
            True(!AppDialogWindow.Confirm(null, "¿Eliminar?", "Confirmación", "Eliminar"));
            Choose("AcceptButton");
            True(AppDialogWindow.Confirm(null, "¿Eliminar?", "Confirmación", "Eliminar"));
        }

        private static void VerifyManualRatesSaveInUi()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "cashflow-rates-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var document = StarterScenarioFactory.CreateStarterDocument();
                var dialog = new ManualExchangeRatesWindow(document, new ScenarioStore(filePath));
                var first = document.ManualExchangeRates.OrderBy(setting => setting.ProviderName).ThenBy(setting => setting.Key).First();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        var panel = (StackPanel)dialog.FindName("RatesPanel");
                        var grid = (Grid)((Border)panel.Children[0]).Child;
                        var input = (TextBox)grid.Children[1];
                        input.Text = "1600";
                        typeof(ManualExchangeRatesWindow)
                            .GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                            .Invoke(dialog, new object[] { dialog, new RoutedEventArgs() });
                    }));
                True(dialog.ShowDialog() == true);
                Equal(1600m, first.ExchangeRate);
                var saved = JsonSerializer.Deserialize<ScenarioDocument>(File.ReadAllText(filePath))!;
                True(saved.ManualExchangeRates.Any(setting => setting.Key == first.Key && setting.ExchangeRate == 1600m));
                True(saved.Scenarios.SelectMany(scenario => scenario.Routes)
                    .Where(route => route.ManualExchangeRateKey == first.Key)
                    .All(route => route.ExchangeRate == 1600m));
                var blockedPath = filePath + ".blocked";
                Directory.CreateDirectory(blockedPath);
                try
                {
                    var blocked = new ManualExchangeRatesWindow(document, new ScenarioStore(blockedPath));
                    blocked.Show();
                    typeof(ManualExchangeRatesWindow)
                        .GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(blocked, new object[] { blocked, new RoutedEventArgs() });
                    True(((TextBlock)blocked.FindName("StatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
                    blocked.Close();
                }
                finally
                {
                    if (File.Exists(blockedPath + ".tmp")) File.Delete(blockedPath + ".tmp");
                    Directory.Delete(blockedPath);
                }
                var lockedPath = filePath + ".locked";
                File.WriteAllText(lockedPath + ".tmp", "bloqueado");
                try
                {
                    using var lockedTemporary = new FileStream(lockedPath + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    var locked = new ManualExchangeRatesWindow(document, new ScenarioStore(lockedPath));
                    locked.Show();
                    typeof(ManualExchangeRatesWindow)
                        .GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(locked, new object[] { locked, new RoutedEventArgs() });
                    True(((TextBlock)locked.FindName("StatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
                    locked.Close();
                }
                finally
                {
                    File.Delete(lockedPath + ".tmp");
                }
                var blockedTemporaryPath = filePath + ".unauthorized";
                File.WriteAllText(blockedTemporaryPath + ".tmp", "sin sobrescribir");
                File.SetAttributes(blockedTemporaryPath + ".tmp", FileAttributes.ReadOnly);
                try
                {
                    try
                    {
                        new ScenarioStore(blockedTemporaryPath).Save(document);
                        throw new InvalidOperationException("Se esperaba un fallo de escritura.");
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    var blocked = new ManualExchangeRatesWindow(document, new ScenarioStore(blockedTemporaryPath));
                    blocked.Show();
                    typeof(ManualExchangeRatesWindow)
                        .GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                        .Invoke(blocked, new object[] { blocked, new RoutedEventArgs() });
                    True(((TextBlock)blocked.FindName("StatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
                    blocked.Close();
                }
                finally
                {
                    File.SetAttributes(blockedTemporaryPath + ".tmp", FileAttributes.Normal);
                    File.Delete(blockedTemporaryPath + ".tmp");
                }
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        private static void VerifyManualRatesInvalidAndEmptyStatesInUi()
        {
            var filePath = Path.Combine(Path.GetTempPath(), "cashflow-rates-invalid-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var document = StarterScenarioFactory.CreateStarterDocument();
                var previous = document.ManualExchangeRates[0].ExchangeRate;
                var dialog = new ManualExchangeRatesWindow(document, new ScenarioStore(filePath));
                dialog.Show();
                var panel = (StackPanel)dialog.FindName("RatesPanel");
                var input = (TextBox)((Grid)((Border)panel.Children[0]).Child).Children[1];
                input.Text = "0";
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        var alert = Application.Current.Windows.OfType<AppDialogWindow>().Single(window => window.IsVisible);
                        True(((TextBlock)alert.FindName("MessageText")).Text.Contains("mayor que cero", StringComparison.Ordinal));
                        ((Button)alert.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                typeof(ManualExchangeRatesWindow)
                    .GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(dialog, new object[] { dialog, new RoutedEventArgs() });
                True(!File.Exists(filePath));
                Equal(previous, document.ManualExchangeRates[0].ExchangeRate);
                dialog.Close();

                var empty = new ManualExchangeRatesWindow(new ScenarioDocument(), new ScenarioStore(filePath));
                var emptyPanel = (StackPanel)empty.FindName("RatesPanel");
                Equal(1, emptyPanel.Children.Count);
                True(((TextBlock)emptyPanel.Children[0]).Text.Contains("No hay cotizaciones", StringComparison.Ordinal));
                empty.Close();
            }
            finally
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
        }

        private static void VerifyInputValidation(UserControl view, params (string Name, string Invalid)[] invalidFields)
        {
            var method = view.GetType().GetMethod("TrySaveInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            True(method != null);
            foreach (var (name, invalid) in invalidFields)
            {
                var box = view.FindName(name) as TextBox;
                True(box != null);
                var original = box!.Text;
                box.Text = invalid;
                True(!(bool)method!.Invoke(view, new object[] { false })!);
                box.Text = original;
            }
            True((bool)method!.Invoke(view, new object[] { false })!);
        }

        private static void VerifyRetirementDynamicInputValidation(RetirementView view)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var validate = typeof(RetirementView).GetMethod("TrySaveInputs", flags)!;
            object FirstEditor(string fieldName)
            {
                var editors = (System.Collections.IList)typeof(RetirementView).GetField(fieldName, flags)!.GetValue(view)!;
                return editors[0]!;
            }
            TextBox Box(object editor, string name) => (TextBox)editor.GetType().GetProperty(name)!.GetValue(editor)!;
            void Invalid(TextBox box, string value)
            {
                var original = box.Text;
                box.Text = value;
                True(!(bool)validate.Invoke(view, new object[] { false })!);
                box.Text = original;
            }

            var income = FirstEditor("_incomeEditors");
            Invalid(Box(income, "NameBox"), " ");
            Invalid(Box(income, "AmountBox"), "-1");

            var reserve = FirstEditor("_reserveEditors");
            Invalid(Box(reserve, "NameBox"), " ");
            Invalid(Box(reserve, "CurrentBox"), "-1");
            Invalid(Box(reserve, "TargetBox"), "-1");
            Invalid(Box(reserve, "CapBox"), "-1");
            Invalid(Box(reserve, "StartBox"), "1201");

            var deferred = (CheckBox)reserve.GetType().GetProperty("AfterRetirementGoalBox")!.GetValue(reserve)!;
            var start = Box(reserve, "StartBox");
            deferred.IsChecked = true;
            True(!start.IsEnabled);
            deferred.IsChecked = false;
            True(start.IsEnabled);
            True((bool)validate.Invoke(view, new object[] { false })!);
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

        private static void RetirementSettingsNormalizeLegacyAndPartialCollections()
        {
            var settings = new RetirementSettings
            {
                InitialInvestedUsd = 1000m,
                MonthlyIncomeUsd = 200m,
                OrdinaryMonthlyExpensesUsd = 50m,
                ExtraMonthlyExpensesUsd = 25m,
                TargetInvestedUsd = 2000m,
                StockAllocationPercentage = 30m,
                MonthlyIncomes = null!,
                Reserves = null!
            };
            True(settings.MigrateLegacyMoneyToCents());
            Equal(30000L, settings.InitialStocksCents);
            Equal(70000L, settings.InitialBondsCents);
            Equal(20000L, settings.MonthlyIncomeCents);
            Equal(5000L, settings.OrdinaryMonthlyExpensesCents);
            Equal(2500L, settings.ExtraMonthlyExpensesCents);
            Equal(200000L, settings.TargetInvestedCents);
            True(!settings.MigrateLegacyMoneyToCents());
            True(settings.EnsurePlanningCollections());
            Equal(20000L, settings.MonthlyIncomes[0].MonthlyAmountCents);
            Equal(3, settings.Reserves.Count);

            var partial = new RetirementSettings
            {
                MonthlyIncomes = new System.Collections.Generic.List<RetirementIncomeSettings>
                {
                    new RetirementIncomeSettings { Id = "", Name = " " }
                },
                Reserves = new System.Collections.Generic.List<RetirementReserveSettings>
                {
                    new RetirementReserveSettings { Id = "", Name = " " }
                }
            };
            True(partial.EnsurePlanningCollections());
            Equal(4, partial.Reserves.Count);
            True(!string.IsNullOrWhiteSpace(partial.MonthlyIncomes[0].Id));
            Equal("Ingreso", partial.MonthlyIncomes[0].Name);
            True(!string.IsNullOrWhiteSpace(partial.Reserves[0].Id));
            True(partial.Reserves.All(reserve => !string.IsNullOrWhiteSpace(reserve.Name)));
            True(!partial.EnsurePlanningCollections());
        }

        private static void RetirementRejectsInvalidAssumptionsAndSpendsLowerReturnFirst()
        {
            var settings = CreateRetirementSettings();
            settings.InitialStocksCents = 10000;
            settings.InitialBondsCents = 10000;
            settings.OrdinaryMonthlyExpensesCents = 1000;
            settings.StockAnnualReturnPercentage = 0m;
            settings.BondAnnualReturnPercentage = 10m;
            settings.TargetInvestedCents = 10000;
            settings.Reserves[0].CurrentCents = 10000;
            settings.Reserves[0].TargetCents = 10000;
            var calculator = new RetirementCalculator();
            var projection = calculator.Calculate(settings);
            Equal(0, projection.MonthsToTarget!.Value);
            Equal(0, projection.ReserveGoals[0].ReachedMonth!.Value);
            settings.Reserves[0].IsIncluded = false;
            var withoutReserves = calculator.Calculate(settings);
            True(withoutReserves.Runway.Points[1].StocksUsd < 100d);
            True(withoutReserves.Runway.Points[1].BondsUsd > 100d);

            void Reject(Action<RetirementSettings> mutate)
            {
                var invalid = CreateRetirementSettings();
                mutate(invalid);
                Throws<ArgumentException>(() => calculator.Calculate(invalid));
            }
            Reject(value => value.InitialStocksCents = -1);
            Reject(value => value.Reserves[0].TargetCents = -1);
            Reject(value => value.Reserves[0].MonthlyCapCents = -1);
            Reject(value => value.TargetInvestedCents = 0);
            Reject(value => value.TargetStocksCents = value.TargetInvestedCents + 1);
            Reject(value => value.ExtraExpenseMonths = 1201);
            Reject(value => value.Reserves[0].StartAfterMonths = 1201);
            Reject(value => value.Reserves[0].StartAfterMonths = -1);
            Reject(value => value.EmergencyRunwayTargetYears = 0);
            Reject(value => value.StockAnnualReturnPercentage = 101m);
            Reject(value => value.BondAnnualReturnPercentage = -100m);
            Reject(value => value.UsInflationPercentage = 101m);
            Reject(value => value.WithdrawalRatePercentage = 101m);
            var allocation = typeof(RetirementCalculator).GetMethod("AllocateReserve",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Near(5d, (double)allocation.Invoke(null, new object[] { 10d, 0d, 20d, 5d, 1, 0 })!);
            Near(10d, (double)allocation.Invoke(null, new object[] { 10d, 0d, 20d, 0d, 1, 0 })!);
            var temporaryExpenses = CreateRetirementSettings();
            temporaryExpenses.ExtraExpenseMonths = 1;
            temporaryExpenses.ExtraMonthlyExpensesCents = 100;
            temporaryExpenses.TargetInvestedCents = 100000000;
            True(calculator.Calculate(temporaryExpenses).MonthsToTarget == null);
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

        private static void RetirementCompletesDeferredReservesAfterGoal()
        {
            var settings = CreateRetirementSettings();
            settings.MonthlyIncomes[0].MonthlyAmountCents = 10000;
            settings.TargetInvestedCents = 20000;
            settings.TargetStocksCents = 0;
            settings.Reserves[0].Name = "Reserva posterior";
            settings.Reserves[0].TargetCents = 20000;
            settings.Reserves[0].StartAfterRetirementGoal = true;

            var projection = new RetirementCalculator().Calculate(settings);
            var reserve = projection.ReserveGoals.Single(goal => goal.Name == "Reserva posterior");

            Equal(2, projection.MonthsToTarget!.Value);
            Equal(4, reserve.ReachedMonth!.Value);
            Equal(4, projection.Points[projection.Points.Count - 1].Month);
            Near(200d, projection.FinalBondsRealUsd);
        }

        private static void RetirementHiddenReservesDoNotAffectCalculations()
        {
            var settings = CreateRetirementSettings();
            settings.MonthlyIncomes[0].MonthlyAmountCents = 10000;
            settings.TargetInvestedCents = 20000;
            var reserve = settings.Reserves[0];
            reserve.Name = "Objetivo temporal";
            reserve.CurrentCents = 5000;
            reserve.TargetCents = 10000;
            var calculator = new RetirementCalculator();

            var shown = calculator.Calculate(settings);
            Equal(3, shown.MonthsToTarget!.Value);
            Equal(1, shown.ReserveGoals.Count(goal => goal.TargetUsd > 0d));
            Near(50d, shown.Runway.InitialLiquidReservesUsd);

            reserve.IsIncluded = false;
            var hidden = calculator.Calculate(settings);
            Equal(2, hidden.MonthsToTarget!.Value);
            Equal(0, hidden.ReserveGoals.Count(goal => goal.TargetUsd > 0d));
            Near(0d, hidden.TotalReservedUsd);
            Near(0d, hidden.Runway.InitialLiquidReservesUsd);
            Near(0d, hidden.Runway.SustainableMonthlyExpenseUsd);
            Equal(3, settings.Reserves.Count);
            Equal(5000L, reserve.CurrentCents);
            Equal(10000L, reserve.TargetCents);

            reserve.IsIncluded = true;
            var restored = calculator.Calculate(settings);
            Equal(shown.MonthsToTarget, restored.MonthsToTarget);
            Near(shown.Runway.InitialLiquidReservesUsd, restored.Runway.InitialLiquidReservesUsd);
            Equal(1, restored.ReserveGoals.Count(goal => goal.TargetUsd > 0d));
        }

        private static void RetirementReserveVisibilityPersistsAndLegacyDefaultsToShown()
        {
            var reserve = new RetirementReserveSettings
            {
                Name = "Casa",
                CurrentCents = 12345,
                TargetCents = 2000000,
                IsIncluded = false
            };
            var restored = JsonSerializer.Deserialize<RetirementReserveSettings>(JsonSerializer.Serialize(reserve))!;
            True(!restored.IsIncluded);
            Equal(reserve.Name, restored.Name);
            Equal(reserve.CurrentCents, restored.CurrentCents);
            Equal(reserve.TargetCents, restored.TargetCents);

            var legacy = JsonSerializer.Deserialize<RetirementReserveSettings>("{\"Name\":\"Reserva anterior\"}")!;
            True(legacy.IsIncluded);
        }

        private static void RetirementChartsCaptureWheelAtMinimumZoom()
        {
            Exception? chartError = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var projectionChart = new RetirementProjectionChart();
                    var projectionWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = Mouse.MouseWheelEvent
                    };
                    projectionChart.RaiseEvent(projectionWheel);
                    True(projectionWheel.Handled);

                    var planningChart = new RetirementReserveTimelineChart();
                    var planningWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                    {
                        RoutedEvent = Mouse.MouseWheelEvent
                    };
                    planningChart.RaiseEvent(planningWheel);
                    True(planningWheel.Handled);
                }
                catch (Exception exception)
                {
                    chartError = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (chartError != null)
            {
                throw new InvalidOperationException("Los gráficos de Jubilación no retuvieron la rueda en el zoom mínimo: " + chartError.Message);
            }
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
            throw new InvalidOperationException("Se esperaba " + typeof(T).Name + ".");
        }
    }
}
