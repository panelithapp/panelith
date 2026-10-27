using System.Text;
using Panelith.Expressions;
using Panelith.Themes;
using SkiaSharp;

namespace Panelith.Rendering.Widgets;

/// <summary>Draws templated text with shrink-to-fit and width stabilisation.</summary>
internal static class TextWidgetRenderer
{
    private const char FigureSpace = '\u2007';

    public static void Render(TextWidget widget, ThemeRect rect, WidgetRenderContext ctx)
    {
        if (rect.IsEmpty || widget.Opacity <= 0d)
        {
            return;
        }

        if (!TextTemplate.TryParse(widget.Text, out var template, out _))
        {
            ctx.DrawErrorMarker(rect);
            return;
        }

        var text = template!.Render(ctx.Bindings);
        if (widget.StabilizeWidth)
        {
            text = Stabilize(text);
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var family = widget.FontFamily ?? ctx.Defaults.FontFamily;
        var typeface = ctx.Fonts.Resolve(family, widget.FontWeight, widget.Italic);
        var font = ctx.Paints.Font;
        font.Typeface = typeface;
        font.Size = (float)Math.Max(1d, widget.FontSize);
        font.Edging = SKFontEdging.SubpixelAntialias;

        var layout = new TextLayout(widget.LetterSpacing, widget.TabularNumbers);
        float naturalWidth = MeasureWidth(font, text, layout);
        var scrolling = widget.Scroll && naturalWidth > rect.Width + 1d;
        if (widget.ShrinkToFit && !scrolling)
        {
            var fits = ShrinkToFit(font, text, rect, layout);
            font.Size = StickySize(ctx.StateFor<FitState>(widget), fits, ctx.Ticks);
        }

        var color = ResolveTextColor(widget, template, ctx);

        float textWidth = MeasureWidth(font, text, layout);
        var metrics = font.Metrics;
        float textHeight = metrics.Descent - metrics.Ascent;

        var x = widget.Align switch
        {
            HorizontalAlignment.Center => (float)(rect.X + ((rect.Width - textWidth) / 2d)),
            HorizontalAlignment.Right => (float)(rect.Right - textWidth),
            _ => (float)rect.X,
        };

        var baseline = widget.VerticalAlign switch
        {
            VerticalAlignment.Middle => (float)(rect.CenterY - ((metrics.Ascent + metrics.Descent) / 2d)),
            VerticalAlignment.Bottom => (float)(rect.Bottom - metrics.Descent),
            VerticalAlignment.Baseline => (float)rect.Bottom,
            _ => (float)(rect.Y - metrics.Ascent),
        };

        _ = textHeight;

        if (scrolling)
        {
            var shift = MarqueeShift((float)(textWidth - rect.Width), WidgetRenderContext.SecondsBetween(0, ctx.Ticks));
            var clip = new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
            ctx.Canvas.Save();
            ctx.Canvas.ClipRect(clip);
            DrawLine(ctx, widget, text, (float)rect.X - shift, baseline, font, layout, color);
            DrawLine(ctx, widget, text, (float)rect.X - shift + textWidth + MarqueeGap, baseline, font, layout, color);
            ctx.Canvas.Restore();
            return;
        }

        DrawLine(ctx, widget, text, x, baseline, font, layout, color);
    }

    private static void DrawLine(
        WidgetRenderContext ctx,
        TextWidget widget,
        string text,
        float x,
        float baseline,
        SKFont font,
        TextLayout layout,
        SKColor color)
    {
        if (widget.StrokeColor is not null && widget.StrokeWidth > 0d)
        {
            var stroke = ctx.Paints.Stroke;
            stroke.Color = ctx.ResolveColor(widget.StrokeColor, null);
            stroke.StrokeWidth = (float)widget.StrokeWidth;
            stroke.Style = SKPaintStyle.Stroke;
            stroke.IsStroke = true;
            DrawText(ctx.Canvas, text, x, baseline, font, stroke, layout);
        }

        var fill = ctx.Paints.Fill;
        fill.Color = color;
        fill.Style = SKPaintStyle.Fill;
        fill.IsStroke = false;
        DrawText(ctx.Canvas, text, x, baseline, font, fill, layout);
    }

    /// <summary>Empty space between the end of a scrolling line and the copy that follows it.</summary>
    internal const float MarqueeGap = 64f;

    /// <summary>How long a scrolling line waits at the start of each pass.</summary>
    internal const double MarqueePauseSeconds = 0.8d;

    /// <summary>How fast a scrolling line moves, in design units per second.</summary>
    internal const double MarqueeSpeed = 48d;

    /// <summary>
    /// How far a line that overflows its box by <paramref name="overflow"/> has
    /// moved at <paramref name="elapsedSeconds"/>. It pauses, slides until the
    /// whole line and a gap have passed, then starts again.
    /// </summary>
    internal static float MarqueeShift(float overflow, double elapsedSeconds)
    {
        if (overflow <= 0f || elapsedSeconds <= 0d)
        {
            return 0f;
        }

        var travel = overflow + MarqueeGap;
        var cycle = MarqueePauseSeconds + (travel / MarqueeSpeed);
        var t = elapsedSeconds % cycle;
        return t < MarqueePauseSeconds ? 0f : (float)((t - MarqueePauseSeconds) * MarqueeSpeed);
    }

    /// <summary>How long a larger font must keep fitting before the text grows back to it.</summary>
    internal static readonly TimeSpan GrowDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The font size to draw at, given the largest size that fits this frame.
    /// Shrinks at once so text never overflows, but grows back only after the
    /// larger size has fit for <see cref="GrowDelay"/>, so a value flicking
    /// between widths does not make the text pulse.
    /// </summary>
    internal static float StickySize(FitState state, float fits, long ticks)
    {
        if (state.Size <= 0f || fits < state.Size)
        {
            state.Size = fits;
            state.GrowingSince = null;
        }
        else if (fits > state.Size)
        {
            state.GrowingSince ??= ticks;
            if (WidgetRenderContext.SecondsBetween(state.GrowingSince.Value, ticks) >= GrowDelay.TotalSeconds)
            {
                state.Size = fits;
                state.GrowingSince = null;
            }
        }
        else
        {
            state.GrowingSince = null;
        }

        return state.Size;
    }

    internal sealed class FitState
    {
        public float Size { get; set; }

        public long? GrowingSince { get; set; }
    }

    internal readonly record struct TextLayout(double LetterSpacing, bool Tabular);

    private static SKColor ResolveTextColor(TextWidget widget, TextTemplate template, WidgetRenderContext ctx)
    {
        if (widget.ColorStops is { Count: > 0 })
        {
            double? number = null;
            if (widget.ColorBinding is { } binding
                && BindingExpression.TryParse(binding, out var expr, out _))
            {
                number = expr!.Evaluate(ctx.Bindings).AsNumber();
            }
            else if (template.PrimarySensor is { } sensor)
            {
                number = ctx.Bindings.ResolveSensor(sensor).AsNumber();
            }

            if (number is { } value)
            {
                var fallback = ctx.Colors.Get(widget.Color, ctx.Defaults.Color, ThemeColor.White).ToTheme();
                return ColorStopResolver.Resolve(widget.ColorStops, value, fallback).ToSk().WithOpacity(ctx.Opacity);
            }
        }

        return ctx.ResolveColor(widget.Color, ctx.Defaults.Color);
    }

    /// <summary>The largest size, from the widget's own down, at which the text fits its box.</summary>
    private static float ShrinkToFit(SKFont font, string text, ThemeRect rect, TextLayout layout)
    {
        var size = font.Size;
        while (size > 4f)
        {
            font.Size = size;
            if (MeasureWidth(font, text, layout) <= rect.Width
                && (font.Metrics.Descent - font.Metrics.Ascent) <= rect.Height)
            {
                return size;
            }

            size -= 0.5f;
        }

        return Math.Max(4f, size);
    }

    private static bool NeedsPerCharacter(string text, TextLayout layout) =>
        layout.LetterSpacing != 0d || (layout.Tabular && text.AsSpan().IndexOfAnyInRange('0', '9') >= 0);

    private static bool IsTabular(char ch) => char.IsAsciiDigit(ch) || ch == FigureSpace;

    /// <summary>The advance of the widest digit, which every digit gets in tabular layout.</summary>
    private static float DigitWidth(SKFont font)
    {
        var widest = 0f;
        for (var digit = '0'; digit <= '9'; digit++)
        {
            widest = Math.Max(widest, font.MeasureText(DigitStrings[digit - '0']));
        }

        return widest;
    }

    private static readonly string[] DigitStrings = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    internal static float MeasureWidth(SKFont font, string text, TextLayout layout)
    {
        if (!NeedsPerCharacter(text, layout))
        {
            return font.MeasureText(text);
        }

        if (layout.LetterSpacing == 0d)
        {
            var digitWidth = DigitWidth(font);
            var total = 0f;
            foreach (var (start, length, digits) in Runs(text))
            {
                total += digits ? digitWidth * length : font.MeasureText(text.AsSpan(start, length));
            }

            return total;
        }

        var digit = layout.Tabular ? DigitWidth(font) : 0f;
        var width = 0f;
        foreach (var ch in text)
        {
            width += layout.Tabular && IsTabular(ch) ? digit : font.MeasureText(ch.ToString());
        }

        if (text.Length > 1)
        {
            width += (float)(layout.LetterSpacing * (text.Length - 1));
        }

        return width;
    }

    /// <summary>Splits text into runs of tabular digits and runs of everything else.</summary>
    private static IEnumerable<(int Start, int Length, bool Digits)> Runs(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var digits = IsTabular(text[start]);
            var end = start + 1;
            while (end < text.Length && IsTabular(text[end]) == digits)
            {
                end++;
            }

            yield return (start, end - start, digits);
            start = end;
        }
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float baseline,
        SKFont font,
        SKPaint paint,
        TextLayout layout)
    {
        if (!NeedsPerCharacter(text, layout))
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
            return;
        }

