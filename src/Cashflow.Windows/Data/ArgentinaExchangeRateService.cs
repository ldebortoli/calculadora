using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cashflow.Windows.Data
{
    public sealed class ArgentinaExchangeRateService
    {
        private static readonly HttpClient Client = new HttpClient
        {
            BaseAddress = new Uri("https://dolarapi.com"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        public async Task<ArgentinaExchangeRates> GetRatesAsync(CancellationToken cancellationToken = default)
        {
            var blueTask = GetRateAsync("/v1/dolares/blue", cancellationToken);
            var officialTask = GetRateAsync("/v1/dolares/oficial", cancellationToken);
            await Task.WhenAll(blueTask, officialTask);

            return new ArgentinaExchangeRates
            {
                Blue = await blueTask,
                Official = await officialTask,
                FetchedAt = DateTimeOffset.Now,
                Source = "DolarAPI"
            };
        }

        private static async Task<ArgentinaExchangeRate> GetRateAsync(string path, CancellationToken cancellationToken)
        {
            using var response = await Client.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            return new ArgentinaExchangeRate
            {
                Buy = ParseDecimal(root.GetProperty("compra")),
                Sell = ParseDecimal(root.GetProperty("venta")),
                UpdatedAt = root.GetProperty("fechaActualizacion").GetDateTimeOffset()
            };
        }

        private static decimal ParseDecimal(JsonElement element) =>
            element.ValueKind == JsonValueKind.Number
                ? element.GetDecimal()
                : decimal.Parse(element.GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public sealed class ArgentinaExchangeRates
    {
        public ArgentinaExchangeRate Blue { get; set; } = new ArgentinaExchangeRate();
        public ArgentinaExchangeRate Official { get; set; } = new ArgentinaExchangeRate();
        public DateTimeOffset FetchedAt { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public sealed class ArgentinaExchangeRate
    {
        public decimal Buy { get; set; }
        public decimal Sell { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
