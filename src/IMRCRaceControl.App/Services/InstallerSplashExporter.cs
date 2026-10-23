using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IMRCRaceControl.Services;

internal static class InstallerSplashExporter
{
    public static void Export(string output, ImageSource logo)
    {
        const int width = 720, height = 380, frameCount = 24;
        var encoder = new GifBitmapEncoder();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        for (var frame = 0; frame < frameCount; frame++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brush("#111318"), null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(36, 40, 49), Color.FromRgb(17, 19, 24), 35), null, new Rect(0, 0, width, 190));
                dc.DrawRectangle(Brush("#DB3545"), null, new Rect(0, 0, width, 5));
                // Subtle diagonal track lines keep the background quiet behind the wordmark.
                var line = new Pen(Brush("#30313B"), 1);
                for (var x = 510; x < 900; x += 34)
                    dc.DrawLine(line, new Point(x, 5), new Point(x - 160, 185));
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                dc.DrawImage(logo, new Rect(38, 35, 136, 136));
                Text(dc, "IMRC RACING N REPAIR", 200, 52, 13, "#BDC2CB", FontWeights.SemiBold);
                Text(dc, "RACE CONTROL", 197, 77, 38, "#FFFFFF", FontWeights.Bold);
                Text(dc, "READY FOR RACE DAY", 200, 130, 12, "#E96A75", FontWeights.SemiBold);
                dc.DrawRectangle(Brush("#30343E"), null, new Rect(40, 190, 640, 1));
                Text(dc, "Setting up Race Control", 40, 217, 23, "#FFFFFF", FontWeights.SemiBold);
                Text(dc, "Installing or updating your application. Please wait…", 40, 257, 14, "#B6BDC9", FontWeights.Normal);
                // Indeterminate activity, deliberately no fictional percentage or timed completion.
                dc.DrawRoundedRectangle(Brush("#2A2E38"), null, new Rect(40, 303, 640, 4), 2, 2);
                dc.PushClip(new RectangleGeometry(new Rect(40, 303, 640, 4)));
                var position = -140 + frame * 780d / frameCount;
                dc.DrawRoundedRectangle(Brush("#DB3545"), null, new Rect(40 + position, 303, 140, 4), 2, 2);
                dc.Pop();
                Text(dc, "Your saved race data stays in place.", 40, 333, 12, "#8E97A7", FontWeights.Normal);
                Text(dc, $"v{version?.Major}.{version?.Minor}.{version?.Build}", 610, 333, 12, "#8E97A7", FontWeights.Normal);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            if (frame == frameCount / 2)
            {
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var preview = File.Create(Path.Combine(output, "Installer-Preview.png")); png.Save(preview);
            }
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)8);
            metadata.SetQuery("/grctlext/Disposal", (byte)1);
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        using var encoded = new MemoryStream(); encoder.Save(encoded);
        var bytes = encoded.ToArray();
        // Insert the standard GIF loop extension after the global palette. This
        // avoids relying on WIC's platform-specific application-extension metadata.
        var offset = 13 + ((bytes[10] & 0x80) != 0 ? 3 * (1 << ((bytes[10] & 7) + 1)) : 0);
        using (var file = File.Create(Path.Combine(output, "Installer-Splash.gif")))
        {
            file.Write(bytes, 0, offset);
            file.Write(new byte[] { 0x21, 0xFF, 0x0B });
            file.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            file.Write(new byte[] { 3, 1, 0, 0, 0 });
            file.Write(bytes, offset, bytes.Length - offset);
        }
        using var check = File.OpenRead(Path.Combine(output, "Installer-Splash.gif"));
        var decoded = new GifBitmapDecoder(check, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoded.Frames.Count != frameCount || decoded.Frames.Any(f => f.PixelWidth != width || f.PixelHeight != height))
            throw new InvalidDataException("Installer animation failed validation.");
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static void Text(DrawingContext dc, string text, double x, double y, double size, string color, FontWeight weight)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, Brush(color), 1);
        dc.DrawText(formatted, new Point(x, y));
    }
}
