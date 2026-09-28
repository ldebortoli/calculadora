using System;
using System.Linq;
using Cashflow.Core.Models;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class ManualRateEdgeTests
{
    public static void Run()
    {
        Throws<ArgumentNullException>(() => ManualExchangeRateSynchronizer.EnsureSynchronized(null!));
        var document = StarterScenarioFactory.CreateStarterDocument();
        var scenario = document.Scenarios[0];
        var manualRoute = scenario.Routes.First(route => route.ExchangeRateIsManual);
        var from = scenario.Nodes.First(node => node.Id == manualRoute.FromNodeId);
        var to = scenario.Nodes.First(node => node.Id == manualRoute.ToNodeId);
        document.ManualExchangeRates = null!;
        ManualExchangeRateSynchronizer.EnsureSynchronized(document);
        Check(document.ManualExchangeRates != null);

        Throws<ArgumentNullException>(() => ManualExchangeRateSynchronizer.MarkAndApply(
            null!, manualRoute, from, to, 100m, DateTimeOffset.Now));
        Throws<ArgumentNullException>(() => ManualExchangeRateSynchronizer.MarkAndApply(
            document, null!, from, to, 100m, DateTimeOffset.Now));
        Throws<ArgumentOutOfRangeException>(() => ManualExchangeRateSynchronizer.Apply(
            document, "manual:key:usd:ars", "Cuenta", "USD", "ARS", 0m, DateTimeOffset.Now));
        document.ManualExchangeRates = null!;
        ManualExchangeRateSynchronizer.Apply(document, "manual:otra:usd:ars", "Otra", "USD", "ARS", 100m, DateTimeOffset.Now);
        Check(document.ManualExchangeRates.Count == 1);
        Check(ManualExchangeRateSynchronizer.CreateKey(null!, "USD", "ARS") == "manual::usd:ars");

        document = StarterScenarioFactory.CreateStarterDocument();
        var setting = document.ManualExchangeRates.First();
        var linked = document.Scenarios.SelectMany(item => item.Routes)
            .First(route => route.ManualExchangeRateKey == setting.Key);
        linked.ExchangeRate = setting.ExchangeRate;
        linked.ExchangeRateConfigured = true;
        linked.ManualExchangeRateUpdatedAt = null;
        setting.UpdatedAt = DateTimeOffset.Now;
        Check(ManualExchangeRateSynchronizer.EnsureSynchronized(document));
        Check(linked.ManualExchangeRateUpdatedAt == setting.UpdatedAt);

        document = StarterScenarioFactory.CreateStarterDocument();
        document.ManualExchangeRates.Clear();
        document.ActiveScenarioId = "ausente";
        foreach (var route in document.Scenarios.SelectMany(item => item.Routes).Where(route => route.ExchangeRateIsManual))
            route.ManualExchangeRateUpdatedAt = null;
        Check(ManualExchangeRateSynchronizer.EnsureSynchronized(document));
        Check(document.ManualExchangeRates.Count > 0);
        document = StarterScenarioFactory.CreateStarterDocument();
        document.ManualExchangeRates.Clear();
        document.Scenarios[0].Routes.First(route => route.ExchangeRateIsManual).ManualExchangeRateUpdatedAt = DateTimeOffset.Now;
        Check(ManualExchangeRateSynchronizer.EnsureSynchronized(document));
        Check(document.ManualExchangeRates.Any(setting => setting.UpdatedAt.HasValue));
        document = StarterScenarioFactory.CreateStarterDocument();
        document.ManualExchangeRates.Clear();
        document.ActiveScenarioId = document.Scenarios[0].Id;
        foreach (var route in document.Scenarios.SelectMany(item => item.Routes).Where(route => route.ExchangeRateIsManual))
            route.ManualExchangeRateUpdatedAt = null;
        Check(ManualExchangeRateSynchronizer.EnsureSynchronized(document));
        Check(document.ManualExchangeRates.Count > 0);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Se esperaba " + typeof(T).Name + ".");
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("La cotización manual dejó de sincronizarse.");
    }
}
