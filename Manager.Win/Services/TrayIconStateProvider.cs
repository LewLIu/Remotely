using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Remotely.Manager.Win.Services;
using SkiaSharp;

namespace Remotely.Manager.Win.Services;

public sealed class TrayIconStateProvider
{
    private static readonly Uri IconUri = new("avares://Remotely_Manager/Assets/Remotely_Icon.png");

    public WindowIcon Create(RemotelyServiceState state)
    {
        using var source = AssetLoader.Open(IconUri);
        using var bitmap = SKBitmap.Decode(source)
            ?? throw new InvalidOperationException("Unable to decode Remotely tray icon.");

        if (state == RemotelyServiceState.Stopped)
        {
            MakeGrayscale(bitmap);
        }
        else if (state is RemotelyServiceState.StartPending or RemotelyServiceState.StopPending)
        {
            DrawBadge(bitmap, SKColors.Gold);
        }
        else if (state is RemotelyServiceState.Error or RemotelyServiceState.Missing)
        {
            DrawBadge(bitmap, SKColors.Red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new WindowIcon(new Bitmap(stream));
    }

    private static void MakeGrayscale(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var gray = (byte)Math.Clamp((int)Math.Round(
                    color.Red * 0.299 + color.Green * 0.587 + color.Blue * 0.114), 0, 255);
                bitmap.SetPixel(x, y, new SKColor(gray, gray, gray, color.Alpha));
            }
        }
    }

    private static void DrawBadge(SKBitmap bitmap, SKColor color)
    {
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        var radius = Math.Max(3f, Math.Min(bitmap.Width, bitmap.Height) * 0.18f);
        canvas.DrawCircle(bitmap.Width - radius, bitmap.Height - radius, radius, paint);
    }
}
