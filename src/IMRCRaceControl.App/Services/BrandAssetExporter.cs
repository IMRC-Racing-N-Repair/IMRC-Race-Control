using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Controls;
namespace IMRCRaceControl.Services;
internal static class BrandAssetExporter
{
    public static void Export(string output)
    {
        Directory.CreateDirectory(output);
        var source = new BitmapImage(new Uri("pack://application:,,,/IMRCRaceControl;component/Assets/RC-Approved.png"));
        var sizes = new[] { 16,24,32,48,64,128,256 };
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.HighQuality);
            var padding = Math.Max(1,size/64d);
            using (var drawing = visual.RenderOpen())
                drawing.DrawImage(source,new Rect(padding,padding,size-2*padding,size-2*padding));
            var rendered = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
            rendered.Render(visual);
            var png = Encode(rendered); frames.Add(png);
            File.WriteAllBytes(Path.Combine(output,$"RC-Icon-{size}.png"),png);
        }
        // Use the square RC artwork directly, rather than shrinking a wide wordmark
        // into a tiny strip in the desktop shortcut or Windows taskbar.
        File.WriteAllBytes(Path.Combine(output,"IMRC-Race-Control.png"),frames[^1]);
        using (var writer = new BinaryWriter(File.Create(Path.Combine(output,"IMRC-Race-Control.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            var offset = 6+16*sizes.Length;
            for (var i=0;i<sizes.Length;i++)
            {
                var dimension = sizes[i]==256 ? (byte)0 : (byte)sizes[i];
                writer.Write(dimension); writer.Write(dimension); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
        foreach (var theme in Enum.GetValues<IMRCRaceControl.Models.AppTheme>())
        {
            ThemeService.Apply(theme);
            var preview = new Border { Width=840,Height=347,Padding=new Thickness(20),Child=new BrandLogo() };
            preview.SetResourceReference(Border.BackgroundProperty,"PanelBrush");
            preview.Measure(new Size(840,347)); preview.Arrange(new Rect(0,0,840,347)); preview.UpdateLayout();
            var rendered = new RenderTargetBitmap(840,347,96,96,PixelFormats.Pbgra32); rendered.Render(preview);
            File.WriteAllBytes(Path.Combine(output,$"IMRC-Race-Control-{theme}.png"),Encode(rendered));
        }
    }

    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
