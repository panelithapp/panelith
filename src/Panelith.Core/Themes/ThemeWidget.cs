using System.Text.Json.Serialization;

namespace Panelith.Themes;

/// <summary>
/// One element of a theme layout.
/// </summary>
/// <remarks>
/// Widgets are plain data: they carry no rendering logic and no reference to the
/// renderer, so a theme can be loaded, validated, edited and round-tripped
/// without a graphics stack present. That keeps the theme editor, the CLI
/// validator and the headless service able to work with the same objects.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(TextWidget), "text")]
[JsonDerivedType(typeof(BarWidget), "bar")]
[JsonDerivedType(typeof(GaugeWidget), "gauge")]
[JsonDerivedType(typeof(GraphWidget), "graph")]
[JsonDerivedType(typeof(ImageWidget), "image")]
[JsonDerivedType(typeof(VideoWidget), "video")]
[JsonDerivedType(typeof(ShapeWidget), "shape")]
[JsonDerivedType(typeof(GroupWidget), "group")]
public abstract record ThemeWidget
{
    /// <summary>
    /// Identifier unique within the theme. Optional, but required to target the
    /// widget from the editor or from a condition.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>Distance from the left edge of the parent, in design units.</summary>
    public double X { get; init; }

    /// <summary>Distance from the top edge of the parent, in design units.</summary>
    public double Y { get; init; }

    /// <summary>Width in design units.</summary>
    public double Width { get; init; }

    /// <summary>Height in design units.</summary>
    public double Height { get; init; }

    /// <summary>Which parent edges this widget stays pinned to when the canvas is resized.</summary>
    public AnchorEdges Anchor { get; init; } = AnchorEdges.None;

    /// <summary>Opacity from 0 (invisible) to 1 (opaque).</summary>
    public double Opacity { get; init; } = 1d;

    /// <summary>Clockwise rotation in degrees about the widget's centre.</summary>
    public double Rotation { get; init; }

    /// <summary>
    /// An expression that must evaluate truthy for the widget to be drawn, such
    /// as <c>@gpu.temp &gt; 70</c>. When absent, the widget is always drawn.
    /// </summary>
    /// <remarks>
    /// Conditional visibility is what makes a single theme able to show an
    /// overheating warning, hide a GPU block on a machine with no discrete GPU,
    /// or swap units, without needing one theme variant per case.
    /// </remarks>
    public string? VisibleWhen { get; init; }

    /// <summary>
    /// Whether values on this widget glide between sensor readings. Set to
    /// false for values that must show each reading exactly as it arrives.
    /// </summary>
    public bool Animate { get; init; } = true;

    /// <summary>The widget's box in its parent's coordinate space.</summary>
    [JsonIgnore]
    public ThemeRect Bounds => new(X, Y, Width, Height);

    /// <summary>Child widgets, for container types. Empty for leaves.</summary>
    [JsonIgnore]
    public virtual IReadOnlyList<ThemeWidget> Children => [];
}

/// <summary>Draws a line of text, optionally interpolating sensor values.</summary>
public sealed record TextWidget : ThemeWidget
{
    /// <summary>
    /// The text to draw. <c>{...}</c> placeholders are replaced with formatted
    /// sensor values, for example <c>CPU {@cpu.temp:0}&#176;C</c>.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Font family name. Falls back to the theme default, then to a bundled font.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Font size in design units.</summary>
    public double FontSize { get; init; } = 16d;

    /// <summary>Font weight from 100 to 900.</summary>
    public int FontWeight { get; init; } = 400;

    /// <summary>Whether the font is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Fill colour as <c>#rgb</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Horizontal alignment within the widget box.</summary>
    public HorizontalAlignment Align { get; init; } = HorizontalAlignment.Left;

    /// <summary>Vertical alignment within the widget box.</summary>
    public VerticalAlignment VerticalAlign { get; init; } = VerticalAlignment.Top;

    /// <summary>Extra space between characters, in design units.</summary>
    public double LetterSpacing { get; init; }

    /// <summary>Line height multiplier for wrapped text.</summary>
    public double LineHeight { get; init; } = 1.2d;

    /// <summary>Whether text wraps inside <see cref="ThemeWidget.Width"/>.</summary>
    public bool Wrap { get; init; }

    /// <summary>
    /// Shrink the font until the text fits the box. Prevents a long GPU name or
    /// a four-digit fan speed from overflowing a layout authored for shorter values.
    /// Ignored while <see cref="Scroll"/> is on, so a title keeps its size and moves instead.
    /// </summary>
    public bool ShrinkToFit { get; init; } = true;

