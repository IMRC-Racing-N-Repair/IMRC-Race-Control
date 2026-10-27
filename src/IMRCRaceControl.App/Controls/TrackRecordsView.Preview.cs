using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl.Controls;
public sealed partial class TrackRecordsView
{
    internal static void ExportPreview(string output)
    {
        var track=new TrackProfile { Name="Riverside Raceway" };
        var classes=new List<ClassDefinition> { new() { Id="stock",Name="Slash Pro Stock",PrintColor="#EF5350" },new() { Id="truck",Name="25.5 Foam Tire Truck",PrintColor="#009FE3" } };
        var ev=new RaceDayEventDefinition { Track=track,Name="Saturday Club Racing",EventDate=new DateTime(2026,10,24) };
        string[] names=["Alex Rivera","Jordan Lee","Taylor Smith","Sam Miller","Riley Jones","Casey Wilson","Morgan Davis","Jamie Parker","Drew Thomas","Cameron Adams","Charlie Brown","Robin Scott"];
        foreach(var cls in classes)
        {
            var session=new EventSessionDefinition { ClassId=cls.Id,ClassName=cls.Name,ProgressStatus=EventSessionStatus.Completed };
            for(int i=0;i<names.Length;i++)
            {
                session.Entrants.Add(new() { CarNumber=i+1,DriverId="driver"+i,DriverName=names[i] });
                session.Laps.Add(new(i+1,i+1,names[i],1,TimeSpan.FromSeconds(5.121+i*0.123),TimeSpan.FromSeconds(5.121+i*0.123),DateTimeOffset.UtcNow,"decoder",false));
            }
            ev.Sessions.Add(session);
        }
        void Save(Visual visual,int width,int height,string name)
        {
            var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        foreach(var theme in Enum.GetValues<AppTheme>())
        {
            ThemeService.Apply(theme);
            var view=new TrackRecordsView();view.Configure(()=>(track,new List<RaceDayEventDefinition>{ev},classes));
            var window=new Window { Width=900,Height=740,Content=view };window.Show();window.UpdateLayout();
            if(view._report?.Sections.Count!=2 || view._report.Sections.Any(s=>s.Rows.Count!=10))throw new InvalidDataException("Track records did not default to all classes/top ten.");
            view._count.Text="5";
            if(view._report.Sections.Any(s=>s.Rows.Count!=5))throw new InvalidDataException("Track records top five did not update immediately.");
            window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            Save(view,(int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),"Track-Records-"+theme);
            var dialog=new ReportPrintDialog(view._report);
            var doc=dialog.BuildDocument(816,1056);
            var paginator=((IDocumentPaginatorSource)doc).DocumentPaginator;paginator.ComputePageCount();
            if(paginator.PageCount<1)throw new InvalidDataException("Track record print produced no pages.");
            if(theme==AppTheme.Light)
                for(int i=0;i<paginator.PageCount;i++) Save(paginator.GetPage(i).Visual,816,1056,"Track-Records-Print-"+(i+1));
            view._classes.SelectedIndex=1;
            if(view._report?.Sections.Count!=1 || view._report.Sections[0].Rows.Count!=5)throw new InvalidDataException("Track record class filter did not update.");
            view._count.Text="0";
            if(view._print.IsEnabled || view._report is not null)throw new InvalidDataException("Invalid count could still print stale records.");
            dialog.Close();window.Close();
        }
        File.WriteAllText(Path.Combine(output,"Track-Records-Checks.txt"),"PASS: all themes; all classes/top ten default; immediate top five and class filter; invalid count disables printing; print pagination renders.");
    }
}
