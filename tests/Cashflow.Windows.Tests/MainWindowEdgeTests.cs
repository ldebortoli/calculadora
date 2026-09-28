using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Cashflow.Core.Calculation;
using Cashflow.Core.Models;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;

namespace Cashflow.Windows.Tests;

internal static class MainWindowEdgeTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-main-edges-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.AutoRefreshEnabled = true;
            document.MusicSession.InternetFetchedAt = null;
            var store = new ScenarioStore(path);
            store.Save(document);
            using var client = MarketServiceTests.CreateFixtureClient();
            var window = new MainWindow(store,
                new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                new ArgentinaExchangeRateService(client));
            window.Show();
            window.Hide();
            try
            {
                var marketTimer = (DispatcherTimer)typeof(MainWindow).GetField("_marketTimer", PrivateInstance)!.GetValue(window)!;
                var frame = new DispatcherFrame();
                EventHandler stopTimer = (_, _) => { marketTimer.Stop(); frame.Continue = false; };
                marketTimer.Tick += stopTimer;
                marketTimer.Interval = TimeSpan.FromMilliseconds(1);
                marketTimer.Start();
                Dispatcher.PushFrame(frame);
                marketTimer.Tick -= stopTimer;
                var live = (ScenarioDocument)typeof(MainWindow).GetField("_document", PrivateInstance)!.GetValue(window)!;
                var scenario = live.Scenarios[0];
                var second = live.Scenarios[1];
                var scenarios = (ComboBox)window.FindName("ScenarioCombo");
                scenarios.SelectedItem = second;
                Check(ReferenceEquals(((GraphCanvas)window.FindName("Graph")).Scenario, second));
                scenarios.SelectedItem = scenario;
                Check(ReferenceEquals(((GraphCanvas)window.FindName("Graph")).Scenario, scenario));

                live.Scenarios.Remove(second);
                Alert("AcceptButton", "al menos un escenario");
                Click(window, "DeleteScenario_Click");
                Check(live.Scenarios.Count == 1);
                live.Scenarios.Add(second);

                var nodes = scenario.Nodes.ToArray();
                scenario.Nodes.RemoveRange(1, scenario.Nodes.Count - 1);
                Alert("AcceptButton", "al menos dos plataformas");
                Click(window, "AddRoute_Click");
                Check(scenario.Routes.Count == 9);
                scenario.Nodes.AddRange(nodes.Skip(1));
                typeof(MainWindow).GetMethod("RefreshNodeLists", PrivateInstance)!.Invoke(window, new object?[] { null, null });

                var source = scenario.Nodes.Single(node => node.Name == "GrabrFi" && node.Kind == NodeKind.Source);
                SelectNode(window, source);
                Check(((Border)window.FindName("NodeManualNotice")).Visibility == Visibility.Visible);
                var nameBox = (TextBox)window.FindName("NodeNameBox");
                nameBox.Text = string.Empty;
                Alert("AcceptButton", "nombre, la moneda");
                Click(window, "ApplyNode_Click");
                Check(source.Name == "GrabrFi");
                nameBox.Text = "GrabrFi verificada";
                Click(window, "ApplyNode_Click");
                Check(source.Name == "GrabrFi verificada");
                Check(scenario.Routes.Where(route => route.ExchangeRateIsManual && route.FromNodeId == source.Id)
                    .All(route => !string.IsNullOrWhiteSpace(route.ManualExchangeRateKey)));

                var destination = (ComboBox)window.FindName("MainDestinationCombo");
                destination.SelectedItem = source;
                SelectNode(window, source);
                Click(window, "AddRoute_Click");
                var added = scenario.Routes.Last();
                Check(added.ToNodeId != source.Id);
                Click(window, "DeleteRoute_Click");
                destination.SelectedItem = scenario.Nodes.First(node => node.Kind == NodeKind.Destination && node.Currency == "ARS");

                var manual = scenario.Routes.First(route => route.ExchangeRateIsManual);
                SelectRoute(window, manual);
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                    new Action(() => Application.Current.Windows.OfType<ManualExchangeRatesWindow>()
                        .Single(candidate => candidate.IsVisible).Close()));
                Click(window, "OpenManualRates_Click");
                Check(ReferenceEquals(typeof(MainWindow).GetField("_selectedRoute", PrivateInstance)!.GetValue(window), manual));

                var from = (ComboBox)window.FindName("RouteFromCombo");
                var to = (ComboBox)window.FindName("RouteToCombo");
                var originalTo = to.SelectedItem;
                to.SelectedItem = from.SelectedItem;
                Alert("AcceptButton", "deben ser distintos");
                Click(window, "ApplyRoute_Click");
                to.SelectedItem = originalTo;
                var feeChoice = (ComboBox)window.FindName("RouteFeeApplicationCombo");
                var originalChoice = feeChoice.SelectedItem;
                feeChoice.SelectedItem = null;
                Alert("AcceptButton", "Elegí cómo se cobra");
                Click(window, "ApplyRoute_Click");
                feeChoice.SelectedItem = originalChoice;

                var liveRoute = scenario.Routes.First(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdcForUsdt);
                SelectRoute(window, liveRoute);
                var manualCheck = (CheckBox)window.FindName("RouteManualRateCheck");
                manualCheck.IsChecked = true;
                Check(manualCheck.IsChecked == false && !manualCheck.IsEnabled);
                from.SelectedItem = source;
                ((TextBox)window.FindName("RouteRateBox")).Text = "1";
                Click(window, "ApplyRoute_Click");
                Check(liveRoute.LiveQuoteKey == null && !liveRoute.ExchangeRateIsManual);

                var amount = (TextBox)window.FindName("AmountBox");
                var mainSource = (ComboBox)window.FindName("MainSourceCombo");
                var originalSource = mainSource.SelectedItem;
                var originalDestination = destination.SelectedItem;
                mainSource.SelectedItem = null;
                Alert("AcceptButton", "origen y un destino");
                Click(window, "Calculate_Click");
                mainSource.SelectedItem = originalSource;
                destination.SelectedItem = originalDestination;
                amount.Text = "0";
                Alert("AcceptButton", "monto mayor que cero");
                Click(window, "Calculate_Click");
                Alert("AcceptButton", "antes de actualizar");
                Click(window, "UpdateBinanceSpot_Click");
                amount.Text = "2500";
                destination.SelectedItem = mainSource.SelectedItem;
                Alert("AcceptButton", "distintos");
                Click(window, "Calculate_Click");
                destination.SelectedItem = originalDestination;

                var originalEnabled = scenario.Routes.Select(route => route.Enabled).ToArray();
                foreach (var route in scenario.Routes) route.Enabled = false;
                Click(window, "Calculate_Click");
                Check(((TextBlock)window.FindName("ResultSummaryText")).Text.Contains("No hay una ruta", StringComparison.Ordinal));
                for (var index = 0; index < scenario.Routes.Count; index++) scenario.Routes[index].Enabled = originalEnabled[index];

                Click(window, "Calculate_Click");
                var results = (StackPanel)window.FindName("ResultsPanel");
                Check(results.Children.Count > 0);
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
                    new Action(() => Application.Current.Windows.OfType<RouteDetailsWindow>()
                        .Single(candidate => candidate.IsVisible).Close()));
                ((Border)results.Children[0]).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,
                    Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });

                SelectRoute(window, scenario.Routes.First(route => route.LiveQuoteKey != null));
                var refresh = typeof(MainWindow).GetMethod("RefreshInternetMarketsAsync", PrivateInstance)!;
                ((Task)refresh.Invoke(window, new object[] { 2500m, true })!).GetAwaiter().GetResult();
                Check(((TextBlock)window.FindName("MarketStatusText")).Text.Contains("actualiz", StringComparison.OrdinalIgnoreCase));
                Click(window, "UpdateBinanceSpot_Click");

                var marketStatus = typeof(MainWindow).GetMethod("BuildMarketStatus", PrivateStatic)!;
                Check(((string)marketStatus.Invoke(null, new object[] { true, false, true })!).Contains("Blue y oficial", StringComparison.Ordinal));
                Check(((string)marketStatus.Invoke(null, new object[] { false, true, true })!).Contains("Binance actualizado", StringComparison.Ordinal));
                Check(((string)marketStatus.Invoke(null, new object[] { false, false, true })!).Contains("No se pudo", StringComparison.Ordinal));
                Check(((string)marketStatus.Invoke(null, new object[] { false, false, false })!).Contains("Sin actualización", StringComparison.Ordinal));
                var sampleAmount = typeof(MainWindow).GetMethod("GetMarketSampleAmount", PrivateInstance)!;
                amount.Text = "0";
                live.MusicSession.TargetUsd = 0m;
                Check((decimal)sampleAmount.Invoke(window, null)! == 1m);
                amount.Text = "2500";
                Check((decimal)sampleAmount.Invoke(window, null)! == 2500m);

                typeof(MainWindow).GetField("_marketRefreshInProgress", PrivateInstance)!.SetValue(window, true);
                ((Task)refresh.Invoke(window, new object[] { 2500m, true })!).GetAwaiter().GetResult();
                typeof(MainWindow).GetField("_marketRefreshInProgress", PrivateInstance)!.SetValue(window, false);
                ((Task)refresh.Invoke(window, new object[] { 0m, true })!).GetAwaiter().GetResult();

                var formatStep = typeof(MainWindow).GetMethod("FormatStep", PrivateStatic)!;
                var step = new RouteStepResult
                {
                    From = source,
                    To = (PlatformNode)destination.SelectedItem,
                    Route = manual,
                    InputAmount = 100m,
                    TradeableInputAmount = 100m,
                    DebitedAmount = 100m,
                    FeeAmount = 2m,
                    OutputAmount = 98m
                };
                step.Route.FeeApplication = FeeApplicationMode.DeductFromAmount;
                Check(((string)formatStep.Invoke(null, new object[] { step })!).Contains("cargo descontado", StringComparison.Ordinal));
                step.FeeAmount = 0m;
                Check(((string)formatStep.Invoke(null, new object[] { step })!).Contains("100", StringComparison.Ordinal));
                var match = typeof(MainWindow).GetMethod("LiveQuoteStillMatches", PrivateStatic)!;
                var arsRoute = scenario.Routes.First(route => route.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs);
                var usdt = scenario.Nodes.First(node => node.Currency == "USDT");
                var ars = scenario.Nodes.First(node => node.Currency == "ARS");
                Check((bool)match.Invoke(null, new object[] { arsRoute, usdt, ars })!);
                Check(!(bool)match.Invoke(null, new object[] { arsRoute, ars, usdt })!);

                manual.PercentageFeeMinimum = 1m;
                manual.PercentageFeeMaximum = 5m;
                manual.MaximumInputAmount = 10000m;
                manual.ManualExchangeRateUpdatedAt = DateTimeOffset.Now;
                SelectRoute(window, manual);
                Check(!string.IsNullOrWhiteSpace(((TextBox)window.FindName("RouteMinimumFeeBox")).Text));
                Check(!string.IsNullOrWhiteSpace(((TextBox)window.FindName("RouteMaximumFeeBox")).Text));
                Check(!string.IsNullOrWhiteSpace(((TextBox)window.FindName("RouteMaximumAmountBox")).Text));
                ((TextBox)window.FindName("RouteLabelBox")).Text = " ";
                Click(window, "ApplyRoute_Click");
                Check(manual.Label == "Transferencia");

                SelectRoute(window, arsRoute);
                Click(window, "ApplyRoute_Click");
                Check(arsRoute.LiveQuoteKey == MarketQuoteKeys.BinanceSellUsdtForArs);

                var pending = new TransferRoute
                {
                    FromNodeId = source.Id,
                    ToNodeId = ars.Id,
                    Label = "Cotización pendiente",
                    ExchangeRateConfigured = false
                };
                scenario.Routes.Add(pending);
                SelectRoute(window, pending);
                Check(((TextBlock)window.FindName("RouteRateHint")).Text.Contains("Pendiente", StringComparison.Ordinal));
                scenario.Routes.Remove(pending);
                var selectedRouteForCopy = typeof(MainWindow).GetField("_selectedRoute", PrivateInstance)!;
                var savedRouteForCopy = selectedRouteForCopy.GetValue(window);
                selectedRouteForCopy.SetValue(window, null);
                typeof(MainWindow).GetMethod("UpdateRouteRateCopy", PrivateInstance)!.Invoke(window, null);
                selectedRouteForCopy.SetValue(window, savedRouteForCopy);

                var oneResult = new RouteCalculator().Calculate(scenario, source.Id, ars.Id, 2500m).First();
                typeof(MainWindow).GetMethod("RenderResults", PrivateInstance)!
                    .Invoke(window, new object[] { new[] { oneResult } });
                Check(((TextBlock)window.FindName("ResultSummaryText")).Text.StartsWith("1 alternativa ·", StringComparison.Ordinal));

                var retainedSource = mainSource.SelectedItem;
                typeof(MainWindow).GetField("_selectedNode", PrivateInstance)!.SetValue(window, null);
                Click(window, "AddRoute_Click");
                Click(window, "DeleteRoute_Click");
                mainSource.SelectedItem = null;
                Click(window, "AddRoute_Click");
                Click(window, "DeleteRoute_Click");
                mainSource.SelectedItem = retainedSource;

                var removedDestinations = scenario.Nodes.Where(node => node.Kind == NodeKind.Destination).ToArray();
                scenario.Nodes.RemoveAll(node => node.Kind == NodeKind.Destination);
                typeof(MainWindow).GetMethod("RefreshNodeLists", PrivateInstance)!
                    .Invoke(window, new object?[] { null, "destino-ausente" });
                Check(((ComboBox)window.FindName("MainDestinationCombo")).SelectedItem is PlatformNode);
                scenario.Nodes.AddRange(removedDestinations);
                typeof(MainWindow).GetMethod("RefreshNodeLists", PrivateInstance)!
                    .Invoke(window, new object?[] { source.Id, ars.Id });

                var scenarioField = typeof(MainWindow).GetField("_scenario", PrivateInstance)!;
                var selectedNodeField = typeof(MainWindow).GetField("_selectedNode", PrivateInstance)!;
                var selectedRouteField = typeof(MainWindow).GetField("_selectedRoute", PrivateInstance)!;
                var priorNode = selectedNodeField.GetValue(window);
                var priorRoute = selectedRouteField.GetValue(window);
                scenarioField.SetValue(window, null);
                selectedNodeField.SetValue(window, null);
                selectedRouteField.SetValue(window, null);
                typeof(MainWindow).GetMethod("RefreshNodeLists", PrivateInstance)!.Invoke(window, new object?[] { null, null });
                foreach (var handler in new[] { "DeleteScenario_Click", "AddNode_Click",
                    "ApplyNode_Click", "DeleteNode_Click", "ApplyRoute_Click", "DeleteRoute_Click" })
                {
                    Click(window, handler);
                }
                Alert("AcceptButton", "al menos dos plataformas");
                Click(window, "AddRoute_Click");
                SelectNode(window, source);
                SelectRoute(window, manual);
                typeof(MainWindow).GetMethod("SaveScenarioName", PrivateInstance)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("RefreshScenarioPicker", PrivateInstance)!.Invoke(window, null);
                typeof(MainWindow).GetMethod("SaveSilently", PrivateInstance)!.Invoke(window, null);
                scenarioField.SetValue(window, scenario);
                selectedNodeField.SetValue(window, priorNode);
                selectedRouteField.SetValue(window, priorRoute);

                SelectNode(window, source);
                var routesBeforeDelete = scenario.Routes.Count;
                Alert("AcceptButton", "transiciones");
                Click(window, "DeleteNode_Click");
                Check(scenario.Routes.Count < routesBeforeDelete);
                Click(window, "ApplyRoute_Click");

                var routeRateLabel = typeof(MainWindow).GetField("RouteRateLabel",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                var originalLabel = routeRateLabel.GetValue(window);
                routeRateLabel.SetValue(window, null);
                typeof(MainWindow).GetMethod("UpdateRouteRateCopy", PrivateInstance)!.Invoke(window, null);
                routeRateLabel.SetValue(window, originalLabel);

                File.Delete(path);
                Directory.CreateDirectory(path);
                typeof(MainWindow).GetMethod("SaveSilently", PrivateInstance)!.Invoke(window, null);
                Check(((TextBlock)window.FindName("SaveStatusText")).Text.Contains("No se pudo", StringComparison.Ordinal));
                Alert("AcceptButton", "No se pudo guardar");
                Click(window, "Save_Click");

                Directory.Delete(path);
                File.WriteAllText(path + ".tmp", "bloqueado");
                using (var lockedTemporary = new FileStream(path + ".tmp", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    typeof(MainWindow).GetMethod("SaveSilently", PrivateInstance)!.Invoke(window, null);
                    Check(((TextBlock)window.FindName("SaveStatusText")).Text.Contains("No se pudo", StringComparison.Ordinal));
                    Alert("AcceptButton", "No se pudo guardar");
                    Click(window, "Save_Click");
                }
                File.Delete(path + ".tmp");
                File.WriteAllText(path + ".tmp", "sin sobrescribir");
                File.SetAttributes(path + ".tmp", FileAttributes.ReadOnly);
                try
                {
                    typeof(MainWindow).GetMethod("SaveSilently", PrivateInstance)!.Invoke(window, null);
                    Alert("AcceptButton", "No se pudo guardar");
                    Click(window, "Save_Click");
                }
                finally
                {
                    File.SetAttributes(path + ".tmp", FileAttributes.Normal);
                    File.Delete(path + ".tmp");
                }
                var storeField = typeof(MainWindow).GetField("_store", PrivateInstance)!;
                var workingStore = storeField.GetValue(window);
                storeField.SetValue(window, new ScenarioStore("\0"));
                try
                {
                    ExpectArgumentError(() => typeof(MainWindow).GetMethod("SaveSilently", PrivateInstance)!.Invoke(window, null));
                    ExpectArgumentError(() => Click(window, "Save_Click"));
                }
                finally
                {
                    storeField.SetValue(window, workingStore);
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            if (Directory.Exists(path)) Directory.Delete(path);
        }
        MarketFailuresPreserveSavedQuotes();
    }

    private static void MarketFailuresPreserveSavedQuotes()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-market-failures-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var document = StarterScenarioFactory.CreateStarterDocument();
            document.MusicSession.AutoRefreshEnabled = false;
            document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
            document.ActiveScenarioId = "escenario-ausente";
            var store = new ScenarioStore(path);
            store.Save(document);
            using var client = MarketServiceTests.CreateFixtureClient(_ => true);
            var window = new MainWindow(store,
                new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)),
                new ArgentinaExchangeRateService(client));
            window.Show();
            window.Hide();
            try
            {
                var refresh = typeof(MainWindow).GetMethod("RefreshInternetMarketsAsync", PrivateInstance)!;
                ((Task)refresh.Invoke(window, new object[] { 100m, true })!).GetAwaiter().GetResult();
                Check(((TextBlock)window.FindName("MarketStatusText")).Text.Contains("No se pudo", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Click(MainWindow window, string handler) =>
        typeof(MainWindow).GetMethod(handler, PrivateInstance)!
            .Invoke(window, new object[] { window, new RoutedEventArgs() });

    private static void SelectNode(MainWindow window, PlatformNode node) =>
        typeof(MainWindow).GetMethod("SelectNode", PrivateInstance)!.Invoke(window, new object[] { node });

    private static void SelectRoute(MainWindow window, TransferRoute route) =>
        typeof(MainWindow).GetMethod("SelectRoute", PrivateInstance)!.Invoke(window, new object[] { route });

    private static void Alert(string buttonName, string message)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(candidate => candidate.IsVisible);
            Check(((TextBlock)dialog.FindName("MessageText")).Text.Contains(message, StringComparison.Ordinal));
            ((Button)dialog.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Falló un estado verificable de la ventana principal.");
    }

    private static void ExpectArgumentError(Action action)
    {
        try { action(); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentException) { return; }
        throw new InvalidOperationException("Se esperaba rechazar una ruta local inválida.");
    }
}
