using System;
using System.Collections.Generic;
using System.Linq;
using Cashflow.Core.Models;

namespace Cashflow.Windows.Data
{
    public static class ManualExchangeRateSynchronizer
    {
        public static bool EnsureSynchronized(ScenarioDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.ManualExchangeRates ??= new List<ManualExchangeRateSetting>();
            var changed = false;

            var manualRoutes = EnumerateManualRoutes(document).ToArray();
            foreach (var item in manualRoutes)
            {
                var key = string.IsNullOrWhiteSpace(item.Route.ManualExchangeRateKey)
                    ? CreateKey(item.From.Name, item.From.Currency, item.To.Currency)
                    : item.Route.ManualExchangeRateKey!;
                if (item.Route.ManualExchangeRateKey != key)
                {
                    item.Route.ManualExchangeRateKey = key;
                    changed = true;
                }
            }

            manualRoutes = EnumerateManualRoutes(document).ToArray();
            foreach (var group in manualRoutes.GroupBy(item => item.Route.ManualExchangeRateKey!))
            {
                var key = group.Key;
                var setting = document.ManualExchangeRates.FirstOrDefault(candidate => candidate.Key == key);
                if (setting == null)
                {
                    var candidates = group
                        .Where(candidate => candidate.Route.ExchangeRateConfigured && candidate.Route.ExchangeRate > 0m)
                        .OrderByDescending(candidate => candidate.Route.ManualExchangeRateUpdatedAt.HasValue)
                        .ThenByDescending(candidate => candidate.Route.ManualExchangeRateUpdatedAt)
                        .ToArray();
                    if (candidates.Length == 0)
                    {
                        continue;
                    }
                    var selected = candidates.Any(candidate => candidate.Route.ManualExchangeRateUpdatedAt.HasValue)
                        ? candidates.First()
                        : candidates.FirstOrDefault(candidate => candidate.Scenario.Id == document.ActiveScenarioId) ?? candidates.First();
                    setting = new ManualExchangeRateSetting
                    {
                        Key = key,
                        ProviderName = selected.From.Name,
                        FromCurrency = selected.From.Currency.ToUpperInvariant(),
                        ToCurrency = selected.To.Currency.ToUpperInvariant(),
                        ExchangeRate = selected.Route.ExchangeRate,
                        UpdatedAt = selected.Route.ManualExchangeRateUpdatedAt
                    };
                    document.ManualExchangeRates.Add(setting);
                    changed = true;
                }
            }

            foreach (var setting in document.ManualExchangeRates)
            {
                foreach (var item in manualRoutes.Where(item => item.Route.ManualExchangeRateKey == setting.Key))
                {
                    if (item.Route.ExchangeRate != setting.ExchangeRate ||
                        item.Route.ManualExchangeRateUpdatedAt != setting.UpdatedAt ||
                        !item.Route.ExchangeRateConfigured)
                    {
                        item.Route.ExchangeRate = setting.ExchangeRate;
                        item.Route.ExchangeRateConfigured = true;
                        item.Route.ManualExchangeRateUpdatedAt = setting.UpdatedAt;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        public static void MarkAndApply(
            ScenarioDocument document,
            TransferRoute route,
            PlatformNode from,
            PlatformNode to,
            decimal exchangeRate,
            DateTimeOffset updatedAt)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (route == null) throw new ArgumentNullException(nameof(route));
            route.ExchangeRateIsManual = true;
            route.ManualExchangeRateKey = CreateKey(from.Name, from.Currency, to.Currency);
            Apply(document, route.ManualExchangeRateKey, from.Name, from.Currency, to.Currency, exchangeRate, updatedAt);
        }

        public static void Apply(
            ScenarioDocument document,
            string key,
            string providerName,
            string fromCurrency,
            string toCurrency,
            decimal exchangeRate,
            DateTimeOffset updatedAt)
        {
            if (exchangeRate <= 0m) throw new ArgumentOutOfRangeException(nameof(exchangeRate));
            document.ManualExchangeRates ??= new List<ManualExchangeRateSetting>();
            var setting = document.ManualExchangeRates.FirstOrDefault(candidate => candidate.Key == key);
            if (setting == null)
            {
                setting = new ManualExchangeRateSetting { Key = key };
                document.ManualExchangeRates.Add(setting);
            }

            setting.ProviderName = providerName;
            setting.FromCurrency = fromCurrency.ToUpperInvariant();
            setting.ToCurrency = toCurrency.ToUpperInvariant();
            setting.ExchangeRate = exchangeRate;
            setting.UpdatedAt = updatedAt;

            foreach (var item in EnumerateManualRoutes(document).Where(item => item.Route.ManualExchangeRateKey == key))
            {
                item.Route.ExchangeRate = exchangeRate;
                item.Route.ExchangeRateConfigured = true;
                item.Route.ManualExchangeRateUpdatedAt = updatedAt;
            }
        }

        public static string CreateKey(string providerName, string fromCurrency, string toCurrency)
        {
            var normalizedProvider = new string((providerName ?? string.Empty)
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
            return $"manual:{normalizedProvider}:{fromCurrency.ToLowerInvariant()}:{toCurrency.ToLowerInvariant()}";
        }

        private static IEnumerable<ManualRoute> EnumerateManualRoutes(ScenarioDocument document)
        {
            foreach (var scenario in document.Scenarios)
            {
                foreach (var route in scenario.Routes.Where(route => route.ExchangeRateIsManual))
                {
                    var from = scenario.Nodes.FirstOrDefault(node => node.Id == route.FromNodeId);
                    var to = scenario.Nodes.FirstOrDefault(node => node.Id == route.ToNodeId);
                    if (from != null && to != null)
                    {
                        yield return new ManualRoute(scenario, route, from, to);
                    }
                }
            }
        }

        private sealed class ManualRoute
        {
            public ManualRoute(CashflowScenario scenario, TransferRoute route, PlatformNode from, PlatformNode to)
            {
                Scenario = scenario;
                Route = route;
                From = from;
                To = to;
            }

            public CashflowScenario Scenario { get; }
            public TransferRoute Route { get; }
            public PlatformNode From { get; }
            public PlatformNode To { get; }
        }
    }
}
