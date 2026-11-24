using System.Windows;
using System.Windows.Controls;

namespace IMRCRaceControl.Controls;

// Preserve the readable 360-unit design width, but let the lap history use
// the entire allocated height. Only scale the whole card down when multiple
// rows leave too little room for its minimum design surface.
public sealed class PracticeCardSurface : Viewbox
{
    internal const double DesignWidth = 360;
    internal const double MinimumDesignHeight = 210;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is FrameworkElement surface)
        {
            var height = double.IsFinite(availableSize.Width) && availableSize.Width > 0 &&
                         double.IsFinite(availableSize.Height)
                ? Math.Max(MinimumDesignHeight, availableSize.Height * DesignWidth / availableSize.Width)
                : MinimumDesignHeight;
            if (surface.Height != height) surface.Height = height;
        }
        return base.MeasureOverride(availableSize);
    }
}
