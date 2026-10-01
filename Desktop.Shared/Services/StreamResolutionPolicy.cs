using SkiaSharp;

namespace Remotely.Desktop.Shared.Services;

public static class StreamResolutionPolicy
{
    public static SKSizeI CalculateSize(
        int sourceWidth,
        int sourceHeight,
        int? maxWidth,
        int? maxHeight)
    {
        if (sourceWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));

        if (maxWidth is null && maxHeight is null)
        {
            return new SKSizeI(sourceWidth, sourceHeight);
        }

        var widthScale = maxWidth is null ? 1d : (double)maxWidth.Value / sourceWidth;
        var heightScale = maxHeight is null ? 1d : (double)maxHeight.Value / sourceHeight;
        var scale = Math.Min(1d, Math.Min(widthScale, heightScale));

        return new SKSizeI(
            Math.Max(1, (int)Math.Floor(sourceWidth * scale)),
            Math.Max(1, (int)Math.Floor(sourceHeight * scale)));
    }

    public static SKRectI MapToSource(
        SKRectI streamRect,
        int sourceWidth,
        int sourceHeight,
        int streamWidth,
        int streamHeight)
    {
        if (sourceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (streamWidth <= 0) throw new ArgumentOutOfRangeException(nameof(streamWidth));
        if (streamHeight <= 0) throw new ArgumentOutOfRangeException(nameof(streamHeight));

        var xScale = (double)sourceWidth / streamWidth;
        var yScale = (double)sourceHeight / streamHeight;

        var left = Math.Clamp((int)Math.Floor(streamRect.Left * xScale), 0, sourceWidth);
        var top = Math.Clamp((int)Math.Floor(streamRect.Top * yScale), 0, sourceHeight);
        var right = Math.Clamp((int)Math.Ceiling(streamRect.Right * xScale), left, sourceWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(streamRect.Bottom * yScale), top, sourceHeight);

        return new SKRectI(left, top, right, bottom);
    }
}
