using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    internal static void CheckDashboardStartupPreview(string output)
    {
        void Stage(string name) => File.AppendAllText(Path.Combine(output, "RentalPreviewProgress.txt"),
            $"{DateTimeOffset.UtcNow:O} | DASHBOARD-{name}{Environment.NewLine}");
        if (Application.Current.TryFindResource("DashboardCenteredCellTextStyle") is not Style sharedStyle ||
            sharedStyle.TargetType != typeof(TextBlock))
            throw new InvalidDataException("Dashboard text style must be accessible to both sibling tables.");
        var main = new MainWindow(SettingsService.CreateDefaults(),
            new SettingsService(Path.Combine(output, "preview-settings.json")),
            new RaceControlDatabaseService(Path.Combine(output, "preview.db")),
            previewOnly: true, previewTrace: Stage);
        try
        {
            var grid = main.FindName("RoundProjectionGrid") as DataGrid;
            // Inspect the actual projection columns rather than a duplicate test template.
            // Find the grid by its identifying headers if the XAML name changes.
            grid ??= FindProjectionGrid(main);
            var currentStyle = Application.Current.TryFindResource("ProjectionCurrentTextStyle") as Style;
            if (currentStyle is null || !ReferenceEquals(currentStyle.BasedOn, sharedStyle))
                throw new InvalidDataException("Projection text must use the shared centered style.");
            foreach (var name in new[] { "POS", "PACE / FINISH", "PACE GAP" })
            {
                if (grid.Columns.SingleOrDefault(c => Equals(c.Header, name)) is not DataGridTextColumn column)
                    throw new InvalidDataException($"Projection column {name} is missing.");
                var expected = name == "POS"
                    ? Application.Current.TryFindResource("ProjectionPositionTextStyle") as Style
                    : currentStyle;
                if (expected is null || !ReferenceEquals(column.ElementStyle, expected))
                    throw new InvalidDataException($"Projection column {name} does not use its shared centered style.");
                if (name == "POS" && (!ReferenceEquals(expected.BasedOn, currentStyle) || column.Width.Value < 48 ||
                    !expected.Setters.OfType<Setter>().Any(setter => setter.Property == TextBlock.FontSizeProperty && setter.Value is double size && size >= 16) ||
                    !expected.Setters.OfType<Setter>().Any(setter => setter.Property == FrameworkElement.MarginProperty && setter.Value is Thickness margin && margin.Left >= 3 && margin.Right >= 3)))
                    throw new InvalidDataException("Projection position must be larger, centered and padded.");
            }
            // A detached DataGridCell has no presentation resource scope and
            // does not reliably evaluate its style triggers. Test the actual
            // generated cells and text blocks in a hosted projection grid.
            var racer = new RoundProjectionRow { Key = "live", DriverName = "Hayden Johnson",
                ProjectedPosition = 1, PaceFinish = "8/0:54.021", PaceGapDisplay = "—", IsCurrentHeat = true };
            var historical = new RoundProjectionRow { Key = "past", DriverName = "Aaron Patterson",
                ProjectedPosition = 2, PaceFinish = "8/0:54.073", PaceGapDisplay = "+0.052" };
            ((Panel)grid.Parent).Children.Remove(grid);
            grid.DataContext = main;
            grid.ItemsSource = new[] { racer, historical };
            grid.EnableRowVirtualization = false;
            grid.EnableColumnVirtualization = false;
            var host = new Window { Width = 405, Height = 260, Content = grid, ShowInTaskbar = false };
            try
            {
                ThemeService.Apply(AppTheme.Thanksgiving);
                host.Show();
                void Layout()
                {
                    host.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.Loaded);
                    host.UpdateLayout();
                }
                Layout();
                CheckRow(racer, "WarnBrush");
                CheckRow(historical, "DashboardTextBrush");
                racer.IsCurrentHeat = false;
                historical.IsCurrentHeat = true;
                Layout();
                CheckRow(racer, "DashboardTextBrush");
                CheckRow(historical, "WarnBrush");

                // Exercise the original movement-number requirement in a narrow panel.
                historical.MovementState = "UP";
                historical.MovementDisplay = "UP 2";
                Layout();
                CheckMovement(historical, "+2", "DashboardPositionUpBrush");
                historical.MovementState = "DOWN";
                historical.MovementDisplay = "DOWN 2";
                Layout();
                CheckMovement(historical, "-2", "DashboardPositionDownBrush");
                var shot = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                shot.Render(host);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(shot));
                using (var file = File.Create(Path.Combine(output, "Live-Round-Position.png"))) png.Save(file);

                void CheckMovement(RoundProjectionRow item, string expectedBadge, string brush)
                {
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(item);
                    var badge = Descendants(row).OfType<TextBlock>().Single(t => t.Name == "MovementBadgeText");
                    var name = Descendants(row).OfType<TextBlock>().Single(t => t.Name == "MovementDriverText");
                    var border = Descendants(row).OfType<Border>().Single(t => t.Name == "MovementNameHighlight");
                    if (badge.Visibility != Visibility.Visible || badge.Text != expectedBadge ||
                        badge.FontSize < 12 || badge.Margin.Top < 3 || badge.Margin.Right < 4 ||
                        border.Background is not SolidColorBrush actual ||
                        actual.Color != ((SolidColorBrush)host.FindResource(brush)).Color)
                        throw new InvalidDataException("Movement badge must be readable, inset and preserve its highlight.");
                    var nameEnd = name.TranslatePoint(new Point(name.ActualWidth, 0), border).X;
                    var badgeStart = badge.TranslatePoint(new Point(0, 0), border).X;
                    if (nameEnd > badgeStart + 1)
                        throw new InvalidDataException("Movement badge overlaps the driver name.");
                }

                void CheckRow(RoundProjectionRow item, string resource)
                {
                    if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
                        throw new InvalidDataException("Projection row was not realized: " + item.DriverName);
                    var expected = (SolidColorBrush)host.FindResource(resource);
                    var cells = Descendants(row).OfType<DataGridCell>().ToList();
                    if (cells.Count != 4)
                        throw new InvalidDataException("Projection row must contain four realized cells.");
                    foreach (var cell in cells)
                    {
                        var text = Descendants(cell).OfType<TextBlock>()
                            .FirstOrDefault(t => t.Visibility == Visibility.Visible && !string.IsNullOrEmpty(t.Text));
                        if (text is null || text.Foreground is not SolidColorBrush actual ||
                            actual.Color != expected.Color || text.VerticalAlignment != VerticalAlignment.Center)
                            throw new InvalidDataException($"Projection {item.DriverName} / {cell.Column.Header}: expected {expected.Color} and centered text, got {text?.Foreground} / {text?.VerticalAlignment}.");
                        var center = text.TranslatePoint(new Point(0, text.ActualHeight / 2), cell).Y;
                        if (Math.Abs(center - cell.ActualHeight / 2) > 3)
                            throw new InvalidDataException($"Projection {cell.Column.Header} text is not visually centered: {center} vs {cell.ActualHeight / 2}.");
                        if (Equals(cell.Column.Header, "DRIVER") &&
                            (text.Text != item.DriverName || text.TextTrimming != TextTrimming.None ||
                             !Descendants(cell).OfType<Viewbox>().Any(v => v.StretchDirection == StretchDirection.DownOnly)))
                            throw new InvalidDataException("Full driver names must fit without ellipsis.");
                    }
                }
            }
            finally { host.Close(); }
            File.WriteAllText(Path.Combine(output, "Dashboard-Startup-Checks.txt"),
                "PASS: real dashboard XAML initializes; centered and padded projection columns resolve; current-heat text follows the theme and resets for historical rows.");
        }
        finally { main.Close(); }
        Controls.NextRaceStrip.CheckResponsiveLayout(output);

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        static DataGrid FindProjectionGrid(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item is DataGrid data && data.Columns.Any(c => Equals(c.Header, "PACE GAP"))) return data;
                foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
                    queue.Enqueue(child);
            }
            throw new InvalidDataException("Qualifying projection grid was not found.");
        }
    }

    internal static void CheckCompactRunRow(DataGrid grid, Func<object,bool> isRun, Func<object,bool> isSingle)
    {
        static IEnumerable<DataGridRow> Rows(DependencyObject parent)
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);
                if(child is DataGridRow row)yield return row;
                else foreach(var nested in Rows(child))yield return nested;
            }
        }

        var rows=Rows(grid).ToList();
        var run=rows.Single(r=>isRun(r.Item));
        var single=rows.First(r=>isSingle(r.Item));
        if(Math.Abs(run.ActualHeight-single.ActualHeight)>1)
            throw new InvalidDataException("Combined and regular race rows must have the same height.");
    }

    internal static void ExportEventRunsPreview(string output, RaceDayEventDefinition raceEvent)
    {
        void Stage(string name) => File.AppendAllText(Path.Combine(output, "RentalPreviewProgress.txt"),
            $"{DateTimeOffset.UtcNow:O} | EVENT-RUNS-{name}{Environment.NewLine}");
        Stage("CREATE-DASHBOARD");
        var main = new MainWindow(SettingsService.CreateDefaults(),
            new SettingsService(Path.Combine(output, "preview-settings.json")),
            new RaceControlDatabaseService(Path.Combine(output, "preview.db")),
            previewOnly: true, previewTrace: Stage) { _activeEvent = raceEvent };
        Stage("REFRESH-SCHEDULE");
        main.RefreshPlannedSessions();
        Stage("SCHEDULE-REFRESHED");
        var grid=main.PlannedSessionGrid;
        ((Panel)grid.Parent).Children.Remove(grid);
        grid.DataContext=main;
        var preview=new Window {Title="Event Rounds / Mains",Width=1024,Height=500,Content=grid,ShowInTaskbar=false};
        try
        {
            Stage("SHOW-GRID");
            preview.Show();
            preview.UpdateLayout();
            main.FitEventSessionColumns();
            preview.UpdateLayout();
            Stage("GRID-SHOWN");
            var classWidth = main.EventSessionClassColumn.Width;
            var nameWidth = main.EventSessionNameColumn.Width;
            main.FitEventSessionColumns();
            if (main.EventSessionClassColumn.Width != classWidth || main.EventSessionNameColumn.Width != nameWidth)
                throw new InvalidDataException("Repeated event column fitting did not converge.");
            foreach(var property in new[]{nameof(EventRunRow.RaceNumber),nameof(EventRunRow.RacerCount),nameof(EventRunRow.ClassDisplay)})
            {
                Stage("SORT-" + property);
                main._eventSessionSortProperty=property;
                main._eventSessionSortDirection=ListSortDirection.Descending;
                main.ApplyEventSessionSort();
                var rows=grid.Items.Cast<EventRunRow>().ToList();
                var index=rows.FindIndex(r=>r.IsCombinedHeader);
                if(index<0 || rows[index].RoundRaceCount!=1 ||
                    !rows.Skip(index+1).Take(2).All(r=>r.IsCombinedChild) ||
                    rows.Count(r=>r.IsCombinedChild)!=2 || rows[index].RacerCount!=2)
                    throw new InvalidDataException("Combined run must count once and remain next to its class rows when sorted.");
                grid.SelectedItem=rows[index+2];
                if(!ReferenceEquals(main.SelectedPlannedSession,rows[index+2].Session))
                    throw new InvalidDataException("Selecting an indented class must select its saved heat.");
            }
            main._eventSessionSortProperty=nameof(EventRunRow.RaceNumber);
            main._eventSessionSortDirection=ListSortDirection.Ascending;
            main.ApplyEventSessionSort();
            grid.SelectedItem=null;
            preview.UpdateLayout();
            CheckCompactRunRow(grid,item=>item is EventRunRow {IsCombinedHeader:true},
                item=>item is EventRunRow {IsCombinedHeader:false,IsCombinedChild:false});
            Stage("RENDER");
            var shot=new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth),(int)Math.Ceiling(preview.ActualHeight),96,96,PixelFormats.Pbgra32);
            shot.Render(preview);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));
            using var file=File.Create(Path.Combine(output,"Combined-Event-Rounds.png"));
            png.Save(file);
        }
        finally { preview.Close(); main.Close(); }
        Stage("DONE");
    }
}
