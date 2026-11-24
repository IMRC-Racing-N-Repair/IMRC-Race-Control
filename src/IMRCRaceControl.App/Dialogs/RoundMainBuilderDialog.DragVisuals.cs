using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace IMRCRaceControl.Dialogs;

public partial class RoundMainBuilderDialog
{
    private LineupDragAdorner? _dragVisual;

    private DragDropEffects DragWithVisual(DataGrid source, DataObject data, string title, string subtitle)
    {
        var layer = AdornerLayer.GetAdornerLayer(LineupDragRoot);
        var visual = new LineupDragAdorner(LineupDragRoot, title, subtitle) { IsHitTestVisible = false };
        _dragVisual = visual;
        layer?.Add(visual);
        void Feedback(object sender, GiveFeedbackEventArgs e)
        {
            if (GetCursorPos(out var p) && PresentationSource.FromVisual(LineupDragRoot) is not null)
                visual.MoveTo(LineupDragRoot.PointFromScreen(new Point(p.X, p.Y)));
            if (e.Effects == DragDropEffects.None) visual.ClearTarget();
            visual.Allowed = e.Effects != DragDropEffects.None;
            visual.InvalidateVisual();
            e.UseDefaultCursors = true;
            e.Handled = true;
        }
        source.GiveFeedback += Feedback;
        try
        {
            visual.MoveTo(Mouse.GetPosition(LineupDragRoot));
            return DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally
        {
            source.GiveFeedback -= Feedback;
            layer?.Remove(visual);
            _dragVisual = null;
        }
    }

    private DataGridRow? DropRow(DragEventArgs e) =>
        e.OriginalSource is DependencyObject node
            ? ItemsControl.ContainerFromElement(HeatGrid, node) as DataGridRow : null;

    private bool DropAfter(DragEventArgs e) =>
        DropRow(e) is DataGridRow row && IsAfter(e.GetPosition(row).Y, row.ActualHeight);

    private static bool IsAfter(double y, double height) => y >= height / 2;

    private void ShowDropTarget(DragEventArgs e, bool allowed, bool driver)
    {
        if (_dragVisual is not { } visual) return;
        visual.Allowed = allowed;
        visual.MoveTo(e.GetPosition(LineupDragRoot));
        if (!allowed || DropRow(e) is not DataGridRow row)
        {
            visual.ClearTarget();
            return;
        }
        var bounds = row.TransformToAncestor(LineupDragRoot)
            .TransformBounds(new Rect(row.RenderSize));
        var gridBounds = HeatGrid.TransformToAncestor(LineupDragRoot)
            .TransformBounds(new Rect(HeatGrid.RenderSize));
        bounds.Intersect(gridBounds);
        if (bounds.IsEmpty) { visual.ClearTarget(); return; }
        visual.SetTarget(bounds, driver, DropAfter(e));
    }

    private void RaceDragLeave(object sender, DragEventArgs e) => _dragVisual?.ClearTarget();

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    private sealed class LineupDragAdorner(UIElement root, string title, string subtitle) : Adorner(root)
    {
        private Point _cursor;
        private Rect _target = Rect.Empty;
        private bool _driver;
        private bool _after;
        public bool Allowed { get; set; }
        public Point CursorPosition => _cursor;
        public Rect Target => _target;
        public bool After => _after;
        public bool DriverTarget => _driver;

        // Resolve the same resources used by the lineup dialog, including
        // seasonal palettes and any locally overridden theme resources.
        internal Brush ThemeBrush(string key) =>
            ((FrameworkElement)AdornedElement).FindResource(key) as Brush
                ?? throw new InvalidOperationException($"Missing drag preview theme brush: {key}");

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
        public void MoveTo(Point point) { _cursor = point; InvalidateVisual(); }
        public void SetTarget(Rect target, bool driver, bool after)
        { _target = target; _driver = driver; _after = after; InvalidateVisual(); }
        public void ClearTarget() { _target = Rect.Empty; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;
            var accent = ThemeBrush("AccentBrush");
            var edge = new Pen(accent, 3);
            if (Allowed && !_target.IsEmpty)
            {
                if (_driver)
                {
                    var tint = accent.CloneCurrentValue();
                    tint.Opacity *= 0.15;
                    dc.DrawRoundedRectangle(tint, edge,
                        new Rect(_target.X+2,_target.Y+2,Math.Max(0,_target.Width-4),Math.Max(0,_target.Height-4)), 4, 4);
                }
                else
                {
                    var y = _after ? _target.Bottom : _target.Top;
                    dc.DrawLine(edge, new Point(_target.Left+3,y), new Point(_target.Right-3,y));
                    dc.DrawEllipse(accent,null,new Point(_target.Left+4,y),4,4);
                    dc.DrawEllipse(accent,null,new Point(_target.Right-4,y),4,4);
                }
            }
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var width = Math.Min(330, size.Width);
            var height = Math.Min(54, size.Height);
            var x = Math.Clamp(_cursor.X+16, 0, Math.Max(0,size.Width-width));
            var yCard = _cursor.Y+18+height <= size.Height ? _cursor.Y+18 : _cursor.Y-height-12;
            yCard = Math.Clamp(yCard,0,Math.Max(0,size.Height-height));
            var box = new Rect(x,yCard,width,height);
            var fill = ThemeBrush("PanelAltBrush");
            var outline = new Pen(Allowed ? accent : ThemeBrush("BorderStrongBrush"),1.5);
            dc.DrawRoundedRectangle(fill,outline,box,6,6);
            void DrawText(string value, double font, double top, bool bold, Brush color)
            {
                var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                        bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),font,color,dpi)
                { MaxTextWidth = Math.Max(1,width-20), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(text,new Point(x+10,yCard+top));
            }
            DrawText(title,15,6,true,ThemeBrush("TextBrush"));
            DrawText(subtitle,12,29,false,ThemeBrush("MutedBrush"));
        }
    }

