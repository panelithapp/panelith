using Panelith.Expressions;
using Panelith.Sensors;

namespace Panelith.Themes;

/// <summary>How serious a validation finding is.</summary>
public enum ThemeDiagnosticSeverity
{
    /// <summary>Worth mentioning but harmless.</summary>
    Info = 0,

    /// <summary>The theme will render, but probably not as intended.</summary>
    Warning,

    /// <summary>The theme cannot render correctly.</summary>
    Error,
}

/// <summary>One finding from validating a theme.</summary>
/// <param name="Severity">How serious the finding is.</param>
/// <param name="Message">What is wrong, phrased for a theme author.</param>
/// <param name="WidgetId">The widget the finding relates to, when applicable.</param>
/// <param name="Path">A JSON-ish path to the offending property.</param>
public readonly record struct ThemeDiagnostic(
    ThemeDiagnosticSeverity Severity,
    string Message,
    string? WidgetId = null,
    string? Path = null)
{
    /// <inheritdoc />
    public override string ToString()
    {
        var where = Path is not null ? $" [{Path}]" : WidgetId is not null ? $" [{WidgetId}]" : string.Empty;
        return $"{Severity}: {Message}{where}";
    }
}

/// <summary>
/// Checks a theme for problems before it is rendered.
/// </summary>
/// <remarks>
/// Validation runs in the CLI (<c>panelith theme validate</c>), in the editor
/// as the author types, and once when a theme is loaded at runtime. It is
/// deliberately pure: no graphics stack, no sensor backend, no file I/O beyond
/// optionally checking that referenced assets exist.
/// </remarks>
public static class ThemeValidator
{
    /// <summary>
    /// The widest or tallest a canvas may be. Panels are a few thousand pixels
    /// across at most, so this leaves room to design generously while keeping a
    /// shared theme from asking for a surface nothing could lay out.
    /// </summary>
    public const int MaxCanvasDimension = 8192;

    /// <summary>
    /// Validates a theme.
    /// </summary>
    /// <param name="theme">The theme to check.</param>
    /// <param name="availableSensors">
    /// Sensor references known to exist on this machine. When supplied, bindings
    /// naming anything else are reported as warnings rather than silently
    /// rendering as placeholders.
    /// </param>
    /// <param name="checkAssets">
    /// Whether to verify that referenced image files exist relative to
    /// <see cref="Theme.BasePath"/>.
    /// </param>
    public static IReadOnlyList<ThemeDiagnostic> Validate(
        Theme theme,
        IReadOnlySet<string>? availableSensors = null,
        bool checkAssets = true)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var diagnostics = new List<ThemeDiagnostic>();

