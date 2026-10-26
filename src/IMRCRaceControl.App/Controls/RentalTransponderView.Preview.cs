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
            var addedId = settings.RentalTransponders.Inventory.Single(x => x.Number == "1000004").Id;
            view.Refresh(addedId); view.RemoveClicked(view, new RoutedEventArgs());
            if (settings.RentalTransponders.Inventory.Any(x => x.Id == addedId)) throw new InvalidDataException("Rental removal failed.");
            view.NumberInput.Text = "1000004"; view.AddClicked(view, new RoutedEventArgs());
            if (settings.RentalTransponders.Inventory.Single(x => x.Number == "1000004").Nickname != "1000004***") throw new InvalidDataException("Short rental default nickname failed.");
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
        var printSettings = new AppSettings();
        printSettings.RentalTransponders.NewNicknameStyle = "ColorNumber";
        printSettings.RentalTransponders.NewLabelColor = "Red";
        printSettings.RentalTransponders.PrintInColor = true;
        printSettings.RentalTransponders.PrintFontSize = 15;
        RentalTransponderRegistry.Add(printSettings, "1000001");
        var classes = new[] { new ClassDefinition { Id="stock",Name="21.5 STOCK",PrintColor="#009FE3" }, new ClassDefinition { Id="modified",Name="MODIFIED TOURING",PrintColor="#EF5350" }, new ClassDefinition { Id="novice",Name="NOVICE",PrintColor="#24BD60" } };
        var printEvent = new RaceDayEventDefinition { Name="IMRC Club Racing",EventDate=DateTime.Today };
        var ratings = new List<DriverRatingRow>();
        string[] names = ["Alex Rivera","Jordan Lee","Taylor Smith","Sam Miller","Riley Jones","Casey Wilson","Morgan Davis","Jamie Parker"];
        foreach (var cls in classes)
        {
            var count=cls.Id=="stock" ? 8 : cls.Id=="modified" ? 7 : 6;
            var prior=new EventSessionDefinition { ClassId=cls.Id,ClassName=cls.Name,Round=1,ProgressStatus=EventSessionStatus.Completed };
            var next=new EventSessionDefinition { ClassId=cls.Id,ClassName=cls.Name,Round=2 };
            for(var i=0;i<count;i++)
            {
                var id=cls.Id+i;
                prior.Entrants.Add(new SessionEntrant { DriverId=id,DriverName=names[i],CarNumber=i+1 });
                prior.Results.Add(new StandingRow { CarNumber=i+1,Laps=40-i,TotalTime=$"4:{i+1:00}.345",Position=i+1 });
                next.Entrants.Add(new SessionEntrant { DriverId=id,DriverName=names[i],CarNumber=i+1,TransponderId=i==0 ? "1000001" : (6001200+i).ToString() });
                ratings.Add(new DriverRatingRow { ClassId=cls.Id,DriverId=id,Rating=1740-i*52,Safety=4.6-i*.14 });
            }
            printEvent.Sessions.Add(prior);printEvent.Sessions.Add(next);
        }
        var report=RaceReports.Lineups(printEvent,scope:new EventReportScope(SessionType.Qualifying,2),rentals:printSettings.RentalTransponders,classes:classes,ratings:ratings);
        var document = IMRCRaceControl.Dialogs.LineupPrintDocument.Build(report, 816, 1056);
        var page = document.Pages[0].Child;
        page.Measure(new Size(816, 1056)); page.Arrange(new Rect(0, 0, 816, 1056)); page.UpdateLayout();
        var printImage = new RenderTargetBitmap(816, 1056, 96, 96, PixelFormats.Pbgra32);
        printImage.Render(page);
        var printEncoder = new PngBitmapEncoder(); printEncoder.Frames.Add(BitmapFrame.Create(printImage));
        using (var file = File.Create(Path.Combine(output, "Transponder-Print.png"))) printEncoder.Save(file);
        var longReport=RaceReports.Lineups(printEvent,scope:new EventReportScope(SessionType.Qualifying,2),rentals:printSettings.RentalTransponders,classes:classes,ratings:ratings);
        var longSection=longReport.Sections[0];
        longSection.Rows.Clear();
        for(var i=1;i<=100;i++) longSection.Rows.Add([i.ToString(), i==35 ? "A driver with a deliberately long name that must wrap inside the driver column" : $"Racer {i}","Red #1","Q1 · 42 / 4:02.345","1620 / 4.20"]);
        var longDocument=IMRCRaceControl.Dialogs.LineupPrintDocument.Build(longReport,816,1056);
        if(longDocument.Pages.Count<3)throw new InvalidDataException("Long lineup pagination did not split into pages.");
        foreach(var sheet in longDocument.Pages)
        {
            sheet.Child.Measure(new Size(816,1056));sheet.Child.Arrange(new Rect(0,0,816,1056));sheet.Child.UpdateLayout();
            foreach(var child in sheet.Child.Children.OfType<System.Windows.Controls.StackPanel>())
                if(System.Windows.Documents.FixedPage.GetTop(child)+child.ActualHeight>1013)throw new InvalidDataException("Lineup contents overlap the footer.");
        }
        var continued=longDocument.Pages[1].Child;
        var continuation=new RenderTargetBitmap(816,1056,96,96,PixelFormats.Pbgra32);continuation.Render(continued);
        var continuationPng=new PngBitmapEncoder();continuationPng.Frames.Add(BitmapFrame.Create(continuation));
        using(var file=File.Create(Path.Combine(output,"Transponder-Print-Continuation.png")))continuationPng.Save(file);
        var owner = new Window { Width = 1000, Height = 720 }; owner.Show();
        foreach (var dialog in new Window[] { new IMRCRaceControl.Dialogs.EventEditorDialog(), new IMRCRaceControl.Dialogs.ClassEditorDialog(), new RentalAppearanceDialog(printSettings) })
        {
            dialog.Owner = owner; dialog.Show();
            dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            if (dialog.WindowStyle != WindowStyle.None) throw new InvalidDataException("Dialog title bar is not themed.");
            var shot = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32); shot.Render(dialog);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot));
            using (var file = File.Create(Path.Combine(output, $"Dialog-{dialog.GetType().Name}.png"))) png.Save(file);
            dialog.Close();
        }
        owner.Close();
        EventRegistrationView.ExportPreview(output);
        File.WriteAllText(Path.Combine(output, "Transponder-UI-Checks.txt"), "PASS: themed page loads; duplicate input rejected visibly; manual addition; selected label; three actual-use events retained through week five.\n");
    }
}