    private void CheckDragVisuals()
    {
        var layer = AdornerLayer.GetAdornerLayer(LineupDragRoot)
            ?? throw new InvalidOperationException("Lineup drag overlay has no adorner layer.");
        var visual = new LineupDragAdorner(LineupDragRoot,"Race 2 · Test class","Q1 · Heat 2/2")
            { IsHitTestVisible = false, Allowed = true };
        layer.Add(visual);
        try
        {
            foreach (var key in new[] { "AccentBrush", "PanelAltBrush", "BorderStrongBrush", "TextBrush", "MutedBrush" })
            {
                if (!ReferenceEquals(visual.ThemeBrush(key), LineupDragRoot.FindResource(key)))
                    throw new InvalidOperationException("Drag overlay does not use the current theme: " + key);
            }
            // Verify that changing a palette resource is reflected without recreating the overlay.
            var resources = LineupDragRoot.Resources;
            var hadAccent = resources.Contains("AccentBrush");
            var priorAccent = hadAccent ? resources["AccentBrush"] : null;
            try
            {
                var changed = new SolidColorBrush(Colors.CornflowerBlue);
                resources["AccentBrush"] = changed;
                if (!ReferenceEquals(visual.ThemeBrush("AccentBrush"), changed))
                    throw new InvalidOperationException("Drag overlay retained a stale theme accent.");
            }
            finally
            {
                if (hadAccent) resources["AccentBrush"] = priorAccent;
                else resources.Remove("AccentBrush");
            }
            visual.MoveTo(new Point(20,30));
            visual.SetTarget(new Rect(20,80,200,35),false,true);
            if (visual.CursorPosition != new Point(20,30) || !visual.After || visual.DriverTarget ||
                visual.Target.Bottom != 115 || IsAfter(4,35) || !IsAfter(30,35))
                throw new InvalidOperationException("Race drag preview did not preserve insertion direction.");
            visual.MoveTo(new Point(70,90));
            visual.SetTarget(new Rect(30,100,200,35),true,false);
            if (visual.CursorPosition != new Point(70,90) || !visual.DriverTarget || visual.Target.IsEmpty)
                throw new InvalidOperationException("Driver drag preview did not follow cursor and destination.");
            visual.ClearTarget();
            if (!visual.Target.IsEmpty || visual.IsHitTestVisible)
                throw new InvalidOperationException("Drag preview blocks hit testing or retains an invalid target.");
        }
        finally { layer.Remove(visual); }
    }
}
