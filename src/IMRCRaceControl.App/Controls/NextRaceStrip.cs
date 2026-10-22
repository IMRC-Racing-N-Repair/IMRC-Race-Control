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
    private readonly TextBlock _heading = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,16,0) };
    private readonly Canvas _viewport = new() { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _belt = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _first = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _copy = new() { FontWeight = FontWeights.SemiBold };
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
        TextElement.SetForeground(this, Brushes.White);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2,GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3,GridUnitType.Star) });
        grid.Children.Add(_heading); Grid.SetColumn(_viewport,1); grid.Children.Add(_viewport); Child=grid;
        _belt.Children.Add(_first); _belt.Children.Add(_copy); _belt.RenderTransform=_offset; _viewport.Children.Add(_belt);
        _refresh.Tick += (_,_) => Refresh();
        Loaded += (_,_) => { Refresh(); _refresh.Start(); Reflow(); };
        Unloaded += (_,_) => { _refresh.Stop(); Stop(); };
        _viewport.SizeChanged += (_,_) => Reflow();
        IsVisibleChanged += (_,_) => { if (IsVisible) Reflow(); else Stop(); };
    }
    private void Refresh()
    {
        var preview=PreviewProvider?.Invoke();
        Visibility=preview is null || !ShowStrip ? Visibility.Collapsed : Visibility.Visible;
        if(preview == _previous) return;
        _previous=preview; _heading.Text=preview?.Heading ?? ""; _first.Text=_copy.Text=preview?.Names ?? "";
        ToolTip=preview is null ? null : preview.Heading+"\n"+preview.Names;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(Reflow));
    }
    private void Stop() { _offset.BeginAnimation(TranslateTransform.XProperty,null); _offset.X=0; }
    private void Reflow()
    {
        Stop(); _copy.Visibility=Visibility.Collapsed; _first.Margin=new Thickness(0);
        _first.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        var width=_first.DesiredSize.Width; _viewport.Height=Math.Max(26,_first.DesiredSize.Height+4);
        if(!IsLoaded || !IsVisible || _viewport.ActualWidth<=0 || width<=0) return;
        const double gap=64, pixelsPerSecond=45;
        _first.Margin=new Thickness(0,0,gap,0); _copy.Visibility=Visibility.Visible;
        _offset.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(0,-(width+gap),TimeSpan.FromSeconds((width+gap)/pixelsPerSecond)) { RepeatBehavior=RepeatBehavior.Forever });
    }
}
