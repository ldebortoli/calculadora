using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cashflow.Core.Input;
using Cashflow.Windows.Data;

namespace Cashflow.Windows
{
    public partial class ManualExchangeRatesWindow : Window
    {
        private readonly ScenarioDocument _document;
        private readonly ScenarioStore _store;
        private readonly List<RateEditor> _editors = new List<RateEditor>();

        public ManualExchangeRatesWindow(ScenarioDocument document, ScenarioStore store)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            InitializeComponent();
            ManualExchangeRateSynchronizer.EnsureSynchronized(_document);
            BuildEditors();
        }

        private void BuildEditors()
        {
            RatesPanel.Children.Clear();
            _editors.Clear();
            foreach (var setting in _document.ManualExchangeRates.OrderBy(setting => setting.ProviderName).ThenBy(setting => setting.Key))
            {
                var routes = _document.Scenarios
                    .SelectMany(scenario => scenario.Routes)
                    .Count(route => route.ExchangeRateIsManual && route.ManualExchangeRateKey == setting.Key);
                var input = new TextBox
                {
                    Text = setting.ExchangeRate.ToString("0.########", CultureInfo.CurrentCulture),
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromRgb(255, 247, 232)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(241, 199, 126))
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
                var copy = new StackPanel();
                copy.Children.Add(new TextBlock
                {
                    Text = setting.ProviderName,
                    Foreground = new SolidColorBrush(Color.FromRgb(35, 48, 71)),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                });
                copy.Children.Add(new TextBlock
                {
                    Text = $"{setting.ToCurrency} por {setting.FromCurrency} · {routes} transición(es) vinculada(s)",
                    Foreground = new SolidColorBrush(Color.FromRgb(105, 115, 134)),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 12, 0)
                });
                copy.Children.Add(new TextBlock
                {
                    Text = setting.UpdatedAt.HasValue
                        ? "Última revisión: " + setting.UpdatedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                        : "Todavía no tiene una fecha de revisión",
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 102, 18)),
                    FontSize = 10,
                    Margin = new Thickness(0, 5, 12, 0)
                });
                grid.Children.Add(copy);
                Grid.SetColumn(input, 1);
                grid.Children.Add(input);
                RatesPanel.Children.Add(new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 239)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(11),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 10),
                    Child = grid
                });
                _editors.Add(new RateEditor(setting, input));
            }

            if (_editors.Count == 0)
            {
                RatesPanel.Children.Add(new TextBlock
                {
                    Text = "No hay cotizaciones marcadas como manuales en los escenarios actuales.",
                    Foreground = new SolidColorBrush(Color.FromRgb(105, 115, 134)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 8, 4, 0)
                });
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var parsed = new List<(RateEditor Editor, decimal Rate)>();
            foreach (var editor in _editors)
            {
                if (!DecimalInputParser.TryParse(editor.Input.Text, out var rate) || rate <= 0m)
                {
                    MessageBox.Show(this, $"La cotización de {editor.Setting.ProviderName} debe ser mayor que cero.", "Revisá los valores", MessageBoxButton.OK, MessageBoxImage.Information);
                    editor.Input.Focus();
                    editor.Input.SelectAll();
                    return;
                }
                parsed.Add((editor, rate));
            }

            var updatedAt = DateTimeOffset.Now;
            foreach (var item in parsed)
            {
                ManualExchangeRateSynchronizer.Apply(
                    _document,
                    item.Editor.Setting.Key,
                    item.Editor.Setting.ProviderName,
                    item.Editor.Setting.FromCurrency,
                    item.Editor.Setting.ToCurrency,
                    item.Rate,
                    updatedAt);
            }

            try
            {
                _store.Save(_document);
                DialogResult = true;
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                StatusText.Text = "No se pudieron guardar los valores: " + exception.Message;
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 56));
            }
        }

        private sealed class RateEditor
        {
            public RateEditor(ManualExchangeRateSetting setting, TextBox input)
            {
                Setting = setting;
                Input = input;
            }

            public ManualExchangeRateSetting Setting { get; }
            public TextBox Input { get; }
        }
    }
}
