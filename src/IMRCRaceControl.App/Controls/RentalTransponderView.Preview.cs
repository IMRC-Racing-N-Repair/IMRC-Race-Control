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
        // Exercise the actual shared dashboard/pop-out style, not a copied precedence rule.
        var highlightRow=new StandingRow { DriverName="BREAKOUT PRIORITY",IsCheckedIn=false,IsLastPassing=true,IsMinimumLapViolationGlow=true };
        var highlightBorder=new System.Windows.Controls.Border { DataContext=highlightRow,Style=(Style)Application.Current.FindResource("RaceDriverBorderStyle"),Child=new System.Windows.Controls.TextBlock {Text=highlightRow.DriverName,FontSize=22,Foreground=Brushes.White} };
        var highlightWindow=new Window {Width=500,Height=160,Background=Brushes.Black,Content=highlightBorder};highlightWindow.Show();
        foreach(var color in new[]{Colors.Orange,Colors.Magenta})
        {
            highlightWindow.Resources["DashboardMinimumLapGlowBrush"]=new SolidColorBrush(color);
            highlightWindow.Resources["DriverCrossingBrush"]=Brushes.Lime;
            highlightWindow.Resources["DriverCrossingThickness"]=new Thickness(0,0,0,3);
            highlightWindow.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);highlightWindow.UpdateLayout();
            if((highlightBorder.BorderBrush as SolidColorBrush)?.Color!=color || highlightBorder.BorderThickness!=new Thickness(2))throw new InvalidDataException($"Breakout did not override crossing highlight: expected {color}, actual {highlightBorder.BorderBrush}, thickness {highlightBorder.BorderThickness}.");
        }
        highlightRow.IsMinimumLapViolationGlow=false;
        highlightWindow.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if((highlightBorder.BorderBrush as SolidColorBrush)?.Color!=Colors.Lime || highlightBorder.BorderThickness!=new Thickness(0,0,0,3))throw new InvalidDataException("Crossing highlight did not resume after breakout.");
        highlightWindow.Close();
        File.WriteAllText(Path.Combine(output,"Breakout-Highlight-Checks.txt"),"PASS: breakout overrides simultaneous crossing/check-in; configured colors update; underline crossing resumes after breakout expires.");

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
        var lapSession = printEvent.Sessions.First();
        var reportStart=DateTimeOffset.Now.AddMinutes(-10);
        var qualifying=new EventSessionDefinition {ClassId=lapSession.ClassId,ClassName=lapSession.ClassName,Round=1,
            ProgressStatus=EventSessionStatus.Completed,CompletedAt=reportStart.AddMinutes(-5),RaceNumber=1,
            Entrants=lapSession.Entrants.Select(e=>new SessionEntrant {DriverId=e.DriverId,DriverName=e.DriverName,CarNumber=e.CarNumber}).ToList(),
            Results=lapSession.Results.Select(r=>new StandingRow {CarNumber=r.CarNumber,DriverName=names[r.CarNumber-1],Position=r.Position,Laps=r.Laps,TotalTime=r.TotalTime,BestLap="5.895",Top3="18.125"}).ToList()};
        printEvent.Sessions.Add(qualifying);
        lapSession.Round=2;lapSession.StartedAt=reportStart;lapSession.CompletedAt=reportStart.AddMinutes(8);
        foreach(var entrant in lapSession.Entrants)
            for (var lap = 1; lap <= 70-entrant.CarNumber; lap++)
                lapSession.Laps.Add(new LapRecord(lap*8+entrant.CarNumber,entrant.CarNumber,entrant.DisplayName,lap,TimeSpan.FromSeconds(6+entrant.CarNumber*.13),TimeSpan.FromSeconds(lap*(6+entrant.CarNumber*.13)),reportStart.AddSeconds(lap*(6+entrant.CarNumber*.13)),"decoder",lap==70-entrant.CarNumber));
        lapSession.Breakouts.Add(new BreakoutRecord(1,1,names[0],11,TimeSpan.FromSeconds(2.95),TimeSpan.FromSeconds(64.25),TimeSpan.FromSeconds(3),DateTimeOffset.Now,"decoder"));
        lapSession.Breakouts.Add(new BreakoutRecord(2,1,names[0],70,TimeSpan.FromSeconds(2.1),TimeSpan.FromSeconds(430),TimeSpan.FromSeconds(3),DateTimeOffset.Now,"decoder"));
        for(var car=2;car<=8;car++)
            lapSession.Breakouts.Add(new BreakoutRecord(car+10,car,names[car-1],11,TimeSpan.FromSeconds(2.95),TimeSpan.FromSeconds(64.25),TimeSpan.FromSeconds(3),reportStart.AddSeconds(64.25),"decoder"));
        lapSession.RaceMinutesOverride=4;
        foreach(var result in lapSession.Results)
        {
            result.DriverName=names[result.CarNumber-1];
            var laps=lapSession.Laps.Where(l=>l.CarNumber==result.CarNumber).ToList();
            result.Laps=laps.Count;result.TotalTime=laps.Last().ElapsedDisplay;result.BestLap=laps.First().LapTimeDisplay;
            result.Average5=result.Average10=result.Average15=result.BestLap;result.GapBehind=result.Position==1?"—":"1.250";
        }
        foreach(var count in new[]{8,3,1})
        {
            var matrix=RaceReports.CompletedRace(printEvent,lapSession);
            if(matrix.ResultsSummary?.Rows.Count!=8 || matrix.PriorQualifying?.Rows.Count!=8)
                throw new InvalidDataException("Race report must include finishing results and prior qualifying.");
            var section=matrix.Sections[0];
            if(count==8 && (section.Columns.Length!=9 || !section.Rows.Single(r=>r[0]=="11").Skip(1).All(c=>c.StartsWith("B-Out: 2.950\n"))))
                throw new InvalidDataException("Eight-driver breakout report labels are missing.");
            matrix.Sections[0]=section with { Columns=section.Columns.Take(count+1).ToArray(),Rows=section.Rows.Select(row=>row.Take(count+1).ToArray()).ToList() };
            var pages=IMRCRaceControl.Dialogs.LapMatrixPrintDocument.Build(matrix,816,1056);
            for(var index=0;index<pages.Pages.Count;index++)
            {
                var sheet=pages.Pages[index].Child;sheet.Measure(new Size(816,1056));sheet.Arrange(new Rect(0,0,816,1056));sheet.UpdateLayout();
                foreach(var content in sheet.Children.OfType<System.Windows.Controls.StackPanel>())
                    if(System.Windows.Documents.FixedPage.GetTop(content)+content.ActualHeight>1005)throw new InvalidDataException("Race report overlaps footer.");
                var bitmap=new RenderTargetBitmap(816,1056,96,96,PixelFormats.Pbgra32);bitmap.Render(sheet);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"Lap-Matrix-{count}-{index+1}.png"));encoder.Save(file);
            }
        }
        foreach(var total in new[]{10,18})
        {
            var splitEvent=System.Text.Json.JsonSerializer.Deserialize<RaceDayEventDefinition>(System.Text.Json.JsonSerializer.Serialize(printEvent))!;
            var splitRace=splitEvent.Sessions.Single(s=>s.Id==lapSession.Id);
            var splitQual=splitEvent.Sessions.Single(s=>s.Id==qualifying.Id);
            for(var car=9;car<=total;car++)
            {
                var entrant=new SessionEntrant {CarNumber=car,DriverId="split"+car,DriverName="Racer "+car};
                splitRace.Entrants.Add(entrant);splitQual.Entrants.Add(entrant);
                splitRace.Results.Add(new StandingRow {CarNumber=car,DriverName=entrant.DriverName,Position=car,BestLap="6.500"});
                splitQual.Results.Add(new StandingRow {CarNumber=car,DriverName=entrant.DriverName,Position=car,Laps=30-car,TotalTime="4:03.000"});
            }
            splitRace.Laps.Clear();splitRace.Breakouts.Clear();
            foreach(var entrant in splitRace.Entrants)
            {
                var seconds=6+entrant.CarNumber*.13;
                for(var lap=1;lap<=12;lap++)splitRace.Laps.Add(new LapRecord(lap*total+entrant.CarNumber,entrant.CarNumber,entrant.DriverName,lap,TimeSpan.FromSeconds(seconds),TimeSpan.FromSeconds(lap*seconds),reportStart.AddSeconds(lap*seconds),"test",lap==12));
                splitRace.Breakouts.Add(new BreakoutRecord(entrant.CarNumber,entrant.CarNumber,entrant.DriverName,11,TimeSpan.FromSeconds(2.95),TimeSpan.FromSeconds(64),TimeSpan.FromSeconds(3),reportStart.AddSeconds(64),"test"));
                var result=splitRace.Results.Single(r=>r.CarNumber==entrant.CarNumber);result.Laps=12;result.TotalTime=RaceEngine.FormatElapsed(TimeSpan.FromSeconds(12*seconds));
            }
            var balanced=RaceReports.CompletedRace(splitEvent,splitRace);
            var expected=total==10?5:6;
            if(balanced.Sections.Count!=(total==10?2:3) || balanced.Sections.Any(s=>s.Columns.Length!=expected+1 || s.ResultsSummary?.Rows.Count!=expected || s.PriorQualifying?.Rows.Count!=expected))
                throw new InvalidDataException("Balanced race report grouping failed.");
            var pages=IMRCRaceControl.Dialogs.LapMatrixPrintDocument.Build(balanced,816,1056);
            for(var index=0;index<pages.Pages.Count;index++)
            {
                var sheet=pages.Pages[index].Child;sheet.Measure(new Size(816,1056));sheet.Arrange(new Rect(0,0,816,1056));sheet.UpdateLayout();
                foreach(var content in sheet.Children.OfType<System.Windows.Controls.StackPanel>())
                    if(System.Windows.Documents.FixedPage.GetTop(content)+content.ActualHeight>1005)throw new InvalidDataException("Balanced report overlaps footer.");
                var bitmap=new RenderTargetBitmap(816,1056,96,96,PixelFormats.Pbgra32);bitmap.Render(sheet);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"Balanced-{total}-{index+1}.png"));encoder.Save(file);
            }
        }
        File.WriteAllText(Path.Combine(output,"Balanced-Report-Checks.txt"),"PASS: 10 drivers = 5/5; 18 drivers = 6/6/6; each report contains its own results, laps, qualifying and page boundaries.");
        File.WriteAllText(Path.Combine(output,"Breakout-Report-8-Checks.txt"),"PASS: eight driver columns with B-Out: 2.950 labels, valid laps and pace, paginated without footer overlap.");
        File.WriteAllText(Path.Combine(output,"Race-Report-Checks.txt"),"PASS: results, prior qualifying, 8/3/1 driver lap grids and pagination without footer overlap.");
        var owner = new Window { Width = 1000, Height = 720 }; owner.Show();
        foreach (var dialog in new Window[] { new IMRCRaceControl.Dialogs.EventEditorDialog(), new IMRCRaceControl.Dialogs.ClassEditorDialog(), new RentalAppearanceDialog(printSettings), new IMRCRaceControl.Dialogs.CompletedRaceExportDialog([printEvent],printEvent,lapSession), new IMRCRaceControl.Dialogs.ReportPrintDialog(RaceReports.CompletedRace(printEvent,lapSession)) })
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
