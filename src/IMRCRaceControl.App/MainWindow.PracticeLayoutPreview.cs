using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMRCRaceControl.Controls;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void CheckPracticeCardLayout(string output)
    {
        var originalColumns = _settings.PracticeCardsPerRow;
        try
        {
            foreach (var columns in new[] { 4, 5, 6 })
            {
                _settings.PracticeCardsPerRow = columns;
                PracticeCards.Clear();
                for (var i = 0; i < columns; i++) PracticeCards.Add(new PracticeCardViewModel());
                RefreshVisiblePracticeCards();
                if (PracticeGridColumns != columns || PracticeGridRows != 1)
                    throw new InvalidDataException("Practice must use full height for one row.");
                PracticeCards.Add(new PracticeCardViewModel());
                RefreshVisiblePracticeCards();
                if (PracticeGridRows != 2)
                    throw new InvalidDataException("Practice must add a row at the selected column limit.");
                PracticeCards.RemoveAt(PracticeCards.Count - 1);
                RefreshVisiblePracticeCards();
                if (PracticeGridRows != 1)
                    throw new InvalidDataException("Practice must restore full height after a driver leaves.");
            }
        }
        finally
        {
            PracticeCards.Clear();
            _settings.PracticeCardsPerRow = originalColumns;
            RefreshVisiblePracticeCards();
        }

        var card = new PracticeCardViewModel { DriverName = "Practice test" };
        for (var i = 1; i <= 30; i++)
            card.AddLap(new PracticeLapEntry { LapNumber = i, LapTime = TimeSpan.FromSeconds(5) });
        var template = (DataTemplate)Application.Current.FindResource("SharedPracticeCardTemplate");
        var content = new ContentControl { Content = card, ContentTemplate = template,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var host = new Window { Width = 390, Height = 650, Content = content, ShowInTaskbar = false };
        try
        {
            host.Show();
            void Layout()
            {
                host.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.Loaded);
                host.UpdateLayout();
            }
            Layout();
            var surface = Visuals(content).OfType<PracticeCardSurface>().Single();
            var scroll = Visuals(surface).OfType<ScrollViewer>().Single(v => v.Name == "PracticeLapScroll");
            var last = Visuals(surface).OfType<TextBlock>().Single(t=>t.Name=="PracticeLastValue");
            if(last.FontSize<36)throw new InvalidDataException("Practice LAST timing is not prominent.");
            var full = scroll.ViewportHeight;
            if (full < 260) throw new InvalidDataException($"Full practice card has insufficient lap history space: {full}.");
            host.Height = 330;
            Layout();
            var shortHeight = scroll.ViewportHeight;
            if (shortHeight >= full - 100) throw new InvalidDataException("Practice history did not shorten when another row was needed.");
            host.Height = 650;
            Layout();
            if (Math.Abs(scroll.ViewportHeight - full) > 3) throw new InvalidDataException("Practice history did not restore its full height.");
            var items = Visuals(scroll).OfType<ItemsControl>().First();
            var lapTexts=Visuals(items).OfType<TextBlock>().ToList();
            if(!lapTexts.Any(t=>t.Text=="Lap 30" && t.FontSize>=15) ||
               !lapTexts.Any(t=>t.Text=="5.000" && t.FontSize>=17))
                throw new InvalidDataException("Practice lap history is not using larger readable text.");
            if (items.Items[0] is not PracticeLapEntry newest || newest.LapNumber != 30 ||
                items.Items[29] is not PracticeLapEntry oldest || oldest.LapNumber != 1)
                throw new InvalidDataException("Practice card must display newest laps first.");
            card.AddLap(new PracticeLapEntry { LapNumber = 31, LapTime = TimeSpan.FromSeconds(4) });
            Layout();
            if (items.Items[0] is not PracticeLapEntry latest || latest.LapNumber != 31)
                throw new InvalidDataException("New practice lap did not appear at the top.");
            File.WriteAllText(Path.Combine(output,"Practice-Layout-Checks.txt"),
                "PASS: 4/5/6-column thresholds, one/two/one row, full-height history, compact history and live newest-first laps.");
        }
        finally { host.Close(); }

        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var nested in Visuals(child)) yield return nested;
            }
        }
    }
}
