using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Controls;
namespace IMRCRaceControl.Services;
internal static class BrandAssetExporter
{
    public static void Export(string output)
    {
        Directory.CreateDirectory(output);
        var logo = new BrandLogo();
        logo.Measure(new Size(800,307)); logo.Arrange(new Rect(0,0,800,307)); logo.UpdateLayout();
        var image = new RenderTargetBitmap(800,307,96,96,PixelFormats.Pbgra32); image.Render(logo);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using(var file=File.Create(Path.Combine(output,"IMRC-Race-Control.png"))) png.Save(file);
        var visual = new DrawingVisual();
        using(var drawing=visual.RenderOpen())
        {
            drawing.DrawImage(image,new Rect(4,80,248,95.17));
        }
        var iconImage = new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32); iconImage.Render(visual);
        var iconPng = new PngBitmapEncoder(); iconPng.Frames.Add(BitmapFrame.Create(iconImage));
        using var bytes=new MemoryStream(); iconPng.Save(bytes); var payload=bytes.ToArray();
        using var writer=new BinaryWriter(File.Create(Path.Combine(output,"IMRC-Race-Control.ico")));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(payload.Length); writer.Write(22); writer.Write(payload);
        // Keep actual Windows-rendered previews for reviewing all supported themes.
        foreach (var theme in Enum.GetValues<IMRCRaceControl.Models.AppTheme>())
        {
            ThemeService.Apply(theme);
            var preview = new System.Windows.Controls.Border { Width=840,Height=347,Padding=new Thickness(20),Child=new BrandLogo() };
            preview.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty,"PanelBrush");
            preview.Measure(new Size(840,347)); preview.Arrange(new Rect(0,0,840,347)); preview.UpdateLayout();
            var rendered = new RenderTargetBitmap(840,347,96,96,PixelFormats.Pbgra32); rendered.Render(preview);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var file=File.Create(Path.Combine(output,$"IMRC-Race-Control-{theme}.png")); encoder.Save(file);
        }
    }
}
