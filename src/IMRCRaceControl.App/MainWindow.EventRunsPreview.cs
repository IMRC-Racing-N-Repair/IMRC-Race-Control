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
        var main=new MainWindow(SettingsService.CreateDefaults(),new SettingsService(),new RaceControlDatabaseService())
            {_activeEvent=raceEvent};
        main.RefreshPlannedSessions();
        var grid=main.PlannedSessionGrid;
        ((Panel)grid.Parent).Children.Remove(grid);
        grid.DataContext=main;
        var preview=new Window {Title="Event Rounds / Mains",Width=1024,Height=500,Content=grid,ShowInTaskbar=false};
        try
        {
            preview.Show();
            preview.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach(var property in new[]{nameof(EventRunRow.RaceNumber),nameof(EventRunRow.RacerCount),nameof(EventRunRow.ClassDisplay)})
            {
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
            var shot=new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth),(int)Math.Ceiling(preview.ActualHeight),96,96,PixelFormats.Pbgra32);
            shot.Render(preview);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));
            using var file=File.Create(Path.Combine(output,"Combined-Event-Rounds.png"));
            png.Save(file);
        }
        finally {preview.Close();}
    }
}
