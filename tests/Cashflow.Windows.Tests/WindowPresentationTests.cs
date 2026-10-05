using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Cashflow.Windows.Controls;
using Cashflow.Windows.Data;
using Cashflow.Windows.Localization;

namespace Cashflow.Windows.Tests;

internal static class WindowPresentationTests
{
    public static void Run()
    {
        Preferences();
        CatalogCompleteness();
        RuntimeLabelsAndBindings();
        MaximizationAndLanguagePicker();
    }

    private static void Preferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "cashflow-language-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LanguagePreferenceStore(path);
            Check(store.Load() == "es", "Primera ejecución usa español.");
            foreach (var language in UiLanguage.Languages)
            {
                store.Save(language.Id);
                Check(store.Load() == language.Id, "Idioma guardado y recuperado.");
            }
            foreach (var json in new[] { "null", "\"unknown\"", "not json" })
            {
                File.WriteAllText(path, json);
                Check(store.Load() == "es", "Preferencia dañada o desconocida recupera español.");
            }
            foreach (var exception in new Exception[] { new IOException(), new UnauthorizedAccessException(), new System.Text.Json.JsonException() })
            {
                var failing = new LanguagePreferenceStore(path, _ => throw exception);
                Check(failing.Load() == "es", "Fallo de almacenamiento permite usar la aplicación.");
            }
            UiLanguage.Initialize(new LanguagePreferenceStore(path));
            UiLanguage.Install();
            UiLanguage.Install();
            _ = new LanguagePreferenceStore();
            try { UiLanguage.SetLanguage("not-a-language"); throw new InvalidOperationException("Idioma inválido aceptado."); }
            catch (ArgumentException) { }
            UiLanguage.Initialize();
            UiLanguage.SetLanguage("es");
        }
        finally { File.Delete(path); }
    }

    private static void CatalogCompleteness()
    {
        Check(UiLanguage.Languages.Select(language => language.Id).SequenceEqual(new[] { "es", "en", "pt", "fr", "it", "de", "ru", "ja", "zh" }),
            "Los nueve idiomas solicitados están disponibles.");
        foreach (var language in UiLanguage.Languages.Where(language => language.Id != "es"))
        {
            UiLanguage.SetLanguage(language.Id);
            foreach (var (source, translations) in UiLanguage.Catalog)
            {
                Check(translations.Count == 8 && translations.TryGetValue(language.Id, out var translated) && !string.IsNullOrWhiteSpace(translated),
                    "Cada clave tiene ocho traducciones completas.");
                Check(UiLanguage.Translate(source) == translations[language.Id], "El catálogo se usa íntegro.");
                Check(Regex.Matches(source, @"\{\d+\}").Select(match => match.Value).OrderBy(value => value)
                    .SequenceEqual(Regex.Matches(translations[language.Id], @"\{\d+\}").Select(match => match.Value).OrderBy(value => value)),
                    "Valores, porcentajes y argumentos conservados.");
                if (source.Contains("COAST", StringComparison.Ordinal))
                    Check(translations[language.Id].Contains("COAST", StringComparison.Ordinal), "COAST conserva su significado financiero.");
            }
            Check(UiLanguage.Translate("") == "", "Texto vacío conservado.");
            var financial = UiLanguage.Translate("Bonos iniciales");
            Check(!financial.Contains("bonus", StringComparison.OrdinalIgnoreCase) && !financial.Contains("bonuses", StringComparison.OrdinalIgnoreCase),
                "Bonos financieros distinguidos de bonificaciones.");
        }
        UiLanguage.SetLanguage("en");
        Check(UiLanguage.Translate("COAST en 74 meses · 6 años y 2 meses") == "COAST in 74 months · 6 years and 2 months",
            "Plazos dinámicos y argumentos traducidos.");
        var title = UiLanguage.Translate("JUBILACIÓN A LOS 65 AÑOS");
        Check(title == "RETIREMENT AT AGE 65", "Edad conservada y etiqueta traducida.");
        Check(UiLanguage.Translate("Custom user title 123") == "Custom user title 123", "Texto propio desconocido conservado.");
        Check(UiLanguage.Translate("Custom user title 123") == "Custom user title 123", "Resultado estable en caché.");
        Check(UiLanguage.Translate("Aportes nuevos: 1.234,00 USD").StartsWith("New contributions:", StringComparison.Ordinal),
            "Etiquetas concatenadas traducidas sin alterar el número.");
        var root = Path.GetFullPath("src/Cashflow.Windows");
        foreach (var path in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            var xml = XDocument.Load(path);
            foreach (var attribute in xml.Descendants().Attributes().Where(attribute =>
                new[] { "Text", "Content", "Header", "Title", "ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText" }.Contains(attribute.Name.LocalName)))
            {
                var text = attribute.Value;
                if (text.StartsWith('{') || !text.Any(char.IsLetter) || text.Trim().Length <= 1) continue;
                Check(UiLanguage.Catalog.ContainsKey(text), "Texto XAML sin catálogo: " + text);
            }
        }
        UiLanguage.SetLanguage("es");
        foreach (var (source, expected) in new[] { ("Source", "Origen"), ("Intermediate", "Intermedio"), ("Destination", "Destino"), ("Nombre", "Nombre") })
            Check(UiLanguage.Translate(source) == expected, "Tipos de nodo localizados en español.");
    }

    private static void RuntimeLabelsAndBindings()
    {
        var button = new Button { Content = "Ocultar", ToolTip = "Idioma de la aplicación" };
        AutomationProperties.SetName(button, "Quitar");
        AutomationProperties.SetHelpText(button, "Mostrar");
        var label = new TextBlock { Text = "Bonos iniciales" };
        var header = new HeaderedContentControl { Header = "Jubilación" };
        var group = new HeaderedItemsControl { Header = "RESERVAS" };
        var editable = new TextBox { Text = "Reserva vacaciones de mi familia" };
        foreach (var element in new FrameworkElement[] { button, label, header, group, editable }) UiLanguage.Attach(element);
        UiLanguage.Attach(button);
        UiLanguage.SetLanguage("en");
        Check(Equals(button.Content, "Hide") && label.Text == "Initial bonds" && Equals(header.Header, "Retirement"),
            "Contenido y encabezados traducidos al cambiar de idioma.");
        Check(AutomationProperties.GetName(button) == "Remove" && AutomationProperties.GetHelpText(button) == "Show",
            "Etiquetas accesibles traducidas.");
        Check(editable.Text == "Reserva vacaciones de mi familia", "Entradas del usuario intactas.");
        button.Content = "Mostrar";
        Check(Equals(button.Content, "Show"), "Texto dinámico se traduce inmediatamente.");
        button.Content = new Border();
        Check(button.Content is Border, "Contenido visual se conserva.");
        button.Content = "Quitar";
        Check(Equals(button.Content, "Remove"), "Cambio de contenido visual a texto.");
        var model = new LabelModel { Name = "Objetivo de acciones" };
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(LabelModel.Name)) { Source = model });
        Check(model.Name == "Objetivo de acciones" && label.Text == "Stock target", "Binding conserva el modelo original.");
        model.Name = "Bonos iniciales";
        label.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget();
        Check(label.Text == "Initial bonds" && model.Name == "Bonos iniciales", "Actualización de binding localizada.");
        UiLanguage.SetLanguage("fr");
        UiLanguage.Detach(label);
        Check(label.Text == "Bonos iniciales", "Descargar restaura la fuente, evitando doble traducción al volver.");
        UiLanguage.Attach(label);
        UiLanguage.SetLanguage("en");
        Check(label.Text == "Initial bonds", "Reabrir y volver a cambiar idioma conserva la fuente: " + label.Text);
        foreach (var element in new FrameworkElement[] { button, label, header, group, editable }) UiLanguage.Detach(element);
        UiLanguage.Detach(button);
        UiLanguage.SetLanguage("es");
    }

    private static void MaximizationAndLanguagePicker()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cashflow-presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var store = new ScenarioStore(Path.Combine(folder, "scenarios.json"));
        var preferences = new LanguagePreferenceStore(Path.Combine(folder, "language.json"));
        var document = StarterScenarioFactory.CreateStarterDocument();
        document.MusicSession.InternetFetchedAt = DateTimeOffset.Now;
        document.MusicSession.AutoRefreshEnabled = false;
        store.Save(document);
        using var client = MarketServiceTests.CreateFixtureClient();
        var window = new MainWindow(store, new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)), new ArgentinaExchangeRateService(client), preferences);
        try
        {
            window.Show();
            Pump();
            UiLanguage.Track(window);
            Check(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight),
                "El ajuste de arranque no deja límites máximos para maximizar.");
            var normal = new Size(window.ActualWidth, window.ActualHeight);
            var handle = new WindowInteropHelper(window).Handle;
            var information = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            Check(GetMonitorInfo(MonitorFromWindow(handle, 2), ref information), "Monitor de la ventana identificado.");
            window.WindowState = WindowState.Maximized;
            Pump();
            var toDevice = PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice;
            Check(window.ActualWidth * toDevice.M11 >= information.Work.Right - information.Work.Left - 2d &&
                  window.ActualHeight * toDevice.M22 >= information.Work.Bottom - information.Work.Top - 2d,
                "La ventana maximizada cubre todo el área útil física del monitor.");
            window.WindowState = WindowState.Normal;
            Pump();
            Check(Math.Abs(window.ActualWidth - normal.Width) < 1d && Math.Abs(window.ActualHeight - normal.Height) < 1d,
                "Restaurar recupera el tamaño normal.");
            var picker = (ComboBox)window.FindName("LanguageCombo");
            foreach (var language in UiLanguage.Languages)
            {
                picker.SelectedItem = language;
                Pump();
                Check(UiLanguage.CurrentId == language.Id && (language.Id == "es" || preferences.Load() == language.Id),
                    "Selector cambia y guarda el idioma elegido.");
                var retirement = (RetirementView)((ContentControl)window.FindName("RetirementHost")).Content;
                var tab = ((TabControl)((Grid)window.Content).Children[1]).Items.Cast<TabItem>().Last();
                tab.IsSelected = true;
                Pump();
                Check(((TextBox)retirement.FindName("BirthDateBox")).Text == "", "Nacimiento opcional no modificado por traducción.");
                var title = ((TextBlock)((StackPanel)retirement.FindName("CoastResultsPanel")).Children[0]).Text;
                Render(window, "presentation-" + language.Id + ".png");
                Check(title == UiLanguage.Translate("COAST FIRE · cuándo podrías dejar de aportar"), "Jubilación usa el idioma activo: " + language.Id + ": " + title);
                var withdrawal = (TextBlock)retirement.FindName("MonthlyWithdrawalText");
                Check(withdrawal.TextWrapping == TextWrapping.Wrap, "Importes y etiquetas largos se ajustan al ancho del idioma.");
            }
            UiLanguage.SetLanguage("es");
            foreach (var failure in new Exception[] { new IOException("disk unavailable"), new UnauthorizedAccessException("read only") })
            {
                var failureWindow = new MainWindow(store, new ScenarioMarketUpdater(new BinanceSpotQuoteService(client)), new ArgentinaExchangeRateService(client),
                    new LanguagePreferenceStore(preferences.FilePath, write: (_, _) => throw failure));
                try
                {
                    failureWindow.Show();
                    Pump();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single(dialog => dialog.IsVisible);
                        ((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    ((ComboBox)failureWindow.FindName("LanguageCombo")).SelectedItem = UiLanguage.Languages.Single(language => language.Id == "en");
                    Check(UiLanguage.CurrentId == "en", "Fallo de persistencia no impide cambiar idioma.");
                }
                finally { failureWindow.Close(); UiLanguage.SetLanguage("es"); }
            }
            picker.SelectedItem = null;
            Check(UiLanguage.CurrentId == "es", "Selección vacía no cambia idioma.");
        }
        finally
        {
            window.Close();
            UiLanguage.SetLanguage("es");
            foreach (var path in Directory.EnumerateFiles(folder)) File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Render(Window window, string filename)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/qa");
        using var output = File.Create(Path.Combine("artifacts/qa", filename));
        encoder.Save(output);
    }

    public sealed class LabelModel { public string Name { get; set; } = ""; }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
}