    /// <summary>
    /// Slide the text sideways when it is wider than the box, then repeat.
    /// For a song title or any other line that is too long to shrink.
    /// </summary>
    public bool Scroll { get; init; }

    /// <summary>Optional outline colour drawn behind the glyphs.</summary>
    public string? StrokeColor { get; init; }

    /// <summary>Outline width in design units.</summary>
    public double StrokeWidth { get; init; }

    /// <summary>
    /// Colour thresholds keyed by the value of <see cref="ColorBinding"/>. The
    /// highest threshold not above the value wins.
    /// </summary>
    public IReadOnlyList<ColorStop>? ColorStops { get; init; }

    /// <summary>
    /// Expression supplying the value <see cref="ColorStops"/> is compared
    /// against. Defaults to the first sensor referenced by <see cref="Text"/>.
    /// </summary>
    public string? ColorBinding { get; init; }

    /// <summary>
    /// Pad numbers to a fixed width using figure spaces so the text does not
    /// jitter as digits change. On by default because a value flicking between
    /// 99 and 100 visibly shifts a centred layout on every update.
    /// </summary>
    public bool StabilizeWidth { get; init; } = true;

    /// <summary>
    /// Give every digit the same width, so a number changing from 11 to 88
    /// does not shift the text around it.
    /// </summary>
    public bool TabularNumbers { get; init; } = true;
}

/// <summary>Draws a linear progress bar.</summary>
public sealed record BarWidget : ThemeWidget
{
    /// <summary>Expression supplying the bar's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Value mapped to an empty bar. Defaults to the sensor's own minimum.</summary>
    public double? Minimum { get; init; }

    /// <summary>Value mapped to a full bar. Defaults to the sensor's own maximum.</summary>
    public double? Maximum { get; init; }

    /// <summary>Direction the bar grows in.</summary>
    public BarDirection Direction { get; init; } = BarDirection.LeftToRight;

    /// <summary>Colour of the filled portion.</summary>
    public string? Color { get; init; }

    /// <summary>Colour of the unfilled portion.</summary>
    public string? TrackColor { get; init; }

    /// <summary>Corner radius in design units.</summary>
    public double CornerRadius { get; init; }

    /// <summary>
    /// Number of discrete segments, or <c>0</c> for a continuous bar. Segmented
    /// bars read better than continuous ones on low-resolution panels.
    /// </summary>
    public int Segments { get; init; }

    /// <summary>Gap between segments in design units.</summary>
    public double SegmentGap { get; init; } = 2d;

    /// <summary>Colour thresholds applied to the fill.</summary>
    public IReadOnlyList<ColorStop>? ColorStops { get; init; }
}

/// <summary>Which way a bar fills.</summary>
public enum BarDirection
{
    /// <summary>Fills rightwards.</summary>
    LeftToRight = 0,

    /// <summary>Fills leftwards.</summary>
    RightToLeft,

    /// <summary>Fills upwards.</summary>
    BottomToTop,

    /// <summary>Fills downwards.</summary>
    TopToBottom,
}

/// <summary>Draws a circular or arc gauge.</summary>
public sealed record GaugeWidget : ThemeWidget
{
    /// <summary>Expression supplying the gauge's value.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>Value at the start of the sweep. Defaults to the sensor's own minimum.</summary>
    public double? Minimum { get; init; }

    /// <summary>Value at the end of the sweep. Defaults to the sensor's own maximum.</summary>
    public double? Maximum { get; init; }

    /// <summary>Angle in degrees where the sweep begins, measured clockwise from twelve o'clock.</summary>
    public double StartAngle { get; init; } = 135d;

    /// <summary>How many degrees the sweep covers.</summary>
    public double SweepAngle { get; init; } = 270d;

    /// <summary>Stroke width of the arc in design units.</summary>
    public double Thickness { get; init; } = 8d;

    /// <summary>Colour of the filled arc.</summary>
    public string? Color { get; init; }

    /// <summary>Colour of the unfilled arc.</summary>
    public string? TrackColor { get; init; }

    /// <summary>Whether the arc ends are rounded.</summary>
    public bool RoundCaps { get; init; } = true;

    /// <summary>Number of tick marks around the arc, or <c>0</c> for none.</summary>
    public int Ticks { get; init; }

    /// <summary>Colour thresholds applied to the arc.</summary>
    public IReadOnlyList<ColorStop>? ColorStops { get; init; }

    /// <summary>Optional text drawn in the middle of the gauge.</summary>
    public TextWidget? Label { get; init; }
}

/// <summary>Plots recent history of a value.</summary>
public sealed record GraphWidget : ThemeWidget
{
    /// <summary>Expressions to plot, drawn in order.</summary>
    public IReadOnlyList<GraphSeries> Series { get; init; } = [];

