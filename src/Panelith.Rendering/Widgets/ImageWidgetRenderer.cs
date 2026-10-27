using Panelith.Expressions;
using Panelith.Sensors;
using Panelith.Themes;
using SkiaSharp;

namespace Panelith.Rendering.Widgets;

/// <summary>Draws static or binding-selected images, including simple frame animation.</summary>
internal static class ImageWidgetRenderer
{
    /// <summary>
    /// The most frames an animation may declare. Frame timings are walked on
    /// every paint, so a shared theme could otherwise ship a small file claiming
    /// an enormous frame count and spend the render loop counting.
    /// </summary>
    private const int MaxAnimationFrames = 1024;

    public static void Render(ImageWidget widget, ThemeRect rect, WidgetRenderContext ctx)
    {
        if (rect.IsEmpty || widget.Opacity <= 0d)
        {
            return;
        }

        var source = ResolveSource(widget.Source, ctx);
        if (string.Equals(source, MediaArt.Source, StringComparison.OrdinalIgnoreCase))
        {
            DrawAlbumArt(rect, widget, ctx);
            return;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            ctx.DrawErrorMarker(rect);
            return;
        }

        var dest = new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
        var paint = ctx.Paints.Image;
        paint.Color = SKColors.White.WithOpacity(ctx.Opacity);

        if (widget.Tint is not null && ThemeColor.TryParse(widget.Tint, out var tint))
        {
            paint.ColorFilter = SKColorFilter.CreateBlendMode(tint.ToSk(), SKBlendMode.Modulate);
        }
        else
        {
            paint.ColorFilter = null;
        }

        // Animated sources: decode frames via SKCodec when AnimationSpeed != 0.
        if (widget.AnimationSpeed != 0d && TryDrawAnimated(widget, source, dest, paint, ctx))
        {
            paint.ColorFilter = null;
            return;
        }

        var bitmap = ctx.Assets.GetBitmap(source);
        ImageFitHelper.Draw(ctx.Canvas, bitmap, dest, widget.Fit, paint);
        paint.ColorFilter = null;
    }

    private static SKBitmap? _album;
    private static long _albumStamp = -1;

    private static void DrawAlbumArt(ThemeRect rect, ImageWidget widget, WidgetRenderContext ctx)
    {
        var path = MediaArt.FilePath;
        if (path is null || !File.Exists(path))
        {
            return;
        }

        long stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (IOException)
        {
            return;
        }

        if (_album is null || stamp != _albumStamp)
        {
            _album?.Dispose();
            _album = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                _album = SKBitmap.Decode(stream);
                _albumStamp = stamp;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }

        if (_album is null)
        {
            return;
        }

        var dest = new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
        var paint = ctx.Paints.Image;
        paint.Color = SKColors.White.WithOpacity(ctx.Opacity);
        paint.ColorFilter = widget.Tint is not null && ThemeColor.TryParse(widget.Tint, out var tint)
            ? SKColorFilter.CreateBlendMode(tint.ToSk(), SKBlendMode.Modulate)
            : null;
        ImageFitHelper.Draw(ctx.Canvas, _album, dest, widget.Fit, paint);
        paint.ColorFilter = null;
    }

    private static string ResolveSource(string source, WidgetRenderContext ctx)
    {
        // Allow "{@sensor}" style path selection without a Core model change.
        if (source.Contains('{', StringComparison.Ordinal)
            && TextTemplate.TryParse(source, out var template, out _))
        {
            return template!.Render(ctx.Bindings);
        }

        return source;
    }

    private static bool TryDrawAnimated(
        ImageWidget widget,
        string source,
        SKRect dest,
        SKPaint paint,
        WidgetRenderContext ctx)
    {
        var bytes = ctx.Assets.TryReadBytes(source);
        if (bytes is null)
        {
            return false;
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.FrameCount <= 1 || codec.FrameCount > MaxAnimationFrames
            || codec.Info.Width > SkiaImageCodec.MaxImageDimension || codec.Info.Height > SkiaImageCodec.MaxImageDimension)
        {
            return false;
        }

        var frameCount = codec.FrameCount;
        var durations = codec.FrameInfo;
        var totalMs = 0;
        for (var i = 0; i < durations.Length; i++)
        {
            var d = durations[i].Duration;
            totalMs += d > 0 ? d : 100;
        }

        if (totalMs <= 0)
        {
            totalMs = frameCount * 100;
        }

        var elapsed = (ctx.Now.ToUnixTimeMilliseconds() * widget.AnimationSpeed) % totalMs;
        if (elapsed < 0)
        {
            elapsed += totalMs;
        }

        var cursor = 0d;
        var frameIndex = 0;
        for (var i = 0; i < frameCount; i++)
        {
            var d = i < durations.Length && durations[i].Duration > 0 ? durations[i].Duration : 100;
            cursor += d;
            if (elapsed < cursor)
            {
                frameIndex = i;
                break;
            }

            frameIndex = i;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var opts = new SKCodecOptions(frameIndex);
        var result = codec.GetPixels(info, bitmap.GetPixels(), opts);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return false;
        }

        ImageFitHelper.Draw(ctx.Canvas, bitmap, dest, widget.Fit, paint);
        return true;
    }
}