        var digit = layout.Tabular ? DigitWidth(font) : 0f;
        var cursor = x;
        if (layout.LetterSpacing == 0d)
        {
            foreach (var (start, length, digits) in Runs(text))
            {
                if (!digits)
                {
                    var run = text.Substring(start, length);
                    canvas.DrawText(run, cursor, baseline, SKTextAlign.Left, font, paint);
                    cursor += font.MeasureText(run);
                    continue;
                }

                for (var i = start; i < start + length; i++)
                {
                    if (text[i] != FigureSpace)
                    {
                        var s = DigitStrings[text[i] - '0'];
                        canvas.DrawText(s, cursor + ((digit - font.MeasureText(s)) / 2f), baseline, SKTextAlign.Left, font, paint);
                    }

                    cursor += digit;
                }
            }

            return;
        }

        foreach (var ch in text)
        {
            var s = ch.ToString();
            var advance = font.MeasureText(s);
            if (layout.Tabular && IsTabular(ch))
            {
                // Centre each digit in a cell as wide as the widest digit.
                if (ch != FigureSpace)
                {
                    canvas.DrawText(s, cursor + ((digit - advance) / 2f), baseline, SKTextAlign.Left, font, paint);
                }

                advance = digit;
            }
            else
            {
                canvas.DrawText(s, cursor, baseline, SKTextAlign.Left, font, paint);
            }

            cursor += advance + (float)layout.LetterSpacing;
        }
    }

    /// <summary>
    /// Pads digit runs with figure spaces so "99" and "100" occupy the same
    /// advance width and a centred readout does not jitter.
    /// </summary>
    internal static string Stabilize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Pad each maximal digit run to at least 3 characters — enough for the
        // common 0-100 percentage / temperature case without over-padding clocks.
        var builder = new StringBuilder(text.Length + 4);
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsAsciiDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                var run = text[start..i];
                // Leave decimals ("13.5"), clocks ("1:00:07") and similar
                // punctuation alone. Padding those runs inserts figure spaces
                // that read as a broken gap ("13.  5").
                var stuckToPunctuation = start > 0 && text[start - 1] is '.' or ':'
                    || i < text.Length && text[i] is '.' or ':';
                const int minDigits = 3;
                if (!stuckToPunctuation && run.Length < minDigits)
                {
                    builder.Append(FigureSpace, minDigits - run.Length);
                }

                builder.Append(run);
            }
            else
            {
                builder.Append(text[i]);
                i++;
            }
        }

        return builder.ToString();
    }
}
