using System;
using System.Reflection;
using System.Windows;
using System.Collections.Generic;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class ConstructorContractTests
{
    public static void Run()
    {
        var document = StarterScenarioFactory.CreateStarterDocument();
        var store = new ScenarioStore();
        Throws<ArgumentNullException>(() => new MusicSessionWindow(null!, store));
        Throws<ArgumentNullException>(() => new MusicSessionWindow(document, null!));
        _ = new MusicSessionWindow(document, store);
        Throws<ArgumentNullException>(() => new RetirementView(null!, store));
        Throws<ArgumentNullException>(() => new RetirementView(document, null!));
        Throws<ArgumentNullException>(() => new ManualExchangeRatesWindow(null!, store));
        Throws<ArgumentNullException>(() => new ManualExchangeRatesWindow(document, null!));
        Throws<ArgumentNullException>(() => new RouteDetailsWindow(null!));
        Throws<ArgumentNullException>(() => ScenarioDocumentEditor.TryDeleteScenario(null!, "id", out _));
        var visible = new SplashWindow();
        visible.Show();
        var applyTheme = typeof(MainWindow).Assembly.GetType("Cashflow.Windows.WindowTheme")!
            .GetMethod("ApplyDarkTitleBar", BindingFlags.Public | BindingFlags.Static)!;
        applyTheme.Invoke(null, new object?[] { visible, null });
        var applied = new List<int>();
        Func<IntPtr, int, int, int, int> oldWindowsTheme = (_, attribute, _, _) =>
        {
            applied.Add(attribute);
            return attribute == 20 ? 1 : 0;
        };
        applyTheme.Invoke(null, new object[] { visible, oldWindowsTheme });
        if (applied.Count != 3 || applied[0] != 20 || applied[1] != 19 || applied[2] != 33)
            throw new InvalidOperationException("No se aplicó el tema compatible con Windows anterior.");
        if (!visible.IsVisible || new System.Windows.Interop.WindowInteropHelper(visible).Handle == IntPtr.Zero)
            throw new InvalidOperationException("La ventana de prueba no obtuvo un identificador nativo.");
        visible.Close();
        var unshown = new MainWindow();
        var fit = typeof(MainWindow).GetMethod("FitToCurrentWorkArea", BindingFlags.NonPublic | BindingFlags.Instance)!;
        fit.Invoke(unshown, new object?[] { null });
        fit.Invoke(unshown, new object[] { new Func<IntPtr, IntPtr>(_ => IntPtr.Zero) });
        fit.Invoke(unshown, new object[] { new Func<IntPtr, IntPtr>(_ => new IntPtr(1)) });
        unshown.Close();
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Se esperaba " + typeof(T).Name + ".");
    }
}
