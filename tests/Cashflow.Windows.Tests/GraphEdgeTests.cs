using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cashflow.Core.Models;
using Cashflow.Windows.Controls;

namespace Cashflow.Windows.Tests;

internal static class GraphEdgeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run()
    {
        var source = new PlatformNode { Id = "source", Name = "Origen", Currency = "USD", X = 20, Y = 100 };
        var target = new PlatformNode { Id = "target", Name = "Destino", Currency = "ARS", X = 500, Y = 100 };
        var route = new TransferRoute { Id = "route", FromNodeId = source.Id, ToNodeId = target.Id, Label = "Directo" };
        var scenario = new CashflowScenario();
        scenario.Nodes.Add(source);
        scenario.Nodes.Add(target);
        scenario.Routes.Add(route);
        var graph = new GraphCanvas { Scenario = scenario };
        graph.Measure(new Size(800, 400));
        graph.Arrange(new Rect(0, 0, 800, 400));
        Render(graph);

        var select = typeof(GraphCanvas).GetMethod("SelectAt", PrivateInstance)!;
        select.Invoke(graph, new object[] { new Point(source.X + 10, source.Y + 10) });
        Check(ReferenceEquals(typeof(GraphCanvas).GetField("_draggedNode", PrivateInstance)!.GetValue(graph), source));
        typeof(GraphCanvas).GetMethod("OnMouseLeftButtonUp", PrivateInstance)!.Invoke(graph,
            new object[] { new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent } });

        var segment = typeof(GraphCanvas).GetMethod("TryGetRouteSegment", PrivateInstance)!;
        object[] segmentArguments = { route, source, target, default(Point), default(Point), default(Vector) };
        Check((bool)segment.Invoke(graph, segmentArguments)!);
        var start = (Point)segmentArguments[3];
        var end = (Point)segmentArguments[4];
        var midpoint = new Point((start.X + end.X) / 2d, (start.Y + end.Y) / 2d);
        select.Invoke(graph, new object[] { midpoint });
        var findRoute = typeof(GraphCanvas).GetMethod("FindRoute", PrivateInstance)!;
        Check(ReferenceEquals(findRoute.Invoke(graph, new object[] { midpoint }), route));
        Check(findRoute.Invoke(graph, new object[] { new Point(790, 390) }) == null);
        scenario.Routes.Add(new TransferRoute { FromNodeId = source.Id, ToNodeId = source.Id });
        Check(findRoute.Invoke(graph, new object[] { new Point(790, 390) }) == null);

        var summary = typeof(GraphCanvas).GetMethod("BuildRouteSummary", PrivateStatic)!;
        var variant = new TransferRoute { PercentageFeeMinimum = 1m };
        Check(((string)summary.Invoke(null, new object[] { variant })!).Contains("∞", StringComparison.Ordinal));
        variant.PercentageFeeMinimum = null;
        variant.PercentageFeeMaximum = 5m;
        Check(((string)summary.Invoke(null, new object[] { variant })!).Contains("0–5", StringComparison.Ordinal));
        variant.PercentageFeeMaximum = null;
        variant.OutputPercentageFee = 1m;
        Check(((string)summary.Invoke(null, new object[] { variant })!).Contains("salida", StringComparison.Ordinal));
        variant.FixedFee = 1m;
        Check(((string)summary.Invoke(null, new object[] { variant })!).Contains(" + salida", StringComparison.Ordinal));
    }

    private static void Render(GraphCanvas graph)
    {
        var bitmap = new RenderTargetBitmap(800, 400, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        Check(bitmap.PixelWidth == 800);
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Falló una selección o etiqueta del grafo.");
    }
}
