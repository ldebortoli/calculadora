using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace Cashflow.Windows.Localization;

public sealed record LanguageChoice(string Id, string Name);

public sealed class LanguagePreferenceStore
{
    public string FilePath { get; }
    private readonly Func<string, string> _read;
    private readonly Action<string, string> _write;
    public LanguagePreferenceStore(string? path = null, Func<string, string>? read = null, Action<string, string>? write = null)
    {
        FilePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RutaCashflow", "language.json");
        _read = read ?? File.ReadAllText;
        _write = write ?? File.WriteAllText;
    }

    public string Load()
    {
        try
        {
            var id = JsonSerializer.Deserialize<string>(_read(FilePath));
            return UiLanguage.Languages.Any(language => language.Id == id) ? id! : "es";
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
        {
            return "es";
        }
    }

    public void Save(string id)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporaryPath = FilePath + ".tmp";
        _write(temporaryPath, JsonSerializer.Serialize(id));
        File.Move(temporaryPath, FilePath, true);
    }
}

public static class UiLanguage
{
    public static IReadOnlyList<LanguageChoice> Languages { get; } = new[]
    {
        new LanguageChoice("es", "Español"), new LanguageChoice("en", "English"),
        new LanguageChoice("pt", "Português"), new LanguageChoice("fr", "Français"),
        new LanguageChoice("it", "Italiano"), new LanguageChoice("de", "Deutsch"),
        new LanguageChoice("ru", "Русский"), new LanguageChoice("ja", "日本語"),
        new LanguageChoice("zh", "简体中文")
    };
    public static string CurrentId { get; private set; } = "es";
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Catalog { get; } = LoadCatalog();
    private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
    private static readonly List<(string Key, Regex Pattern)> Patterns = Catalog.Keys
        .Where(key => key.Contains('{'))
        .OrderByDescending(key => Placeholder.Replace(key, "").Length)
        .Select(key => (key, new Regex("^" + string.Concat(Placeholder.Split(key).Select((part, index) =>
            index % 2 == 0 ? Regex.Escape(part) : "(?<p" + part + ">.*?)")) + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant)))
        .ToList();
    [ThreadStatic] private static Dictionary<string, string>? _cache;
    private static Dictionary<string, string> Cache => _cache ??= new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LiteralKeys = Catalog.Keys.Where(key => !key.Contains('{'))
        .GroupBy(key => key.Trim()).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private static readonly Regex LiteralPattern = new Regex(@"(?<![\p{L}\p{N}])(?:" +
        string.Join("|", LiteralKeys.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape)) +
        @")(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
    [ThreadStatic] private static HashSet<ElementText>? _elements;
    private static HashSet<ElementText> Elements => _elements ??= new HashSet<ElementText>();
    private static bool _installed;
    private static readonly DependencyProperty HookProperty = DependencyProperty.RegisterAttached(
        "TextHook", typeof(ElementText), typeof(UiLanguage));
    private static readonly DependencyProperty TreeProperty = DependencyProperty.RegisterAttached(
        "TextTree", typeof(TextTree), typeof(UiLanguage));

