using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class RetirementEdgeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-retirement-edges-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            var view = new RetirementView(document, new ScenarioStore(path));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Invoke(view, "UserControl_Loaded");
            Check((bool)typeof(RetirementView).GetField("_initialized", PrivateInstance)!.GetValue(view)!);

            var target = (TextBox)view.FindName("TargetInvestedBox");
            var savedTarget = target.Text;
            target.Text = "0";
            Invoke(view, "UserControl_Unloaded");
            foreach (var handler in new[] { "Calculate_Click", "InflationMode_Click", "AddIncome_Click", "AddReserve_Click" })
            {
                Alert();
                Invoke(view, handler);
            }
            var reserve = document.Retirement.Reserves[0];
            Alert();
            Invoke(view, "ToggleReserve_Click", new Button { Tag = reserve });
            Check(reserve.IsIncluded);
            target.Text = savedTarget;

            reserve.StartAfterRetirementGoal = true;
            typeof(RetirementView).GetMethod("BuildReserveEditors", PrivateInstance)!.Invoke(view, null);
            reserve.StartAfterRetirementGoal = false;
            typeof(RetirementView).GetMethod("BuildReserveEditors", PrivateInstance)!.Invoke(view, null);

            Invoke(view, "RemoveIncome_Click", view);
            Invoke(view, "RemoveReserve_Click", view);
            Invoke(view, "ToggleReserve_Click", view);
            Invoke(view, "RemoveReserve_Click", new Button { Tag = reserve });
            var income = document.Retirement.MonthlyIncomes[0];
            Invoke(view, "RemoveIncome_Click", new Button { Tag = income });

            var annual = (TextBox)view.FindName("AnnualVacationExpensesBox");
            annual.Text = "1200";
            Invoke(view, "MoneyBox_LostFocus", annual);
            Check(((TextBlock)view.FindName("VacationMonthlyProrationText")).Text.Contains("100", StringComparison.Ordinal));
            var stocks = (TextBox)view.FindName("InitialStocksBox");
            stocks.Text = "123";
            Invoke(view, "MoneyBox_LostFocus", stocks);
            Check(stocks.Text.Contains("123", StringComparison.Ordinal));
            Invoke(view, "MoneyBox_LostFocus", view);

            var tryMoney = typeof(RetirementView).GetMethod("TryMoney", PrivateStatic)!;
            object[] moneyArguments = { "100000000000000000", 0L };
            Check(!(bool)tryMoney.Invoke(null, moneyArguments)!);
            var duration = typeof(RetirementView).GetMethod("FormatDuration", PrivateStatic)!;
            foreach (var (months, expected) in new[]
            {
                (0, "Menos de un mes"), (1, "1 mes"), (2, "2 meses"),
                (12, "1 año"), (24, "2 años"), (13, "1 año y 1 mes"),
                (26, "2 años y 2 meses")
            })
            {
                Check(Equals(duration.Invoke(null, new object[] { months }), expected));
            }
            var tryPositiveMoney = typeof(RetirementView).GetMethod("TryPositiveMoney", PrivateStatic)!;
            Check(!(bool)tryPositiveMoney.Invoke(null, new object[] { "incorrecto", 0L })!);
            Check(!(bool)tryPositiveMoney.Invoke(null, new object[] { "0", 0L })!);
            Check((bool)tryPositiveMoney.Invoke(null, new object[] { "1", 0L })!);
            var tryRange = typeof(RetirementView).GetMethod("TryRange", PrivateStatic)!;
            foreach (var (text, expected) in new[] { ("incorrecto", false), ("-1", false), ("101", false), ("50", true) })
                Check((bool)tryRange.Invoke(null, new object[] { text, 0m, 100m, 0m })! == expected);
            var surplusText = new TextBlock();
            typeof(RetirementView).GetMethod("SetSurplusColor", PrivateStatic)!
                .Invoke(null, new object[] { surplusText, -1d });
            Check(surplusText.Foreground != null);

            var reserveStatus = typeof(RetirementView).GetMethod("BuildReserveStatus", PrivateStatic)!;
            var goal = new RetirementReserveGoal { Name = "Casa", TargetUsd = 100d, ReachedMonth = 0 };
            Check(((string)reserveStatus.Invoke(null, new object[] { goal })!).Contains("completa hoy", StringComparison.Ordinal));
            goal.ReachedMonth = 1;
            goal.EstimatedCompletionDate = DateTime.Today.AddMonths(1);
            goal.StartAfterMonths = 2;
            Check(((string)reserveStatus.Invoke(null, new object[] { goal })!).Contains("empieza en 2 meses", StringComparison.Ordinal));
            goal.StartAfterRetirementGoal = true;
            Check(((string)reserveStatus.Invoke(null, new object[] { goal })!).Contains("después del objetivo", StringComparison.Ordinal));
            goal.ReachedMonth = null;
            Check(((string)reserveStatus.Invoke(null, new object[] { goal })!).Contains("pendiente", StringComparison.Ordinal));
            document.Retirement.InflationPeriod = null;
            typeof(RetirementView).GetMethod("UpdateInflationStatus", PrivateInstance)!.Invoke(view, null);
            Check(((TextBlock)view.FindName("InflationStatusText")).Text.Contains("sin período", StringComparison.Ordinal));

            var render = typeof(RetirementView).GetMethod("RenderProjection", PrivateInstance)!;
            var originalTarget = document.Retirement.TargetInvestedCents;
            document.Retirement.TargetInvestedCents = 0;
            render.Invoke(view, null);
            Check(!string.IsNullOrWhiteSpace(((TextBlock)view.FindName("ProjectionStatusText")).Text));
            document.Retirement.TargetInvestedCents = originalTarget;
            render.Invoke(view, null);

            var renderRunway = typeof(RetirementView).GetMethod("RenderRunway", PrivateInstance)!;
            renderRunway.Invoke(view, new object[] { new RetirementRunway
            {
                InitialMonthlyExpenseUsd = 1d,
                UsesInflationAdjustment = true,
                TargetYears = 60,
                SustainableMonthlyExpenseUsd = 2d
            } });
            Check(((TextBlock)view.FindName("RunwayDetailText")).Text.Contains("ajustado", StringComparison.Ordinal));
            renderRunway.Invoke(view, new object[] { new RetirementRunway
            {
                InitialMonthlyExpenseUsd = 1d,
                UsesInflationAdjustment = false,
                TargetYears = 60,
                SustainableMonthlyExpenseUsd = 2d
            } });
            Check(((TextBlock)view.FindName("RunwayDetailText")).Text.Contains("nominal", StringComparison.Ordinal));
            document.Retirement.BondAnnualReturnPercentage = document.Retirement.StockAnnualReturnPercentage + 1m;
            renderRunway.Invoke(view, new object[] { new RetirementRunway
            {
                InitialMonthlyExpenseUsd = 1d,
                TargetYears = 60,
                SustainableMonthlyExpenseUsd = 2d
            } });
            Check(((TextBlock)view.FindName("RunwayAssetsText")).Text.Contains("acciones antes que bonos", StringComparison.Ordinal));

            typeof(RetirementView).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
            Check(((TextBlock)view.FindName("SaveStatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
            var storeField = typeof(RetirementView).GetField("_store", PrivateInstance)!;
            var originalStore = storeField.GetValue(view);
            var lockedPath = path + ".locked";
            File.WriteAllText(lockedPath + ".tmp", "bloqueado");
            try
            {
                using var lockedTemporary = new FileStream(lockedPath + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                storeField.SetValue(view, new ScenarioStore(lockedPath));
                ((TextBlock)view.FindName("SaveStatusText")).Text = string.Empty;
                typeof(RetirementView).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
                Check(((TextBlock)view.FindName("SaveStatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
            }
            finally
            {
                storeField.SetValue(view, originalStore);
                File.Delete(lockedPath + ".tmp");
            }
            storeField.SetValue(view, new ScenarioStore("\0"));
            try
            {
                try
                {
                    typeof(RetirementView).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
                    throw new InvalidOperationException("La ruta inválida debía producir un error de argumento.");
                }
                catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException)
                {
                }
            }
            finally
            {
                storeField.SetValue(view, originalStore);
            }
        }
        finally
        {
            var temporary = path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
            Directory.Delete(path);
        }
    }

    private static void Invoke(RetirementView view, string name, object? sender = null) =>
        typeof(RetirementView).GetMethod(name, PrivateInstance)!
            .Invoke(view, new[] { sender ?? view, new RoutedEventArgs() });

    private static void Alert() =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(candidate => candidate.IsVisible);
            ((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Falló un estado verificable de jubilación.");
    }
}
