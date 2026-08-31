using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;

namespace Cashflow.Windows
{
    public partial class RouteDetailsWindow : Window
    {
        public RouteDetailsWindow(RouteResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            InitializeComponent();
            WindowTheme.ApplyDarkTitleBar(this);

            PathText.Text = result.PathLabel;
            var sourceCurrency = result.Steps.Count > 0 ? result.Steps[0].From.Currency : string.Empty;
            BudgetText.Text = FormatMoney(result.SourceBudgetAmount, sourceCurrency);
            DebitText.Text = FormatMoney(result.SourceDebitedAmount, sourceCurrency);
            FinalAmountText.Text = FormatMoney(result.FinalAmount, result.DestinationCurrency);

            for (var index = 0; index < result.Steps.Count; index++)
            {
                StepsPanel.Children.Add(CreateStepCard(result.Steps[index], index + 1));
            }
        }

        private static Border CreateStepCard(RouteStepResult step, int number)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = $"TRAMO {number}",
                Foreground = new SolidColorBrush(Color.FromRgb(84, 214, 190)),
                FontSize = 9,
                FontWeight = FontWeights.Bold
            });
            content.Children.Add(new TextBlock
            {
                Text = step.Route.Label,
                Foreground = new SolidColorBrush(Color.FromRgb(231, 237, 247)),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{step.From.Name} ({step.From.Currency})  →  {step.To.Name} ({step.To.Currency})",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 165, 188)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 10)
            });

            AddLine(content, "Saldo disponible", FormatMoney(step.InputAmount, step.From.Currency));
            AddLine(content, "Monto transferido u operado", FormatMoney(step.TradeableInputAmount, step.From.Currency));
            AddLine(content, "Regla de comisión", BuildFeeRule(step.Route));
            if (step.FeeAmount > 0m)
            {
                var feeMode = step.Route.FeeApplication == FeeApplicationMode.ChargeSeparately
                    ? "incluida dentro del presupuesto"
                    : "descontada del monto operado";
                AddLine(content, $"Comisión de entrada ({feeMode})", FormatMoney(step.FeeAmount, step.From.Currency));
            }
            AddLine(content, "Débito total del tramo", FormatMoney(step.DebitedAmount, step.From.Currency));
            if (step.InputRemainder > 0.00000001m)
            {
                AddLine(content, "Saldo no utilizado", FormatMoney(step.InputRemainder, step.From.Currency));
            }
            AddLine(content, "Cotización aplicada", $"1 {step.From.Currency.ToUpperInvariant()} = {step.Route.ExchangeRate:N8} {step.To.Currency.ToUpperInvariant()}");
            if (step.Route.ExchangeRateIsManual)
            {
                var reviewed = step.Route.ManualExchangeRateUpdatedAt.HasValue
                    ? step.Route.ManualExchangeRateUpdatedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                    : "sin fecha registrada";
                AddLine(content, "Origen de la cotización", $"Manual · última revisión: {reviewed}", Color.FromRgb(241, 185, 85));
            }
            else if (!string.IsNullOrWhiteSpace(step.Route.LiveQuoteKey))
            {
                AddLine(content, "Origen de la cotización", "Mercado actualizado desde internet");
            }
            AddLine(content, "Salida bruta luego del cambio", FormatMoney(step.GrossOutputAmount, step.To.Currency));
            if (step.TradingFeeAmount > 0m)
            {
                AddLine(content, "Fee de trade", FormatMoney(step.TradingFeeAmount, step.To.Currency));
            }
            if (step.OutputFeeAmount > 0m)
            {
                AddLine(content, "Cargo sobre la salida", FormatMoney(step.OutputFeeAmount, step.To.Currency));
            }
            AddLine(content, "Neto que pasa al siguiente nodo", FormatMoney(step.OutputAmount, step.To.Currency), Color.FromRgb(84, 214, 190), true);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(16, 24, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 82)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 11),
                Child = content
            };
        }

        private static void AddLine(
            Panel panel,
            string label,
            string value,
            Color? valueColor = null,
            bool bold = false)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 165, 188)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            });
            var valueText = new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(valueColor ?? Color.FromRgb(217, 226, 240)),
                FontSize = 10,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);
            panel.Children.Add(row);
        }

        private static string BuildFeeRule(TransferRoute route)
        {
            var percentage = route.PercentageFee > 0m ? $"{route.PercentageFee:N4}%" : "0%";
            var bounds = route.PercentageFeeMinimum.HasValue || route.PercentageFeeMaximum.HasValue
                ? $" · mínimo {route.PercentageFeeMinimum?.ToString("N4", CultureInfo.CurrentCulture) ?? "—"} · máximo {route.PercentageFeeMaximum?.ToString("N4", CultureInfo.CurrentCulture) ?? "—"}"
                : string.Empty;
            var fixedFee = route.FixedFee > 0m ? $" + {route.FixedFee:N4} fijo" : string.Empty;
            return percentage + bounds + fixedFee;
        }

        private static string FormatMoney(decimal amount, string currency) =>
            $"{amount:N2} {currency.ToUpperInvariant()}";

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
