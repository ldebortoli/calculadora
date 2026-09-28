using System;
using System.Reflection;
using System.Text.Json;
using Cashflow.Core.Models;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class MigrationBoundaryTests
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run()
    {
        var previous = new CashflowScenario { Name = "GrabrFi · tarifas configuradas" };
        previous.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
        previous.Nodes.Add(new PlatformNode { Id = "ach", Name = "Cuenta bancaria (ACH)", Currency = "USD" });
        previous.Nodes.Add(new PlatformNode { Id = "usdc", Name = "Wallet USDC", Currency = "USDC" });
        previous.Nodes.Add(new PlatformNode { Id = "usdt", Name = "Wallet USDT", Currency = "USDT" });
        previous.Nodes.Add(new PlatformNode { Id = "ars", Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
        foreach (var label in new[] { "Retiro directo a pesos · cotización manual", "Transferencia ACH", "Transferencia en USDC", "Transferencia en USDT" })
            previous.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "ars", Label = label });
        var isPrevious = typeof(StarterScenarioFactory).GetMethod("IsPreviousGrabrFiTemplate", PrivateStatic)!;
        Check((bool)isPrevious.Invoke(null, new object[] { previous })!);
        foreach (var change in new Action<CashflowScenario>[]
        {
            scenario => scenario.Nodes[0].Name = "Cuenta propia",
            scenario => scenario.Nodes[0].Currency = "USDC",
            scenario => scenario.Nodes[0].Kind = NodeKind.Intermediate,
            scenario => scenario.Nodes[1].Name = "ACH editado",
            scenario => scenario.Nodes[1].Currency = "ARS",
            scenario => scenario.Nodes[2].Name = "USDC editado",
            scenario => scenario.Nodes[2].Currency = "USD",
            scenario => scenario.Nodes[3].Name = "USDT editado",
            scenario => scenario.Nodes[3].Currency = "USD",
            scenario => scenario.Nodes[4].Name = "Destino editado",
            scenario => scenario.Nodes[4].Currency = "USD",
            scenario => scenario.Routes[0].Label = "Retiro editado",
            scenario => scenario.Routes[1].Label = "ACH editado",
            scenario => scenario.Routes[2].Label = "USDC editado",
            scenario => scenario.Routes[3].Label = "USDT editado"
        })
        {
            var edited = Copy(previous);
            change(edited);
            Check(!(bool)isPrevious.Invoke(null, new object[] { edited })!);
        }

        var legacy = new CashflowScenario { Name = "GrabrFi · ejemplo editable" };
        legacy.Nodes.Add(new PlatformNode { Id = "source", Name = "GrabrFi", Currency = "USD", Kind = NodeKind.Source });
        legacy.Nodes.Add(new PlatformNode { Id = "provider", Name = "Proveedor intermedio", Currency = "USD", Kind = NodeKind.Intermediate });
        legacy.Nodes.Add(new PlatformNode { Id = "ars", Name = "Cuenta local", Currency = "ARS", Kind = NodeKind.Destination });
        legacy.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "ars", Label = "Ejemplo directo", PercentageFee = 4m, ExchangeRate = 1000m });
        legacy.Routes.Add(new TransferRoute { FromNodeId = "source", ToNodeId = "provider", Label = "Entrada al proveedor", PercentageFee = 1m, FixedFee = 1m, ExchangeRate = 1m });
        legacy.Routes.Add(new TransferRoute { FromNodeId = "provider", ToNodeId = "ars", Label = "Salida a cuenta local", PercentageFee = 1m, FixedFee = 2m, ExchangeRate = 1000m });
        var isLegacy = typeof(StarterScenarioFactory).GetMethod("IsUntouchedLegacyDemo", PrivateStatic)!;
        Check((bool)isLegacy.Invoke(null, new object[] { legacy })!);
        foreach (var change in new Action<CashflowScenario>[]
        {
            scenario => scenario.Nodes[0].Name = "Otro origen",
            scenario => scenario.Nodes[1].Name = "Otro intermediario",
            scenario => scenario.Nodes[2].Currency = "USD",
            scenario => scenario.Routes[0].FromNodeId = "provider",
            scenario => scenario.Routes[0].ToNodeId = "provider",
            scenario => scenario.Routes[0].Label = "Directo editado",
            scenario => scenario.Routes[0].PercentageFee = 3m,
            scenario => scenario.Routes[0].FixedFee = 1m,
            scenario => scenario.Routes[0].ExchangeRate = 1100m,
            scenario => scenario.Routes[1].FromNodeId = "provider",
            scenario => scenario.Routes[1].ToNodeId = "ars",
            scenario => scenario.Routes[1].Label = "Entrada editada",
            scenario => scenario.Routes[1].PercentageFee = 2m,
            scenario => scenario.Routes[1].FixedFee = 2m,
            scenario => scenario.Routes[1].ExchangeRate = 2m,
            scenario => scenario.Routes[2].FromNodeId = "source",
            scenario => scenario.Routes[2].ToNodeId = "provider",
            scenario => scenario.Routes[2].Label = "Salida editada",
            scenario => scenario.Routes[2].PercentageFee = 2m,
            scenario => scenario.Routes[2].FixedFee = 3m,
            scenario => scenario.Routes[2].ExchangeRate = 1100m
        })
        {
            var edited = Copy(legacy);
            change(edited);
            Check(!(bool)isLegacy.Invoke(null, new object[] { edited })!);
        }
    }

    private static CashflowScenario Copy(CashflowScenario scenario) =>
        JsonSerializer.Deserialize<CashflowScenario>(JsonSerializer.Serialize(scenario))!;

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Una plantilla personalizada se reconoció como versión histórica intacta.");
    }
}
