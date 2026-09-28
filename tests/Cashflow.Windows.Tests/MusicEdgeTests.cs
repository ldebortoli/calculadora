using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class MusicEdgeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-music-edges-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.InternetFetchedAt = null;
            document.MusicSession.AutoRefreshEnabled = false;
            using var client = MarketServiceTests.CreateFixtureClient();
            var view = new MusicSessionWindow(document, new ScenarioStore(path),
                new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                new ArgentinaExchangeRateService(client));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(document.MusicSession.InternetFetchedAt.HasValue);
            var loadSettings = typeof(MusicSessionWindow).GetMethod("LoadSettings", PrivateInstance)!;
            document.MusicSession.BinanceUsdcTransferFee = null;
            document.MusicSession.BinanceUsdtTransferFee = null;
            loadSettings.Invoke(view, null);
            Check(((TextBox)view.FindName("BinanceUsdcFeeBox")).Text == string.Empty);
            Check(((TextBox)view.FindName("BinanceUsdtFeeBox")).Text == string.Empty);
            document.MusicSession.BinanceUsdcTransferFee = 0m;
            document.MusicSession.BinanceUsdtTransferFee = 0m;
            loadSettings.Invoke(view, null);

            document.MusicSession.BlueBuy = 1500m;
            document.MusicSession.BlueSell = 1550m;
            document.MusicSession.OfficialSell = 1500m;
            document.MusicSession.OfficialPurchaseAvailable = true;
            typeof(MusicSessionWindow).GetMethod("RenderCalculation", PrivateInstance)!.Invoke(view, null);
            Check(((TextBlock)view.FindName("PendingText")).Text == "Ninguno.");

            var tryPositive = typeof(MusicSessionWindow).GetMethod("TryPositive", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check(!(bool)tryPositive.Invoke(null, new object[] { "incorrecto", 0m })!);
            Check(!(bool)tryPositive.Invoke(null, new object[] { "0", 0m })!);
            Check((bool)tryPositive.Invoke(null, new object[] { "1", 0m })!);
            var tryPercentage = typeof(MusicSessionWindow).GetMethod("TryPercentage", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var (text, expected) in new[] { ("incorrecto", false), ("-1", false), ("101", false), ("50", true) })
                Check((bool)tryPercentage.Invoke(null, new object[] { text, 0m })! == expected);
            var timer = (DispatcherTimer)typeof(MusicSessionWindow).GetField("_timer", PrivateInstance)!.GetValue(view)!;
            var frame = new DispatcherFrame();
            EventHandler stopTimer = (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Tick += stopTimer;
            timer.Interval = TimeSpan.FromMilliseconds(1);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Tick -= stopTimer;

            var target = (TextBox)view.FindName("TargetUsdBox");
            var savedTarget = target.Text;
            target.Text = "0";
            Alert();
            Invoke(view, "Calculate_Click");
            Alert();
            Invoke(view, "Refresh_Click");
            target.Text = savedTarget;
            Invoke(view, "Refresh_Click");
            Check(((Button)view.FindName("RefreshButton")).IsEnabled);

            var refresh = typeof(MusicSessionWindow).GetMethod("RefreshMarketsAsync", PrivateInstance)!;
            typeof(MusicSessionWindow).GetField("_refreshing", PrivateInstance)!.SetValue(view, true);
            ((Task)refresh.Invoke(view, new object[] { true })!).GetAwaiter().GetResult();
            typeof(MusicSessionWindow).GetField("_refreshing", PrivateInstance)!.SetValue(view, false);

            typeof(MusicSessionWindow).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
            Check(((TextBlock)view.FindName("RefreshStatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
            var storeField = typeof(MusicSessionWindow).GetField("_store", PrivateInstance)!;
            var normalStore = storeField.GetValue(view);
            var lockedPath = path + ".locked";
            File.WriteAllText(lockedPath + ".tmp", "bloqueado");
            try
            {
                using var lockedTemporary = new FileStream(lockedPath + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                storeField.SetValue(view, new ScenarioStore(lockedPath));
                ((TextBlock)view.FindName("RefreshStatusText")).Text = string.Empty;
                typeof(MusicSessionWindow).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
                Check(((TextBlock)view.FindName("RefreshStatusText")).Text.Contains("No se pudieron", StringComparison.Ordinal));
            }
            finally
            {
                storeField.SetValue(view, normalStore);
                File.Delete(lockedPath + ".tmp");
            }
            storeField.SetValue(view, new ScenarioStore("\0"));
            try
            {
                try
                {
                    typeof(MusicSessionWindow).GetMethod("TrySave", PrivateInstance)!.Invoke(view, null);
                    throw new InvalidOperationException("La ruta inválida debía producir un error de argumento.");
                }
                catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException)
                {
                }
            }
            finally
            {
                storeField.SetValue(view, normalStore);
            }
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            var temporary = path + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);
            Directory.Delete(path);
        }
    }

    private static void Invoke(MusicSessionWindow view, string name) =>
        typeof(MusicSessionWindow).GetMethod(name, PrivateInstance)!
            .Invoke(view, new object[] { view, new RoutedEventArgs() });

    private static void Alert() =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(candidate => candidate.IsVisible);
            ((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Falló un estado verificable de sesión musical.");
    }
}
