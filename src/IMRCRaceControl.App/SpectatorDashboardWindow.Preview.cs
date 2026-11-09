using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class SpectatorDashboardWindow
{
    internal static void ExportClassSectionsPreview(string output)
    {
        var settings = SettingsService.CreateDefaults();
        settings.WebShowOverallPosition = true;
        settings.SpectatorVisibleColumns = ["LAPS", "LAST LAP", "BEST LAP", "CLOCK", "PACE"];
        var race = new MainWindow(settings,new SettingsService(),new RaceControlDatabaseService());
        var drivers = new[]
        {
            (Name:"Riley Jones",Class:"Grom Off-Road",Color:"#FFD600",Position:1),
            (Name:"Casey Lane",Class:"GT12 - On-Road",Color:"#00C6C6",Position:1),
            (Name:"Avery Brooks",Class:"Grom Off-Road",Color:"#FFD600",Position:2)
        };
        for(var i=0;i<drivers.Length;i++)
        {
            var driver=drivers[i];
            race.Standings.Add(new StandingRow
            {
                CarNumber=i+1,Position=driver.Position,DriverName=driver.Name,ClassName=driver.Class,
                ClassColor=driver.Color,ScoringGroupId=driver.Class,IsCheckedIn=i!=2,Clock="4:52.0"
            });
            race.RoundProjectionRows.Add(new RoundProjectionRow
            {
                Key=driver.Name,DriverName=driver.Name,ClassName=driver.Class,ClassColor=driver.Color,
                ProjectedPosition=driver.Position,PaceFinish="36/5:03.140"
            });
        }
        var screen=new SpectatorDashboardWindow(race) {Width=1300,Height=650,ShowInTaskbar=false};
        try
        {
            screen.Show();
            screen.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            screen.UpdateLayout();
            static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
                {
                    var child=VisualTreeHelper.GetChild(parent,i);
                    yield return child;
                    foreach(var nested in Descendants(child))yield return nested;
                }
            }
            foreach(var grid in new[]{screen.SpectatorStandingsGrid,screen.SpectatorProjectionGrid})
            {
                var names=Descendants(grid).OfType<TextBlock>().Select(t=>t.Text).ToList();
                foreach(var group in drivers.Select(d=>d.Class).Distinct())
                    if(names.Count(n=>n==group)!=1)throw new InvalidDataException("Class must appear once as a section heading.");
                if(drivers.Any(d=>!names.Contains(d.Name) || names.Contains(d.Name+" • "+d.Class)))
                    throw new InvalidDataException("Driver cells should contain only driver names.");
            }
            var shot=new RenderTargetBitmap((int)Math.Ceiling(screen.ActualWidth),(int)Math.Ceiling(screen.ActualHeight),96,96,PixelFormats.Pbgra32);
            shot.Render(screen);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));
            using var file=File.Create(Path.Combine(output,"Combined-Class-Displays.png"));
            png.Save(file);
        }
        finally {screen.Close();}
    }
}
