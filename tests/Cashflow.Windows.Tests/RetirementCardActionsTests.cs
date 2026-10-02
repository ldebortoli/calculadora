using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class RetirementCardActionsTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        HiddenIncomeChangesProjectionAndPersists();
        AllCardsCanBeToggledAndRemoved();
        EstimatedDurationIncludesYears();
    }

    private static RetirementSettings Settings()
    {
        var settings = new RetirementSettings
        {
            TargetInvestedCents = 360000,
            TargetStocksCents = 360000,
            StockAnnualReturnPercentage = 0m,
            BondAnnualReturnPercentage = 0m,
            UsInflationPercentage = 0m,
            UseInflationAdjustment = false
        };
        settings.EnsurePlanningCollections();
        settings.MonthlyIncomes[0].MonthlyAmountCents = 10000;
        settings.MonthlyIncomes.Add(new RetirementIncomeSettings { Name = "Trabajo 2", MonthlyAmountCents = 20000 });
        return settings;
    }

    private static void HiddenIncomeChangesProjectionAndPersists()
    {
        var settings = Settings();
        var calculator = new RetirementCalculator();
        var shown = calculator.Calculate(settings);
        Check(shown.TotalMonthlyIncomeUsd == 300d && shown.MonthsToTarget == 12, "Ingresos visibles sumados.");
        settings.MonthlyIncomes[1].IsIncluded = false;
        var hidden = calculator.Calculate(settings);
        Check(hidden.TotalMonthlyIncomeUsd == 100d && hidden.MonthsToTarget == 36, "Ingreso oculto excluido de aportes y plazo.");
        Check(hidden.Points[1].TotalRealUsd < shown.Points[1].TotalRealUsd, "Gráfico recalculado sin el ingreso oculto.");
        var saved = JsonSerializer.Deserialize<RetirementSettings>(JsonSerializer.Serialize(settings))!;
        Check(!saved.MonthlyIncomes[1].IsIncluded && saved.MonthlyIncomes[1].MonthlyAmountCents == 20000,
            "Visibilidad e importe persistidos.");
        Check(JsonSerializer.Deserialize<RetirementIncomeSettings>("{\"Name\":\"Anterior\",\"MonthlyAmountCents\":12300}")!.IsIncluded,
            "Ingresos anteriores visibles por defecto.");
        saved.MonthlyIncomes[1].IsIncluded = true;
        Check(calculator.Calculate(saved).MonthsToTarget == shown.MonthsToTarget, "Mostrar restaura la proyección.");
        saved.MonthlyIncomes.Clear();
        saved.Reserves.Clear();
        Check(!saved.EnsurePlanningCollections(), "Listas vacías inicializadas no se repueblan.");
        var empty = calculator.Calculate(saved);
        Check(empty.TotalMonthlyIncomeUsd == 0d && empty.MonthsToTarget == null && empty.ReserveGoals.Count == 0,
            "Cero ingresos y reservas es una configuración válida.");
    }

    private static void AllCardsCanBeToggledAndRemoved()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-card-actions-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.Retirement = Settings();
            var store = new ScenarioStore(path);
            var view = new RetirementView(document, store);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var incomes = (StackPanel)view.FindName("IncomeRowsPanel");
            var reserves = (StackPanel)view.FindName("ReserveRowsPanel");
            Check(((TextBlock)view.FindName("TargetDateDetailText")).Text.Contains("12 meses · 1 año", StringComparison.Ordinal),
                "Fecha estimada muestra meses y años.");
            foreach (var panel in new[] { incomes, reserves })
            {
                panel.Measure(new Size(300, double.PositiveInfinity));
                panel.Arrange(new Rect(0, 0, 300, panel.DesiredSize.Height));
                foreach (Border card in panel.Children)
                {
                    var actions = (StackPanel)((DockPanel)((StackPanel)card.Child).Children[0]).Children[0];
                    Check(actions.Children.Count == 2, "Todas las tarjetas ofrecen dos acciones.");
                    foreach (Button button in actions.Children)
                    {
                        var right = button.TranslatePoint(new Point(button.ActualWidth, 0), card).X;
                        Check(button.IsEnabled && right <= card.ActualWidth && button.ActualWidth > 0,
                            "Acciones disponibles dentro de una tarjeta compacta.");
                    }
                }
            }

            var income = document.Retirement.MonthlyIncomes[0];
            ActionButton(incomes, 0, "Ocultar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!income.IsIncluded && incomes.Children.Count == 2, "Ocultar mantiene la tarjeta de ingreso.");
            Check(!store.Load().Retirement.MonthlyIncomes[0].IsIncluded, "Ingreso oculto se conserva al reabrir.");
            ActionButton(incomes, 0, "Mostrar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(income.IsIncluded && income.MonthlyAmountCents == 10000, "Mostrar conserva el monto del ingreso.");

            foreach (var reserve in document.Retirement.Reserves.ToArray())
            {
                var index = document.Retirement.Reserves.IndexOf(reserve);
                ActionButton(reserves, index, "Ocultar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!reserve.IsIncluded && reserves.Children.Count == 3, "Reserva inicial se puede ocultar.");
                ActionButton(reserves, index, "Mostrar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(reserve.IsIncluded, "Reserva inicial se puede mostrar.");
            }

            var target = (TextBox)view.FindName("TargetInvestedBox");
            var targetText = target.Text;
            foreach (var handler in new[] { "ToggleIncome_Click", "RemoveIncome_Click", "ToggleReserve_Click", "RemoveReserve_Click" })
            {
                Invoke(view, handler, view);
                Invoke(view, handler, new Button { Tag = new object() });
                target.Text = "0";
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(window => window.IsVisible);
                    ((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Invoke(view, handler, new Button
                {
                    Tag = handler.Contains("Income", StringComparison.Ordinal)
                        ? (object)document.Retirement.MonthlyIncomes[0]
                        : document.Retirement.Reserves[0]
                });
                target.Text = targetText;
            }
            Check(income.IsIncluded && document.Retirement.MonthlyIncomes.Count == 2 && document.Retirement.Reserves.Count == 3,
                "Ediciones inválidas se informan sin cambiar tarjetas.");

            while (reserves.Children.Count > 0)
                ActionButton(reserves, 0, "Quitar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (incomes.Children.Count > 0)
                ActionButton(incomes, 0, "Quitar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var reopened = store.Load();
            Check(reopened.Retirement.PlanningCollectionsInitialized && reopened.Retirement.Reserves.Count == 0 &&
                reopened.Retirement.MonthlyIncomes.Count == 0, "Quitar todas las tarjetas persiste sin recrearlas.");
            var reopenedView = new RetirementView(reopened, store);
            reopenedView.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(((StackPanel)reopenedView.FindName("IncomeRowsPanel")).Children.Count == 0 &&
                ((StackPanel)reopenedView.FindName("ReserveRowsPanel")).Children.Count == 0, "Reabrir respeta el estado vacío.");
            Invoke(view, "AddIncome_Click", view);
            Invoke(view, "AddReserve_Click", view);
            Check(incomes.Children.Count == 1 && reserves.Children.Count == 1, "Se pueden agregar tarjetas después de quitar todas.");
            document.Retirement.UseInflationAdjustment = true;
            typeof(RetirementView).GetMethod("RenderProjection", PrivateInstance)!.Invoke(view, null);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            reopenedView.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void EstimatedDurationIncludesYears()
    {
        var method = typeof(RetirementView).GetMethod("FormatTargetDuration", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (months, expected) in new[]
        {
            (0, "0 meses · 0 años"), (1, "1 mes · menos de 1 año"), (6, "6 meses · menos de 1 año"),
            (12, "12 meses · 1 año"), (74, "74 meses · 6 años y 2 meses")
        })
            Check(Equals(method.Invoke(null, new object[] { months }), expected), "Plazo en meses y años correcto.");
    }

    private static Button ActionButton(StackPanel panel, int index, string label)
    {
        var actions = (StackPanel)((DockPanel)((StackPanel)((Border)panel.Children[index]).Child).Children[0]).Children[0];
        return actions.Children.OfType<Button>().Single(button => Equals(button.Content, label));
    }

    private static void Invoke(RetirementView view, string handler, object sender) =>
        typeof(RetirementView).GetMethod(handler, PrivateInstance)!.Invoke(view, new[] { sender, new RoutedEventArgs() });

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
