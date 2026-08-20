using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cashflow.Core.Calculation;
using Cashflow.Core.Input;
using Cashflow.Core.Models;
using Cashflow.Windows.Data;

namespace Cashflow.Windows
{
    public partial class MainWindow : Window
    {
        private readonly ScenarioStore _store = new ScenarioStore();
        private readonly RouteCalculator _calculator = new RouteCalculator();
        private readonly ScenarioMarketUpdater _marketUpdater = new ScenarioMarketUpdater();
        private readonly ArgentinaExchangeRateService _argentinaRates = new ArgentinaExchangeRateService();
        private readonly DispatcherTimer _marketTimer = new DispatcherTimer();
        private readonly FeeApplicationChoice[] _feeApplicationChoices =
        {
            new FeeApplicationChoice(FeeApplicationMode.DeductFromAmount, "Se descuenta del monto"),
            new FeeApplicationChoice(FeeApplicationMode.ChargeSeparately, "Se cobra aparte")
        };
        private ScenarioDocument _document = new ScenarioDocument();
        private CashflowScenario? _scenario;
        private PlatformNode? _selectedNode;
        private TransferRoute? _selectedRoute;
        private bool _loading;
        private bool _marketRefreshInProgress;

        public MainWindow()
        {
            InitializeComponent();
            NodeKindCombo.ItemsSource = Enum.GetValues(typeof(NodeKind));
            RouteFeeApplicationCombo.ItemsSource = _feeApplicationChoices;
            Graph.NodeSelected += SelectNode;
            Graph.RouteSelected += SelectRoute;
            Graph.GraphChanged += SaveSilently;
            _marketTimer.Tick += async (_, __) => await RefreshInternetMarketsAsync(GetMarketSampleAmount(), false);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FitToCurrentWorkArea();
            _document = _store.Load();
            MusicSessionHost.Content = new MusicSessionWindow(_document, _store);
            RetirementHost.Content = new RetirementView(_document, _store);
            _loading = true;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            var active = _document.Scenarios.FirstOrDefault(item => item.Id == _document.ActiveScenarioId)
                ?? _document.Scenarios.First();
            ScenarioCombo.SelectedItem = active;
            _loading = false;
            ActivateScenario(active);
            SaveSilently();
            ConfigureMarketTimer();
            var refreshMinutes = Math.Max(1, _document.MusicSession.RefreshMinutes);
            if (!_document.MusicSession.InternetFetchedAt.HasValue ||
                DateTimeOffset.Now - _document.MusicSession.InternetFetchedAt.Value >= TimeSpan.FromMinutes(refreshMinutes))
            {
                _ = RefreshInternetMarketsAsync(GetMarketSampleAmount(), false);
            }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _marketTimer.Stop();
            SaveSilently();
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
            var rounded = 2;
            DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        }