    /// <summary>How many seconds of history the full width represents.</summary>
    public double WindowSeconds { get; init; } = 60d;

    /// <summary>Lower bound of the vertical axis, or <see langword="null"/> to scale automatically.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound of the vertical axis, or <see langword="null"/> to scale automatically.</summary>
    public double? Maximum { get; init; }

    /// <summary>Colour of the horizontal grid lines, or <see langword="null"/> for no grid.</summary>
    public string? GridColor { get; init; }

    /// <summary>Number of horizontal grid divisions.</summary>
    public int GridDivisions { get; init; } = 4;

    /// <summary>Background colour behind the plot.</summary>
    public string? BackgroundColor { get; init; }
}

/// <summary>One plotted line within a <see cref="GraphWidget"/>.</summary>
/// <param name="Value">Expression supplying the series value.</param>
/// <param name="Color">Line colour.</param>
/// <param name="Thickness">Line width in design units.</param>
/// <param name="Fill">Whether to fill the area under the line.</param>
/// <param name="FillOpacity">Opacity of that fill.</param>
/// <param name="Smooth">Whether to draw a smoothed curve instead of straight segments.</param>
public sealed record GraphSeries(
    string Value,
    string? Color = null,
    double Thickness = 2d,
    bool Fill = false,
    double FillOpacity = 0.25d,
    bool Smooth = true);

/// <summary>Draws an image from the theme's asset folder.</summary>
public sealed record ImageWidget : ThemeWidget
{
    /// <summary>Path to the image, relative to the theme folder.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>How the image fills its box.</summary>
    public ImageFit Fit { get; init; } = ImageFit.Contain;

    /// <summary>Tint applied to the image, multiplied with its own colours.</summary>
    public string? Tint { get; init; }

    /// <summary>
    /// For animated sources, the playback rate multiplier. <c>0</c> freezes on
    /// the first frame.
    /// </summary>
    public double AnimationSpeed { get; init; } = 1d;
}

/// <summary>Plays a looping clip baked for the panel (a <c>.ptclip</c> file).</summary>
public sealed record VideoWidget : ThemeWidget
{
    /// <summary>Path to the clip, relative to the theme folder.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>How the clip fills its box.</summary>
    public ImageFit Fit { get; init; } = ImageFit.Cover;

    /// <summary>Playback rate multiplier. <c>0</c> freezes on the first frame.</summary>
    public double Speed { get; init; } = 1d;
}

/// <summary>Which primitive a <see cref="ShapeWidget"/> draws.</summary>
public enum ShapeKind
{
    /// <summary>An axis-aligned rectangle, optionally with rounded corners.</summary>
    Rectangle = 0,

    /// <summary>An ellipse inscribed in the widget box.</summary>
    Ellipse,

    /// <summary>A straight line from the top-left to the bottom-right of the box.</summary>
    Line,
}

/// <summary>Draws a filled or stroked primitive.</summary>
public sealed record ShapeWidget : ThemeWidget
{
    /// <summary>Which primitive to draw.</summary>
    public ShapeKind Shape { get; init; } = ShapeKind.Rectangle;

    /// <summary>Fill colour, or <see langword="null"/> for no fill.</summary>
    public string? Fill { get; init; }

    /// <summary>Stroke colour, or <see langword="null"/> for no stroke.</summary>
    public string? Stroke { get; init; }

    /// <summary>Stroke width in design units.</summary>
    public double StrokeWidth { get; init; } = 1d;

    /// <summary>Corner radius for rectangles.</summary>
    public double CornerRadius { get; init; }

    /// <summary>
    /// A second fill colour; when set, the fill becomes a linear gradient from
    /// <see cref="Fill"/> to this colour.
    /// </summary>
    public string? GradientTo { get; init; }

    /// <summary>Gradient direction in degrees, clockwise from left-to-right.</summary>
    public double GradientAngle { get; init; }
}

/// <summary>Groups widgets so they can be positioned, hidden and faded together.</summary>
public sealed record GroupWidget : ThemeWidget
{
    /// <summary>Child widgets, positioned relative to this group's box.</summary>
    public IReadOnlyList<ThemeWidget> Items { get; init; } = [];

    /// <summary>Whether children are clipped to the group's box.</summary>
    public bool Clip { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public override IReadOnlyList<ThemeWidget> Children => Items;
}

/// <summary>
/// A colour that applies from a threshold value upwards.
/// </summary>
/// <param name="At">The value at or above which <paramref name="Color"/> applies.</param>
/// <param name="Color">The colour to use.</param>
public readonly record struct ColorStop(double At, string Color);