        ValidateMetadata(theme, diagnostics);
        ValidateCanvas(theme, diagnostics);
        ValidateVariables(theme, diagnostics);
        ValidateBackground(theme, diagnostics, checkAssets);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var widget in theme.Widgets)
        {
            ValidateWidget(theme, widget, $"widgets[{index}]", seenIds, availableSensors, checkAssets, diagnostics);
            index++;
        }

        if (theme.Widgets.Count == 0)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                "The theme has no widgets, so it will render as an empty background.",
                Path: "widgets"));
        }

        return diagnostics;
    }

    /// <summary>Whether <paramref name="diagnostics"/> contains anything fatal.</summary>
    public static bool HasErrors(IEnumerable<ThemeDiagnostic> diagnostics) =>
        diagnostics.Any(d => d.Severity == ThemeDiagnosticSeverity.Error);

    private static void ValidateMetadata(Theme theme, List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(theme.Id))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                "The theme has no id, so its folder name will be used instead.",
                Path: "id"));
        }

        if (string.IsNullOrWhiteSpace(theme.Name))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Info,
                "The theme has no display name.",
                Path: "name"));
        }
    }

    private static void ValidateCanvas(Theme theme, List<ThemeDiagnostic> diagnostics)
    {
        if (theme.Canvas.Width <= 0 || theme.Canvas.Height <= 0)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"The canvas must have a positive size but is {theme.Canvas.Width}x{theme.Canvas.Height}.",
                Path: "canvas"));
        }
        else if (theme.Canvas.Width > MaxCanvasDimension || theme.Canvas.Height > MaxCanvasDimension)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"The canvas is {theme.Canvas.Width}x{theme.Canvas.Height}; it may be at most {MaxCanvasDimension}x{MaxCanvasDimension}.",
                Path: "canvas"));
        }

        if (theme.Canvas.Background is { } background && !ThemeColor.TryParse(background, out _))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"\"{background}\" is not a colour Panelith recognises.",
                Path: "canvas.background"));
        }
    }

    private static void ValidateVariables(Theme theme, List<ThemeDiagnostic> diagnostics)
    {
        if (theme.Variables is null)
        {
            return;
        }

        foreach (var (name, expression) in theme.Variables)
        {
            if (!BindingExpression.TryParse(expression, out var parsed, out var error))
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error,
                    $"Variable ${name} is not a valid expression. {error}",
                    Path: $"variables.{name}"));
                continue;
            }

            // A variable referring to itself, directly or through another
            // variable, would recurse until the stack ran out at render time.
            if (ReferencesSelf(theme, name, parsed!, []))
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error,
                    $"Variable ${name} refers to itself, which would never finish evaluating.",
                    Path: $"variables.{name}"));
            }
        }
    }

    private static bool ReferencesSelf(Theme theme, string target, BindingExpression expression, HashSet<string> visited)
    {
        foreach (var reference in expression.ReferencedVariables)
        {
            if (string.Equals(reference, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!visited.Add(reference))
            {
                continue;
            }

            if (theme.Variables is not null
                && theme.Variables.TryGetValue(reference, out var nested)
                && BindingExpression.TryParse(nested, out var parsed, out _)
                && ReferencesSelf(theme, target, parsed!, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateBackground(Theme theme, List<ThemeDiagnostic> diagnostics, bool checkAssets)
    {
        var background = theme.Background;

        if (background.Color is { } color && !ThemeColor.TryParse(color, out _))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"\"{color}\" is not a colour Panelith recognises.",
                Path: "background.color"));
        }

        if (background.GradientTo is { } gradient && !ThemeColor.TryParse(gradient, out _))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"\"{gradient}\" is not a colour Panelith recognises.",
                Path: "background.gradientTo"));
        }

        if (checkAssets && !string.IsNullOrWhiteSpace(background.Image))
        {
            CheckAsset(theme, background.Image, "background.image", null, diagnostics);
        }

        CheckColor(background.Scrim, "background.scrim", null, diagnostics);
        if (!string.IsNullOrWhiteSpace(background.Video))
        {
            CheckClip(theme, background.Video, "background.video", null, checkAssets, diagnostics);
        }
    }

    private static void CheckClip(Theme theme, string source, string path, string? widgetId, bool checkAssets, List<ThemeDiagnostic> diagnostics)
    {
        if (!source.EndsWith(Media.ClipFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"\"{source}\" is not a {Media.ClipFile.Extension} clip. Import videos through Panelith so they are baked for the panel.",
                widgetId,
                path));
            return;
        }

        if (!checkAssets || theme.BasePath is null)
        {
            return;
        }

        var before = diagnostics.Count;
        CheckAsset(theme, source, path, widgetId, diagnostics);
        if (diagnostics.Count != before)
        {
            return;
        }

        try
        {
            Media.ClipFile.ReadInfo(Path.GetFullPath(Path.Combine(theme.BasePath, source)));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"The clip \"{source}\" cannot be played: {ex.Message}",
                widgetId,
                path));
        }
    }

    private static void ValidateWidget(
        Theme theme,
        ThemeWidget widget,
        string path,
        HashSet<string> seenIds,
        IReadOnlySet<string>? availableSensors,
        bool checkAssets,
        List<ThemeDiagnostic> diagnostics)
    {
        if (widget.Id is { } id && !string.IsNullOrWhiteSpace(id) && !seenIds.Add(id))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"More than one widget has the id \"{id}\".",
                id,
                $"{path}.id"));
        }

        if (widget is not GroupWidget && (widget.Width <= 0d || widget.Height <= 0d))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"The widget has no area ({widget.Width}x{widget.Height}) and will not be visible.",
                widget.Id,
                path));
        }

        if (widget.Opacity is < 0d or > 1d)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"Opacity must be between 0 and 1 but is {widget.Opacity}.",
                widget.Id,
                $"{path}.opacity"));
        }

        if (widget.VisibleWhen is { } condition)
        {
            CheckExpression(condition, $"{path}.visibleWhen", widget.Id, availableSensors, diagnostics);
        }

        switch (widget)
        {
            case TextWidget text:
                CheckTemplate(text.Text, $"{path}.text", widget.Id, availableSensors, diagnostics);
                CheckColor(text.Color, $"{path}.color", widget.Id, diagnostics);
                CheckColor(text.StrokeColor, $"{path}.strokeColor", widget.Id, diagnostics);
                CheckColorStops(text.ColorStops, $"{path}.colorStops", widget.Id, diagnostics);
                if (text.ColorBinding is { } colorBinding)
                {
                    CheckExpression(colorBinding, $"{path}.colorBinding", widget.Id, availableSensors, diagnostics);
                }

                if (text.FontSize <= 0d)
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Error,
                        $"Font size must be positive but is {text.FontSize}.",
                        widget.Id,
                        $"{path}.fontSize"));
                }

                break;

            case BarWidget bar:
                CheckExpression(bar.Value, $"{path}.value", widget.Id, availableSensors, diagnostics);
                CheckColor(bar.Color, $"{path}.color", widget.Id, diagnostics);
                CheckColor(bar.TrackColor, $"{path}.trackColor", widget.Id, diagnostics);
                CheckColorStops(bar.ColorStops, $"{path}.colorStops", widget.Id, diagnostics);
                CheckRange(bar.Minimum, bar.Maximum, path, widget.Id, diagnostics);
                break;

            case GaugeWidget gauge:
                CheckExpression(gauge.Value, $"{path}.value", widget.Id, availableSensors, diagnostics);
                CheckColor(gauge.Color, $"{path}.color", widget.Id, diagnostics);
                CheckColor(gauge.TrackColor, $"{path}.trackColor", widget.Id, diagnostics);
                CheckColorStops(gauge.ColorStops, $"{path}.colorStops", widget.Id, diagnostics);
                CheckRange(gauge.Minimum, gauge.Maximum, path, widget.Id, diagnostics);
                if (Math.Abs(gauge.SweepAngle) < 1d)
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Warning,
                        $"A sweep of {gauge.SweepAngle} degrees will not be visible.",
                        widget.Id,
                        $"{path}.sweepAngle"));
                }

                if (gauge.Label is { } label)
                {
                    ValidateWidget(theme, label, $"{path}.label", seenIds, availableSensors, checkAssets, diagnostics);
                }

                break;

            case GraphWidget graph:
                if (graph.Series.Count == 0)
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Warning,
                        "The graph has no series and will render empty.",
                        widget.Id,
                        $"{path}.series"));
                }

                for (var i = 0; i < graph.Series.Count; i++)
                {
                    CheckExpression(graph.Series[i].Value, $"{path}.series[{i}].value", widget.Id, availableSensors, diagnostics);
                    CheckColor(graph.Series[i].Color, $"{path}.series[{i}].color", widget.Id, diagnostics);
                }

                CheckColor(graph.GridColor, $"{path}.gridColor", widget.Id, diagnostics);
                CheckColor(graph.BackgroundColor, $"{path}.backgroundColor", widget.Id, diagnostics);
                if (graph.WindowSeconds <= 0d)
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Error,
                        $"The history window must be positive but is {graph.WindowSeconds} seconds.",
                        widget.Id,
                        $"{path}.windowSeconds"));
                }

                break;

            case ImageWidget image:
                if (string.IsNullOrWhiteSpace(image.Source))
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Error,
                        "The image widget has no source.",
                        widget.Id,
                        $"{path}.source"));
                }
                else if (checkAssets && !string.Equals(image.Source, MediaArt.Source, StringComparison.OrdinalIgnoreCase))
                {
                    CheckAsset(theme, image.Source, $"{path}.source", widget.Id, diagnostics);
                }

                CheckColor(image.Tint, $"{path}.tint", widget.Id, diagnostics);
                break;

            case VideoWidget video:
                if (string.IsNullOrWhiteSpace(video.Source))
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Error,
                        "The video widget has no source.",
                        widget.Id,
                        $"{path}.source"));
                }
                else
                {
                    CheckClip(theme, video.Source, $"{path}.source", widget.Id, checkAssets, diagnostics);
                }

                break;

            case ShapeWidget shape:
                CheckColor(shape.Fill, $"{path}.fill", widget.Id, diagnostics);
                CheckColor(shape.Stroke, $"{path}.stroke", widget.Id, diagnostics);
                CheckColor(shape.GradientTo, $"{path}.gradientTo", widget.Id, diagnostics);
                if (shape.Fill is null && shape.Stroke is null)
                {
                    diagnostics.Add(new ThemeDiagnostic(
                        ThemeDiagnosticSeverity.Warning,
                        "The shape has neither a fill nor a stroke and will not be visible.",
                        widget.Id,
                        path));
                }

                break;

            case GroupWidget group:
                for (var i = 0; i < group.Items.Count; i++)
                {
                    ValidateWidget(theme, group.Items[i], $"{path}.items[{i}]", seenIds, availableSensors, checkAssets, diagnostics);
                }

                break;
        }
    }

    private static void CheckRange(double? minimum, double? maximum, string path, string? widgetId, List<ThemeDiagnostic> diagnostics)
    {
        if (minimum is { } min && maximum is { } max && min >= max)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"The minimum ({min}) must be below the maximum ({max}).",
                widgetId,
                path));
        }
    }

    private static void CheckColor(string? color, string path, string? widgetId, List<ThemeDiagnostic> diagnostics)
    {
        if (color is not null && !ThemeColor.TryParse(color, out _))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"\"{color}\" is not a colour Panelith recognises. Use #rrggbb, rgba(), or a named colour.",
                widgetId,
                path));
        }
    }

    private static void CheckColorStops(
        IReadOnlyList<ColorStop>? stops,
        string path,
        string? widgetId,
        List<ThemeDiagnostic> diagnostics)
    {
        if (stops is null || stops.Count == 0)
        {
            return;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            CheckColor(stops[i].Color, $"{path}[{i}].color", widgetId, diagnostics);

            // Stops are matched by scanning for the highest threshold at or
            // below the value, so out-of-order entries silently never match.
            if (i > 0 && stops[i].At <= stops[i - 1].At)
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Warning,
                    $"Colour stops must be ordered by ascending value; {stops[i].At} follows {stops[i - 1].At}.",
                    widgetId,
                    $"{path}[{i}].at"));
            }
        }
    }

    private static void CheckExpression(
        string expression,
        string path,
        string? widgetId,
        IReadOnlySet<string>? availableSensors,
        List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                "The binding is empty.",
                widgetId,
                path));
            return;
        }

        if (!BindingExpression.TryParse(expression, out var parsed, out var error))
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, error!, widgetId, path));
            return;
        }

        CheckSensorReferences(parsed!.ReferencedSensors, path, widgetId, availableSensors, diagnostics);
    }

    private static void CheckTemplate(
        string template,
        string path,
        string? widgetId,
        IReadOnlySet<string>? availableSensors,
        List<ThemeDiagnostic> diagnostics)
    {
        if (!TextTemplate.TryParse(template, out var parsed, out var error))
        {
            diagnostics.Add(new ThemeDiagnostic(ThemeDiagnosticSeverity.Error, error!, widgetId, path));
            return;
        }

        CheckSensorReferences(parsed!.ReferencedSensors, path, widgetId, availableSensors, diagnostics);
    }

    private static void CheckSensorReferences(
        IReadOnlyList<string> references,
        string path,
        string? widgetId,
        IReadOnlySet<string>? availableSensors,
        List<ThemeDiagnostic> diagnostics)
    {
        foreach (var reference in references)
        {
            if (WellKnownSensors.IsAlias(reference) && !WellKnownSensors.All.ContainsKey(reference))
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error,
                    $"\"{reference}\" is not a known sensor alias. Run 'panelith sensors list --aliases' to see them all.",
                    widgetId,
                    path));
                continue;
            }

            if (availableSensors is not null && !availableSensors.Contains(reference))
            {
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Warning,
                    $"\"{reference}\" is not available on this machine and will render as \"{ValueFormatter.MissingPlaceholder}\".",
                    widgetId,
                    path));
            }
        }
    }

    private static void CheckAsset(
        Theme theme,
        string source,
        string path,
        string? widgetId,
        List<ThemeDiagnostic> diagnostics)
    {
        if (theme.BasePath is null)
        {
            return;
        }

        if (Path.IsPathRooted(source))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Warning,
                $"\"{source}\" is an absolute path, so the theme will not work on another machine.",
                widgetId,
                path));
            return;
        }

        // A theme must not reach outside its own folder: themes are shared and
        // are expected to be self-contained. This is the same rule the renderer
        // enforces when it actually reads the file.
        if (!ThemePath.TryResolveInside(theme.BasePath, source, out var full))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"\"{source}\" points outside the theme folder.",
                widgetId,
                path));
            return;
        }

        if (!File.Exists(full))
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Error,
                $"The asset \"{source}\" does not exist in the theme folder.",
                widgetId,
                path));
        }
    }
}
