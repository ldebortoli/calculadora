using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class MarketFailureTypesTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        Verify(_ => MarketServiceTests.Json("{invalid"));
        Verify(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/v1/dolares/", StringComparison.Ordinal))
                return MarketServiceTests.Json("{\"compra\":\"incorrecto\",\"venta\":\"2\",\"fechaActualizacion\":\"2026-09-28T12:00:00Z\"}");
            if (path.EndsWith("depth", StringComparison.Ordinal))
                return MarketServiceTests.Json("{\"bids\":[[\"incorrecto\",\"100\"]],\"asks\":[[\"1\",\"100\"]]}");
            return MarketServiceTests.Json("{\"symbols\":[{\"filters\":[{\"filterType\":\"LOT_SIZE\",\"minQty\":\"1\",\"stepSize\":\"1\"}]}]}");
        });
        Verify(_ => throw new TaskCanceledException("Tiempo de espera simulado"));
        Verify(MarketServiceTests.FixtureResponse, feeConsumesTrade: true);
    }

    private static void Verify(Func<HttpRequestMessage, HttpResponseMessage> response, bool feeConsumesTrade = false)
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-provider-errors-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
            document.MusicSession.AutoRefreshEnabled = false;
            if (feeConsumesTrade)
            {
                var route = document.Scenarios[0].Routes.Find(candidate => candidate.LiveQuoteKey ==
                    Cashflow.Core.Models.MarketQuoteKeys.BinanceSellUsdcForUsdt)!;
                route.PercentageFee = 100m;
                route.FeeApplication = Cashflow.Core.Models.FeeApplicationMode.DeductFromAmount;
            }
            var store = new ScenarioStore(path);
            store.Save(document);
            using var client = MarketServiceTests.CreateClient(response);
            var updater = new ScenarioMarketUpdater(new BinanceSpotQuoteService(client));
            var rates = new ArgentinaExchangeRateService(client);
            var window = new MainWindow(store, updater, rates);
            window.Show();
            window.Hide();
            try
            {
                var refresh = typeof(MainWindow).GetMethod("RefreshInternetMarketsAsync", PrivateInstance)!;
                ((Task)refresh.Invoke(window, new object[] { 100m, true })!).GetAwaiter().GetResult();
                Check(((TextBlock)window.FindName("MarketStatusText")).Text.Contains(
                    feeConsumesTrade ? "Blue y oficial" : "No se pudo", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }

            var view = new MusicSessionWindow(document, store, updater, rates);
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var musicRefresh = typeof(MusicSessionWindow).GetMethod("RefreshMarketsAsync", PrivateInstance)!;
            ((Task)musicRefresh.Invoke(view, new object[] { true })!).GetAwaiter().GetResult();
            Check(((TextBlock)view.FindName("RefreshStatusText")).Text.Contains(
                feeConsumesTrade ? "Blue y oficial" : "No se pudo", StringComparison.Ordinal));
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("La cotización fallida reemplazó un valor persistido.");
    }
}
