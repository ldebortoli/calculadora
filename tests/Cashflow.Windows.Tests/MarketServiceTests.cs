using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cashflow.Core.Models;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class MarketServiceTests
{
    internal static HttpClient CreateFixtureClient(Func<HttpRequestMessage, bool>? shouldFail = null) => CreateClient(request =>
    {
        if (shouldFail?.Invoke(request) == true)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
        return FixtureResponse(request);
    });

    internal static HttpResponseMessage FixtureResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/v1/dolares/", StringComparison.Ordinal))
        {
            return Json("{\"compra\":1500,\"venta\":1550,\"fechaActualizacion\":\"2026-09-28T12:00:00Z\"}");
        }
        if (path.EndsWith("depth", StringComparison.Ordinal))
        {
            var price = request.RequestUri.Query.Contains("USDCUSDT", StringComparison.Ordinal) ? "1.001" : "1500";
            return Json("{\"bids\":[[\"" + price + "\",\"1000000\"]],\"asks\":[[\"" + price + "\",\"1000000\"]]}");
        }
        return Json("{\"symbols\":[{\"filters\":[" +
            "{\"filterType\":\"LOT_SIZE\",\"minQty\":\"1\",\"stepSize\":\"1\"}," +
            "{\"filterType\":\"NOTIONAL\",\"minNotional\":\"5\"}]}]}");
    }

    public static void Run()
    {
        ArgentinaRatesParseBothNumberFormats();
        InflationUsesMatchingMonthAndRejectsIncompleteData();
        BinanceQuotesAndMarketUpdaterUseOrderBooksAndRules();
    }

    private static void ArgentinaRatesParseBothNumberFormats()
    {
        var requests = new ConcurrentBag<string>();
        using var client = CreateClient(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath.EndsWith("blue", StringComparison.Ordinal)
                ? Json("{\"compra\":1510.5,\"venta\":1530.25,\"fechaActualizacion\":\"2026-09-28T12:00:00Z\"}")
                : Json("{\"compra\":\"1400.00\",\"venta\":\"1450.50\",\"fechaActualizacion\":\"2026-09-28T12:01:00Z\"}");
        });
        var rates = new ArgentinaExchangeRateService(client).GetRatesAsync().GetAwaiter().GetResult();
        Equal(1510.5m, rates.Blue.Buy);
        Equal(1530.25m, rates.Blue.Sell);
        Equal(1400m, rates.Official.Buy);
        Equal(1450.5m, rates.Official.Sell);
        Equal("DolarAPI", rates.Source);
        True(requests.Contains("/v1/dolares/blue") && requests.Contains("/v1/dolares/oficial"));

        using var failingClient = CreateClient(request => request.RequestUri!.AbsolutePath.EndsWith("oficial", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json("{\"compra\":1,\"venta\":2,\"fechaActualizacion\":\"2026-09-28T12:00:00Z\"}"));
        Throws<HttpRequestException>(() => new ArgentinaExchangeRateService(failingClient).GetRatesAsync().GetAwaiter().GetResult());
    }

    private static void InflationUsesMatchingMonthAndRejectsIncompleteData()
    {
        var year = DateTime.UtcNow.Year;
        var valid = Data($"{{\"year\":\"{year}\",\"period\":\"M08\",\"value\":\"120\"}}," +
            $"{{\"year\":\"{year - 1}\",\"period\":\"M08\",\"value\":\"100\"}}," +
            $"{{\"year\":\"{year}\",\"period\":\"M13\",\"value\":\"999\"}}");
        using var client = CreateClient(_ => Json(valid));
        var quote = new UsInflationService(client).GetLatestAsync().GetAwaiter().GetResult();
        Equal(20m, quote.Percentage);
        Equal(8, quote.Period.Month);
        Equal(year, quote.Period.Year);

        using var badStatus = CreateClient(_ => Json("{\"status\":\"REQUEST_FAILED\"}"));
        Throws<InvalidOperationException>(() => new UsInflationService(badStatus).GetLatestAsync().GetAwaiter().GetResult());
        using var noMonthly = CreateClient(_ => Json(Data($"{{\"year\":\"{year}\",\"period\":\"M13\",\"value\":\"120\"}}")));
        Throws<InvalidOperationException>(() => new UsInflationService(noMonthly).GetLatestAsync().GetAwaiter().GetResult());
        using var noPrevious = CreateClient(_ => Json(Data($"{{\"year\":\"{year}\",\"period\":\"M08\",\"value\":\"120\"}}")));
        Throws<InvalidOperationException>(() => new UsInflationService(noPrevious).GetLatestAsync().GetAwaiter().GetResult());
    }

    private static void BinanceQuotesAndMarketUpdaterUseOrderBooksAndRules()
    {
        var requests = new ConcurrentBag<string>();
        using var client = CreateClient(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            var pair = request.RequestUri.Query.Contains("USDCUSDT", StringComparison.Ordinal) ? "USDCUSDT" : "USDTARS";
            if (request.RequestUri.AbsolutePath.EndsWith("depth", StringComparison.Ordinal))
            {
                var price = pair == "USDCUSDT" ? "1.001" : "1500";
                return Json("{\"bids\":[[\"" + price + "\",\"100\"]],\"asks\":[[\"" + price + "\",\"100\"]]}");
            }
            var notionalType = pair == "USDCUSDT" ? "NOTIONAL" : "MIN_NOTIONAL";
            return Json("{\"symbols\":[{\"filters\":[" +
                "{\"filterType\":\"LOT_SIZE\",\"minQty\":\"1\",\"stepSize\":\"1\"}," +
                "{\"filterType\":\"" + notionalType + "\",\"minNotional\":\"5\"}]}]}");
        });
        var service = new BinanceSpotQuoteService(client);
        var quotes = service.GetQuotesAsync(2.7m, 3.2m).GetAwaiter().GetResult();
        Equal(1.001m, quotes.UsdtPerUsdc);
        Equal(1500m, quotes.ArsPerUsdt);
        Equal(1m, quotes.UsdcUsdtRules.BaseQuantityStep);
        Equal(5m, quotes.UsdtArsRules.MinimumNotional);
        True(requests.Count == 4);
        Throws<ArgumentOutOfRangeException>(() => service.GetQuotesAsync(0m, 1m).GetAwaiter().GetResult());
        Throws<ArgumentOutOfRangeException>(() => service.GetQuotesAsync(1m, 0m).GetAwaiter().GetResult());
        Throws<InvalidOperationException>(() => service.GetQuotesAsync(0.5m, 3m).GetAwaiter().GetResult());
        var roundDown = typeof(BinanceSpotQuoteService).GetMethod("RoundDownToStep",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Equal(2.7m, (decimal)roundDown.Invoke(null, new object[] { 2.7m, 0m })!);

        var document = StarterScenarioFactory.CreateStarterDocument();
        var update = new ScenarioMarketUpdater(service).UpdateBinanceAsync(document.Scenarios, 100m).GetAwaiter().GetResult();
        Equal(4, update.UpdatedRoutes);
        True(document.Scenarios.SelectMany(scenario => scenario.Routes)
            .Where(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs)
            .All(route => route.ExchangeRateConfigured && route.ExchangeRate == 1500m && route.InputAmountStep == 1m));
        Throws<ArgumentOutOfRangeException>(() => new ScenarioMarketUpdater(service).UpdateBinanceAsync(document.Scenarios, 0m).GetAwaiter().GetResult());
        var noLiveRoutes = StarterScenarioFactory.CreateEmpty("Sin cotizaciones");
        Equal(0, new ScenarioMarketUpdater(service).UpdateBinanceAsync(new[] { noLiveRoutes }, 100m)
            .GetAwaiter().GetResult().UpdatedRoutes);
        var feeConsumed = StarterScenarioFactory.CreateStarterDocument();
        var feeRoute = feeConsumed.Scenarios.SelectMany(scenario => scenario.Routes)
            .First(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt);
        feeRoute.FeeApplication = FeeApplicationMode.DeductFromAmount;
        feeRoute.PercentageFee = 100m;
        Throws<InvalidOperationException>(() => new ScenarioMarketUpdater(service)
            .UpdateBinanceAsync(feeConsumed.Scenarios, 100m).GetAwaiter().GetResult());
        var separatelyCharged = StarterScenarioFactory.CreateStarterDocument();
        var separateRoute = separatelyCharged.Scenarios.SelectMany(scenario => scenario.Routes)
            .First(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt);
        separateRoute.FeeApplication = FeeApplicationMode.ChargeSeparately;
        Equal(4, new ScenarioMarketUpdater(service).UpdateBinanceAsync(separatelyCharged.Scenarios, 100m)
            .GetAwaiter().GetResult().UpdatedRoutes);
    }

    private static string Data(string entries) => "{\"status\":\"REQUEST_SUCCEEDED\",\"Results\":{\"series\":[{\"data\":[" + entries + "]}]}}";

    internal static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("https://test.invalid") };

    internal static HttpResponseMessage Json(string value) => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("La condición esperada no se cumplió.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException($"Esperado: {expected}. Obtenido: {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Se esperaba " + typeof(T).Name + ".");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_response(request));
    }
}
