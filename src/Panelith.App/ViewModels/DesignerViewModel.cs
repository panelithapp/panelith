using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Panelith.App.Controls;
using Panelith.App.Services;
using Panelith.Expressions;
using Panelith.Hosting;
using Panelith.Imaging;
using Panelith.Rendering;
using Panelith.Themes;

namespace Panelith.App.ViewModels;

/// <summary>A screen that can be opened in the designer.</summary>
public sealed record ScreenChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One widget in the layers list.</summary>
public sealed record LayerItem(int Index, string Name, string Kind);

/// <summary>A colour from the open screen, shown in the swatch.</summary>
public sealed record SwatchChoice(string Value, Avalonia.Media.IBrush Brush);

/// <summary>What sort of editor a property gets.</summary>
public enum PropertyKind
{
    Text,
    LongText,
    Number,
    Flag,
    Choice,
    Color,
}

/// <summary>One editable property of a widget or of the screen.</summary>
public sealed partial class PropertyField : ObservableObject
{
    private readonly Action<PropertyField> _commit;
    private bool _loading = true;

    internal PropertyField(string path, string label, PropertyKind kind, Type type, Action<PropertyField> commit, IReadOnlyList<string>? options = null, IReadOnlyList<SwatchChoice>? palette = null)
    {
        Path = path;
        Label = label;
        Kind = kind;
        ValueType = type;
        Options = options ?? [];
        Palette = palette ?? [];
        _commit = commit;
    }

    /// <summary>Where the value lives in the theme document, such as <c>widgets/3/fontSize</c>.</summary>
    public string Path { get; }

    public string Label { get; }

    public PropertyKind Kind { get; }

    internal Type ValueType { get; }

    public IReadOnlyList<string> Options { get; }

    public IReadOnlyList<SwatchChoice> Palette { get; }

    public bool IsText => Kind is PropertyKind.Text or PropertyKind.Number or PropertyKind.Color;

    public bool IsLongText => Kind == PropertyKind.LongText;

    public bool IsFlag => Kind == PropertyKind.Flag;

    public bool IsChoice => Kind == PropertyKind.Choice;

    public bool IsColor => Kind == PropertyKind.Color;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Swatch))]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Flag { get; set; }

    [ObservableProperty]
    public partial string? Choice { get; set; }

    /// <summary>The colour, for the swatch next to colour fields.</summary>
    public Avalonia.Media.IBrush Swatch =>
        Avalonia.Media.Color.TryParse(ToAvaloniaColor(Text), out var color)
            ? new Avalonia.Media.SolidColorBrush(color)
            : Avalonia.Media.Brushes.Transparent;

    internal void Load(JsonNode? value)
    {
        _loading = true;
        switch (Kind)
        {
            case PropertyKind.Flag:
                Flag = value?.GetValue<bool>() ?? false;
                break;
            case PropertyKind.Choice:
                Choice = value?.GetValue<string>();
                break;
            default:
                Text = value is null ? string.Empty
                    : value.GetValueKind() == System.Text.Json.JsonValueKind.Number
                        ? value.GetValue<double>().ToString("0.###", CultureInfo.InvariantCulture)
                        : value.GetValue<string>();
                break;
        }

        _loading = false;
    }

    partial void OnTextChanged(string value) => Commit();

    partial void OnFlagChanged(bool value) => Commit();

    partial void OnChoiceChanged(string? value) => Commit();

    private void Commit()
    {
        if (!_loading)
        {
            _commit(this);
        }
    }

    [RelayCommand]
    private void UseSwatch(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            Text = color;
        }
    }

    /// <summary>Theme colours are #rrggbbaa; Avalonia reads #aarrggbb.</summary>
    private static string ToAvaloniaColor(string text)
    {
        var t = text.Trim();
        return t.Length == 9 && t[0] == '#' ? "#" + t[7..9] + t[1..7] : t;
    }
}

/// <summary>
/// The screen designer: a live canvas with the widgets of one screen, their
/// properties, layers, undo and redo, and saving to the user's screens folder.
/// </summary>
public sealed partial class DesignerViewModel : ObservableObject, IDisposable
{
    private const int MaxUndo = 100;
    private const int PreviewWidth = 1440;

    private static readonly HashSet<string> SkippedProperties = ["Anchor", "Children", "Items", "Bounds", "Series", "ColorStops", "Label"];
    private static readonly HashSet<string> ColorNames = ["Fill", "Stroke", "Tint", "GradientTo", "Scrim"];
    private static readonly HashSet<string> PrimaryProperties = ["Text", "FontSize", "Color", "Align", "Value", "Thickness", "Fill", "CornerRadius", "Fit"];

    private readonly PanelithService _service;
    private readonly ScreensViewModel _screens;
    private readonly ScreenWorkshop _workshop;
    private readonly ILogger _logger;
    private readonly Stack<Theme> _undo = new();
    private readonly Stack<Theme> _redo = new();
    private readonly object _renderGate = new();
    private ThemeRenderer? _renderer;
    private Theme? _rendererFor;
    private bool _rendering;
    private bool _loadingFields;
    private Theme? _editStart;
    private readonly HashSet<int> _group = [];
    private readonly Dictionary<int, ThemeRect> _dragStart = [];

    public DesignerViewModel(PanelithService service, ScreensViewModel screens)
    {
        _service = service;
        _screens = screens;
        _workshop = new ScreenWorkshop(service.Library, service.Runtime.Paths);
        _logger = service.Log.CreateLogger("Panelith.App.Designer");
        RefreshScreens();
    }

    public ObservableCollection<ScreenChoice> ScreenChoices { get; } = [];

    public ObservableCollection<LayerItem> Layers { get; } = [];

    public ObservableCollection<PropertyField> Properties { get; } = [];

    /// <summary>Position and the less common settings, hidden until More is opened.</summary>
    public ObservableCollection<PropertyField> MoreProperties { get; } = [];

    public ObservableCollection<PropertyField> ScreenFields { get; } = [];

    public ObservableCollection<string> SensorAliases { get; } = [];

