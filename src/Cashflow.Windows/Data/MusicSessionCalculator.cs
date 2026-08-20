using System;
using System.Collections.Generic;
using System.Linq;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public sealed class MusicSessionCalculator
    {
        private readonly TargetFundingCalculator _funding = new TargetFundingCalculator();

        public MusicSessionCalculation Calculate(ScenarioDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var settings = document.MusicSession;
            if (settings.TargetUsd <= 0m) throw new ArgumentOutOfRangeException(nameof(settings.TargetUsd));

            var result = new MusicSessionCalculation();
            var blueMid = settings.BlueBuy.HasValue && settings.BlueSell.HasValue
                ? (settings.BlueBuy.Value + settings.BlueSell.Value) / 2m
                : (decimal?)null;
            var directArsTarget = blueMid * settings.TargetUsd;
            var officialArsTarget = settings.OfficialPurchaseAvailable && settings.OfficialSell.HasValue
                ? settings.TargetUsd * settings.OfficialSell.Value * (1m + settings.OfficialPurchaseExtraPercentage / 100m)
                : (decimal?)null;

            foreach (var source in GetSources(document))
            {
                AddCashOption(document, settings, source, result);

                if (directArsTarget.HasValue)
                {
                    AddArsOption(source, directArsTarget.Value, "Pago directo en ARS", "Promedio compra/venta del dólar blue", result);
                }

                if (officialArsTarget.HasValue)
                {
                    AddArsOption(source, officialArsTarget.Value, "Recompra al oficial", "Compra USD al valor oficial de venta y retiro sin costo", result);
                }
            }

            if (!settings.BlueBuy.HasValue || !settings.BlueSell.HasValue)
            {
                result.Pending.Add("Falta la cotización blue de internet para calcular el pago directo en ARS.");
            }
            if (!settings.OfficialPurchaseAvailable)
            {
                result.Pending.Add("La recompra al oficial está desactivada. Habilitala solamente si tu banco confirma que podés acceder; no es un rulo repetible por la restricción posterior de 90 días.");
            }
            else if (!settings.OfficialSell.HasValue)
            {
                result.Pending.Add("Falta la cotización oficial de venta para calcular la recompra de dólares.");
            }
            if (!settings.BinanceUsdcTransferFee.HasValue)
            {
                result.Pending.Add("Completá manualmente el costo de envío USDC desde Binance para habilitar ese camino a efectivo.");
            }
            if (!settings.BinanceUsdtTransferFee.HasValue)
            {
                result.Pending.Add("Completá manualmente el costo de envío USDT desde Binance para habilitar ese camino a efectivo.");
            }

            result.Options = result.Options
                .OrderBy(option => option.SourceDebitAmount)
                .ThenBy(option => option.Method)
                .ToList();
            return result;
        }

        private void AddCashOption(
            ScenarioDocument document,
            MusicSessionSettings settings,
            MusicSource source,
            MusicSessionCalculation calculation)
        {
            var cashScenario = BuildCashScenario(source.Scenario, settings, out var cashNode);
            var cashSource = cashScenario.Nodes.First(node => node.Id == source.Node.Id);
            var candidates = _funding.Calculate(cashScenario, cashSource.Id, cashNode.Id, settings.TargetUsd);
            var best = candidates.FirstOrDefault();
            if (best == null)
            {
                calculation.Pending.Add($"{source.Label}: no hay un camino completo a efectivo con los datos cargados.");
                return;
            }

            calculation.Options.Add(CreateOption(
                source,
                best,
                "Efectivo vía stablecoin",
                $"Recibir USD {settings.TargetUsd:N2} en efectivo",
                null,
                MusicSessionCategory.WithoutArs,
                usesManualData: true));
        }

        private void AddArsOption(
            MusicSource source,
            decimal requiredArs,
            string method,
            string targetDetail,
            MusicSessionCalculation calculation)
        {
            var destination = source.Scenario.Nodes.FirstOrDefault(node =>
                node.Kind == NodeKind.Destination && node.Currency.Equals("ARS", StringComparison.OrdinalIgnoreCase));
            if (destination == null)
            {
                return;
            }

            var candidates = _funding.Calculate(source.Scenario, source.Node.Id, destination.Id, requiredArs);
            var best = candidates.FirstOrDefault();
            if (best == null)
            {
                calculation.Pending.Add($"{source.Label}: faltan cotizaciones de una ruta a ARS para “{method}”.");
                return;
            }

            calculation.Options.Add(CreateOption(
                source,
                best,
                method,
                targetDetail,
                requiredArs,
                MusicSessionCategory.BankedArs,
                best.Route.Steps.Any(step => step.Route.ExchangeRateIsManual)));
        }

        private static MusicSessionOption CreateOption(
            MusicSource source,
            TargetFundingResult funding,
            string method,
            string targetDetail,
            decimal? requiredArs,
            MusicSessionCategory category,
            bool usesManualData) =>
            new MusicSessionOption
            {
                Source = source.Label,
                SourceCurrency = source.Node.Currency,
                Method = method,
                TargetDetail = targetDetail,
                SourceDebitAmount = funding.SourceDebitAmount,
                RequiredArs = requiredArs,
                FinalAmount = funding.Route.FinalAmount,
                Path = string.Join("  ›  ", funding.Route.Steps.Select(step => step.Route.Label)),
                Category = category,
                UsesManualData = usesManualData
            };

        private static CashflowScenario BuildCashScenario(
            CashflowScenario original,
            MusicSessionSettings settings,
            out PlatformNode cashNode)
        {
            var scenario = new CashflowScenario { Name = original.Name + " · sesión musical" };
            scenario.Nodes.AddRange(original.Nodes);
            scenario.Routes.AddRange(original.Routes);
            cashNode = new PlatformNode
            {
                Id = "music-cash-" + Guid.NewGuid().ToString("N"),
                Name = "Efectivo para sesión",
                Currency = "USD",
                Kind = NodeKind.Destination
            };
            scenario.Nodes.Add(cashNode);

            var personMultiplier = 1m - settings.PersonFeePercentage / 100m;
            if (personMultiplier <= 0m)
            {
                return scenario;
            }

            foreach (var node in original.Nodes)
            {
                if (node.Name == "GrabrFi" && node.Currency == "USD")
                {
                    AddCashRoute(scenario, node, cashNode, "GrabrFi → persona · USDC", 0.5m, 1m, settings.CashUsdPerUsdc * personMultiplier, FeeApplicationMode.ChargeSeparately);
                    AddCashRoute(scenario, node, cashNode, "GrabrFi → persona · USDT", 0.6m, 1m, settings.CashUsdPerUsdt * personMultiplier, FeeApplicationMode.ChargeSeparately);
                }
                else if (node.Name == "Wallbit Pro" && node.Currency == "USD")
                {
                    AddCashRoute(scenario, node, cashNode, "Wallbit Pro → persona · USDC Polygon", 1m, 0m, settings.CashUsdPerUsdc * personMultiplier, FeeApplicationMode.ChargeSeparately);
                    AddCashRoute(scenario, node, cashNode, "Wallbit Pro → persona · USDT TRON", 1.25m, 0m, settings.CashUsdPerUsdt * personMultiplier, FeeApplicationMode.ChargeSeparately);
                }
                else if (node.Name == "Binance · USDC" && settings.BinanceUsdcTransferFee.HasValue)
                {
                    AddCashRoute(scenario, node, cashNode, "Binance → persona · envío USDC", 0m, settings.BinanceUsdcTransferFee.Value, settings.CashUsdPerUsdc * personMultiplier, FeeApplicationMode.DeductFromAmount);
                }
                else if (node.Name == "Binance · USDT" && settings.BinanceUsdtTransferFee.HasValue)
                {
                    AddCashRoute(scenario, node, cashNode, "Binance → persona · envío USDT", 0m, settings.BinanceUsdtTransferFee.Value, settings.CashUsdPerUsdt * personMultiplier, FeeApplicationMode.DeductFromAmount);
                }
            }

            return scenario;
        }

        private static void AddCashRoute(
            CashflowScenario scenario,
            PlatformNode from,
            PlatformNode to,
            string label,
            decimal percentageFee,
            decimal fixedFee,
            decimal exchangeRate,
            FeeApplicationMode feeApplication)
        {
            if (exchangeRate <= 0m)
            {
                return;
            }

            scenario.Routes.Add(new TransferRoute
            {
                FromNodeId = from.Id,
                ToNodeId = to.Id,
                Label = label,
                PercentageFee = percentageFee,
                FixedFee = fixedFee,
                FeeApplication = feeApplication,
                ExchangeRate = exchangeRate
            });
        }

        private static IReadOnlyList<MusicSource> GetSources(ScenarioDocument document)
        {
            var sources = new List<MusicSource>();
            var matches = document.Scenarios
                .Select(scenario => new
                {
                    Scenario = scenario,
                    Node = scenario.Nodes.FirstOrDefault(node =>
                        node.Name == "GrabrFi" && node.Currency == "USD")
                })
                .Where(match => match.Node != null)
                .OrderByDescending(match => match.Node!.Kind == NodeKind.Source)
                .ToArray();
            var selected = matches.FirstOrDefault();
            if (selected != null)
            {
                sources.Add(new MusicSource(selected.Scenario, selected.Node!, "GrabrFi"));
            }

            return sources;
        }

        private sealed class MusicSource
        {
            public MusicSource(CashflowScenario scenario, PlatformNode node, string label)
            {
                Scenario = scenario;
                Node = node;
                Label = label;
            }

            public CashflowScenario Scenario { get; }
            public PlatformNode Node { get; }
            public string Label { get; }
        }
    }

    public sealed class MusicSessionCalculation
    {
        public List<MusicSessionOption> Options { get; set; } = new List<MusicSessionOption>();
        public List<string> Pending { get; } = new List<string>();
    }

    public sealed class MusicSessionOption
    {
        public string Source { get; set; } = string.Empty;
        public string SourceCurrency { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public string TargetDetail { get; set; } = string.Empty;
        public decimal SourceDebitAmount { get; set; }
        public decimal? RequiredArs { get; set; }
        public decimal FinalAmount { get; set; }
        public string Path { get; set; } = string.Empty;
        public MusicSessionCategory Category { get; set; }
        public bool UsesManualData { get; set; }
    }

    public enum MusicSessionCategory
    {
        WithoutArs,
        BankedArs
    }
}
