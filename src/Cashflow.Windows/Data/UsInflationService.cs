using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cashflow.Windows.Data
{
    public sealed class UsInflationService
    {
        private const string SeriesId = "CUUR0000SA0";
        private static readonly HttpClient Client = new HttpClient
        {
            BaseAddress = new Uri("https://api.bls.gov"),
            Timeout = TimeSpan.FromSeconds(12)
        };

        public async Task<UsInflationQuote> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            var currentYear = DateTime.UtcNow.Year;
            var payload = JsonSerializer.Serialize(new
            {
                seriesid = new[] { SeriesId },
                startyear = (currentYear - 2).ToString(CultureInfo.InvariantCulture),
                endyear = currentYear.ToString(CultureInfo.InvariantCulture)
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Client.PostAsync("/publicAPI/v2/timeseries/data/", content, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "REQUEST_SUCCEEDED")
            {
                throw new InvalidOperationException("El BLS no devolvió una respuesta utilizable.");
            }

            var data = root.GetProperty("Results").GetProperty("series")[0].GetProperty("data");
            var points = new List<CpiPoint>();
            foreach (var item in data.EnumerateArray())
            {
                var period = item.GetProperty("period").GetString();
                if (period == null || period.Length != 3 || period[0] != 'M' ||
                    !int.TryParse(period.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
                    month < 1 || month > 12 ||
                    !int.TryParse(item.GetProperty("year").GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
                    !decimal.TryParse(item.GetProperty("value").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                points.Add(new CpiPoint(year, month, value));
            }

            var latest = points.OrderByDescending(point => point.Year).ThenByDescending(point => point.Month).FirstOrDefault();
            if (latest == null)
            {
                throw new InvalidOperationException("El BLS no publicó índices CPI-U mensuales en la respuesta.");
            }
            var previous = points.FirstOrDefault(point => point.Year == latest.Year - 1 && point.Month == latest.Month);
            if (previous == null || previous.Value <= 0m)
            {
                throw new InvalidOperationException("No se encontró el mismo mes del año anterior para calcular la inflación.");
            }

            var percentage = Math.Round((latest.Value / previous.Value - 1m) * 100m, 2, MidpointRounding.AwayFromZero);
            return new UsInflationQuote
            {
                Percentage = percentage,
                Period = new DateTimeOffset(latest.Year, latest.Month, 1, 0, 0, 0, TimeSpan.Zero),
                FetchedAt = DateTimeOffset.Now,
                Source = "U.S. Bureau of Labor Statistics · CPI-U All items (CUUR0000SA0)"
            };
        }

        private sealed class CpiPoint
        {
            public CpiPoint(int year, int month, decimal value)
            {
                Year = year;
                Month = month;
                Value = value;
            }

            public int Year { get; }
            public int Month { get; }
            public decimal Value { get; }
        }
    }

    public sealed class UsInflationQuote
    {
        public decimal Percentage { get; set; }
        public DateTimeOffset Period { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
