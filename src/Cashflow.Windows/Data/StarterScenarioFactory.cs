using System;
using System.Linq;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public static class StarterScenarioFactory
    {
        public const int CurrentDocumentVersion = 9;
        private const string LegacyBinanceSellUsdtForUsdc = "binance-spot-sell-usdt-usdc";

        public static CashflowScenario CreateDemo() =>
            CreateGrabrFiTemplate("GrabrFi · circuito completo");

        public static ScenarioDocument CreateStarterDocument()
        {
            var grabrFi = CreateDemo();
            var document = new ScenarioDocument
            {
                Version = CurrentDocumentVersion,
                ActiveScenarioId = grabrFi.Id,
                Scenarios =
                {
                    grabrFi,
                    CreateWallbitTemplate("Wallbit Pro · circuito completo")
                }
            };
            ManualExchangeRateSynchronizer.EnsureSynchronized(document);
            return document;
        }

        public static CashflowScenario CreateGrabrFiTemplate(string name) =>
            CreateCompleteTemplate(name, startAtWallbit: false);

        public static CashflowScenario CreateWallbitTemplate(string name) =>
            CreateCompleteTemplate(name, startAtWallbit: true);

        public static bool UpgradeStarterTemplates(ScenarioDocument document)
        {
            if (document.Version >= CurrentDocumentVersion)
            {
                return false;
            }

            if (document.Version < 2)
            {
                var previousIndex = document.Scenarios.FindIndex(scenario =>
                    IsPreviousGrabrFiTemplate(scenario) || IsUntouchedLegacyDemo(scenario));
                if (previousIndex >= 0)
                {
                    var previousId = document.Scenarios[previousIndex].Id;
                    var replacement = CreateDemo();
                    replacement.Id = previousId;
                    document.Scenarios[previousIndex] = replacement;
                }
                else if (!document.Scenarios.Any(HasCompleteGrabrFiCircuit))
                {
                    document.Scenarios.Add(CreateDemo());
                }

                if (!document.Scenarios.Any(HasWallbitSource))
                {
                    document.Scenarios.Add(CreateWallbitTemplate("Wallbit Pro · circuito completo"));
                }
            }

            if (document.Version < 3)
            {
                foreach (var scenario in document.Scenarios)
                {
                    UpgradeBinanceArsRoutes(scenario);
                }
            }

            if (document.Version < 4)
            {
                foreach (var scenario in document.Scenarios)
                {
                    UpgradeBinanceTradingRules(scenario);
                }
            }

            if (document.Version < 7)
            {
                foreach (var scenario in document.Scenarios)
                {
                    ConfigureInitialManualExchangeRates(scenario);
                }
            }

            if (document.Version < 8)
            {
                foreach (var scenario in document.Scenarios)
                {
                    RemoveLegacyUsdtToUsdcRoute(scenario);
                }
            }

            if (document.Version < 9)
            {
                ManualExchangeRateSynchronizer.EnsureSynchronized(document);
            }

            document.Version = CurrentDocumentVersion;
            return true;
        }

        private static CashflowScenario CreateCompleteTemplate(string name, bool startAtWallbit)
        {
            var grabrFi = CreateNode("GrabrFi", "USD", startAtWallbit ? NodeKind.Intermediate : NodeKind.Source, startAtWallbit ? 245 : 25, startAtWallbit ? 55 : 210);
            var wallbit = CreateNode("Wallbit Pro", "USD", startAtWallbit ? NodeKind.Source : NodeKind.Intermediate, startAtWallbit ? 25 : 245, startAtWallbit ? 210 : 55);
            var binanceUsdc = CreateNode("Binance · USDC", "USDC", NodeKind.Intermediate, 245, 220);
            var binanceUsdt = CreateNode("Binance · USDT", "USDT", NodeKind.Intermediate, 245, 385);
            var destination = CreateNode("Cuenta local", "ARS", NodeKind.Destination, 485, 210);

            var scenario = new CashflowScenario { Name = name };
            scenario.Nodes.Add(startAtWallbit ? wallbit : grabrFi);
            scenario.Nodes.Add(startAtWallbit ? grabrFi : wallbit);
            scenario.Nodes.Add(binanceUsdc);
            scenario.Nodes.Add(binanceUsdt);
            scenario.Nodes.Add(destination);

            AddDirectLocalRoutes(scenario, grabrFi, wallbit, destination);
            AddStablecoinRoutes(scenario, grabrFi, wallbit, binanceUsdc, binanceUsdt);
            AddBinanceRoutes(scenario, binanceUsdc, binanceUsdt, destination);
            scenario.Routes.Add(startAtWallbit
                ? CreateWallbitAchRoute(wallbit, grabrFi)
                : CreateGrabrFiAchRoute(grabrFi, wallbit));

            return scenario;
        }

        private static void AddDirectLocalRoutes(
            CashflowScenario scenario,
            PlatformNode grabrFi,
            PlatformNode wallbit,
            PlatformNode destination)
        {
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = grabrFi.Id,
                ToNodeId = destination.Id,
                Label = "GrabrFi → ARS · cotización manual",
                FixedFee = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1537.87m,
                ExchangeRateIsManual = true,
                ManualExchangeRateKey = ManualExchangeRateSynchronizer.CreateKey("GrabrFi", "USD", "ARS")
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = wallbit.Id,
                ToNodeId = destination.Id,
                Label = "Wallbit Pro → ARS · cotización manual",
                FeeApplication = FeeApplicationMode.ChargeSeparately,
                ExchangeRate = 1562.15m,
                ExchangeRateIsManual = true,
                ManualExchangeRateKey = ManualExchangeRateSynchronizer.CreateKey("Wallbit Pro", "USD", "ARS")
            });
        }

        private static void AddStablecoinRoutes(
            CashflowScenario scenario,
            PlatformNode grabrFi,
            PlatformNode wallbit,
            PlatformNode binanceUsdc,
            PlatformNode binanceUsdt)
        {
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = grabrFi.Id,
                ToNodeId = binanceUsdc.Id,
                Label = "GrabrFi → Binance · USDC",
                PercentageFee = 0.5m,
                FixedFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = grabrFi.Id,
                ToNodeId = binanceUsdt.Id,
                Label = "GrabrFi → Binance · USDT",
                PercentageFee = 0.6m,
                FixedFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = wallbit.Id,
                ToNodeId = binanceUsdc.Id,
                Label = "Wallbit Pro → Binance · USDC Polygon",
                PercentageFee = 1m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = wallbit.Id,
                ToNodeId = binanceUsdt.Id,
                Label = "Wallbit Pro → Binance · USDT TRON",
                PercentageFee = 1.25m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            });
        }

        private static void AddBinanceRoutes(
            CashflowScenario scenario,
            PlatformNode binanceUsdc,
            PlatformNode binanceUsdt,
            PlatformNode destination)
        {
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = binanceUsdt.Id,
                ToNodeId = destination.Id,
                Label = "Binance Spot · USDT → ARS",
                TradingFeePercentage = 0.1m,
                OutputPercentageFee = 1m,
                InputAmountStep = 1m,
                MinimumInputAmount = 1m,
                MinimumOutputAmount = 2000m,
                FeeApplication = FeeApplicationMode.DeductFromAmount,
                ExchangeRateConfigured = false,
                LiveQuoteKey = MarketQuoteKeys.BinanceSellUsdtForArs
            });
            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = binanceUsdc.Id,
                ToNodeId = binanceUsdt.Id,
                Label = "Binance Spot · USDC → USDT",
                TradingFeePercentage = 0.095m,
                InputAmountStep = 1m,
                MinimumInputAmount = 1m,
                MinimumOutputAmount = 5m,
                ExchangeRateConfigured = false,
                LiveQuoteKey = MarketQuoteKeys.BinanceSellUsdcForUsdt
            });
        }

        private static void UpgradeBinanceArsRoutes(CashflowScenario scenario)
        {
            scenario.Routes.RemoveAll(route => route.Label.StartsWith("Binance P2P ·", StringComparison.Ordinal));

            var binanceUsdt = scenario.Nodes.FirstOrDefault(node => node.Name == "Binance · USDT" && node.Currency == "USDT");
            var destination = scenario.Nodes.FirstOrDefault(node => node.Name == "Cuenta local" && node.Currency == "ARS");
            if (binanceUsdt == null || destination == null)
            {
                return;
            }

            var route = scenario.Routes.FirstOrDefault(candidate =>
                candidate.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs ||
                candidate.Label == "Binance Spot · USDT → ARS");
            if (route == null)
            {
                scenario.Routes.Add(new TransferRoute
                {
                    FromNodeId = binanceUsdt.Id,
                    ToNodeId = destination.Id,
                    Label = "Binance Spot · USDT → ARS",
                    PercentageFee = 1m,
                    FeeApplication = FeeApplicationMode.DeductFromAmount,
                    ExchangeRateConfigured = false,
                    LiveQuoteKey = MarketQuoteKeys.BinanceSellUsdtForArs
                });
                return;
            }

            route.FromNodeId = binanceUsdt.Id;
            route.ToNodeId = destination.Id;
            route.Label = "Binance Spot · USDT → ARS";
            route.PercentageFee = 1m;
            route.FixedFee = 0m;
            route.FeeApplication = FeeApplicationMode.DeductFromAmount;
            route.ExchangeRateConfigured = false;
            route.LiveQuoteKey = MarketQuoteKeys.BinanceSellUsdtForArs;
        }

        private static void UpgradeBinanceTradingRules(CashflowScenario scenario)
        {
            foreach (var route in scenario.Routes)
            {
                if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt)
                {
                    route.TradingFeePercentage = 0.095m;
                    route.InputAmountStep = 1m;
                    route.MinimumInputAmount = 1m;
                    route.MinimumOutputAmount = 5m;
                }
                else if (route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs)
                {
                    route.PercentageFee = 0m;
                    route.PercentageFeeMinimum = null;
                    route.PercentageFeeMaximum = null;
                    route.FixedFee = 0m;
                    route.TradingFeePercentage = 0.1m;
                    route.OutputPercentageFee = 1m;
                    route.InputAmountStep = 1m;
                    route.MinimumInputAmount = 1m;
                    route.MinimumOutputAmount = 2000m;
                    route.FeeApplication = FeeApplicationMode.DeductFromAmount;
                }
            }
        }

        private static void RemoveLegacyUsdtToUsdcRoute(CashflowScenario scenario) =>
            scenario.Routes.RemoveAll(route =>
                route.LiveQuoteKey == LegacyBinanceSellUsdtForUsdc ||
                route.Label.Equals("Binance Spot · USDT → USDC", StringComparison.Ordinal));

        private static void ConfigureInitialManualExchangeRates(CashflowScenario scenario)
        {
            foreach (var route in scenario.Routes)
            {
                route.ExchangeRateIsManual = string.IsNullOrWhiteSpace(route.LiveQuoteKey) &&
                    route.Label.IndexOf("cotización manual", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!route.ExchangeRateIsManual)
                {
                    route.ManualExchangeRateKey = null;
                    route.ManualExchangeRateUpdatedAt = null;
                }
            }
        }

        private static TransferRoute CreateGrabrFiAchRoute(PlatformNode grabrFi, PlatformNode wallbit) =>
            new TransferRoute
            {
                FromNodeId = grabrFi.Id,
                ToNodeId = wallbit.Id,
                Label = "GrabrFi → Wallbit Pro · ACH",
                PercentageFee = 0.3m,
                PercentageFeeMinimum = 1m,
                PercentageFeeMaximum = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            };

        private static TransferRoute CreateWallbitAchRoute(PlatformNode wallbit, PlatformNode grabrFi) =>
            new TransferRoute
            {
                FromNodeId = wallbit.Id,
                ToNodeId = grabrFi.Id,
                Label = "Wallbit Pro → GrabrFi · ACH",
                PercentageFee = 0.5m,
                PercentageFeeMinimum = 5m,
                FeeApplication = FeeApplicationMode.ChargeSeparately
            };

        private static PlatformNode CreateNode(string name, string currency, NodeKind kind, double x, double y) =>
            new PlatformNode
            {
                Name = name,
                Currency = currency,
                Kind = kind,
                X = x,
                Y = y
            };

        private static bool HasCompleteGrabrFiCircuit(CashflowScenario scenario) =>
            scenario.Nodes.Any(node => node.Name == "GrabrFi" && node.Kind == NodeKind.Source) &&
            scenario.Nodes.Any(node => node.Name == "Wallbit Pro") &&
            scenario.Nodes.Any(node => node.Name == "Binance · USDT");

        private static bool HasWallbitSource(CashflowScenario scenario) =>
            scenario.Nodes.Any(node => node.Name == "Wallbit Pro" && node.Kind == NodeKind.Source);

        private static bool IsPreviousGrabrFiTemplate(CashflowScenario scenario)
        {
            if (!string.Equals(scenario.Name, "GrabrFi · tarifas configuradas", StringComparison.Ordinal) ||
                scenario.Nodes.Count != 5 || scenario.Routes.Count != 4)
            {
                return false;
            }

            return scenario.Nodes.Any(node => node.Name == "GrabrFi" && node.Currency == "USD" && node.Kind == NodeKind.Source) &&
                   scenario.Nodes.Any(node => node.Name == "Cuenta bancaria (ACH)" && node.Currency == "USD") &&
                   scenario.Nodes.Any(node => node.Name == "Wallet USDC" && node.Currency == "USDC") &&
                   scenario.Nodes.Any(node => node.Name == "Wallet USDT" && node.Currency == "USDT") &&
                   scenario.Nodes.Any(node => node.Name == "Cuenta local" && node.Currency == "ARS") &&
                   scenario.Routes.Any(route => route.Label == "Retiro directo a pesos · cotización manual") &&
                   scenario.Routes.Any(route => route.Label == "Transferencia ACH") &&
                   scenario.Routes.Any(route => route.Label == "Transferencia en USDC") &&
                   scenario.Routes.Any(route => route.Label == "Transferencia en USDT");
        }

        private static bool IsUntouchedLegacyDemo(CashflowScenario scenario)
        {
            if (!string.Equals(scenario.Name, "GrabrFi · ejemplo editable", StringComparison.Ordinal) ||
                scenario.Nodes.Count != 3 || scenario.Routes.Count != 3)
            {
                return false;
            }

            var source = scenario.Nodes.SingleOrDefault(node =>
                node.Name == "GrabrFi" && node.Currency == "USD" && node.Kind == NodeKind.Source);
            var provider = scenario.Nodes.SingleOrDefault(node =>
                node.Name == "Proveedor intermedio" && node.Currency == "USD" && node.Kind == NodeKind.Intermediate);
            var destination = scenario.Nodes.SingleOrDefault(node =>
                node.Name == "Cuenta local" && node.Currency == "ARS" && node.Kind == NodeKind.Destination);
            if (source == null || provider == null || destination == null)
            {
                return false;
            }

            return scenario.Routes.Any(route =>
                       route.FromNodeId == source.Id && route.ToNodeId == destination.Id &&
                       route.Label == "Ejemplo directo" && route.PercentageFee == 4m &&
                       route.FixedFee == 0m && route.ExchangeRate == 1000m) &&
                   scenario.Routes.Any(route =>
                       route.FromNodeId == source.Id && route.ToNodeId == provider.Id &&
                       route.Label == "Entrada al proveedor" && route.PercentageFee == 1m &&
                       route.FixedFee == 1m && route.ExchangeRate == 1m) &&
                   scenario.Routes.Any(route =>
                       route.FromNodeId == provider.Id && route.ToNodeId == destination.Id &&
                       route.Label == "Salida a cuenta local" && route.PercentageFee == 1m &&
                       route.FixedFee == 2m && route.ExchangeRate == 1000m);
        }

        public static CashflowScenario CreateEmpty(string name)
        {
            var source = new PlatformNode
            {
                Name = "Origen",
                Currency = "USD",
                Kind = NodeKind.Source,
                X = 75,
                Y = 175
            };
            var destination = new PlatformNode
            {
                Name = "Destino local",
                Currency = "ARS",
                Kind = NodeKind.Destination,
                X = 515,
                Y = 175
            };

            var scenario = new CashflowScenario { Name = name };
            scenario.Nodes.Add(source);
            scenario.Nodes.Add(destination);
            return scenario;
        }
    }
}
