using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cashflow.Core.Calculation;

namespace Cashflow.Windows.Data
{
    public sealed class BinanceSpotQuoteService
    {
        private static readonly HttpClient Client = new HttpClient
        {
            BaseAddress = new Uri("https://api.binance.com"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<BinanceSpotQuotes> GetQuotesAsync(
            decimal usdcSellAmount,
            decimal usdtArsAmount,
            CancellationToken cancellationToken = default)
        {
            if (usdcSellAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(usdcSellAmount));
            if (usdtArsAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(usdtArsAmount));

            var stablecoinBookTask = GetOrderBookAsync("USDCUSDT", cancellationToken);
            var arsBookTask = GetOrderBookAsync("USDTARS", cancellationToken);
            var stablecoinRulesTask = GetSymbolRulesAsync("USDCUSDT", cancellationToken);
            var arsRulesTask = GetSymbolRulesAsync("USDTARS", cancellationToken);
            await Task.WhenAll(stablecoinBookTask, arsBookTask, stablecoinRulesTask, arsRulesTask);

            var stablecoinBook = await stablecoinBookTask;
            var arsBook = await arsBookTask;
            var stablecoinRules = await stablecoinRulesTask;
            var arsRules = await arsRulesTask;
            var roundedUsdcSellAmount = RoundDownToStep(usdcSellAmount, stablecoinRules.BaseQuantityStep);
            var roundedUsdtArsAmount = RoundDownToStep(usdtArsAmount, arsRules.BaseQuantityStep);
            if (roundedUsdcSellAmount <= 0m || roundedUsdtArsAmount <= 0m)
            {
                throw new InvalidOperationException("El monto no alcanza el paso mínimo de una orden Spot.");
            }

            return new BinanceSpotQuotes
            {
                UsdtPerUsdc = OrderBookQuoteCalculator.RateForSellingBase(stablecoinBook.Bids, roundedUsdcSellAmount),
                ArsPerUsdt = OrderBookQuoteCalculator.RateForSellingBase(arsBook.Bids, roundedUsdtArsAmount),
                UsdcUsdtRules = stablecoinRules,
                UsdtArsRules = arsRules,
                RetrievedAt = DateTimeOffset.Now
            };
        }

        private static async Task<OrderBook> GetOrderBookAsync(string symbol, CancellationToken cancellationToken)
        {
            using var response = await Client.GetAsync($"/api/v3/depth?symbol={symbol}&limit=100", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return new OrderBook
            {
                Bids = ParseLevels(document.RootElement.GetProperty("bids")),
                Asks = ParseLevels(document.RootElement.GetProperty("asks"))
            };
        }

        private static async Task<BinanceSymbolRules> GetSymbolRulesAsync(string symbol, CancellationToken cancellationToken)
        {
            using var response = await Client.GetAsync($"/api/v3/exchangeInfo?symbol={symbol}", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var rules = new BinanceSymbolRules();
            var symbolData = document.RootElement.GetProperty("symbols")[0];
            foreach (var filter in symbolData.GetProperty("filters").EnumerateArray())
            {
                var filterType = filter.GetProperty("filterType").GetString();
                if (filterType == "LOT_SIZE")
                {
                    rules.MinimumBaseQuantity = ParseDecimal(filter.GetProperty("minQty"));
                    rules.BaseQuantityStep = ParseDecimal(filter.GetProperty("stepSize"));
                }
                else if (filterType == "NOTIONAL" || filterType == "MIN_NOTIONAL")
                {
                    rules.MinimumNotional = ParseDecimal(filter.GetProperty("minNotional"));
                }
            }

            return rules;
        }

        private static IReadOnlyList<(decimal Price, decimal Quantity)> ParseLevels(JsonElement levels)
        {
            var result = new List<(decimal Price, decimal Quantity)>();
            foreach (var level in levels.EnumerateArray())
            {
                var price = decimal.Parse(level[0].GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
                var quantity = decimal.Parse(level[1].GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
                result.Add((price, quantity));
            }

            return result;
        }

        private static decimal ParseDecimal(JsonElement value) =>
            decimal.Parse(value.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);

        private static decimal RoundDownToStep(decimal amount, decimal step) =>
            step <= 0m ? amount : Math.Floor(amount / step) * step;

        private sealed class OrderBook
        {
            public IReadOnlyList<(decimal Price, decimal Quantity)> Bids { get; set; } = Array.Empty<(decimal, decimal)>();
            public IReadOnlyList<(decimal Price, decimal Quantity)> Asks { get; set; } = Array.Empty<(decimal, decimal)>();
        }
    }

    public sealed class BinanceSpotQuotes
    {
        public decimal UsdtPerUsdc { get; set; }
        public decimal ArsPerUsdt { get; set; }
        public BinanceSymbolRules UsdcUsdtRules { get; set; } = new BinanceSymbolRules();
        public BinanceSymbolRules UsdtArsRules { get; set; } = new BinanceSymbolRules();
        public DateTimeOffset RetrievedAt { get; set; }
    }

    public sealed class BinanceSymbolRules
    {
        public decimal MinimumBaseQuantity { get; set; }
        public decimal BaseQuantityStep { get; set; }
        public decimal MinimumNotional { get; set; }
    }
}
