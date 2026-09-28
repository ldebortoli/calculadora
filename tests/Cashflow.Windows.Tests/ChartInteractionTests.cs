using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class ChartInteractionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(
        RetirementProjectionChart projectionChart,
        RetirementReserveTimelineChart reserveChart,
        RetirementRunwayChart runwayChart,
        MusicSessionGraphCanvas musicChart,
        RetirementProjection projection)
    {
        ProjectionInteractions(projectionChart, projection);
        PlanningInteractions(reserveChart, runwayChart);
        MusicInteractions(musicChart);
    }

    private static void ProjectionInteractions(RetirementProjectionChart chart, RetirementProjection projection)
    {
        var selection = typeof(RetirementProjectionChart).GetMethod("SelectAt", PrivateInstance)!;
        var selectedField = typeof(RetirementProjectionChart).GetField("_selectedPoint", PrivateInstance)!;
        selectedField.SetValue(chart, null);
        var zoomField = typeof(RetirementProjectionChart).GetField("_zoom", PrivateInstance)!;
        var panField = typeof(RetirementProjectionChart).GetField("_panOffset", PrivateInstance)!;
        zoomField.SetValue(chart, 1d);
        panField.SetValue(chart, new Vector());

        var outside = Button(MouseButton.Left, true);
        selection.Invoke(chart, new object[] { new Point(0, 0), outside });
        Check(!outside.Handled && selectedField.GetValue(chart) == null);

        var plot = new Rect(66d, 24d, chart.ActualWidth - 92d, chart.ActualHeight - 72d);
        var point = projection.Points[Math.Min(1, projection.Points.Count - 1)];
        var maximumYear = Math.Max(1d, projection.Points.Max(item => item.Year));
        var maximumValue = Math.Max(1d,
            Math.Max(projection.TargetRealUsd, projection.Points.Max(item => item.TotalRealUsd)) * 1.08d);
        var position = new Point(
            plot.Left + point.Year / maximumYear * plot.Width,
            plot.Bottom - Math.Max(0d, point.TotalRealUsd) / maximumValue * plot.Height);
        var choose = Button(MouseButton.Left, true);
        selection.Invoke(chart, new object[] { position, choose });
        Check(choose.Handled && selectedField.GetValue(chart) != null);
        var chosen = selectedField.GetValue(chart);
        selection.Invoke(chart, new object[] { position, Button(MouseButton.Left, true) });
        Check(selectedField.GetValue(chart) == null);
        Check(chosen != null);
        Invoke(chart, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));

        zoomField.SetValue(chart, 1d);
        Invoke(chart, "OnMouseRightButtonDown", Button(MouseButton.Right, true));
        zoomField.SetValue(chart, 1.5d);
        var rightDown = Button(MouseButton.Right, true);
        Invoke(chart, "OnMouseRightButtonDown", rightDown);
        Check(rightDown.Handled);
        var panStart = (Point)typeof(RetirementProjectionChart).GetField("_panStart", PrivateInstance)!.GetValue(chart)!;
        var moving = Motion();
        Invoke(chart, "OnMouseMove", Motion());
        typeof(RetirementProjectionChart).GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(chart, new object[] { panStart, false, Motion() });
        typeof(RetirementProjectionChart).GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(chart, new object[] { panStart - new Vector(20, 15), true, moving });
        Check(moving.Handled && ((Vector)panField.GetValue(chart)!).X <= 0d);
        Invoke(chart, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(chart, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(chart, "OnLostMouseCapture", Motion());
        zoomField.SetValue(chart, 1.5d);
        Invoke(chart, "OnMouseWheel", Wheel(-120));
        Check((double)zoomField.GetValue(chart)! < 1.5d);
        zoomField.SetValue(chart, 1d);
        panField.SetValue(chart, new Vector(-20, -15));
        typeof(RetirementProjectionChart).GetMethod("CoercePan", PrivateInstance)!.Invoke(chart, null);
        Check(((Vector)panField.GetValue(chart)!).Length == 0d);

        var compactMoney = typeof(RetirementProjectionChart).GetMethod("CompactMoney",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(((string)compactMoney.Invoke(null, new object[] { 1_500_000d })!).Contains("M", StringComparison.Ordinal));
        var longProjection = new RetirementProjection
        {
            UsesInflationAdjustment = false,
            TargetRealUsd = 2_000_000d,
            Points = new[]
            {
                new RetirementProjectionPoint { Month = 0, Year = 0d, StocksRealUsd = 1000d, TotalRealUsd = 1000d },
                new RetirementProjectionPoint { Month = 240, Year = 20d, StocksRealUsd = 2_000_000d, TotalRealUsd = 2_000_000d }
            }
        };
        var longChart = new RetirementProjectionChart();
        longChart.ShowProjection(longProjection);
        Render(longChart, 850, 310);
        selectedField.SetValue(longChart, longProjection.Points[0]);
        longChart.InvalidateVisual();
        Render(longChart, 850, 310);
        selectedField.SetValue(longChart, longProjection.Points[1]);
        longChart.InvalidateVisual();
        Render(longChart, 850, 310);

        var empty = new RetirementProjectionChart();
        Layout(empty, 480, 250);
        Invoke(empty, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));
        Invoke(empty, "OnMouseRightButtonDown", Button(MouseButton.Right, true));
        Invoke(empty, "OnMouseWheel", Wheel(-120));
    }

    private static void PlanningInteractions(
        RetirementReserveTimelineChart reserve,
        RetirementRunwayChart runway)
    {
        var zoomField = typeof(InteractiveRetirementChart).GetField("_zoom", PrivateInstance)!;
        var panField = typeof(InteractiveRetirementChart).GetField("_panOffset", PrivateInstance)!;
        zoomField.SetValue(reserve, 1d);
        panField.SetValue(reserve, new Vector());
        var reserveSelect = typeof(RetirementReserveTimelineChart).GetMethod("SelectAt", PrivateInstance)!;
        var reserveSelected = typeof(RetirementReserveTimelineChart).GetField("_selectedGoal", PrivateInstance)!;
        reserveSelected.SetValue(reserve, null);
        reserveSelect.Invoke(reserve, new object[] { new Point(200, 0), Button(MouseButton.Left, true) });
        var reserveClick = Button(MouseButton.Left, true);
        reserveSelect.Invoke(reserve, new object[] { new Point(200, 62), reserveClick });
        Check(reserveClick.Handled && reserveSelected.GetValue(reserve) != null);
        reserveSelect.Invoke(reserve, new object[] { new Point(200, 62), Button(MouseButton.Left, true) });
        Check(reserveSelected.GetValue(reserve) == null);
        Invoke(reserve, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));

        zoomField.SetValue(runway, 1d);
        panField.SetValue(runway, new Vector());
        var runwaySelect = typeof(RetirementRunwayChart).GetMethod("SelectAt", PrivateInstance)!;
        var runwaySelected = typeof(RetirementRunwayChart).GetField("_selectedPoint", PrivateInstance)!;
        runwaySelected.SetValue(runway, null);
        runwaySelect.Invoke(runway, new object[] { new Point(0, 0), Button(MouseButton.Left, true) });
        var runwayClick = Button(MouseButton.Left, true);
        runwaySelect.Invoke(runway, new object[] { new Point(200, 90), runwayClick });
        Check(runwayClick.Handled && runwaySelected.GetValue(runway) != null);
        runwaySelect.Invoke(runway, new object[] { new Point(200, 90), Button(MouseButton.Left, true) });
        Check(runwaySelected.GetValue(runway) == null);
        Invoke(runway, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));

        Invoke(reserve, "OnMouseRightButtonDown", Button(MouseButton.Right, true));
        zoomField.SetValue(reserve, 1.5d);
        var rightDown = Button(MouseButton.Right, true);
        Invoke(reserve, "OnMouseRightButtonDown", rightDown);
        Check(rightDown.Handled);
        var panStart = (Point)typeof(InteractiveRetirementChart).GetField("_panStart", PrivateInstance)!.GetValue(reserve)!;
        var moving = Motion();
        Invoke(reserve, "OnMouseMove", Motion());
        typeof(InteractiveRetirementChart).GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(reserve, new object[] { panStart, false, Motion() });
        typeof(InteractiveRetirementChart).GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(reserve, new object[] { panStart - new Vector(25, 20), true, moving });
        Check(moving.Handled && ((Vector)panField.GetValue(reserve)!).X <= 0d);
        Invoke(reserve, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(reserve, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(reserve, "OnLostMouseCapture", Motion());
        zoomField.SetValue(reserve, 1.5d);
        Invoke(reserve, "OnMouseWheel", Wheel(-120));
        Check((double)zoomField.GetValue(reserve)! < 1.5d);
        zoomField.SetValue(reserve, 1d);
        panField.SetValue(reserve, new Vector(-20, -15));
        typeof(InteractiveRetirementChart).GetMethod("CoercePan", PrivateInstance)!.Invoke(reserve, null);
        Check(((Vector)panField.GetValue(reserve)!).Length == 0d);
        Check((double)typeof(InteractiveRetirementChart).GetProperty("ChartZoom", PrivateInstance)!.GetValue(reserve)! == 1d);
        Invoke(reserve, "OnMouseWheel", Wheel(0));
        var compactMoney = typeof(InteractiveRetirementChart).GetMethod("CompactMoney",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(((string)compactMoney.Invoke(null, new object[] { 1_500_000d })!).Contains("M", StringComparison.Ordinal));

        var pending = new RetirementReserveTimelineChart();
        var pendingProjection = new RetirementProjection
        {
            ReserveGoals = new[] { new RetirementReserveGoal { Name = "Meta pendiente", TargetUsd = 1000d, ReachedMonth = null } }
        };
        pending.ShowProjection(pendingProjection);
        Render(pending, 850, 230);
        reserveSelected.SetValue(pending, pendingProjection.ReserveGoals[0]);
        pending.InvalidateVisual();
        Render(pending, 850, 230);
        var completed = new RetirementReserveTimelineChart();
        completed.ShowProjection(new RetirementProjection
        {
            ReserveGoals = new[]
            {
                new RetirementReserveGoal
                {
                    Name = "Objetivo de reserva extraordinariamente largo",
                    TargetUsd = 2_000_000d,
                    ReachedMonth = 0,
                    EstimatedCompletionDate = DateTime.Today
                }
            }
        });
        Render(completed, 850, 230);

        var empty = new RetirementReserveTimelineChart();
        Layout(empty, 480, 250);
        Invoke(empty, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));
        Invoke(empty, "OnMouseRightButtonDown", Button(MouseButton.Right, true));
        Invoke(empty, "OnMouseWheel", Wheel(-120));
        var emptyRunway = new RetirementRunwayChart();
        Layout(emptyRunway, 480, 250);
        Invoke(emptyRunway, "OnMouseLeftButtonDown", Button(MouseButton.Left, true));
    }

    private static void MusicInteractions(MusicSessionGraphCanvas chart)
    {
        var type = typeof(MusicSessionGraphCanvas);
        var order = type.GetMethod("SourceOrder", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((int)order.Invoke(null, new object[] { "Wallbit Pro" })! == 1);
        Check((int)order.Invoke(null, new object[] { "Binance USDC" })! == 2);
        Check((int)order.Invoke(null, new object[] { "Binance USDT" })! == 3);
        Check((int)order.Invoke(null, new object[] { "Otra cuenta" })! == 10);

        var rightDown = Button(MouseButton.Right, true);
        Invoke(chart, "OnMouseRightButtonDown", rightDown);
        Check(rightDown.Handled);
        var panStart = (Point)type.GetField("_panStart", PrivateInstance)!.GetValue(chart)!;
        var moving = Motion();
        Invoke(chart, "OnMouseMove", Motion());
        type.GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(chart, new object[] { panStart, false, Motion() });
        type.GetMethod("UpdatePan", PrivateInstance)!
            .Invoke(chart, new object[] { panStart + new Vector(25, 20), true, moving });
        Check(moving.Handled);
        Invoke(chart, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(chart, "OnMouseRightButtonUp", Button(MouseButton.Right, false));
        Invoke(chart, "OnLostMouseCapture", Motion());
        type.GetField("_zoom", PrivateInstance)!.SetValue(chart, 2.25d);
        var atMaximum = Wheel(120);
        Invoke(chart, "OnMouseWheel", atMaximum);
        Check(atMaximum.Handled);
        Invoke(chart, "OnMouseWheel", Wheel(-120));

        var empty = new MusicSessionGraphCanvas();
        Layout(empty, 800, 300);
        Invoke(empty, "OnMouseRightButtonDown", Button(MouseButton.Right, true));
        Invoke(empty, "OnMouseWheel", Wheel(120));

        var calculation = new MusicSessionCalculation();
        calculation.Options.Add(new MusicSessionOption
        {
            Source = "GrabrFi", SourceCurrency = "USD", Method = "Pago directo en ARS",
            SourceDebitAmount = 390m, TargetDetail = "Pago local", Path = "Directo",
            Category = MusicSessionCategory.BankedArs, RequiredArs = 600000m
        });
        calculation.Options.Add(new MusicSessionOption
        {
            Source = "Wallbit Pro", SourceCurrency = "USD", Method = "Pago directo en ARS",
            SourceDebitAmount = 410m, TargetDetail = "Pago local", Path = "Con escala",
            Category = MusicSessionCategory.BankedArs, RequiredArs = 600000m
        });
        empty.ShowCalculation(calculation, 400m);
        Render(empty, 800, 300);
        calculation.Options[0].RequiredArs = null;
        empty.ShowCalculation(calculation, 400m);
        Render(empty, 800, 300);
        var truncate = type.GetMethod("Truncate", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(((string)truncate.Invoke(null, new object[] { "Texto demasiado largo", 8 })!).EndsWith("…", StringComparison.Ordinal));
    }

    private static void Render(FrameworkElement element, int width, int height)
    {
        Layout(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        Check(bitmap.PixelWidth == width);
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    private static void Invoke(object target, string method, object argument) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, new[] { argument });

    private static MouseButtonEventArgs Button(MouseButton button, bool down) =>
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
        {
            RoutedEvent = button == MouseButton.Right
                ? down ? UIElement.MouseRightButtonDownEvent : UIElement.MouseRightButtonUpEvent
                : UIElement.MouseLeftButtonDownEvent
        };

    private static MouseEventArgs Motion() => new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
    {
        RoutedEvent = Mouse.MouseMoveEvent
    };

    private static MouseWheelEventArgs Wheel(int delta) =>
        new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent
        };

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Falló una interacción verificable del gráfico.");
    }
}