        private void FitToCurrentWorkArea()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(handle, 2);
            var information = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref information))
            {
                return;
            }

            var source = PresentationSource.FromVisual(this);
            var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = fromDevice.Transform(new Point(information.WorkArea.Left, information.WorkArea.Top));
            var bottomRight = fromDevice.Transform(new Point(information.WorkArea.Right, information.WorkArea.Bottom));
            const double margin = 14d;
            var availableWidth = Math.Max(720d, bottomRight.X - topLeft.X - margin * 2d);
            var availableHeight = Math.Max(560d, bottomRight.Y - topLeft.Y - margin * 2d);
            MinWidth = Math.Min(MinWidth, availableWidth);
            MinHeight = Math.Min(MinHeight, availableHeight);
            MaxWidth = availableWidth;
            MaxHeight = availableHeight;
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
            Left = topLeft.X + (bottomRight.X - topLeft.X - Width) / 2d;
            Top = topLeft.Y + (bottomRight.Y - topLeft.Y - Height) / 2d;
            WindowState = WindowState.Normal;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation information);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInformation
        {
            public int Size;
            public NativeRectangle MonitorArea;
            public NativeRectangle WorkArea;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private void ActivateScenario(CashflowScenario scenario)
        {
            _scenario = scenario;
            _document.ActiveScenarioId = scenario.Id;
            ScenarioNameBox.Text = scenario.Name;
            _selectedNode = null;
            _selectedRoute = null;
            Graph.Scenario = scenario;
            Graph.SelectedNodeId = null;
            Graph.SelectedRouteId = null;
            Graph.HighlightedRouteIds = Array.Empty<string>();
            RefreshNodeLists();
            ShowEmptyEditor();
            ClearResults();
            Graph.RefreshGraph();
        }

        private void RefreshNodeLists(string? sourceId = null, string? destinationId = null)
        {
            if (_scenario == null) return;

            sourceId ??= (MainSourceCombo.SelectedItem as PlatformNode)?.Id;
            destinationId ??= (MainDestinationCombo.SelectedItem as PlatformNode)?.Id;
            var nodes = _scenario.Nodes.ToList();

            _loading = true;
            MainSourceCombo.ItemsSource = nodes;
            MainDestinationCombo.ItemsSource = nodes;
            RouteFromCombo.ItemsSource = nodes;
            RouteToCombo.ItemsSource = nodes;
            MainSourceCombo.SelectedItem = nodes.FirstOrDefault(node => node.Id == sourceId)
                ?? nodes.FirstOrDefault(node => node.Kind == NodeKind.Source)
                ?? nodes.FirstOrDefault();
            MainDestinationCombo.SelectedItem = nodes.FirstOrDefault(node => node.Id == destinationId)
                ?? nodes.FirstOrDefault(node => node.Kind == NodeKind.Destination)
                ?? nodes.LastOrDefault();
            _loading = false;
        }

        private void ScenarioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ScenarioCombo.SelectedItem is not CashflowScenario scenario) return;
            SaveSilently();
            ActivateScenario(scenario);
        }

        private void NewScenario_Click(object sender, RoutedEventArgs e)
        {
            SaveScenarioName();
            var scenario = StarterScenarioFactory.CreateEmpty($"Nuevo escenario {_document.Scenarios.Count + 1}");
            _document.Scenarios.Add(scenario);
            _loading = true;
            ScenarioCombo.ItemsSource = null;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            ScenarioCombo.SelectedItem = scenario;
            _loading = false;
            ActivateScenario(scenario);
            SaveSilently();
        }

        private void NewGrabrFiScenario_Click(object sender, RoutedEventArgs e)
        {
            SaveScenarioName();
            var scenario = StarterScenarioFactory.CreateGrabrFiTemplate($"GrabrFi · escenario {_document.Scenarios.Count + 1}");
            _document.Scenarios.Add(scenario);
            ManualExchangeRateSynchronizer.EnsureSynchronized(_document);
            _loading = true;
            ScenarioCombo.ItemsSource = null;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            ScenarioCombo.SelectedItem = scenario;
            _loading = false;
            ActivateScenario(scenario);
            SaveSilently();
        }

        private void NewWallbitScenario_Click(object sender, RoutedEventArgs e)
        {
            SaveScenarioName();
            var scenario = StarterScenarioFactory.CreateWallbitTemplate($"Wallbit Pro · escenario {_document.Scenarios.Count + 1}");
            _document.Scenarios.Add(scenario);
            ManualExchangeRateSynchronizer.EnsureSynchronized(_document);
            _loading = true;
            ScenarioCombo.ItemsSource = null;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            ScenarioCombo.SelectedItem = scenario;
            _loading = false;
            ActivateScenario(scenario);
            SaveSilently();
        }

        private void DeleteScenario_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null) return;
            if (_document.Scenarios.Count <= 1)
            {
                MessageBox.Show(this, "Tiene que quedar al menos un escenario.", "Calculadora", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = MessageBox.Show(
                this,
                $"¿Eliminar el escenario “{_scenario.Name}”? Esta acción quita ese grafo y no se puede deshacer desde la aplicación.",
                "Eliminar escenario",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes ||
                !ScenarioDocumentEditor.TryDeleteScenario(_document, _scenario.Id, out var nextScenario) ||
                nextScenario == null)
            {
                return;
            }

            _loading = true;
            ScenarioCombo.ItemsSource = null;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            ScenarioCombo.SelectedItem = nextScenario;
            _loading = false;
            ActivateScenario(nextScenario);
            SaveSilently();
        }

        private void OpenManualRates_Click(object sender, RoutedEventArgs e)
        {
            SaveScenarioName();
            var window = new ManualExchangeRatesWindow(_document, _store) { Owner = this };
            window.ShowDialog();
            if (_selectedRoute != null)
            {
                SelectRoute(_selectedRoute);
            }
            Graph.RefreshGraph();
            ClearResults();
            SaveSilently();
        }

        private void AddNode_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null) return;
            var count = _scenario.Nodes.Count;
            var node = new PlatformNode
            {
                Name = $"Nueva plataforma {count + 1}",
                Currency = "USD",
                Kind = NodeKind.Intermediate,
                X = 70 + (count % 3) * 215,
                Y = 70 + (count / 3) * 120
            };
            _scenario.Nodes.Add(node);
            RefreshNodeLists();
            SelectNode(node);
            SaveSilently();
        }

        private void AddRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null || _scenario.Nodes.Count < 2)
            {
                MessageBox.Show(this, "Necesitás al menos dos plataformas para crear una transición.", "Calculadora", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var from = _selectedNode
                ?? MainSourceCombo.SelectedItem as PlatformNode
                ?? _scenario.Nodes.First();
            var to = MainDestinationCombo.SelectedItem as PlatformNode;
            if (to == null || to.Id == from.Id)
            {
                to = _scenario.Nodes.First(node => node.Id != from.Id);
            }

            var route = new TransferRoute
            {
                FromNodeId = from.Id,
                ToNodeId = to.Id,
                Label = "Nueva transición",
                ExchangeRate = 1m,
                ExchangeRateConfigured = from.Currency.Equals(to.Currency, StringComparison.OrdinalIgnoreCase),
                ExchangeRateIsManual = !from.Currency.Equals(to.Currency, StringComparison.OrdinalIgnoreCase)
            };
            _scenario.Routes.Add(route);
            ManualExchangeRateSynchronizer.EnsureSynchronized(_document);
            SelectRoute(route);
            SaveSilently();
        }

        private void SelectNode(PlatformNode node)
        {
            _selectedNode = node;
            _selectedRoute = null;
            Graph.SelectedNodeId = node.Id;
            Graph.SelectedRouteId = null;
            InspectorTitle.Text = node.Name;
            InspectorSubtitle.Text = "Plataforma o cuenta del circuito";
            NodeNameBox.Text = node.Name;
            NodeCurrencyBox.Text = node.Currency;
            NodeKindCombo.SelectedItem = node.Kind;
            var manualRoutes = _scenario?.Routes
                .Where(route => route.Enabled && route.FromNodeId == node.Id && route.ExchangeRateIsManual)
                .Select(route => route.Label)
                .ToArray() ?? Array.Empty<string>();
            NodeManualNotice.Visibility = manualRoutes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NodeManualNoticeText.Text = manualRoutes.Length > 0
                ? "Este nodo tiene datos manuales para revisar: " + string.Join(" · ", manualRoutes) + ". Seleccioná la línea naranja correspondiente."
                : string.Empty;
            EmptyEditorPanel.Visibility = Visibility.Collapsed;
            RouteEditorPanel.Visibility = Visibility.Collapsed;
            NodeEditorPanel.Visibility = Visibility.Visible;
            Graph.RefreshGraph();
        }

        private void SelectRoute(TransferRoute route)
        {
            if (_scenario == null) return;
            _selectedNode = null;
            _selectedRoute = route;
            Graph.SelectedNodeId = null;
            Graph.SelectedRouteId = route.Id;
            InspectorTitle.Text = route.Label;
            InspectorSubtitle.Text = route.ExchangeRateConfigured
                ? "Transición dirigida y sus costos"
                : "Transición pendiente de actualizar su cotización";
            RouteLabelBox.Text = route.Label;
            RouteFromCombo.SelectedItem = _scenario.Nodes.FirstOrDefault(node => node.Id == route.FromNodeId);
            RouteToCombo.SelectedItem = _scenario.Nodes.FirstOrDefault(node => node.Id == route.ToNodeId);
            RoutePercentageBox.Text = route.PercentageFee.ToString("0.####", CultureInfo.CurrentCulture);
            RouteFixedBox.Text = route.FixedFee.ToString("0.####", CultureInfo.CurrentCulture);
            RouteMinimumFeeBox.Text = route.PercentageFeeMinimum?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteMaximumFeeBox.Text = route.PercentageFeeMaximum?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteFeeApplicationCombo.SelectedItem = _feeApplicationChoices.First(choice => choice.Mode == route.FeeApplication);
            RouteTradingFeeBox.Text = route.TradingFeePercentage.ToString("0.####", CultureInfo.CurrentCulture);
            RouteOutputFeeBox.Text = route.OutputPercentageFee.ToString("0.####", CultureInfo.CurrentCulture);
            RouteInputStepBox.Text = route.InputAmountStep?.ToString("0.########", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteMinimumAmountBox.Text = route.MinimumInputAmount?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteMaximumAmountBox.Text = route.MaximumInputAmount?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteMinimumOutputBox.Text = route.MinimumOutputAmount?.ToString("0.####", CultureInfo.CurrentCulture) ?? string.Empty;
            RouteRateBox.Text = route.ExchangeRateConfigured
                ? route.ExchangeRate.ToString("0.########", CultureInfo.CurrentCulture)
                : string.Empty;
            RouteManualRateCheck.IsChecked = route.ExchangeRateIsManual;
            RouteEnabledCheck.IsChecked = route.Enabled;
            UpdateRouteRateCopy();
            EmptyEditorPanel.Visibility = Visibility.Collapsed;
            NodeEditorPanel.Visibility = Visibility.Collapsed;
            RouteEditorPanel.Visibility = Visibility.Visible;
            Graph.RefreshGraph();
        }

        private void RouteEndpoint_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRouteRateCopy();
        }

        private void RouteManualRateCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateRouteRateCopy();
        }

        private void UpdateRouteRateCopy()
        {
            if (RouteRateLabel == null || RouteRateBox == null || RouteRateHint == null ||
                RouteMinimumAmountLabel == null || RouteMaximumAmountLabel == null ||
                RouteInputStepLabel == null || RouteMinimumOutputLabel == null)
            {
                return;
            }

            if (RouteFromCombo.SelectedItem is not PlatformNode from || RouteToCombo.SelectedItem is not PlatformNode to)
            {
                RouteRateLabel.Text = "Tipo de cambio";
                RouteRateHint.Text = "Unidades de moneda destino por cada unidad neta de moneda origen. Se guarda localmente al aplicar cambios.";
                RouteMinimumAmountLabel.Text = "Monto mínimo";
                RouteMaximumAmountLabel.Text = "Monto máximo";
                RouteInputStepLabel.Text = "Paso de orden límite";
                RouteMinimumOutputLabel.Text = "Mínimo recibido";
                return;
            }

            RouteRateLabel.Text = $"Cotización ({to.Currency.ToUpperInvariant()} por {from.Currency.ToUpperInvariant()})";
            RouteMinimumAmountLabel.Text = $"Monto mínimo ({from.Currency.ToUpperInvariant()})";
            RouteMaximumAmountLabel.Text = $"Monto máximo ({from.Currency.ToUpperInvariant()})";
            RouteInputStepLabel.Text = $"Paso orden límite ({from.Currency.ToUpperInvariant()})";
            RouteMinimumOutputLabel.Text = $"Mínimo recibido ({to.Currency.ToUpperInvariant()})";
            var pending = _selectedRoute?.ExchangeRateConfigured == false && string.IsNullOrWhiteSpace(RouteRateBox.Text)
                ? "Pendiente: "
                : string.Empty;
            var selectedLiveQuoteMatches = _selectedRoute != null &&
                !string.IsNullOrWhiteSpace(_selectedRoute.LiveQuoteKey) &&
                LiveQuoteStillMatches(_selectedRoute, from, to);
            var live = !selectedLiveQuoteMatches
                ? string.Empty
                : "Usá ‘Actualizar mercados Binance’ para calcularla desde el libro Spot. ";
            RouteRateHint.Text = pending + live + $"También podés ingresar cuántas unidades de {to.Currency.ToUpperInvariant()} entrega una unidad neta de {from.Currency.ToUpperInvariant()}; el valor se guarda localmente.";

            var isLive = selectedLiveQuoteMatches;
            RouteManualRateCheck.IsEnabled = !isLive;
            if (isLive)
            {
                RouteManualRateCheck.IsChecked = false;
            }

            var isManual = !isLive && RouteManualRateCheck.IsChecked == true;
            RouteRateBox.Background = new SolidColorBrush(isManual ? Color.FromRgb(44, 35, 23) : Color.FromRgb(17, 26, 42));
            RouteRateBox.BorderBrush = new SolidColorBrush(isManual ? Color.FromRgb(114, 80, 37) : Color.FromRgb(42, 56, 82));
            RouteManualNotice.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            var reviewedAt = _selectedRoute?.ManualExchangeRateUpdatedAt;
            var reviewed = reviewedAt.HasValue
                ? reviewedAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                : "sin fecha registrada";
            RouteManualNoticeText.Text = isManual
                ? $"● Dato manual · última revisión: {reviewed}. Copiá aquí la cotización que ofrece la plataforma y tocá Aplicar cambios."
                : string.Empty;
        }

        private void ShowEmptyEditor()
        {
            InspectorTitle.Text = "Editá tu circuito";
            InspectorSubtitle.Text = "Seleccioná una plataforma o una transición del mapa.";
            EmptyEditorPanel.Visibility = Visibility.Visible;
            NodeEditorPanel.Visibility = Visibility.Collapsed;
            RouteEditorPanel.Visibility = Visibility.Collapsed;
        }

        private void ApplyNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null) return;
            var name = NodeNameBox.Text.Trim();
            var currency = NodeCurrencyBox.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currency) || NodeKindCombo.SelectedItem is not NodeKind kind)
            {
                ShowValidation("Completá el nombre, la moneda y el rol de la plataforma.");
                return;
            }

            _selectedNode.Name = name;
            _selectedNode.Currency = currency;
            _selectedNode.Kind = kind;
            foreach (var route in _scenario!.Routes.Where(route =>
                         route.ExchangeRateIsManual &&
                         (route.FromNodeId == _selectedNode.Id || route.ToNodeId == _selectedNode.Id)))
            {
                var from = _scenario.Nodes.First(node => node.Id == route.FromNodeId);
                var to = _scenario.Nodes.First(node => node.Id == route.ToNodeId);
                route.ManualExchangeRateKey = ManualExchangeRateSynchronizer.CreateKey(from.Name, from.Currency, to.Currency);
            }
            ManualExchangeRateSynchronizer.EnsureSynchronized(_document);
            var selectedId = _selectedNode.Id;
            RefreshNodeLists();
            SelectNode(_scenario.Nodes.First(node => node.Id == selectedId));
            RefreshScenarioPicker();
            SaveSilently();
        }

        private void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null || _selectedNode == null) return;
            var result = MessageBox.Show(
                this,
                $"¿Eliminar “{_selectedNode.Name}” y todas sus transiciones?",
                "Eliminar plataforma",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            var id = _selectedNode.Id;
            _scenario.Nodes.Remove(_selectedNode);
            _scenario.Routes.RemoveAll(route => route.FromNodeId == id || route.ToNodeId == id);
            _selectedNode = null;
            RefreshNodeLists();
            ShowEmptyEditor();
            ClearResults();
            Graph.SelectedNodeId = null;
            Graph.RefreshGraph();
            SaveSilently();
        }

        private void ApplyRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRoute == null || RouteFromCombo.SelectedItem is not PlatformNode from || RouteToCombo.SelectedItem is not PlatformNode to)
            {
                return;
            }
            if (from.Id == to.Id)
            {
                ShowValidation("El origen y el destino de una transición deben ser distintos.");
                return;
            }
            if (!DecimalInputParser.TryParse(RoutePercentageBox.Text, out var percentage) || percentage < 0m || percentage > 100m)
            {
                ShowValidation("La comisión porcentual debe estar entre 0 y 100.");
                return;
            }
            if (!DecimalInputParser.TryParse(RouteFixedBox.Text, out var fixedFee) || fixedFee < 0m)
            {
                ShowValidation("La comisión fija debe ser un número mayor o igual que cero.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteMinimumFeeBox.Text, out var minimumFee))
            {
                ShowValidation("El mínimo de la comisión porcentual debe quedar vacío o ser un número mayor o igual que cero.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteMaximumFeeBox.Text, out var maximumFee))
            {
                ShowValidation("El máximo de la comisión porcentual debe quedar vacío o ser un número mayor o igual que cero.");
                return;
            }
            if (minimumFee.HasValue && maximumFee.HasValue && minimumFee.Value > maximumFee.Value)
            {
                ShowValidation("El mínimo de comisión no puede ser mayor que el máximo.");
                return;
            }
            if (RouteFeeApplicationCombo.SelectedItem is not FeeApplicationChoice feeApplication)
            {
                ShowValidation("Elegí cómo se cobra la comisión.");
                return;
            }
            if (!DecimalInputParser.TryParse(RouteTradingFeeBox.Text, out var tradingFee) || tradingFee < 0m || tradingFee > 100m)
            {
                ShowValidation("El fee de trade debe estar entre 0 y 100.");
                return;
            }
            if (!DecimalInputParser.TryParse(RouteOutputFeeBox.Text, out var outputFee) || outputFee < 0m || outputFee > 100m)
            {
                ShowValidation("El cargo sobre la salida debe estar entre 0 y 100.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteInputStepBox.Text, out var inputStep) || inputStep == 0m)
            {
                ShowValidation("El paso de la orden debe quedar vacío o ser un número mayor que cero.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteMinimumAmountBox.Text, out var minimumAmount))
            {
                ShowValidation("El monto mínimo debe quedar vacío o ser un número mayor o igual que cero.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteMaximumAmountBox.Text, out var maximumAmount) || maximumAmount == 0m)
            {
                ShowValidation("El monto máximo debe quedar vacío o ser un número mayor que cero.");
                return;
            }
            if (minimumAmount.HasValue && maximumAmount.HasValue && minimumAmount.Value > maximumAmount.Value)
            {
                ShowValidation("El monto mínimo no puede ser mayor que el máximo.");
                return;
            }
            if (!TryParseOptionalNonNegative(RouteMinimumOutputBox.Text, out var minimumOutput))
            {
                ShowValidation("El mínimo recibido debe quedar vacío o ser un número mayor o igual que cero.");
                return;
            }
            if (!DecimalInputParser.TryParse(RouteRateBox.Text, out var rate) || rate <= 0m)
            {
                ShowValidation("El tipo de cambio debe ser mayor que cero.");
                return;
            }

            _selectedRoute.Label = string.IsNullOrWhiteSpace(RouteLabelBox.Text) ? "Transferencia" : RouteLabelBox.Text.Trim();
            _selectedRoute.FromNodeId = from.Id;
            _selectedRoute.ToNodeId = to.Id;
            _selectedRoute.PercentageFee = percentage;
            _selectedRoute.PercentageFeeMinimum = minimumFee;
            _selectedRoute.PercentageFeeMaximum = maximumFee;
            _selectedRoute.FixedFee = fixedFee;
            _selectedRoute.FeeApplication = feeApplication.Mode;
            _selectedRoute.TradingFeePercentage = tradingFee;
            _selectedRoute.OutputPercentageFee = outputFee;
            _selectedRoute.InputAmountStep = inputStep;
            _selectedRoute.MinimumInputAmount = minimumAmount;
            _selectedRoute.MaximumInputAmount = maximumAmount;
            _selectedRoute.MinimumOutputAmount = minimumOutput;
            _selectedRoute.ExchangeRate = rate;
            _selectedRoute.ExchangeRateConfigured = true;
            if (!LiveQuoteStillMatches(_selectedRoute, from, to))
            {
                _selectedRoute.LiveQuoteKey = null;
            }
            var isManual = string.IsNullOrWhiteSpace(_selectedRoute.LiveQuoteKey) &&
                RouteManualRateCheck.IsChecked == true;
            if (isManual)
            {
                ManualExchangeRateSynchronizer.MarkAndApply(
                    _document,
                    _selectedRoute,
                    from,
                    to,
                    rate,
                    DateTimeOffset.Now);
            }
            else
            {
                _selectedRoute.ExchangeRateIsManual = false;
                _selectedRoute.ManualExchangeRateKey = null;
                _selectedRoute.ManualExchangeRateUpdatedAt = null;
            }
            _selectedRoute.Enabled = RouteEnabledCheck.IsChecked == true;
            SelectRoute(_selectedRoute);
            ClearResults();
            SaveSilently();
        }

        private void DeleteRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null || _selectedRoute == null) return;
            _scenario.Routes.Remove(_selectedRoute);
            _selectedRoute = null;
            Graph.SelectedRouteId = null;
            ShowEmptyEditor();
            ClearResults();
            Graph.RefreshGraph();
            SaveSilently();
        }

        private void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (_scenario == null || MainSourceCombo.SelectedItem is not PlatformNode source || MainDestinationCombo.SelectedItem is not PlatformNode destination)
            {
                ShowValidation("Elegí un origen y un destino.");
                return;
            }
            if (!DecimalInputParser.TryParse(AmountBox.Text, out var amount) || amount <= 0m)
            {
                ShowValidation("Ingresá un monto mayor que cero.");
                return;
            }

            try
            {
                var results = _calculator.Calculate(_scenario, source.Id, destination.Id, amount);
                RenderResults(results);
                SaveSilently();
            }
            catch (ArgumentException exception)
            {
                ShowValidation(exception.Message);
            }
        }

        private async void UpdateBinanceSpot_Click(object sender, RoutedEventArgs e)
        {
            if (!DecimalInputParser.TryParse(AmountBox.Text, out var amount) || amount <= 0m)
            {
                ShowValidation("Ingresá un monto mayor que cero antes de actualizar los mercados.");
                return;
            }

            await RefreshInternetMarketsAsync(amount, true);
        }

        private async Task RefreshInternetMarketsAsync(decimal amount, bool userRequested)
        {
            if (_marketRefreshInProgress || amount <= 0m)
            {
                return;
            }

            _marketRefreshInProgress = true;
            BinanceUpdateButton.IsEnabled = false;
            MarketStatusText.Text = "Consultando Binance Spot, dólar blue y dólar oficial…";
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
                    exception is HttpRequestException || exception is TaskCanceledException ||
                    exception is FormatException || exception is JsonException)
                {
                    // Las últimas cotizaciones locales siguen disponibles.
                }

                try
                {
                    await _marketUpdater.UpdateBinanceAsync(_document.Scenarios, amount);
                    binanceOk = true;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException || exception is TaskCanceledException ||
                    exception is InvalidOperationException || exception is FormatException || exception is JsonException)
                {
                    // Las últimas cotizaciones Spot guardadas siguen disponibles.
                }

                if (_selectedRoute?.LiveQuoteKey != null)
                {
                    SelectRoute(_selectedRoute);
                }
                Graph.RefreshGraph();
                ClearResults();
                SaveSilently();
                MarketStatusText.Text = BuildMarketStatus(argentinaOk, binanceOk, userRequested);
            }
            finally
            {
                _marketRefreshInProgress = false;
                BinanceUpdateButton.IsEnabled = true;
            }
        }

        private void ConfigureMarketTimer()
        {
            _marketTimer.Stop();
            if (_document.MusicSession.AutoRefreshEnabled)
            {
                _marketTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, _document.MusicSession.RefreshMinutes));
                _marketTimer.Start();
            }
        }

        private decimal GetMarketSampleAmount() =>
            DecimalInputParser.TryParse(AmountBox.Text, out var amount) && amount > 0m
                ? amount
                : Math.Max(1m, _document.MusicSession.TargetUsd);

        private static string BuildMarketStatus(bool argentinaOk, bool binanceOk, bool userRequested)
        {
            if (argentinaOk && binanceOk)
            {
                return $"Mercados actualizados · {DateTime.Now:HH:mm}. Binance Spot, blue y oficial al día.";
            }
            if (argentinaOk)
            {
                return "Blue y oficial actualizados; Binance conserva sus últimos valores.";
            }
            if (binanceOk)
            {
                return "Binance actualizado; blue y oficial conservan sus últimos valores.";
            }
            return userRequested
                ? "No se pudo actualizar internet. Se conservaron todos los valores guardados."
                : "Sin actualización nueva; se usan los últimos valores guardados.";
        }

        private void RenderResults(IReadOnlyList<RouteResult> results)
        {
            ResultsPanel.Children.Clear();
            if (results.Count == 0)
            {
                ResultSummaryText.Text = "No hay una ruta válida entre los nodos elegidos.";
                BestAmountText.Text = "Sin ruta";
                BestPathText.Text = "Revisá las transiciones activas";
                Graph.HighlightedRouteIds = Array.Empty<string>();
                Graph.RefreshGraph();
                return;
            }

            var best = results[0];
            ResultSummaryText.Text = $"{results.Count} alternativa{(results.Count == 1 ? string.Empty : "s")} · mayor llegada sin superar el presupuesto total";
            BestAmountText.Text = FormatMoney(best.FinalAmount, best.DestinationCurrency);
            BestPathText.Text = "Mejor importe a destino";
            Graph.HighlightedRouteIds = best.RouteIds;
            Graph.RefreshGraph();

            for (var index = 0; index < Math.Min(results.Count, 8); index++)
            {
                ResultsPanel.Children.Add(CreateResultCard(results[index], index));
            }
        }

        private Border CreateResultCard(RouteResult result, int index)
        {
            var panel = new StackPanel();
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var path = new TextBlock
            {
                Text = $"#{index + 1}   {result.PathLabel}",
                Foreground = new SolidColorBrush(Color.FromRgb(225, 233, 245)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var amount = new TextBlock
            {
                Text = FormatMoney(result.FinalAmount, result.DestinationCurrency),
                Foreground = new SolidColorBrush(index == 0 ? Color.FromRgb(74, 222, 196) : Color.FromRgb(177, 190, 210)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(14, 0, 0, 0)
            };
            Grid.SetColumn(amount, 1);
            heading.Children.Add(path);
            heading.Children.Add(amount);
            panel.Children.Add(heading);

            var details = string.Join("   |   ", result.Steps.Select(FormatStep));
            panel.Children.Add(new TextBlock
            {
                Text = details,
                Foreground = new SolidColorBrush(Color.FromRgb(157, 172, 194)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Hacé clic para abrir el detalle completo",
                Foreground = new SolidColorBrush(Color.FromRgb(13, 147, 125)),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 0)
            });

            var card = new Border
            {
                Background = new SolidColorBrush(index == 0 ? Color.FromRgb(16, 43, 41) : Color.FromRgb(23, 34, 53)),
                BorderBrush = new SolidColorBrush(index == 0 ? Color.FromRgb(27, 107, 93) : Color.FromRgb(42, 56, 82)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 7),
                Cursor = Cursors.Hand,
                Child = panel
            };
            card.MouseLeftButtonUp += (_, __) =>
            {
                Graph.HighlightedRouteIds = result.RouteIds;
                Graph.RefreshGraph();
                var detailsWindow = new RouteDetailsWindow(result) { Owner = this };
                detailsWindow.ShowDialog();
            };
            return card;
        }

        private void ClearResults()
        {
            ResultsPanel.Children.Clear();
            ResultSummaryText.Text = "Ingresá un monto y calculá para comparar alternativas.";
            BestAmountText.Text = "—";
            BestPathText.Text = "Mejor resultado";
            Graph.HighlightedRouteIds = Array.Empty<string>();
            Graph.RefreshGraph();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveScenarioName();
            try
            {
                _store.Save(_document);
                SaveStatusText.Text = "Guardado localmente · " + DateTime.Now.ToString("HH:mm");
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "No se pudo guardar el archivo local. " + exception.Message, "Calculadora", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSilently()
        {
            if (_loading || _scenario == null) return;
            SaveScenarioName();
            try
            {
                _store.Save(_document);
                SaveStatusText.Text = "Guardado localmente · " + DateTime.Now.ToString("HH:mm");
            }
            catch (Exception exception) when (exception is System.IO.IOException || exception is UnauthorizedAccessException)
            {
                SaveStatusText.Text = "No se pudo guardar; usá Guardar cambios";
            }
        }

        private void SaveScenarioName()
        {
            if (_scenario == null) return;
            var name = ScenarioNameBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(name) && name != _scenario.Name)
            {
                _scenario.Name = name;
                RefreshScenarioPicker();
            }
        }

        private void RefreshScenarioPicker()
        {
            if (_scenario == null) return;
            var current = _scenario;
            _loading = true;
            ScenarioCombo.ItemsSource = null;
            ScenarioCombo.ItemsSource = _document.Scenarios;
            ScenarioCombo.SelectedItem = current;
            _loading = false;
        }

        private static string FormatMoney(decimal amount, string currency) =>
            $"{amount:N2} {currency.ToUpperInvariant()}";

        private static string FormatStep(RouteStepResult step)
        {
            var parts = new List<string>();
            if (step.Route.FeeApplication == FeeApplicationMode.ChargeSeparately)
            {
                parts.Add($"presupuesto {FormatMoney(step.InputAmount, step.From.Currency)}");
                parts.Add($"envía {FormatMoney(step.TradeableInputAmount, step.From.Currency)}");
                if (step.FeeAmount > 0m)
                {
                    parts.Add($"comisión incluida {FormatMoney(step.FeeAmount, step.From.Currency)}");
                }
                parts.Add($"débito total {FormatMoney(step.DebitedAmount, step.From.Currency)}");
            }
            else if (step.InputRemainder > 0m)
            {
                parts.Add($"opera {FormatMoney(step.TradeableInputAmount, step.From.Currency)} de {FormatMoney(step.InputAmount, step.From.Currency)}");
            }
            else
            {
                parts.Add(FormatMoney(step.TradeableInputAmount, step.From.Currency));
            }

            if (step.InputRemainder > 0.00000001m)
            {
                parts.Add($"saldo no usado {FormatMoney(step.InputRemainder, step.From.Currency)}");
            }

            if (step.FeeAmount > 0m && step.Route.FeeApplication == FeeApplicationMode.DeductFromAmount)
            {
                parts.Add($"cargo descontado {FormatMoney(step.FeeAmount, step.From.Currency)}");
            }

            if (step.TradingFeeAmount > 0m)
            {
                parts.Add($"fee trade {FormatMoney(step.TradingFeeAmount, step.To.Currency)}");
            }

            if (step.OutputFeeAmount > 0m)
            {
                parts.Add($"cargo salida {FormatMoney(step.OutputFeeAmount, step.To.Currency)}");
            }

            return $"{step.Route.Label}: {string.Join(", ", parts)} → neto {FormatMoney(step.OutputAmount, step.To.Currency)}";
        }

        private static bool LiveQuoteStillMatches(TransferRoute route, PlatformNode from, PlatformNode to) =>
            route.LiveQuoteKey switch
            {
                MarketQuoteKeys.BinanceSellUsdcForUsdt => from.Currency.Equals("USDC", StringComparison.OrdinalIgnoreCase) && to.Currency.Equals("USDT", StringComparison.OrdinalIgnoreCase),
                MarketQuoteKeys.BinanceSellUsdtForArs => from.Currency.Equals("USDT", StringComparison.OrdinalIgnoreCase) && to.Currency.Equals("ARS", StringComparison.OrdinalIgnoreCase),
                _ => true
            };

        private static bool TryParseOptionalNonNegative(string text, out decimal? value)
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

        private void ShowValidation(string message)
        {
            MessageBox.Show(this, message, "Revisá los datos", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private sealed class FeeApplicationChoice
        {
            public FeeApplicationChoice(FeeApplicationMode mode, string label)
            {
                Mode = mode;
                Label = label;
            }

            public FeeApplicationMode Mode { get; }
            public string Label { get; }
        }
    }
}
