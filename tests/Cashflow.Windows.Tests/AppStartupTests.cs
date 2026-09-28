using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class AppStartupTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(App app)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var defaultWindow = (MainWindow)typeof(App).GetMethod("CreateMainWindow", PrivateInstance)!.Invoke(app, null)!;
        defaultWindow.Close();
        ((Task)typeof(App).GetMethod("DelayStartupAsync", PrivateInstance)!.Invoke(app, null)!)
            .GetAwaiter().GetResult();
        typeof(App).GetMethod("SetApplicationIdentity", PrivateInstance)!.Invoke(app, null);

        var path = Path.Combine(Path.GetTempPath(), "cashflow-startup-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
            document.MusicSession.AutoRefreshEnabled = false;
            var store = new ScenarioStore(path);
            store.Save(document);
            using var client = MarketServiceTests.CreateFixtureClient();
            var createdWindows = 0;
            typeof(App).GetField("_windowFactory", PrivateInstance)!.SetValue(app, new Func<MainWindow>(() =>
            {
                createdWindows++;
                return new MainWindow(store,
                    new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                    new ArgentinaExchangeRateService(client));
            }));
            typeof(App).GetField("_identitySetter", PrivateInstance)!.SetValue(app,
                new Action(() => throw new InvalidOperationException("Identidad no disponible")));

            var firstDelay = Start(app);
            Check(Application.Current.Windows.OfType<SplashWindow>().Any(window => window.IsVisible));
            firstDelay.SetResult(true);
            PumpUntil(() => app.MainWindow is MainWindow window && window.IsVisible &&
                !Application.Current.Windows.OfType<SplashWindow>().Any(splash => splash.IsVisible));
            Check(!Application.Current.Windows.OfType<SplashWindow>().Any(window => window.IsVisible));
            Check(createdWindows == 1);
            app.MainWindow.Close();

            typeof(App).GetField("_identitySetter", PrivateInstance)!.SetValue(app, new Action(() => { }));
            var secondDelay = Start(app);
            var splash = Application.Current.Windows.OfType<SplashWindow>().Single(window => window.IsVisible);
            splash.Close();
            secondDelay.SetResult(true);
            var completion = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => completion.Continue = false));
            Dispatcher.PushFrame(completion);
            Check(createdWindows == 1);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TaskCompletionSource<bool> Start(App app)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(App).GetField("_startupDelay", PrivateInstance)!.SetValue(app, new Func<Task>(() => signal.Task));
        typeof(App).GetMethod("OnStartup", PrivateInstance)!
            .Invoke(app, new[] { Activator.CreateInstance(typeof(StartupEventArgs), nonPublic: true)! });
        return signal;
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("El arranque no completó su transición.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("La pantalla de inicio no respetó el cierre del usuario.");
    }
}
