using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
namespace IMRCRaceControl.Services;
internal static class ModalBackdrop
{
    private static readonly HashSet<Window> Attached = [];
    internal static void Attach(Window child)
    {
        if (child is SpectatorDashboardWindow || child.Owner is not Window owner || owner is SpectatorDashboardWindow ||
            (owner is not MainWindow && owner.GetType().Namespace != "IMRCRaceControl.Dialogs") ||
            owner.Content is not UIElement content || !Attached.Add(child)) return;
        var layer = AdornerLayer.GetAdornerLayer(content);
        if (layer is null) { Attached.Remove(child); return; }
        var shade = new Shade(content);
        layer.Add(shade);
        child.Closed += (_, _) => { layer.Remove(shade); Attached.Remove(child); };
    }
    private sealed class Shade(UIElement target) : Adorner(target)
    {
        protected override void OnRender(DrawingContext context)
        {
            IsHitTestVisible = false;
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new Rect(AdornedElement.RenderSize));
        }
    }
}
