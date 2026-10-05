using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cashflow.Windows.Data;
using Cashflow.Windows.Localization;

namespace Cashflow.Windows.Tests;

internal static class BirthDateTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        CalendarAgesAndDeadlines();
        OptionalBirthDateAndProjection();
        EditorPersistenceAndLanguages();
    }

    private static void CalendarAgesAndDeadlines()
    {
        var birth = new DateTime(1996, 10, 6);
        Check(RetirementAge.YearsOn(birth, new DateTime(2026, 10, 5)) == 29, "No anticipar cumpleaños.");
        Check(RetirementAge.YearsOn(birth, new DateTime(2026, 10, 6)) == 30, "Cumpleaños exacto.");
        Check(RetirementAge.YearsOn(birth, new DateTime(2026, 10, 7)) == 30, "Cumpleaños pasado.");
        var leapBirth = new DateTime(2000, 2, 29);
        Check(RetirementAge.YearsOn(leapBirth, new DateTime(2027, 2, 27)) == 26 &&
              RetirementAge.YearsOn(leapBirth, new DateTime(2027, 2, 28)) == 27 &&
              RetirementAge.YearsOn(leapBirth, new DateTime(2028, 2, 28)) == 27 &&
              RetirementAge.YearsOn(leapBirth, new DateTime(2028, 2, 29)) == 28,
            "Nacimiento bisiesto: febrero 28 en año ordinario y 29 en año bisiesto.");
        Check(RetirementAge.MonthsUntilBirthday(birth, 30, new DateTime(2026, 10, 5)) == 0,
            "Cumplir la edad mañana no da doce meses de capitalización.");
        Check(RetirementAge.MonthsUntilBirthday(birth, 40, new DateTime(2026, 10, 7)) == 119,
            "No incluir un punto mensual posterior al cumpleaños objetivo.");
        Check(RetirementAge.MonthsUntilBirthday(birth, 30, new DateTime(2026, 10, 6)) == 0 &&
              RetirementAge.MonthsUntilBirthday(birth, 29, new DateTime(2026, 10, 5)) == -12,
            "Plazo exacto y edades ya pasadas.");
        Check(RetirementAge.MonthsUntilBirthday(leapBirth, 27, new DateTime(2026, 1, 31)) == 13,
            "Fin de mes y cumpleaños bisiesto usan fechas de calendario.");
    }

    private static RetirementSettings Settings() => new()
    {
        PlanningCollectionsInitialized = true,
        TargetInvestedCents = 120000,
        TargetStocksCents = 120000,
        StockAnnualReturnPercentage = 0m,
        BondAnnualReturnPercentage = 0m,
        UseInflationAdjustment = false,
        MonthlyIncomes = new List<RetirementIncomeSettings> { new() { MonthlyAmountCents = 10000 } }
    };

    private static void OptionalBirthDateAndProjection()
    {
        var today = new DateTime(2026, 10, 5);
        var calculator = new RetirementCalculator();
        var settings = Settings();
        var noBirth = calculator.Calculate(settings, today);
        Check(noBirth.MonthsToTarget == 12 && noBirth.EstimatedTargetDate == new DateTime(2027, 10, 5) &&
              noBirth.AgeAtTargetYears == null && noBirth.CoastScenarios.Count == 0,
            "Sin nacimiento, proyección válida sin edad inventada ni COAST.");
        settings.BirthDate = new DateTime(1996, 10, 6);
        settings.CoastTargetAges = new List<int> { 30, 40 };
        var withBirth = calculator.Calculate(settings, today);
        Check(withBirth.MonthsToTarget == noBirth.MonthsToTarget && withBirth.AgeAtTargetYears == 30 &&
              withBirth.TotalNewContributionsUsd == noBirth.TotalNewContributionsUsd,
            "Nacimiento añade edad cumplida sin alterar objetivo ni aportes.");
        Check(withBirth.CoastScenarios[0].MonthsUntilRetirement == 0 && withBirth.CoastScenarios[0].ReachedPoint == null &&
              withBirth.CoastScenarios[1].MonthsUntilRetirement == 120,
            "COAST usa el cumpleaños real como plazo.");
        settings.CoastTargetAges.Clear();
        Check(calculator.Calculate(settings, today).AgeAtTargetYears == 30, "Edad estimada independiente de COAST.");
        settings.InitialStocksCents = settings.TargetInvestedCents;
        Check(calculator.Calculate(settings, today).AgeAtTargetYears == 29, "Objetivo cumplido hoy usa edad de hoy.");
        settings.InitialStocksCents = 0;
        settings.MonthlyIncomes.Clear();
        Check(calculator.Calculate(settings, today).AgeAtTargetYears == null, "Objetivo inalcanzable no inventa edad futura.");
        var legacy = JsonSerializer.Deserialize<RetirementSettings>("{\"CoastCurrentAge\":29,\"CoastTargetAges\":[45,55]}")!;
        Check(legacy.BirthDate == null && legacy.CoastTargetAges.SequenceEqual(new[] { 45, 55 }),
            "La edad antigua no se convierte en una fecha arbitraria; objetivos preservados.");
        settings = Settings();
        settings.CoastTargetAges = new List<int> { 0, 220 };
        Check(calculator.Calculate(settings, today).CoastScenarios.Count == 0, "Edades se conservan sin nacimiento.");
        foreach (var age in new[] { -1, 221 })
        {
            settings.CoastTargetAges = new List<int> { age };
            ExpectArgument(() => calculator.Calculate(settings, today));
        }
        settings.CoastTargetAges.Clear();
        foreach (var date in new[] { today.AddDays(1), today.AddYears(-121) })
        {
            settings.BirthDate = date;
            ExpectArgument(() => calculator.Calculate(settings, today));
        }
        foreach (var date in new[] { today, today.AddYears(-120) })
        {
            settings.BirthDate = date;
            Check(calculator.Calculate(settings, today).MonthsToTarget == 12, "Límites de nacimiento válidos.");
        }
        settings.BirthDate = today.AddYears(-60);
        settings.CoastTargetAges = new List<int> { 40, 65 };
        Check(calculator.Calculate(settings, today).CoastScenarios[0].ReachedPoint == null,
            "Una edad objetivo pasada no bloquea la proyección general.");
        settings.CoastTargetAges.Clear();
        settings.Reserves.Add(new RetirementReserveSettings { CurrentCents = 10000, TargetCents = 10000 });
        settings.OrdinaryMonthlyExpensesCents = 10000;
        var futureStart = new DateTime(2030, 1, 31);
        var futureProjection = calculator.Calculate(settings, futureStart);
        Check(futureProjection.ReserveGoals[0].EstimatedCompletionDate == futureStart &&
              futureProjection.Runway.EstimatedFailureDate == new DateTime(2030, 3, 31),
            "Cartera, reservas y autonomía comparten la misma fecha de inicio determinista.");
    }

    private static void EditorPersistenceAndLanguages()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-birth-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.Retirement = Settings();
            var store = new ScenarioStore(path);
            var view = new RetirementView(document, store);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var birthBox = (TextBox)view.FindName("BirthDateBox");
            var ageText = (TextBlock)view.FindName("TargetAgeText");
            var hint = (TextBlock)view.FindName("CoastBirthDateHint");
            var targets = (TextBox)view.FindName("CoastTargetAgesBox");
            Check(view.FindName("CoastCurrentAgeBox") == null && birthBox.Text == "" &&
                  ageText.Visibility == Visibility.Collapsed && hint.Visibility == Visibility.Visible,
                "Campo de nacimiento opcional reemplaza la edad manual.");
            var form = (StackPanel)birthBox.Parent;
            Check(form.Children.IndexOf(birthBox) == 1, "Nacimiento al inicio del formulario fuera de COAST.");
            var save = typeof(RetirementView).GetMethod("TrySaveInputs", PrivateInstance)!;
            var render = typeof(RetirementView).GetMethod("RenderProjection", PrivateInstance)!;
            targets.Text = "0, 220";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Sin nacimiento, guardar planificación funciona.");
            targets.Text = "221";
            Check(Equals(save.Invoke(view, new object[] { false }), false), "Edades fuera del límite global rechazadas.");
            targets.Text = "";
            birthBox.Text = " 6/10/1996 ";
            typeof(RetirementView).GetMethod("Calculate_Click", PrivateInstance)!.Invoke(view, new object[] { view, new RoutedEventArgs() });
            Check(store.Load().Retirement.BirthDate == new DateTime(1996, 10, 6) && ageText.Visibility == Visibility.Visible &&
                  hint.Visibility == Visibility.Collapsed && ((StackPanel)view.FindName("CoastResultsPanel")).Visibility == Visibility.Collapsed,
                "Nacimiento guardado y edad visible incluso con COAST vacío.");
            var expectedAge = RetirementAge.YearsOn(new DateTime(1996, 10, 6), DateTime.Today.AddMonths(12));
            foreach (var language in UiLanguage.Languages)
            {
                UiLanguage.SetLanguage(language.Id);
                view.Measure(new Size(1400, 950));
                view.Arrange(new Rect(0, 0, 1400, 950));
                view.UpdateLayout();
                Check(ageText.Text == UiLanguage.Translate($"Vas a tener {expectedAge} años"), "Edad estimada localizada: " + language.Id);
                Check(AutomationProperties.GetName(birthBox) == UiLanguage.Translate("Fecha de nacimiento (opcional)") &&
                      AutomationProperties.GetHelpText(birthBox) == UiLanguage.Translate("Formato: día/mes/año, por ejemplo 15/06/1997."),
                    "Nombre y ayuda accesibles localizados.");
                Check(birthBox.Text == " 6/10/1996 ", "Cambiar idioma preserva el valor editable.");
                if (language.Id is "es" or "en" or "de" or "ja") Render(view, language.Id);
            }
            UiLanguage.SetLanguage("es");
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            var reopened = new RetirementView(store.Load(), store);
            reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(((TextBox)reopened.FindName("BirthDateBox")).Text == "06/10/1996", "Nacimiento recuperado con formato claro.");
            foreach (var invalid in new[] { "2026-10-05", "31/04/1996", "29/02/1997", DateTime.Today.AddDays(1).ToString("dd/MM/yyyy"), DateTime.Today.AddYears(-121).ToString("dd/MM/yyyy") })
            {
                birthBox.Text = invalid;
                Check(Equals(save.Invoke(view, new object[] { false }), false) && document.Retirement.BirthDate == new DateTime(1996, 10, 6),
                    "Entrada inválida rechazada sin reemplazar datos guardados.");
            }
            birthBox.Text = "29/02/2000";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Nacimiento bisiesto válido aceptado.");
            document.Retirement.MonthlyIncomes[0].MonthlyAmountCents = 0;
            render.Invoke(view, null);
            Check(ageText.Visibility == Visibility.Collapsed && ageText.Text == "", "Inalcanzable limpia edad anterior.");
            birthBox.Text = " ";
            Check(Equals(save.Invoke(view, new object[] { false }), true), "Vaciar el nacimiento es válido.");
            store.Save(document);
            render.Invoke(view, null);
            Check(store.Load().Retirement.BirthDate == null && ageText.Visibility == Visibility.Collapsed && hint.Visibility == Visibility.Visible,
                "Borrar nacimiento persiste y elimina resultados de edad.");
            reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            UiLanguage.SetLanguage("es");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Render(FrameworkElement view, string language)
    {
        var bitmap = new RenderTargetBitmap(1400, 950, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        Directory.CreateDirectory(Path.Combine("artifacts", "qa"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine("artifacts", "qa", "birth-date-" + language + ".png"));
        encoder.Save(output);
    }

    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Se esperaba rechazar la fecha o edad inválida.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
