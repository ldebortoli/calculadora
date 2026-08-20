using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Cashflow.Core.Input;
using Cashflow.Windows.Data;

namespace Cashflow.Windows
{
    public partial class MusicSessionWindow : UserControl
    {
        private readonly ScenarioDocument _document;
        private readonly ScenarioStore _store;
        private readonly MusicSessionCalculator _calculator = new MusicSessionCalculator();
        private readonly ArgentinaExchangeRateService _argentinaRates = new ArgentinaExchangeRateService();
        private readonly ScenarioMarketUpdater _marketUpdater = new ScenarioMarketUpdater();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private bool _refreshing;

        public MusicSessionWindow(ScenarioDocument document, ScenarioStore store)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            InitializeComponent();
            _timer.Tick += async (_, __) => await RefreshMarketsAsync(false);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            UpdateQuoteCards();
            ConfigureTimer();
            RenderCalculation();

            var settings = _document.MusicSession;
            var stale = !settings.InternetFetchedAt.HasValue ||
                DateTimeOffset.Now - settings.InternetFetchedAt.Value >= TimeSpan.FromMinutes(Math.Max(1, settings.RefreshMinutes));
            if (stale)
            {
                await RefreshMarketsAsync(false);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            if (TrySaveInputs(false))
            {
                TrySave();
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveInputs(true))
            {
                return;
            }

            await RefreshMarketsAsync(true);
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveInputs(true))
            {
                return;
            }

            ConfigureTimer();
            TrySave();
            RenderCalculation();
        }