    /// <summary>Background, text and accent colours of the open screen, for the swatch.</summary>
    public ObservableCollection<SwatchChoice> Palette { get; } = [];

    /// <summary>The live picture of the screen being edited.</summary>
    public LiveFrame Frame { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(CanvasWidth), nameof(CanvasHeight), nameof(CanvasAspect), nameof(Title))]
    public partial Theme? Document { get; private set; }

    [ObservableProperty]
    public partial ScreenChoice? OpenScreen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedBounds))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(DuplicateCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(AlignCommand), nameof(DistributeCommand), nameof(MatchSizeCommand))]
    public partial int SelectedIndex { get; set; } = -1;

    [ObservableProperty]
    public partial LayerItem? SelectedLayer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial string? SelectedSensor { get; set; }

    /// <summary>The sensor chosen from the list. Picking one writes it onto the widget.</summary>
    [ObservableProperty]
    public partial object? PickedSensor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreLabel))]
    public partial bool ShowMore { get; set; }

    [ObservableProperty]
    public partial bool ShowScreenSettings { get; set; }

    public bool HasDocument => Document is not null;

    public bool HasSelection => Document is not null && SelectedIndex >= 0 && SelectedIndex < Document.Widgets.Count;

    /// <summary>Two or more widgets are selected, so lining them up is useful.</summary>
    public bool HasSeveral => Chosen().Count >= 2;

    public bool HasMore => MoreProperties.Count > 0;

    public string MoreLabel => ShowMore ? "Less" : "More";

    /// <summary>Whether a widget is part of the current selection, including the primary one.</summary>
    public bool IsChosen(int index) => index == SelectedIndex || _group.Contains(index);

    public double CanvasWidth => Document?.Canvas.Width ?? 1920;

    public double CanvasHeight => Document?.Canvas.Height ?? 480;

    public double CanvasAspect => CanvasWidth / Math.Max(1d, CanvasHeight);

    public string Title => Document is null ? "Designer" : (IsDirty ? "• " : string.Empty) + (Document.Name.Length > 0 ? Document.Name : Document.Id);

    /// <summary>The selected widget's box, in design units.</summary>
    public ThemeRect? SelectedBounds => HasSelection ? Document!.Widgets[SelectedIndex].Bounds : null;

    /// <summary>The screen's id once saved, used to show it on the panel.</summary>
    public string? SavedId { get; private set; }

    /// <summary>Rereads the list of screens that can be opened.</summary>
    public void RefreshScreens()
    {
        var selected = OpenScreen?.Id;
        ScreenChoices.Clear();
        foreach (var card in _screens.Cards.Where(c => !c.HasProblem))
        {
            ScreenChoices.Add(new ScreenChoice(card.Id, card.Name + (card.IsUserTheme ? string.Empty : "  (comes with Panelith)")));
        }

        if (selected is not null && ScreenChoices.FirstOrDefault(c => c.Id == selected) is { } again)
        {
            OpenScreen = again;
        }
    }

    /// <summary>Rereads the sensor names offered by the sensor picker.</summary>
    public void RefreshSensors()
    {
        var aliases = _service.Runtime.Sensors.Aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        if (aliases.SequenceEqual(SensorAliases))
        {
            return;
        }

        SensorAliases.Clear();
        foreach (var alias in aliases)
        {
            SensorAliases.Add(alias);
        }
    }

    partial void OnOpenScreenChanged(ScreenChoice? value)
    {
        if (value is null || value.Id == Document?.Id)
        {
            return;
        }

        try
        {
            var theme = _service.Library.Load(value.Id);
            var file = _service.Library.Resolve(value.Id);
            var isUser = file is not null && Path.GetFullPath(file).StartsWith(Path.GetFullPath(_service.Runtime.Paths.ThemesDirectory), StringComparison.OrdinalIgnoreCase);
            SavedId = isUser ? value.Id : null;
            _undo.Clear();
            _redo.Clear();
            SetDocument(theme, dirty: false);
            Select(-1);
            Status = isUser ? null : "This screen comes with Panelith. Saving makes your own copy, marked \"(custom)\".";
        }
        catch (Exception ex) when (ex is IOException or ThemeFormatException)
        {
            Status = $"Could not open {value.Name}: {ex.Message}";
        }
    }

    partial void OnSelectedLayerChanged(LayerItem? value)
    {
        if (value is not null && value.Index != SelectedIndex)
        {
            Select(value.Index);
        }
    }

    partial void OnPickedSensorChanged(object? value)
    {
        if (value is string picked && picked.Length > 0)
        {
            SelectedSensor = picked;
            InsertSensor();
        }
    }

    /// <summary>Starts a blank screen the size of the panel.</summary>
    [RelayCommand]
    private void NewScreen()
    {
        SetDocument(FreshTheme("My screen"), dirty: true);
        Select(-1);
        ShowScreenSettings = false;
        Status = "A blank screen. Add widgets from the toolbox, then save.";
    }

    /// <summary>Starts a screen that already has a few widgets in place.</summary>
    [RelayCommand]
    private void StartLayout(string kind)
    {
        var name = kind switch
        {
            "number" => "Big number",
            "clock" => "Clock",
            _ => "Gauges",
        };
        var theme = FreshTheme(name);
        var widgets = kind switch
        {
            "number" => NumberLayout(theme.Canvas.Width, theme.Canvas.Height),
            "clock" => ClockLayout(theme.Canvas.Width, theme.Canvas.Height),
            _ => GaugeLayout(theme.Canvas.Width, theme.Canvas.Height),
        };
        SetDocument(theme with { Widgets = widgets }, dirty: true);
        Select(-1);
        ShowScreenSettings = false;
        Status = "Drag a widget to move it. Save when it looks right.";
    }

    [RelayCommand]
    private void ToggleMore() => ShowMore = !ShowMore;

    [RelayCommand]
    private void ToggleScreenSettings() => ShowScreenSettings = !ShowScreenSettings;

    private Theme FreshTheme(string name)
    {
        var (width, height) = _service.PanelSize;
        _undo.Clear();
        _redo.Clear();
        SavedId = null;
        OpenScreen = null;
        return new Theme
        {
            Id = string.Empty,
            Name = name,
            Author = Environment.UserName,
            Canvas = new ThemeCanvas(width, height, ThemeScaleMode.Fit, "#000000"),
            Background = new ThemeBackground("#05070b", GradientTo: "#0d1420", GradientAngle: 90),
            Defaults = ThemeDefaults.Default with { FontFamily = "Segoe UI Variable Display" },
        };
    }

    private static List<ThemeWidget> GaugeLayout(int width, int height)
    {
        var readings = new (string Value, string Number, string Name, string Color)[]
        {
            ("@cpu.load", "{@cpu.load:0}%", "CPU", "#6fb6ff"),
            ("@gpu.load", "{@gpu.load:0}%", "GPU", "#7fdcb0"),
            ("@mem.load", "{@mem.load:0}%", "MEMORY", "#b49cff"),
        };
        var size = Math.Min(height * 0.62, width / 5.2);
        var gap = Math.Max(16d, (width - (size * readings.Length)) / (readings.Length + 1));
        var top = Math.Max(16d, (height - size) * 0.28);
        var widgets = new List<ThemeWidget>();
        for (var i = 0; i < readings.Length; i++)
        {
            var reading = readings[i];
            var x = gap + (i * (size + gap));
            widgets.Add(new GaugeWidget
            {
                X = Math.Round(x),
                Y = Math.Round(top),
                Width = Math.Round(size),
                Height = Math.Round(size),
                Value = reading.Value,
                Thickness = Math.Max(8d, Math.Round(size * 0.07)),
                Color = reading.Color,
                StartAngle = 225,
                SweepAngle = 270,
            });
            widgets.Add(new TextWidget
            {
                X = Math.Round(x),
                Y = Math.Round(top + (size * 0.32)),
                Width = Math.Round(size),
                Height = Math.Round(size * 0.32),
                Text = reading.Number,
                FontSize = Math.Max(18d, Math.Round(size * 0.22)),
                FontWeight = 600,
                Align = HorizontalAlignment.Center,
                VerticalAlign = VerticalAlignment.Middle,
            });
            widgets.Add(new TextWidget
            {
                X = Math.Round(x),
                Y = Math.Round(top + size + 8),
                Width = Math.Round(size),
                Height = Math.Max(24d, Math.Round(height * 0.08)),
                Text = reading.Name,
                FontSize = Math.Max(14d, Math.Round(height * 0.045)),
                FontWeight = 600,
                Align = HorizontalAlignment.Center,
                Color = reading.Color,
            });
        }

        return widgets;
    }

    private static List<ThemeWidget> NumberLayout(int width, int height)
    {
        var boxWidth = Math.Round(width * 0.72);
        var left = Math.Round((width - boxWidth) / 2d);
        return
        [
            new TextWidget
            {
                X = left,
                Y = Math.Round(height * 0.08),
                Width = boxWidth,
                Height = Math.Round(height * 0.28),
                Text = "GPU",
                FontSize = Math.Max(18d, Math.Round(height * 0.08)),
                FontWeight = 600,
                Align = HorizontalAlignment.Center,
                VerticalAlign = VerticalAlignment.Middle,
                Color = "#7fdcb0",
            },
            new TextWidget
            {
                X = left,
                Y = Math.Round(height * 0.32),
                Width = boxWidth,
                Height = Math.Round(height * 0.5),
                Text = "{@gpu.temp:0}\u00B0",
                FontSize = Math.Max(48d, Math.Round(height * 0.42)),
                FontWeight = 600,
                Align = HorizontalAlignment.Center,
                VerticalAlign = VerticalAlignment.Middle,
            },
        ];
    }

    private static List<ThemeWidget> ClockLayout(int width, int height)
    {
        var leftWidth = Math.Round(width * 0.52);
        var columnX = Math.Round(width * 0.58);
        var columnWidth = Math.Round(width * 0.36);
        var line = Math.Max(36d, Math.Round(height * 0.12));
        string[] rows = ["CPU  {@cpu.temp:0}\u00B0", "GPU  {@gpu.temp:0}\u00B0", "RAM  {@mem.load:0}%", "Down  {@net.down:rate}"];
        var widgets = new List<ThemeWidget>
        {
            new TextWidget
            {
                X = Math.Round(width * 0.04),
                Y = Math.Round(height * 0.08),
                Width = leftWidth,
                Height = Math.Round(height * 0.58),
                Text = "{@sys.time}",
                FontSize = Math.Max(48d, Math.Round(height * 0.38)),
                FontWeight = 300,
                VerticalAlign = VerticalAlignment.Middle,
            },
            new TextWidget
            {
                X = Math.Round(width * 0.04),
                Y = Math.Round(height * 0.68),
                Width = leftWidth,
                Height = Math.Round(height * 0.16),
                Text = "{@sys.date}",
                FontSize = Math.Max(16d, Math.Round(height * 0.07)),
                Color = "#8b97a6",
                VerticalAlign = VerticalAlignment.Middle,
            },
        };
        for (var i = 0; i < rows.Length; i++)
        {
            widgets.Add(new TextWidget
            {
                X = columnX,
                Y = Math.Round((height * 0.16) + (i * (line + 8))),
                Width = columnWidth,
                Height = line,
                Text = rows[i],
                FontSize = Math.Max(16d, Math.Round(height * 0.07)),
                FontWeight = 600,
                VerticalAlign = VerticalAlignment.Middle,
            });
        }

        return widgets;
    }

    /// <summary>The first place a box of this size fits without covering a widget. An empty screen uses the centre.</summary>
    private static (double X, double Y) FreeSpot(Theme doc, double width, double height)
    {
        var canvasW = doc.Canvas.Width;
        var canvasH = doc.Canvas.Height;
        width = Math.Min(width, canvasW);
        height = Math.Min(height, canvasH);
        if (doc.Widgets.Count == 0)
        {
            return (Math.Round((canvasW - width) / 2d), Math.Round((canvasH - height) / 2d));
        }

        const double margin = 24d;
        const double step = 24d;
        var maxX = Math.Max(0d, canvasW - width);
        var maxY = Math.Max(0d, canvasH - height);
        for (var y = Math.Min(margin, maxY); y <= maxY; y += step)
        {
            for (var x = Math.Min(margin, maxX); x <= maxX; x += step)
            {
                var spot = new ThemeRect(x, y, width, height);
                if (!doc.Widgets.Any(widget => Overlaps(widget.Bounds, spot)))
                {
                    return (x, y);
                }
            }
        }

        var last = doc.Widgets[^1].Bounds;
        return (
            Math.Round(Math.Clamp(last.X + 28d, 0d, Math.Max(0d, canvasW - width))),
            Math.Round(Math.Clamp(last.Y + 28d, 0d, Math.Max(0d, canvasH - height))));
    }

    private static bool Overlaps(ThemeRect a, ThemeRect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private void RefreshPalette()
    {
        var next = new List<string>();
        void Add(string? colour)
        {
            if (!string.IsNullOrWhiteSpace(colour) && !next.Contains(colour, StringComparer.OrdinalIgnoreCase))
            {
                next.Add(colour);
            }
        }

        if (Document is { } doc)
        {
            Add(doc.Background.Color);
            Add(doc.Background.GradientTo);
            Add(doc.Defaults.Color);
            Add(doc.Defaults.AccentColor);
        }

        if (Palette.Select(choice => choice.Value).SequenceEqual(next))
        {
            return;
        }

        Palette.Clear();
        foreach (var colour in next)
        {
            Palette.Add(new SwatchChoice(colour, BrushFor(colour)));
        }
    }

    private static Avalonia.Media.IBrush BrushFor(string colour)
    {
        var text = colour.Trim();
        if (text.Length == 9 && text[0] == '#')
        {
            text = "#" + text[7..9] + text[1..7];
        }

        return Avalonia.Media.Color.TryParse(text, out var parsed)
            ? new Avalonia.Media.SolidColorBrush(parsed)
            : Avalonia.Media.Brushes.Transparent;
    }

    /// <summary>Adds a widget of <paramref name="kind"/> in the first open spot on the canvas.</summary>
    [RelayCommand]
    private void AddWidget(string kind)
    {
        if (Document is not { } doc)
        {
            NewScreen();
            doc = Document!;
        }

        var unit = doc.Canvas.Height / 480d;
        var (w, h) = kind switch
        {
            "Text" => (520d * unit, 110d * unit),
            "Bar" => (520d * unit, 28d * unit),
            "Gauge" => (220d * unit, 220d * unit),
            "Graph" => (600d * unit, 160d * unit),
            _ => (240d * unit, 160d * unit),
        };
        var (x, y) = FreeSpot(doc, w, h);
        var sensor = SelectedSensor is { Length: > 0 } picked ? picked : "@cpu.load";
        if (!sensor.StartsWith('@'))
        {
            sensor = "@" + sensor;
        }

        ThemeWidget widget = kind switch
        {
            "Text" => new TextWidget { Text = "CPU {@cpu.temp:0}\u00B0", FontSize = 72d * unit, FontWeight = 600, VerticalAlign = VerticalAlignment.Middle },
            "Bar" => new BarWidget { Value = sensor, CornerRadius = 8d * unit },
            "Gauge" => new GaugeWidget { Value = sensor, Thickness = 18d * unit },
            "Graph" => new GraphWidget { Series = [new GraphSeries(sensor, Fill: true)], GridColor = "#ffffff14" },
            "Image" => new ImageWidget(),
            _ => new ShapeWidget { Fill = "#ffffff14", CornerRadius = 16d * unit },
        };
        widget = widget with { X = x, Y = y, Width = Math.Round(w), Height = Math.Round(h) };
        Apply(doc with { Widgets = [.. doc.Widgets, widget] });
        Select(Document!.Widgets.Count - 1);
    }

    /// <summary>Adds a picture copied into the screen's folder. The screen is saved first so it has a folder.</summary>
    public void AddImage(string file)
    {
        if (Document is null || !EnsureSaved())
        {
            return;
        }

        try
        {
            if (new FileInfo(file).Length > 32L * 1024 * 1024)
            {
                Status = "That picture is larger than 32 MB.";
                return;
            }

            var folder = Document!.BasePath!;
            var name = ScreenWorkshop.FreshAssetName(folder, "image", Path.GetExtension(file).ToLowerInvariant());
            File.Copy(file, Path.Combine(folder, name));
            AddWidget("Image");
            SetValue($"widgets/{SelectedIndex}/source", JsonValue.Create(name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"The picture could not be added: {ex.Message}";
        }
    }

    private bool CanEditSelection() => HasSelection;

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Delete()
    {
        var doc = Document!;
        var widgets = doc.Widgets.ToList();
        foreach (var index in Chosen().OrderByDescending(i => i))
        {
            widgets.RemoveAt(index);
        }

        var next = Math.Min(SelectedIndex, widgets.Count - 1);
        Apply(doc with { Widgets = widgets });
        Select(next);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void Duplicate()
    {
        var doc = Document!;
        var copy = doc.Widgets[SelectedIndex] with { Id = null };
        copy = copy with { X = copy.X + 20, Y = copy.Y + 20 };
        Apply(doc with { Widgets = [.. doc.Widgets, copy] });
        Select(Document!.Widgets.Count - 1);
    }

    /// <summary>Brings the selected widget one step forward, so it draws over the next one.</summary>
    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void MoveUp() => Reorder(+1);

    [RelayCommand(CanExecute = nameof(CanEditSelection))]
    private void MoveDown() => Reorder(-1);

    [RelayCommand]
    private void Undo()
    {
        if (Document is null || _undo.Count == 0)
        {
            return;
        }

        _redo.Push(Document);
        SetDocument(_undo.Pop(), dirty: true);
        Select(Math.Min(SelectedIndex, Document!.Widgets.Count - 1));
    }

    [RelayCommand]
    private void Redo()
    {
        if (Document is null || _redo.Count == 0)
        {
            return;
        }

        _undo.Push(Document);
        SetDocument(_redo.Pop(), dirty: true);
        Select(Math.Min(SelectedIndex, Document!.Widgets.Count - 1));
    }

    /// <summary>Makes the canvas the size of the connected panel.</summary>
    [RelayCommand]
    private void MatchPanel()
    {
        if (Document is not { } doc)
        {
            return;
        }

        var (width, height) = _service.PanelSize;
        Apply(doc with { Canvas = doc.Canvas with { Width = width, Height = height } });
    }

    /// <summary>Puts the selected sensor into the selected widget: as a placeholder in text, or as the value of a bar, gauge or graph.</summary>
    [RelayCommand]
    private void InsertSensor()
    {
        if (!HasSelection || SelectedSensor is not { Length: > 0 } sensor)
        {
            return;
        }

        var alias = sensor.Trim();
        if (!alias.StartsWith('@'))
        {
            alias = "@" + alias;
        }

        var index = SelectedIndex;
        switch (Document!.Widgets[index])
        {
            case TextWidget text:
                SetValue($"widgets/{index}/text", JsonValue.Create(TextWithSensor(text.Text, alias)));
                break;
            case BarWidget or GaugeWidget:
                SetValue($"widgets/{index}/value", JsonValue.Create(alias));
                break;
            case GraphWidget graph:
                var series = graph.Series.Count > 0 ? graph.Series[0] with { Value = alias } : new GraphSeries(alias, Fill: true);
                Apply(Document with { Widgets = Document.Widgets.Select((w, i) => i == index ? graph with { Series = [series, .. graph.Series.Skip(1)] } : w).ToList() });
                break;
            default:
                return;
        }

        BuildWidgetFields();
    }

    /// <summary>Keeps the sentence and swaps in <paramref name="alias"/>, or adds it when the line has no sensor yet.</summary>
    private static string TextWithSensor(string text, string alias)
    {
        var token = "{" + alias + ":0}";
        var start = text.IndexOf("{@", StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf('}', start);
        if (start >= 0 && end > start)
        {
            return string.Concat(text.AsSpan(0, start), token, text.AsSpan(end + 1));
        }

        return text.Length == 0 ? token : text + " " + token;
    }

    /// <summary>Writes the screen to the user's screens folder, copying a bundled screen first.</summary>
    [RelayCommand]
    private void Save()
    {
        if (EnsureSaved(force: true))
        {
            Status = $"Saved to {Document!.BasePath}.";
        }
    }

    /// <summary>Saves, then puts the screen on the panel.</summary>
    [RelayCommand]
    private void ShowOnPanel()
    {
        if (EnsureSaved() && SavedId is { } id)
        {
            _screens.ShowNew(id);
            RefreshScreens();
            Status = "On the panel now.";
        }
    }

    /// <summary>
    /// Selects the widget at <paramref name="index"/>, or nothing for -1.
    /// With <paramref name="extend"/>, Ctrl-click adds or removes it and keeps the rest.
    /// </summary>
    public void Select(int index, bool extend = false)
    {
        if (Document is null)
        {
            _group.Clear();
            SelectedIndex = -1;
            SelectedLayer = null;
            ShowMore = false;
            OnPropertyChanged(nameof(HasSeveral));
            return;
        }

        index = Math.Clamp(index, -1, Document.Widgets.Count - 1);
        if (!extend || index < 0 || SelectedIndex < 0)
        {
            _group.Clear();
        }
        else if (index == SelectedIndex)
        {
            if (_group.Count == 0)
            {
                index = -1;
            }
            else
            {
                index = _group.Min();
                _group.Remove(index);
            }
        }
        else if (_group.Contains(index))
        {
            _group.Remove(index);
            index = SelectedIndex;
        }
        else
        {
            _group.Add(SelectedIndex);
        }

        SelectedIndex = index;
        SelectedLayer = SelectedIndex >= 0 ? Layers.FirstOrDefault(l => l.Index == SelectedIndex) : null;
        if (SelectedIndex < 0)
        {
            _group.Clear();
        }

        _group.Remove(SelectedIndex);
        ShowMore = false;
        BuildWidgetFields();
        OnPropertyChanged(nameof(SelectedBounds));
        OnPropertyChanged(nameof(HasSeveral));
        AlignCommand.NotifyCanExecuteChanged();
        DistributeCommand.NotifyCanExecuteChanged();
        MatchSizeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The selected widget indexes, primary included, in list order.</summary>
    public List<int> Chosen()
    {
        if (!HasSelection)
        {
            return [];
        }

        var count = Document!.Widgets.Count;
        return _group.Where(i => i >= 0 && i < count && i != SelectedIndex).Append(SelectedIndex).OrderBy(i => i).ToList();
    }

    /// <summary>
    /// The topmost widget whose box holds the point, in design units, or -1.
    /// Widgets showing right now win over ones hidden by their visibleWhen.
    /// </summary>
    public int HitTest(double x, double y)
    {
        if (Document is null)
        {
            return -1;
        }

        var hidden = -1;
        for (var i = Document.Widgets.Count - 1; i >= 0; i--)
        {
            var widget = Document.Widgets[i];
            var b = widget.Bounds;
            if (x >= b.X && x <= b.X + b.Width && y >= b.Y && y <= b.Y + b.Height)
            {
                if (IsShowing(widget))
                {
                    return i;
                }

                hidden = hidden < 0 ? i : hidden;
            }
        }

        return hidden;
    }

    private bool IsShowing(ThemeWidget widget)
    {
        if (string.IsNullOrWhiteSpace(widget.VisibleWhen) || !BindingExpression.TryParse(widget.VisibleWhen, out var expression, out _))
        {
            return true;
        }

        try
        {
            return expression!.Evaluate(_service.Runtime.Bindings).AsBoolean();
        }
        catch (ExpressionException)
        {
            return true;
        }
    }

    /// <summary>Starts a drag; the whole drag becomes one undo step.</summary>
    public void BeginDrag()
    {
        _editStart = Document;
        _dragStart.Clear();
        if (Document is null)
        {
            return;
        }

        foreach (var index in Chosen())
        {
            _dragStart[index] = Document.Widgets[index].Bounds;
        }
    }

    /// <summary>Moves the selection, or resizes the primary widget, during a drag.</summary>
    public void SetSelectedBounds(double x, double y, double width, double height)
    {
        if (!HasSelection)
        {
            return;
        }

        var doc = Document!;
        var widgets = doc.Widgets.ToList();
        if (_dragStart.TryGetValue(SelectedIndex, out var origin) && _dragStart.Count > 1
            && Math.Abs(width - origin.Width) < 0.5 && Math.Abs(height - origin.Height) < 0.5)
        {
            var dx = x - origin.X;
            var dy = y - origin.Y;
            foreach (var (index, start) in _dragStart)
            {
                widgets[index] = widgets[index] with
                {
                    X = Math.Round(start.X + dx),
                    Y = Math.Round(start.Y + dy),
                    Width = start.Width,
                    Height = start.Height,
                };
            }
        }
        else
        {
            widgets[SelectedIndex] = widgets[SelectedIndex] with
            {
                X = Math.Round(x),
                Y = Math.Round(y),
                Width = Math.Max(4d, Math.Round(width)),
                Height = Math.Max(4d, Math.Round(height)),
            };
        }

        SetDocument(doc with { Widgets = widgets }, dirty: true, rebuildLayers: false);
        OnPropertyChanged(nameof(SelectedBounds));
    }

    /// <summary>Ends a drag, recording it for undo.</summary>
    public void EndDrag()
    {
        if (_editStart is { } start && !ReferenceEquals(start, Document))
        {
            PushUndo(start);
            BuildWidgetFields();
        }

        _editStart = null;
        _dragStart.Clear();
    }

    /// <summary>Moves every selected widget by a few units, from the arrow keys.</summary>
    public void Nudge(double dx, double dy)
    {
        var chosen = Chosen();
        if (chosen.Count == 0 || Document is null)
        {
            return;
        }

        var doc = Document;
        var widgets = doc.Widgets.ToList();
        foreach (var index in chosen)
        {
            var bounds = widgets[index].Bounds;
            widgets[index] = widgets[index] with { X = bounds.X + dx, Y = bounds.Y + dy };
        }

        Apply(doc with { Widgets = widgets });
    }

    /// <summary>Lines the selection up. One widget lines up with the canvas; several line up with each other.</summary>
    [RelayCommand(CanExecute = nameof(CanAlign))]
    private void Align(string edge)
    {
        if (!Enum.TryParse<Arrangement>(edge, true, out var arrangement))
        {
            return;
        }

        var indexes = Chosen();
        if (indexes.Count == 0 || Document is not { } doc)
        {
            return;
        }

        var boxes = indexes.Select(i => doc.Widgets[i].Bounds).ToList();
        var canvas = indexes.Count == 1
            ? new ThemeRect(0, 0, doc.Canvas.Width, doc.Canvas.Height)
            : WidgetArrangement.Union(boxes);
        WriteBounds(indexes, boxes.Select(box => WidgetArrangement.Align(box, canvas, arrangement)).ToList());
    }

    private bool CanAlign(string? edge) => HasSelection;

    /// <summary>Spreads three or more widgets evenly between the outermost two.</summary>
    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void Distribute(string axis)
    {
        var indexes = Chosen();
        if (indexes.Count < 3 || Document is not { } doc)
        {
            return;
        }

        var boxes = indexes.Select(i => doc.Widgets[i].Bounds).ToList();
        WriteBounds(indexes, WidgetArrangement.Distribute(boxes, string.Equals(axis, "x", StringComparison.OrdinalIgnoreCase)));
    }

    private bool CanDistribute(string? axis) => Chosen().Count >= 3;

    /// <summary>Gives the other selected widgets the primary widget's width, height, or both.</summary>
    [RelayCommand(CanExecute = nameof(CanMatchSize))]
    private void MatchSize(string which)
    {
        var indexes = Chosen();
        if (indexes.Count < 2 || Document is not { } doc || !indexes.Contains(SelectedIndex))
        {
            return;
        }

        var boxes = indexes.Select(i => doc.Widgets[i].Bounds).ToList();
        var source = indexes.IndexOf(SelectedIndex);
        var matchWidth = which is "width" or "both";
        var matchHeight = which is "height" or "both";
        WriteBounds(indexes, WidgetArrangement.MatchSize(boxes, source, matchWidth, matchHeight));
    }

    private bool CanMatchSize(string? which) => Chosen().Count >= 2;

    private void WriteBounds(IReadOnlyList<int> indexes, IReadOnlyList<ThemeRect> bounds)
    {
        var doc = Document!;
        var widgets = doc.Widgets.ToList();
        for (var i = 0; i < indexes.Count; i++)
        {
            var box = bounds[i];
            widgets[indexes[i]] = widgets[indexes[i]] with
            {
                X = box.X,
                Y = box.Y,
                Width = box.Width,
                Height = box.Height,
            };
        }

        Apply(doc with { Widgets = widgets });
    }

    /// <summary>Redraws the canvas with current sensor values, off the UI thread.</summary>
    public void RefreshRender()
    {
        var doc = Document;
        if (doc is null || _rendering)
        {
            return;
        }

        _rendering = true;
        var bindings = _service.Runtime.Bindings;
        Task.Run(() =>
        {
            lock (_renderGate)
            {
                if (!ReferenceEquals(_rendererFor, doc))
                {
                    _renderer?.Dispose();
                    var scale = Math.Min(1d, PreviewWidth / (double)Math.Max(1, doc.Canvas.Width));
                    var width = Math.Max(1, (int)Math.Round(doc.Canvas.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(doc.Canvas.Height * scale));
                    _renderer = new ThemeRenderer(doc, new ThemeRenderOptions(width, height, ShowErrorMarkers: true, ThemeFolder: doc.BasePath));
                    _rendererFor = doc;
                }

                return _renderer!.Render(bindings);
            }
        }).ContinueWith(
            task =>
            {
                _rendering = false;
                if (task.IsCompletedSuccessfully)
                {
                    using var frame = task.Result;
                    Frame.Update(frame);
                }
                else
                {
                    _logger.LogDebug("The designer canvas could not be drawn: {Error}", task.Exception?.GetBaseException().Message);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Reloads the open screen from disk, after the wallpaper window changed it.</summary>
    public void ReloadFromDisk(string id)
    {
        try
        {
            var theme = _service.Library.Load(id);
            SavedId = id;
            SetDocument(theme, dirty: false);
            RefreshScreens();
            OpenScreen = ScreenChoices.FirstOrDefault(c => c.Id == id);
        }
        catch (Exception ex) when (ex is IOException or ThemeFormatException)
        {
            Status = ex.Message;
        }
    }

    /// <summary>
    /// Makes sure the screen lives in the user's folder: saves a new screen
    /// or copies a bundled one. With <paramref name="force"/> it saves even
    /// when nothing changed.
    /// </summary>
    public bool EnsureSaved(bool force = false)
    {
        if (Document is not { } doc)
        {
            return false;
        }

        try
        {
            string folder;
            if (SavedId is null)
            {
                if (doc.Id.Length == 0)
                {
                    var (id, created) = _workshop.NewScreenFolder(doc.Name.Length > 0 ? doc.Name : "my-screen");
                    folder = created;
                    doc = doc with { Id = id, BasePath = folder };
                }
                else
                {
                    var (id, copyFolder, copy) = _workshop.Editable(doc.Id);
                    folder = copyFolder;
                    var original = _service.Library.Load(doc.Id);
                    doc = doc with { Id = id, Name = doc.Name == original.Name ? copy.Name : doc.Name, BasePath = folder };
                }

                SavedId = doc.Id;
                force = true;
            }
            else
            {
                folder = doc.BasePath ?? throw new InvalidOperationException("The screen has no folder.");
            }

            if (force || IsDirty)
            {
                _workshop.Save(doc, folder);
                _screens.Reload();
                RefreshScreens();
            }

            SetDocument(doc, dirty: false);
            OpenScreen = ScreenChoices.FirstOrDefault(c => c.Id == doc.Id);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning("The screen could not be saved: {Error}", ex.Message);
            Status = $"Could not save: {ex.Message}";
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_renderGate)
        {
            _renderer?.Dispose();
            _renderer = null;
        }

        Frame.Dispose();
    }

    private void Reorder(int step)
    {
        var doc = Document!;
        var from = SelectedIndex;
        var to = from + step;
        if (to < 0 || to >= doc.Widgets.Count)
        {
            return;
        }

        var widgets = doc.Widgets.ToList();
        (widgets[from], widgets[to]) = (widgets[to], widgets[from]);
        Apply(doc with { Widgets = widgets });
        Select(to);
    }

    private void Apply(Theme updated)
    {
        if (Document is { } before)
        {
            PushUndo(before);
        }

        SetDocument(updated, dirty: true);
        Status = null;
    }

    private void PushUndo(Theme before)
    {
        _undo.Push(before);
        _redo.Clear();
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var theme in keep)
            {
                _undo.Push(theme);
            }
        }
    }

    private void SetDocument(Theme theme, bool dirty, bool rebuildLayers = true)
    {
        var sizeChanged = Document is null || Document.Canvas != theme.Canvas;
        Document = theme;
        IsDirty = dirty;
        if (rebuildLayers)
        {
            Layers.Clear();
            for (var i = 0; i < theme.Widgets.Count; i++)
            {
                Layers.Add(Describe(i, theme.Widgets[i]));
            }

            BuildScreenFields();
            RefreshPalette();
        }

        if (sizeChanged)
        {
            OnPropertyChanged(nameof(CanvasAspect));
        }

        OnPropertyChanged(nameof(SelectedBounds));
        RefreshRender();
    }

    private static LayerItem Describe(int index, ThemeWidget widget)
    {
        var kind = widget.GetType().Name.Replace("Widget", string.Empty, StringComparison.Ordinal);
        var detail = widget switch
        {
            _ when widget.Id is { Length: > 0 } id => id,
            TextWidget t => t.Text,
            BarWidget b => b.Value,
            GaugeWidget g => g.Value,
            GraphWidget g => g.Series.FirstOrDefault()?.Value ?? string.Empty,
            ImageWidget i => i.Source,
            VideoWidget v => v.Source,
            ShapeWidget s => s.Shape.ToString(),
            GroupWidget g => $"{g.Items.Count} items",
            _ => string.Empty,
        };
        if (detail.Length > 38)
        {
            detail = detail[..37] + "…";
        }

        return new LayerItem(index, detail.Length > 0 ? detail : kind, kind);
    }

    private void BuildScreenFields()
    {
        if (Document is null)
        {
            ScreenFields.Clear();
            return;
        }

        if (ScreenFields.Count == 0)
        {
            Add("name", "Name", PropertyKind.Text, typeof(string));
            Add("canvas/width", "Canvas width", PropertyKind.Number, typeof(int));
            Add("canvas/height", "Canvas height", PropertyKind.Number, typeof(int));
            Add("background/color", "Background", PropertyKind.Color, typeof(string));
            Add("background/gradientTo", "Gradient to", PropertyKind.Color, typeof(string));
            Add("background/gradientAngle", "Gradient angle", PropertyKind.Number, typeof(double));
            Add("background/scrim", "Darken wallpaper", PropertyKind.Color, typeof(string));
            Add("background/videoSpeed", "Video speed", PropertyKind.Number, typeof(double));
            Add("defaults/fontFamily", "Font", PropertyKind.Text, typeof(string));
            Add("defaults/color", "Text colour", PropertyKind.Color, typeof(string));
            Add("defaults/accentColor", "Accent colour", PropertyKind.Color, typeof(string));
            Add("defaults/trackColor", "Track colour", PropertyKind.Color, typeof(string));
        }

        var root = ToJson(Document);
        foreach (var field in ScreenFields)
        {
            field.Load(Navigate(root, field.Path));
        }

        void Add(string path, string label, PropertyKind kind, Type type) =>
            ScreenFields.Add(new PropertyField(path, label, kind, type, CommitField, palette: Palette));
    }

    private void BuildWidgetFields()
    {
        _loadingFields = true;
        Properties.Clear();
        MoreProperties.Clear();
        if (HasSelection)
        {
            var widget = Document!.Widgets[SelectedIndex];
            var root = ToJson(Document);
            foreach (var property in OrderedProperties(widget.GetType()))
            {
                var path = $"widgets/{SelectedIndex}/{JsonName(property.Name)}";
                var field = CreateField(path, property);
                if (field is null)
                {
                    continue;
                }

                field.Load(Navigate(root, path));
                if (PrimaryProperties.Contains(property.Name))
                {
                    Properties.Add(field);
                }
                else
                {
                    MoreProperties.Add(field);
                }
            }
        }

        _loadingFields = false;
        OnPropertyChanged(nameof(HasMore));
    }

    private PropertyField? CreateField(string path, PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var label = Humanize(property.Name);
        if (type == typeof(bool))
        {
            return new PropertyField(path, label, PropertyKind.Flag, type, CommitField, palette: Palette);
        }

        if (type.IsEnum)
        {
            return new PropertyField(path, label, PropertyKind.Choice, type, CommitField, [.. Enum.GetNames(type).Select(JsonName)], Palette);
        }

        if (type == typeof(double) || type == typeof(int))
        {
            return new PropertyField(path, label, PropertyKind.Number, property.PropertyType, CommitField, palette: Palette);
        }

        if (type == typeof(string))
        {
            var kind = property.Name.EndsWith("Color", StringComparison.Ordinal) || ColorNames.Contains(property.Name) ? PropertyKind.Color
                : property.Name is "Text" ? PropertyKind.LongText
                : PropertyKind.Text;
            return new PropertyField(path, label, kind, type, CommitField, palette: Palette);
        }

        return null;
    }

    private static IEnumerable<PropertyInfo> OrderedProperties(Type type)
    {
        var all = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null && !SkippedProperties.Contains(p.Name))
            .ToList();
        string[] first = ["X", "Y", "Width", "Height"];
        string[] last = ["Opacity", "Rotation", "VisibleWhen", "Animate", "Id"];
        return all.Where(p => first.Contains(p.Name)).OrderBy(p => Array.IndexOf(first, p.Name))
            .Concat(all.Where(p => !first.Contains(p.Name) && !last.Contains(p.Name)))
            .Concat(all.Where(p => last.Contains(p.Name)).OrderBy(p => Array.IndexOf(last, p.Name)));
    }

    private void CommitField(PropertyField field)
    {
        if (_loadingFields || Document is null)
        {
            return;
        }

        JsonNode? value;
        switch (field.Kind)
        {
            case PropertyKind.Flag:
                value = JsonValue.Create(field.Flag);
                break;
            case PropertyKind.Choice:
                value = field.Choice is null ? null : JsonValue.Create(field.Choice);
                break;
            case PropertyKind.Number:
                if (string.IsNullOrWhiteSpace(field.Text))
                {
                    value = null;
                }
                else if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                {
                    value = field.ValueType == typeof(int) || Nullable.GetUnderlyingType(field.ValueType) == typeof(int)
                        ? JsonValue.Create((int)Math.Round(number))
                        : JsonValue.Create(number);
                }
                else
                {
                    Status = $"{field.Label} needs a number.";
                    return;
                }

                break;
            default:
                value = field.Text.Length == 0 && field.Path is not "name" && !field.Path.EndsWith("/text", StringComparison.Ordinal)
                    ? null
                    : JsonValue.Create(field.Text);
                break;
        }

        SetValue(field.Path, value);
    }

    private void SetValue(string path, JsonNode? value)
    {
        var doc = Document!;
        var root = ToJson(doc);
        var segments = path.Split('/');
        JsonNode parent = root;
        foreach (var segment in segments[..^1])
        {
            var next = parent is JsonArray array ? array[int.Parse(segment, CultureInfo.InvariantCulture)] : parent[segment];
            if (next is null)
            {
                next = new JsonObject();
                ((JsonObject)parent)[segment] = next;
            }

            parent = next;
        }

        var objectNode = (JsonObject)parent;
        if (value is null)
        {
            objectNode.Remove(segments[^1]);
        }
        else
        {
            objectNode[segments[^1]] = value;
        }

        try
        {
            var updated = ThemeSerializer.Deserialize(root.ToJsonString(), doc.BasePath) with { Id = doc.Id };
            var selected = SelectedIndex;
            Apply(updated);
            if (path.StartsWith("widgets/", StringComparison.Ordinal))
            {
                SelectedIndex = selected;
                SelectedLayer = Layers.FirstOrDefault(l => l.Index == selected);
            }
        }
        catch (ThemeFormatException ex)
        {
            Status = ex.Message;
            BuildWidgetFields();
            BuildScreenFields();
        }
    }

    private static JsonNode ToJson(Theme theme) => JsonNode.Parse(ThemeSerializer.Serialize(theme))!;

    private static JsonNode? Navigate(JsonNode root, string path)
    {
        JsonNode? node = root;
        foreach (var segment in path.Split('/'))
        {
            node = node switch
            {
                JsonArray array => array[int.Parse(segment, CultureInfo.InvariantCulture)],
                JsonObject obj => obj[segment],
                _ => null,
            };
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    private static string JsonName(string clrName) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(clrName);

    private static string Humanize(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString() switch
        {
            "X" => "Left",
            "Y" => "Top",
            "Value" => "Sensor",
            "Visible when" => "Show only when",
            "Id" => "Name (id)",
            var s => s,
        };
    }
}
