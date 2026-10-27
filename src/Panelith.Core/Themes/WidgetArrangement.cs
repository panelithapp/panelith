namespace Panelith.Themes;

/// <summary>How a widget is lined up with the others around it, or with the canvas.</summary>
public enum Arrangement
{
    /// <summary>Move to the left edge of the frame.</summary>
    Left,

    /// <summary>Centre horizontally in the frame.</summary>
    Center,

    /// <summary>Move to the right edge of the frame.</summary>
    Right,

    /// <summary>Move to the top edge of the frame.</summary>
    Top,

    /// <summary>Centre vertically in the frame.</summary>
    Middle,

    /// <summary>Move to the bottom edge of the frame.</summary>
    Bottom,
}

/// <summary>
/// Lines widgets up. One widget lines up with the canvas; several line up with
/// the box that contains all of them.
/// </summary>
public static class WidgetArrangement
{
    /// <summary>The box that contains every rectangle.</summary>
    public static ThemeRect Union(IReadOnlyList<ThemeRect> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        if (boxes.Count == 0)
        {
            return ThemeRect.Empty;
        }

        var left = boxes.Min(b => b.X);
        var top = boxes.Min(b => b.Y);
        var right = boxes.Max(b => b.Right);
        var bottom = boxes.Max(b => b.Bottom);
        return new ThemeRect(left, top, right - left, bottom - top);
    }

    /// <summary>Moves <paramref name="box"/> to an edge or the centre of <paramref name="frame"/>.</summary>
    public static ThemeRect Align(ThemeRect box, ThemeRect frame, Arrangement arrangement) => arrangement switch
    {
        Arrangement.Left => box with { X = Math.Round(frame.X) },
        Arrangement.Center => box with { X = Math.Round(frame.CenterX - (box.Width / 2d)) },
        Arrangement.Right => box with { X = Math.Round(frame.Right - box.Width) },
        Arrangement.Top => box with { Y = Math.Round(frame.Y) },
        Arrangement.Middle => box with { Y = Math.Round(frame.CenterY - (box.Height / 2d)) },
        Arrangement.Bottom => box with { Y = Math.Round(frame.Bottom - box.Height) },
        _ => box,
    };

    /// <summary>
    /// Spaces the boxes evenly between the first and the last, leaving those two
    /// where they are. Fewer than three boxes are returned unchanged.
    /// </summary>
    public static ThemeRect[] Distribute(IReadOnlyList<ThemeRect> boxes, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var result = boxes.ToArray();
        if (result.Length < 3)
        {
            return result;
        }

        var order = Enumerable.Range(0, result.Length)
            .OrderBy(i => horizontal ? result[i].X : result[i].Y)
            .ThenBy(i => i)
            .ToArray();
        var first = result[order[0]];
        var last = result[order[^1]];
        var span = horizontal ? last.Right - first.X : last.Bottom - first.Y;
        var occupied = order.Sum(i => horizontal ? result[i].Width : result[i].Height);
        var gap = (span - occupied) / (order.Length - 1);
        var cursor = horizontal ? first.X : first.Y;
        foreach (var index in order)
        {
            var box = result[index];
            result[index] = horizontal ? box with { X = Math.Round(cursor) } : box with { Y = Math.Round(cursor) };
            cursor += (horizontal ? box.Width : box.Height) + gap;
        }

        return result;
    }

    /// <summary>Gives every box the width, the height, or both, of <paramref name="source"/>.</summary>
    public static ThemeRect[] MatchSize(IReadOnlyList<ThemeRect> boxes, int source, bool width, bool height)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var result = boxes.ToArray();
        if (source < 0 || source >= result.Length)
        {
            return result;
        }

        var sample = result[source];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = result[i] with
            {
                Width = width ? sample.Width : result[i].Width,
                Height = height ? sample.Height : result[i].Height,
            };
        }

        return result;
    }
}
