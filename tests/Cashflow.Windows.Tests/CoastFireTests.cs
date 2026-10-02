using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class CoastFireTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        DefaultsAndPersistence();
        EarliestMonthlyCrossings();
        SeparateAssetsAndInflation();
        Validation();
        EditorsAndRendering();
    }

    private static RetirementSettings Settings() => new RetirementSettings
    {
        PlanningCollectionsInitialized = true,
        InitialStocksCents = 10000,
        TargetInvestedCents = 100000,
        TargetStocksCents = 100000,
        StockAnnualReturnPercentage = 10m,
        BondAnnualReturnPercentage = 0m,
        UsInflationPercentage = 0m,
        UseInflationAdjustment = false,
        MonthlyIncomes = new List<RetirementIncomeSettings>
        {
            new RetirementIncomeSettings { MonthlyAmountCents = 2500 }
        }
    };

    private static void DefaultsAndPersistence()
    {
        var legacy = JsonSerializer.Deserialize<RetirementSettings>("{}")!;
        Check(legacy.CoastCurrentAge == 29 && legacy.CoastTargetAges.SequenceEqual(new[] { 40, 50, 60, 65 }),
            "Documentos anteriores reciben las edades iniciales acordadas.");
        legacy.CoastCurrentAge = 31;
        legacy.CoastTargetAges = new List<int> { 45, 55, 70 };
        var saved = JsonSerializer.Deserialize<RetirementSettings>(JsonSerializer.Serialize(legacy))!;
        Check(saved.CoastCurrentAge == 31 && saved.CoastTargetAges.SequenceEqual(legacy.CoastTargetAges),
            "Las edades editadas persisten.");
        saved.CoastTargetAges.Clear();
        saved.EnsurePlanningCollections();
        Check(saved.CoastTargetAges.Count == 0, "Una comparación vacía no se repuebla.");
        saved.CoastTargetAges = null!;
        Check(saved.EnsurePlanningCollections() && saved.CoastTargetAges.Count == 0, "Lista JSON nula normalizada.");
        var existingVacation = new RetirementReserveSettings { Kind = "vacation", CurrentCents = 12300 };
        var existingEmergency = new RetirementReserveSettings { Kind = "emergency", CurrentCents = 45600 };
        var older = new RetirementSettings
        {
            Reserves = new List<RetirementReserveSettings> { existingEmergency, existingVacation }
        };
        older.EnsurePlanningCollections();
        Check(older.Reserves.Count == 3 && ReferenceEquals(older.Reserves[1], existingEmergency) &&
            ReferenceEquals(older.Reserves[2], existingVacation) && existingVacation.CurrentCents == 12300,
            "Migrar el documento con COAST conserva reservas iniciales existentes y su orden.");
    }

    private static void EarliestMonthlyCrossings()
    {
        var settings = Settings();
        settings.CoastTargetAges = new List<int> { 60, 40, 29, 30, 35, 40 };
        var projection = new RetirementCalculator().Calculate(settings);
        Check(projection.CoastScenarios.Select(item => item.RetirementAge).SequenceEqual(new[] { 29, 30, 35, 40, 60 }),
            "Escenarios ordenados y sin duplicados.");
        Check(projection.CoastScenarios[0].ReachedPoint == null && projection.CoastScenarios[1].ReachedPoint == null,
            "Edades demasiado próximas no producen un marcador tardío.");
        Check(projection.CoastScenarios[4].ReachedPoint!.Month == 0, "Escenario lejano ya cumple COAST hoy.");
        foreach (var scenario in projection.CoastScenarios)
        {
            int? expectedMonth = null;
            var stocks = 100d;
            var monthlyGrowth = Math.Pow(1.1d, 1d / 12d);
            for (var month = 0; month <= scenario.MonthsUntilRetirement; month++)
            {
                if (month > 0) stocks = stocks * monthlyGrowth + 25d;
                var future = stocks * Math.Pow(1.1d, (scenario.MonthsUntilRetirement - month) / 12d);
                if (future >= 1000d - 1e-8)
                {
                    expectedMonth = month;
                    break;
                }
            }
            Check(scenario.ReachedPoint?.Month == expectedMonth, "Cruce en el primer mes, verificado con capitalización anual independiente.");
            if (scenario.ReachedPoint != null)
                Check(projection.Points.Contains(scenario.ReachedPoint), "Cada cruce exacto está incluido en el gráfico.");
        }
        Check(projection.CoastScenarios[2].ReachedPoint!.Month > projection.CoastScenarios[3].ReachedPoint!.Month,
            "Más tiempo hasta la jubilación permite dejar de aportar antes.");
        Check(projection.Points.Select(point => point.Month).Distinct().Count() == projection.Points.Count,
            "No hay muestras duplicadas en cruces anuales o simultáneos.");
        settings.CoastTargetAges.Clear();
        var noCoast = new RetirementCalculator().Calculate(settings);
        Check(noCoast.CoastScenarios.Count == 0 && noCoast.MonthsToTarget == projection.MonthsToTarget &&
            noCoast.TotalNewContributionsUsd == projection.TotalNewContributionsUsd,
            "Comparar COAST no altera el camino ni los aportes normales.");

        settings.CoastTargetAges.Add(60);
        settings.MonthlyIncomes[0].IsIncluded = false;
        settings.Reserves.Add(new RetirementReserveSettings { CurrentCents = 100000000, TargetCents = 100000000 });
        settings.InitialStocksCents = 0;
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint == null,
            "Ingresos ocultos y reservas líquidas no se cuentan como capital invertido COAST.");
        settings.InitialStocksCents = settings.TargetInvestedCents;
        settings.CoastTargetAges = new List<int> { 29, 30 };
        var reachedToday = new RetirementCalculator().Calculate(settings);
        Check(reachedToday.Points.Count == 1 && reachedToday.CoastScenarios.All(item => item.ReachedPoint!.Month == 0),
            "Objetivo cumplido hoy, incluso con horizonte cero.");
    }

    private static void SeparateAssetsAndInflation()
    {
        var settings = Settings();
        settings.MonthlyIncomes.Clear();
        settings.InitialStocksCents = 50000;
        settings.InitialBondsCents = 50000;
        settings.TargetInvestedCents = 180000;
        settings.TargetStocksCents = 180000;
        settings.CoastTargetAges = new List<int> { 39 };
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint == null,
            "Los bonos a retorno cero no crecen como las acciones.");
        settings.TargetInvestedCents = 170000;
        settings.TargetStocksCents = 170000;
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint!.Month == 0,
            "Suma de acciones compuestas y bonos independientes.");
        settings.UsInflationPercentage = 10m;
        settings.UseInflationAdjustment = true;
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint == null,
            "Inflación descuenta poder adquisitivo al capital futuro.");
        settings.UseInflationAdjustment = false;
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint!.Month == 0,
            "Modo nominal ignora la inflación guardada.");

        settings.InitialStocksCents = 200000;
        settings.InitialBondsCents = 0;
        settings.StockAnnualReturnPercentage = -10m;
        Check(new RetirementCalculator().Calculate(settings).CoastScenarios[0].ReachedPoint == null,
            "Tener FIRE hoy no implica COAST futuro si el retorno previsto erosiona el objetivo.");
    }

    private static void Validation()
    {
        foreach (var age in new[] { -1, 121 })
        {
            var settings = Settings();
            settings.CoastCurrentAge = age;
            ExpectArgument(() => new RetirementCalculator().Calculate(settings));
        }
        foreach (var age in new[] { 28, 130 })
        {
            var settings = Settings();
            settings.CoastTargetAges = new List<int> { age };
            ExpectArgument(() => new RetirementCalculator().Calculate(settings));
        }
        foreach (var age in new[] { 0, 120 })
        {
            var settings = Settings();
            settings.CoastCurrentAge = age;
            settings.CoastTargetAges = new List<int> { age, age + 100 };
            Check(new RetirementCalculator().Calculate(settings).CoastScenarios.Count == 2, "Límites de edad admitidos.");
        }
    }

    private static void EditorsAndRendering()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-coast-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.Retirement = Settings();
            var store = new ScenarioStore(path);
            var view = new RetirementView(document, store);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var current = (TextBox)view.FindName("CoastCurrentAgeBox");
            var targets = (TextBox)view.FindName("CoastTargetAgesBox");
            Check(current.Text == "29" && targets.Text == "40, 50, 60, 65", "Campos iniciales claros.");
            var save = typeof(RetirementView).GetMethod("TrySaveInputs", PrivateInstance)!;
            foreach (var invalid in new[] { "abc", "-1", "121" })
            {
                current.Text = invalid;
                Check(Equals(save.Invoke(view, new object[] { false }), false), "Edad actual inválida rechazada.");
            }
            current.Text = "29";
            foreach (var invalid in new[] { "abc", "28", "130", "40, 50.5" })
            {
                targets.Text = invalid;
                Check(Equals(save.Invoke(view, new object[] { false }), false), "Edad objetivo inválida rechazada.");
            }
            targets.Text = "65; 40, 50\n60\t40";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Múltiples edades editables.");
            store.Save(document);
            Check(store.Load().Retirement.CoastTargetAges.SequenceEqual(new[] { 40, 50, 60, 65 }), "Edades guardadas por el formulario.");
            current.Text = "30";
            targets.Text = "30, 31, 40, 70";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Edad actual modificable.");
            var render = typeof(RetirementView).GetMethod("RenderProjection", PrivateInstance)!;
            render.Invoke(view, null);
            var cards = (WrapPanel)view.FindName("CoastScenarioRowsPanel");
            Check(cards.Children.Count == 4, "Una tarjeta por escenario.");
            var statuses = cards.Children.Cast<Border>().Select(card => ((TextBlock)((StackPanel)card.Child).Children[1]).Text).ToList();
            Check(statuses.Any(text => text.Contains("COAST hoy", StringComparison.Ordinal)) &&
                statuses.Any(text => text.Contains("No se alcanza", StringComparison.Ordinal)) &&
                statuses.Any(text => text.Contains("meses", StringComparison.Ordinal)), "Hoy, pendiente y cruce futuro visibles.");
            Render(view, 1280, 1000, "coast-fire-view.png");

            var settings = Settings();
            settings.CoastTargetAges = new List<int> { 30, 35, 40, 50, 60, 65 };
            var projection = new RetirementCalculator().Calculate(settings);
            var chart = new RetirementProjectionChart();
            chart.ShowProjection(projection);
            Render(chart, 850, 340, "coast-fire-chart.png");
            var chosen = projection.CoastScenarios.Single(item => item.RetirementAge == 40).ReachedPoint!;
            var maximumYear = Math.Max(1d, projection.Points.Max(point => point.Year));
            var maximumValue = Math.Max(projection.TargetRealUsd, projection.Points.Max(point => point.TotalRealUsd)) * 1.08d;
            var position = new Point(66d + chosen.Year / maximumYear * (850d - 92d),
                340d - 48d - chosen.TotalRealUsd / maximumValue * (340d - 72d));
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
            typeof(RetirementProjectionChart).GetMethod("SelectAt", PrivateInstance)!.Invoke(chart, new object[] { position, click });
            var selected = typeof(RetirementProjectionChart).GetField("_selectedPoint", PrivateInstance)!;
            Check(click.Handled && ReferenceEquals(selected.GetValue(chart), chosen), "Marcador COAST permite consultar el mes exacto.");
            Render(chart, 850, 340, "coast-fire-selected.png");
            var today = projection.CoastScenarios.Single(item => item.RetirementAge == 60).ReachedPoint!;
            selected.SetValue(chart, today);
            chart.InvalidateVisual();
            Render(chart, 850, 340);
            settings.InitialStocksCents = settings.TargetInvestedCents;
            settings.CoastTargetAges = new List<int> { 29, 40 };
            chart.ShowProjection(new RetirementCalculator().Calculate(settings));
            Render(chart, 680, 340);

            targets.Text = "";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Comparación vacía válida.");
            render.Invoke(view, null);
            Check(((StackPanel)view.FindName("CoastResultsPanel")).Visibility == Visibility.Collapsed && cards.Children.Count == 0,
                "Quitar todas las edades oculta resultados y marcadores.");
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Render(FrameworkElement element, int width, int height, string? filename = null)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.InvalidateVisual();
        var bitmap = new RenderTargetBitmap(width, height, 96d, 96d, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        if (filename != null)
        {
            var folder = Path.Combine("artifacts", "qa");
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(folder, filename));
            encoder.Save(output);
        }
    }

    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Se esperaba rechazar una edad inválida.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