    private static Dictionary<string, Dictionary<string, string>> LoadCatalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cashflow.Windows.Localization.catalog.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        // Runtime text, templates and accessible labels share the same localization path.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Attach((FrameworkElement)sender)));
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler((sender, _) => Detach((FrameworkElement)sender)));
    }

    public static void Initialize(LanguagePreferenceStore? store = null)
    {
        Install();
        SetLanguage((store ?? new LanguagePreferenceStore()).Load());
    }

    public static void SetLanguage(string id)
    {
        if (!Languages.Any(language => language.Id == id)) throw new ArgumentException("Idioma no admitido.", nameof(id));
        CurrentId = id;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(id == "zh" ? "zh-CN" : id);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        Cache.Clear();
        foreach (var element in Elements.ToArray()) element.Update();
    }

    public static string Translate(string text)
    {
        if (CurrentId == "es") return text switch
        {
            "Source" => "Origen", "Intermediate" => "Intermedio", "Destination" => "Destino", _ => text
        };
        if (string.IsNullOrEmpty(text)) return text;
        if (Cache.TryGetValue(text, out var cached)) return cached;
        if (Catalog.TryGetValue(text, out var translations)) return Cache[text] = translations[CurrentId];
        foreach (var (key, pattern) in Patterns)
        {
            var match = pattern.Match(text);
            if (!match.Success) continue;
            return Cache[text] = Placeholder.Replace(Catalog[key][CurrentId], placeholder =>
                Translate(match.Groups["p" + placeholder.Groups[1].Value].Value));
        }
        return Cache[text] = LiteralPattern.Replace(text, match => Catalog[LiteralKeys[match.Value]][CurrentId].Trim());
    }

    public static void Attach(FrameworkElement element)
    {
        if (element.GetValue(HookProperty) != null) return;
        var hook = new ElementText(element);
        element.SetValue(HookProperty, hook);
        Elements.Add(hook);
        hook.Update();
    }

    public static void Detach(FrameworkElement element)
    {
        if (element.GetValue(HookProperty) is not ElementText hook) return;
        hook.Dispose();
        Elements.Remove(hook);
        element.ClearValue(HookProperty);
    }

    public static void Track(FrameworkElement root)
    {
        if (root.GetValue(TreeProperty) != null) return;
        root.SetValue(TreeProperty, new TextTree(root));
    }

    private sealed class TextTree
    {
        private readonly FrameworkElement _root;
        private HashSet<FrameworkElement> _tracked = new HashSet<FrameworkElement>();

        public TextTree(FrameworkElement root)
        {
            _root = root;
            root.Loaded += Loaded;
            root.Unloaded += Unloaded;
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            Synchronize();
            _root.LayoutUpdated += LayoutUpdated;
        }

        private void LayoutUpdated(object? sender, EventArgs e) => Synchronize();

        private void Synchronize()
        {
            var visited = new HashSet<DependencyObject>();
            var current = new HashSet<FrameworkElement>();
            Visit(_root, visited, current);
            foreach (var removed in _tracked.Except(current)) Detach(removed);
            _tracked = current;
        }

        private static void Visit(DependencyObject node, HashSet<DependencyObject> visited, HashSet<FrameworkElement> current)
        {
            if (!visited.Add(node)) return;
            if (node is FrameworkElement element)
            {
                current.Add(element);
                Attach(element);
            }
            if (node is Visual)
            {
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                    Visit(VisualTreeHelper.GetChild(node, index), visited, current);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Visit(child, visited, current);
            if (node is Popup popup && popup.Child != null) Visit(popup.Child, visited, current);
        }

        private void Unloaded(object sender, RoutedEventArgs e)
        {
            _root.LayoutUpdated -= LayoutUpdated;
            foreach (var element in _tracked) Detach(element);
            _tracked.Clear();
        }
    }

    private sealed class ElementText : IDisposable
    {
        private readonly FrameworkElement _element;
        private readonly Dictionary<DependencyProperty, string> _sources = new Dictionary<DependencyProperty, string>();
        private readonly List<DependencyPropertyDescriptor> _descriptors = new List<DependencyPropertyDescriptor>();
        private bool _changing;

        public ElementText(FrameworkElement element)
        {
            _element = element;
            var properties = new List<DependencyProperty>
            {
                FrameworkElement.ToolTipProperty, AutomationProperties.NameProperty, AutomationProperties.HelpTextProperty
            };
            if (element is TextBlock) properties.Add(TextBlock.TextProperty);
            if (element is ContentControl) properties.Add(ContentControl.ContentProperty);
            if (element is HeaderedContentControl) properties.Add(HeaderedContentControl.HeaderProperty);
            if (element is HeaderedItemsControl) properties.Add(HeaderedItemsControl.HeaderProperty);
            if (element is Window) properties.Add(Window.TitleProperty);
            foreach (var property in properties)
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
                descriptor.AddValueChanged(element, Changed);
                _descriptors.Add(descriptor);
                Read(property);
            }
        }

        private void Changed(object? sender, EventArgs e)
        {
            if (_changing) return;
            foreach (var descriptor in _descriptors)
            {
                var property = descriptor.DependencyProperty;
                if (_sources.TryGetValue(property, out var original) && Equals(_element.GetValue(property), Translate(original))) continue;
                Read(property);
            }
            Update();
        }

        private void Read(DependencyProperty property)
        {
            if (_element.GetValue(property) is string text) _sources[property] = text;
            else _sources.Remove(property);
        }

        public void Update()
        {
            _changing = true;
            _element.Language = XmlLanguage.GetLanguage(CurrentId == "zh" ? "zh-CN" : CurrentId);
            foreach (var (property, source) in _sources)
            {
                var translated = Translate(source);
                if (!Equals(_element.GetValue(property), translated)) _element.SetCurrentValue(property, translated);
            }
            _element.InvalidateVisual();
            _changing = false;
        }

        public void Dispose()
        {
            foreach (var descriptor in _descriptors) descriptor.RemoveValueChanged(_element, Changed);
            foreach (var (property, source) in _sources) _element.SetCurrentValue(property, source);
        }
    }
}
