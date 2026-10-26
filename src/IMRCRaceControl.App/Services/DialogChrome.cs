using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
namespace IMRCRaceControl.Services;
internal static class DialogChrome
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Attached = new();
    internal static void Attach(Window window)
    {
        if (window is SpectatorDashboardWindow || window is MainWindow || window is StartupLoadingWindow ||
            window.Owner is null || window.Owner is SpectatorDashboardWindow || Attached.TryGetValue(window, out _) || window.Content is not UIElement content) return;
        Attached.Add(window, new object());
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6) });
        var area = SystemParameters.WorkArea;
        window.MinHeight = Math.Min(window.MinHeight, area.Height - 40);
        window.MinWidth = Math.Min(window.MinWidth, area.Width - 40);
        window.MaxHeight = area.Height - 40; window.MaxWidth = area.Width - 40;
        if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height + 38, window.MaxHeight);
        if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, window.MaxWidth);
        var root = new DockPanel();
        root.SetResourceReference(Panel.BackgroundProperty, "PageBrush");
        var caption = new DockPanel { Height = 38, LastChildFill = true };
        caption.SetResourceReference(Panel.BackgroundProperty, "PanelBrush");
        var close = new Button { Content = "✕", Width = 38, Height = 30, Padding = new Thickness(0), Margin = new Thickness(4), ToolTip = "Close" };
        close.Click += (_, _) => window.Close(); DockPanel.SetDock(close, Dock.Right); caption.Children.Add(close);
        var title = new TextBlock { Text = window.Title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12,0,0,0) };
        caption.Children.Add(title);
        title.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption);
        window.Content = null; root.Children.Add(content); window.Content = root;
        window.UpdateLayout();
        window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
        window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
    }
}
