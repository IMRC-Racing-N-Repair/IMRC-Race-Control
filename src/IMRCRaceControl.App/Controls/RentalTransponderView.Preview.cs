using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl.Controls;
public partial class RentalTransponderView
{
    internal static void ExportPreview(string output)
    {
        Directory.CreateDirectory(output);
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            ThemeService.Apply(theme);
            var settings = new AppSettings();
            var driver = new DriverProfile { FirstName = "Alex", LastName = "Rivera" };
            var cls = new ClassDefinition { Name = "21.5 Stock" };
            settings.Drivers.Add(driver); settings.Classes.Add(cls);
            var first = RentalTransponderRegistry.Add(settings, "1000001");
            var second = RentalTransponderRegistry.Add(settings, "1000002");
            RentalTransponderRegistry.Add(settings, "1000003");
            RentalTransponderRegistry.SetLabel(settings, first.Id, "Red", 1);
            RentalTransponderRegistry.SetLabel(settings, second.Id, "Black", 5);
            foreach (var week in new[] { 1, 2, 3, 5 }) settings.Events.Add(new RaceDayEventDefinition
            {
                Name = $"Club racing • Week {week}", EventDate = DateTime.Today.AddDays((week - 5) * 7),
                Status = week == 5 ? EventStatus.Active : EventStatus.Completed, ClassIds = [cls.Id],
                DriverAssignments = [new() { DriverId = driver.Id, ClassId = cls.Id, TransponderId = week == 5 ? "" : first.Number }]
            });
            RentalTransponderRegistry.CaptureAssignments(settings);
            var decoded = new ObservableCollection<DetectedTransponder>();
            foreach (var number in new[] { "6001201", "1000004", "1000001" })
            {
                var row = new DetectedTransponder { TransponderId = number };
                row.Observe(DateTimeOffset.Now, "Preview decoder", null, 10, 100); decoded.Add(row);
            }
            var view = new RentalTransponderView();
            view.Configure(settings, decoded, settings.Events, (change, _) => { change(); return Task.CompletedTask; }, settings.Events.Last().Id);
            var window = new Window { Title = "Transponder", Width = 1280, Height = 780, Content = view, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            window.Show(); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            // Exercise the same add/error handlers used by typing and decoder drops.
            view.NumberInput.Text = "001000001"; view.AddClicked(view, new RoutedEventArgs());
            if (!view.StatusText.Text.Contains("already", StringComparison.OrdinalIgnoreCase) || settings.RentalTransponders.Inventory.Count != 3)
                throw new InvalidDataException("Rental duplicate validation did not surface in the page.");
            view.NumberInput.Text = "1000004"; view.AddClicked(view, new RoutedEventArgs());
            if (settings.RentalTransponders.Inventory.Count != 4) throw new InvalidDataException("Rental manual addition failed.");
            view.Refresh(first.Id);
            if (view.HistoryGrid.Items.Count != 3 || !view.SelectedLabel.Text.Contains("Red #1")) throw new InvalidDataException("Rental used-event history did not render.");
            view.StatusText.Text = "Sample data • Week 5: this rental still shows its uses in weeks 1, 2 and 3.";
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            if (view.InventoryGrid.Columns[1].ActualWidth < 140 || view.HistoryGrid.Columns[1].ActualWidth < 90 || view.DecoderGrid.Columns[0].ActualWidth < 90)
                throw new InvalidDataException("Transponder columns are too narrow to read.");
            var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, $"Transponder-{theme}.png"))) encoder.Save(file);
            window.Close();
        }
        File.WriteAllText(Path.Combine(output, "Transponder-UI-Checks.txt"), "PASS: themed page loads; duplicate input rejected visibly; manual addition; selected label; three actual-use events retained through week five.\n");
    }
}
