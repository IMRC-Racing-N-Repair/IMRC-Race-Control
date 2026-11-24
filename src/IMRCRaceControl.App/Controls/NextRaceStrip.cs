using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IMRCRaceControl.Core;
namespace IMRCRaceControl.Controls;

// Canvas contributes no desired width: long lineups cannot widen their parent.
public sealed class NextRaceStrip : Border
{
    private const double HeadingGap = 12;
    private const double RepeatGap = 32;
    private const double PixelsPerSecond = 45;
    private readonly TextBlock _heading = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,HeadingGap,0) };
    private readonly Canvas _viewport = new() { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _belt = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _first = new() { FontWeight = FontWeights.SemiBold };
    private readonly TranslateTransform _offset = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private NextRacePreview? _previous;
    public bool ShowStrip { get; set; } = true;
    public Func<NextRacePreview?>? PreviewProvider { get; set; }
    public NextRaceStrip()
    {
        Padding = new Thickness(12,8,12,8); Margin = new Thickness(0,0,0,7); CornerRadius = new CornerRadius(7); BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty,"AccentDarkBrush"); SetResourceReference(BorderBrushProperty,"BorderBrush");
        TextElement.SetForeground(this, Brushes.White);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        grid.Children.Add(_heading); Grid.SetColumn(_viewport,1); grid.Children.Add(_viewport); Child=grid;
        _belt.Children.Add(_first); _belt.RenderTransform=_offset; _viewport.Children.Add(_belt);
        _refresh.Tick += (_,_) => Refresh();
        Loaded += (_,_) => { Refresh(); _refresh.Start(); Reflow(); };
        Unloaded += (_,_) => { _refresh.Stop(); Stop(); };
        _viewport.SizeChanged += (_,e) => { if (e.WidthChanged) Reflow(); };
        IsVisibleChanged += (_,_) => { if (IsVisible) Reflow(); else Stop(); };
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // The heading uses its natural text width, but very long class names
        // wrap before consuming the space reserved for the scrolling lineup.
        var available = constraint.Width - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right;
        var cap = double.IsFinite(available) ? Math.Max(0, available * 0.65 - HeadingGap) : double.PositiveInfinity;
        if (_heading.MaxWidth != cap) _heading.MaxWidth = cap;
        return base.MeasureOverride(constraint);
    }

    private void Refresh()
    {
        var preview=PreviewProvider?.Invoke();
        Visibility=preview is null || !ShowStrip ? Visibility.Collapsed : Visibility.Visible;
        if(preview == _previous) return;
        _previous=preview; _heading.Text=preview?.Heading ?? ""; _first.Text=preview?.Names ?? "";
        ToolTip=preview is null ? null : preview.Heading+"\n"+preview.Names;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(Reflow));
    }
    private void Stop() { _offset.BeginAnimation(TranslateTransform.XProperty,null); _offset.X=0; }
    private void Reflow()
    {
        Stop();
        while (_belt.Children.Count > 1) _belt.Children.RemoveAt(_belt.Children.Count - 1);
        _first.Margin=new Thickness(0);
        _first.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var width=_first.DesiredSize.Width; _viewport.Height=Math.Max(26,_first.DesiredSize.Height+4);
        if(_viewport.ActualWidth<=0 || width<=0) return;
        var stride=width+RepeatGap;
        _first.Margin=new Thickness(0,0,RepeatGap,0);
        // A wide screen can be wider than two copies of a short lineup.
        // Fill the whole viewport plus one full animation cycle so neither
        // end goes blank as the text moves from right to left.
        var copies=Math.Max(2, (int)Math.Ceiling(_viewport.ActualWidth/stride)+2);
        for (var i=1;i<copies;i++)
            _belt.Children.Add(new TextBlock { Text=_first.Text, FontWeight=_first.FontWeight, Margin=_first.Margin });
        if (!IsLoaded || !IsVisible) return;
        _offset.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,-stride,TimeSpan.FromSeconds(stride/PixelsPerSecond)) { RepeatBehavior=RepeatBehavior.Forever });
    }

    internal static void CheckResponsiveLayout(string output)
    {
        var preview=new NextRacePreview("NEXT UP · Race 3 · Edm Stock · Q1, Heat 2/2","1  Benton     •     2  Billy     •     3  Hayden Johnson");
        var strip=new NextRaceStrip { PreviewProvider=()=>preview };
        TextElement.SetFontSize(strip,16);
        strip.Refresh();
        void Layout(double width)
        {
            strip.Measure(new Size(width,160));
            strip.Arrange(new Rect(0,0,width,160));
            strip.Reflow();
            strip.UpdateLayout();
        }
        foreach(var width in new[]{760d,1280d,1800d})
        {
            Layout(width);
            var headingRight=strip._heading.TranslatePoint(new Point(strip._heading.ActualWidth,0),strip).X;
            var viewportLeft=strip._viewport.TranslatePoint(new Point(0,0),strip).X;
            var viewportRight=viewportLeft+strip._viewport.ActualWidth;
            if (Math.Abs(viewportLeft-headingRight-HeadingGap)>2 ||
                Math.Abs(viewportRight-(strip.ActualWidth-strip.Padding.Right-strip.BorderThickness.Right))>2)
                throw new InvalidDataException("Next race ticker does not fill the available width.");
            strip._belt.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            var stride=strip._first.DesiredSize.Width;
            if (strip._belt.DesiredSize.Width-stride<strip._viewport.ActualWidth)
                throw new InvalidDataException("Next race ticker leaves a blank region during its repeat cycle.");
        }
        preview=preview with { Heading=string.Join(" ",Enumerable.Repeat("Long class name",20)) };
        strip.Refresh(); Layout(760);
        if(strip._viewport.ActualWidth<200)
            throw new InvalidDataException("Long next race heading crowds out the scrolling lineup.");
        File.WriteAllText(Path.Combine(output,"Next-Race-Layout-Checks.txt"),
            "PASS: natural heading width, 12px heading gap, full right-edge viewport, repeat coverage at three widths, long heading wraps.");
    }
}