        private async Task RefreshMarketsAsync(bool userRequested)
        {
            if (_refreshing)
            {
                return;
            }

            _refreshing = true;
            RefreshButton.IsEnabled = false;
            RefreshStatusText.Text = "Consultando dólar blue, oficial y libros Binance…";
            var argentinaOk = false;
            var binanceOk = false;
            try
            {
                try
                {
                    var rates = await _argentinaRates.GetRatesAsync();
                    var settings = _document.MusicSession;
                    settings.BlueBuy = rates.Blue.Buy;
                    settings.BlueSell = rates.Blue.Sell;
                    settings.BlueUpdatedAt = rates.Blue.UpdatedAt;
                    settings.OfficialBuy = rates.Official.Buy;
                    settings.OfficialSell = rates.Official.Sell;
                    settings.OfficialUpdatedAt = rates.Official.UpdatedAt;
                    settings.InternetFetchedAt = rates.FetchedAt;
                    settings.InternetSource = rates.Source;
                    argentinaOk = true;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException ||
                    exception is TaskCanceledException ||
                    exception is JsonException ||
                    exception is FormatException)
                {
                    // Se conservan las últimas cotizaciones guardadas.
                }

                try
                {
                    await _marketUpdater.UpdateBinanceAsync(_document.Scenarios, _document.MusicSession.TargetUsd);
                    binanceOk = true;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException ||
                    exception is TaskCanceledException ||
                    exception is JsonException ||
                    exception is FormatException ||
                    exception is InvalidOperationException)
                {
                    // El anexo puede seguir usando las últimas tasas Spot guardadas.
                }

                TrySave();
                UpdateQuoteCards();
                RenderCalculation();
                RefreshStatusText.Text = BuildRefreshStatus(argentinaOk, binanceOk, userRequested);
            }
            finally
            {
                _refreshing = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private void LoadSettings()
        {
            var settings = _document.MusicSession;
            TargetUsdBox.Text = settings.TargetUsd.ToString("0.##", CultureInfo.CurrentCulture);
            AutoRefreshCheck.IsChecked = settings.AutoRefreshEnabled;
            RefreshMinutesBox.Text = settings.RefreshMinutes.ToString(CultureInfo.CurrentCulture);
            CashUsdcRateBox.Text = settings.CashUsdPerUsdc.ToString("0.########", CultureInfo.CurrentCulture);
            CashUsdtRateBox.Text = settings.CashUsdPerUsdt.ToString("0.########", CultureInfo.CurrentCulture);
            PersonFeeBox.Text = settings.PersonFeePercentage.ToString("0.####", CultureInfo.CurrentCulture);
            BinanceUsdcFeeBox.Text = settings.BinanceUsdcTransferFee?.ToString("0.########", CultureInfo.CurrentCulture) ?? string.Empty;
            BinanceUsdtFeeBox.Text = settings.BinanceUsdtTransferFee?.ToString("0.########", CultureInfo.CurrentCulture) ?? string.Empty;
            OfficialExtraBox.Text = settings.OfficialPurchaseExtraPercentage.ToString("0.####", CultureInfo.CurrentCulture);
            OfficialAvailableCheck.IsChecked = settings.OfficialPurchaseAvailable;
        }

        private bool TrySaveInputs(bool showErrors)
        {
            if (!TryPositive(TargetUsdBox.Text, out var targetUsd))
            {
                return ValidationError("El monto de la sesión debe ser mayor que cero.", showErrors);
            }
            if (!int.TryParse(RefreshMinutesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var refreshMinutes) ||
                refreshMinutes < 1 || refreshMinutes > 1440)
            {
                return ValidationError("La actualización debe estar entre 1 y 1440 minutos.", showErrors);
            }
            if (!TryPositive(CashUsdcRateBox.Text, out var cashUsdc) || !TryPositive(CashUsdtRateBox.Text, out var cashUsdt))
            {
                return ValidationError("Los dólares en efectivo por USDC/USDT deben ser mayores que cero.", showErrors);
            }
            if (!TryPercentage(PersonFeeBox.Text, out var personFee) || personFee >= 100m)
            {
                return ValidationError("La comisión de la persona debe estar entre 0 y menos de 100.", showErrors);
            }
            if (!TryOptionalNonNegative(BinanceUsdcFeeBox.Text, out var binanceUsdcFee) ||
                !TryOptionalNonNegative(BinanceUsdtFeeBox.Text, out var binanceUsdtFee))
            {
                return ValidationError("Los costos de envío Binance deben quedar vacíos o ser mayores o iguales que cero.", showErrors);
            }
            if (!TryPercentage(OfficialExtraBox.Text, out var officialExtra))
            {
                return ValidationError("El recargo de compra oficial debe estar entre 0 y 100.", showErrors);
            }

            var settings = _document.MusicSession;
            settings.TargetUsd = targetUsd;
            settings.AutoRefreshEnabled = AutoRefreshCheck.IsChecked == true;
            settings.RefreshMinutes = refreshMinutes;
            settings.CashUsdPerUsdc = cashUsdc;
            settings.CashUsdPerUsdt = cashUsdt;
            settings.PersonFeePercentage = personFee;
            settings.BinanceUsdcTransferFee = binanceUsdcFee;
            settings.BinanceUsdtTransferFee = binanceUsdtFee;
            settings.OfficialPurchaseExtraPercentage = officialExtra;
            settings.OfficialPurchaseAvailable = OfficialAvailableCheck.IsChecked == true;
            return true;
        }

        private void ConfigureTimer()
        {
            _timer.Stop();
            if (_document.MusicSession.AutoRefreshEnabled)
            {
                _timer.Interval = TimeSpan.FromMinutes(Math.Max(1, _document.MusicSession.RefreshMinutes));
                _timer.Start();
            }
        }

        private void UpdateQuoteCards()
        {
            var settings = _document.MusicSession;
            if (settings.BlueBuy.HasValue && settings.BlueSell.HasValue)
            {
                var average = (settings.BlueBuy.Value + settings.BlueSell.Value) / 2m;
                BlueQuoteText.Text = $"C {settings.BlueBuy:N2} · V {settings.BlueSell:N2} · Prom. {average:N2}";
                BlueTimeText.Text = $"Dato: {FormatTimestamp(settings.BlueUpdatedAt)} · consulta: {FormatTimestamp(settings.InternetFetchedAt)} · {settings.InternetSource}";
            }
            else
            {
                BlueQuoteText.Text = "Pendiente de actualizar";
                BlueTimeText.Text = "Fuente automática: DolarAPI";
            }

            if (settings.OfficialBuy.HasValue && settings.OfficialSell.HasValue)
            {
                OfficialQuoteText.Text = $"Compra {settings.OfficialBuy:N2} · Venta {settings.OfficialSell:N2}";
                OfficialTimeText.Text = $"Dato: {FormatTimestamp(settings.OfficialUpdatedAt)} · consulta: {FormatTimestamp(settings.InternetFetchedAt)} · {settings.InternetSource}";
            }
            else
            {
                OfficialQuoteText.Text = "Pendiente de actualizar";
                OfficialTimeText.Text = "Fuente automática: DolarAPI";
            }
        }

        private void RenderCalculation()
        {
            MusicSessionCalculation calculation;
            try
            {
                calculation = _calculator.Calculate(_document);
            }
            catch (ArgumentException exception)
            {
                PendingText.Text = exception.Message;
                return;
            }

            OptionsPanel.Children.Clear();
            var withoutArs = calculation.Options
                .Where(option => option.Category == MusicSessionCategory.WithoutArs)
                .OrderBy(option => option.SourceDebitAmount)
                .ToArray();
            var bankedArs = calculation.Options
                .Where(option => option.Category == MusicSessionCategory.BankedArs)
                .OrderBy(option => option.SourceDebitAmount)
                .ToArray();

            RenderBestSummary(
                withoutArs.FirstOrDefault(),
                BestWithoutMethodText,
                BestWithoutPathText,
                BestWithoutCostText,
                "Faltan datos del camino sin pesos");
            RenderBestSummary(
                bankedArs.FirstOrDefault(),
                BestBankedMethodText,
                BestBankedPathText,
                BestBankedCostText,
                "Faltan cotizaciones para pesos bancarizados");

            AddOptionGroup("SIN PASAR POR PESOS", withoutArs, Color.FromRgb(13, 147, 125));
            AddOptionGroup("PESOS BANCARIZADOS · PODRÍAN REQUERIR FACTURACIÓN", bankedArs, Color.FromRgb(65, 111, 190));
            MusicGraph.ShowCalculation(calculation, _document.MusicSession.TargetUsd);

            var pending = calculation.Pending.Distinct().ToArray();
            PendingText.Text = pending.Length == 0 ? "Ninguno." : string.Join("  •  ", pending);
        }

        private static void RenderBestSummary(
            MusicSessionOption? option,
            TextBlock methodText,
            TextBlock pathText,
            TextBlock costText,
            string missingText)
        {
            if (option == null)
            {
                methodText.Text = missingText;
                pathText.Text = "Revisá las cotizaciones y los campos manuales.";
                costText.Text = "—";
                return;
            }

            methodText.Text = $"{option.Source} · {option.Method}";
            pathText.Text = option.Path;
            costText.Text = FormatMoney(option.SourceDebitAmount, option.SourceCurrency);
        }

        private void AddOptionGroup(string title, MusicSessionOption[] options, Color color)
        {
            if (options.Length == 0)
            {
                return;
            }

            OptionsPanel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(color),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 3, 0, 8)
            });
            for (var index = 0; index < options.Length; index++)
            {
                OptionsPanel.Children.Add(CreateOptionCard(options[index], index + 1, index == 0, color));
            }
        }

        private static Border CreateOptionCard(MusicSessionOption option, int rank, bool isCategoryBest, Color categoryColor)
        {
            var panel = new StackPanel();
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock
            {
                Text = $"#{rank}   {option.Source} · {option.Method}",
                Foreground = new SolidColorBrush(Color.FromRgb(225, 233, 245)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            var cost = new TextBlock
            {
                Text = FormatMoney(option.SourceDebitAmount, option.SourceCurrency),
                Foreground = new SolidColorBrush(isCategoryBest ? categoryColor : Color.FromRgb(177, 190, 210)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(14, 0, 0, 0)
            };
            Grid.SetColumn(cost, 1);
            heading.Children.Add(cost);
            panel.Children.Add(heading);

            var target = option.RequiredArs.HasValue
                ? $"Objetivo intermedio: {FormatMoney(option.RequiredArs.Value, "ARS")} · {option.TargetDetail}"
                : option.TargetDetail;
            panel.Children.Add(new TextBlock
            {
                Text = target,
                Foreground = new SolidColorBrush(Color.FromRgb(157, 172, 194)),
                FontSize = 10,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = option.Path,
                Foreground = new SolidColorBrush(Color.FromRgb(126, 143, 168)),
                FontSize = 9,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            if (option.UsesManualData)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "● Incluye uno o más datos manuales; revisá los campos naranjas antes de decidir.",
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 102, 18)),
                    FontSize = 9,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            return new Border
            {
                Background = new SolidColorBrush(isCategoryBest ? Color.FromRgb(20, 35, 59) : Color.FromRgb(23, 34, 53)),
                BorderBrush = new SolidColorBrush(isCategoryBest ? categoryColor : Color.FromRgb(42, 56, 82)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(13, 10, 13, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        private static string BuildRefreshStatus(bool argentinaOk, bool binanceOk, bool userRequested)
        {
            if (argentinaOk && binanceOk)
            {
                return $"Internet actualizado a las {DateTime.Now:HH:mm}. Próxima consulta según el intervalo configurado.";
            }
            if (argentinaOk)
            {
                return "Blue y oficial actualizados; Binance conservó sus últimas cotizaciones guardadas.";
            }
            if (binanceOk)
            {
                return "Binance actualizado; blue y oficial conservaron sus últimos valores guardados.";
            }

            return userRequested
                ? "No se pudo actualizar internet. Se conservaron todos los valores guardados."
                : "Sin conexión nueva; se usan los últimos valores guardados.";
        }

        private void TrySave()
        {
            try
            {
                _store.Save(_document);
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                RefreshStatusText.Text = "No se pudieron guardar los cambios locales.";
            }
        }

        private bool ValidationError(string message, bool show)
        {
            if (show)
            {
                MessageBox.Show(message, "Revisá los datos", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }

        private static bool TryPositive(string text, out decimal value) =>
            DecimalInputParser.TryParse(text, out value) && value > 0m;

        private static bool TryPercentage(string text, out decimal value) =>
            DecimalInputParser.TryParse(text, out value) && value >= 0m && value <= 100m;

        private static bool TryOptionalNonNegative(string text, out decimal? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            if (!DecimalInputParser.TryParse(text, out var parsed) || parsed < 0m)
            {
                return false;
            }
            value = parsed;
            return true;
        }

        private static string FormatTimestamp(DateTimeOffset? timestamp) =>
            timestamp.HasValue ? timestamp.Value.ToLocalTime().ToString("dd/MM HH:mm") : "sin fecha";

        private static string FormatMoney(decimal amount, string currency) =>
            $"{amount:N2} {currency.ToUpperInvariant()}";
    }
}
