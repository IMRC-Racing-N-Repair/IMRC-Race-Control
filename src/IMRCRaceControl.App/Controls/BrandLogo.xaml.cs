using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace IMRCRaceControl.Controls;
public partial class BrandLogo : UserControl
{
    public BrandLogo()
    {
        InitializeComponent();
        // Close-spaced alpha masks form a smooth outline around the artwork,
        // without a rectangular backing or altering the original logo image.
        // Direct references also support installer rendering before Loaded.
        for (var step=0; step<32; step++)
        {
            var angle=step*Math.Tau/32;
            var outline=new Border
            {
                Width=2059,Height=790,Margin=new Thickness(20),
                OpacityMask=new VisualBrush(Artwork) { Stretch=Stretch.Fill },
                RenderTransform=new TranslateTransform(16*Math.Cos(angle),16*Math.Sin(angle))
            };
            outline.SetResourceReference(Border.BackgroundProperty,"BrandOutlineBrush");
            LogoLayers.Children.Insert(0,outline);
        }
    }
}
