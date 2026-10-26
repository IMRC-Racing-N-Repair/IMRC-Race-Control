using System.Windows;
using IMRCRaceControl.Core;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;
namespace IMRCRaceControl;
public partial class MainWindow
{
    private async void PrintReports_Click(object sender,RoutedEventArgs e)
    {
        if (MainTabs.SelectedItem==SeriesTab && _activeSeries is not null && !TryReadSeriesEditor(_activeSeries,true)) return;
        var options=new PrintOptionsDialog(Events.ToList(),SeriesDefinitions.ToList(),Drivers.ToList(),_activeEvent,_activeSeries,MainTabs.SelectedItem==PracticeTab,MainTabs.SelectedItem==SeriesTab,Classes.ToList()) { Owner=this };
        if (options.ShowDialog()!=true) return;
        try
        {
            RaceReport report;
            if (options.ReportType==0)
            {
                var driver=options.Driver!;
                // Capture active cards before asynchronous reads; a stint can
                // be finalized by the practice timer while the report loads.
                var activeSnapshots = PracticeCards.Where(c=>c.DriverId==driver.Id && c.StartedAt.LocalDateTime.Date>=options.From && c.StartedAt.LocalDateTime.Date<=options.Through)
                    .Select(card => (
                        Stint: new PracticeHistoryRow { StintId=card.StintId,DriverId=card.DriverId,DriverName=card.DriverName,TransponderId=card.TransponderId,StartedAt=card.StartedAt,EndedAt=card.LastCrossingAt,LapCount=card.Laps.Count,
                            BestLapSeconds=card.Laps.Count>0 ? card.Laps.Min(l=>l.LapTime.TotalSeconds) : null,AverageLapSeconds=card.Laps.Count>0 ? card.Laps.Average(l=>l.LapTime.TotalSeconds) : null },
                        Laps: (IReadOnlyList<PracticeHistoryLapRow>)card.Laps.Select(l=>new PracticeHistoryLapRow { LapNumber=l.LapNumber,LapTimeSeconds=l.LapTime.TotalSeconds,Timestamp=l.Timestamp,Source=l.Source }).ToList())).ToList();
                var rows=await _practiceHistoryService.LoadRecentStintsAsync(driverId:driver.Id);
                var stints=new List<(PracticeHistoryRow Stint,IReadOnlyList<PracticeHistoryLapRow> Laps)>();
                foreach (var row in rows.Where(r=>r.StartedAt.LocalDateTime.Date>=options.From && r.StartedAt.LocalDateTime.Date<=options.Through))
                    stints.Add((row,await _practiceHistoryService.LoadLapsAsync(row.StintId)));
                stints.AddRange(activeSnapshots.Where(a=>!stints.Any(s=>s.Stint.StintId==a.Stint.StintId)));
                report=RaceReports.Practice(driver.DisplayName,options.From,options.Through,stints,_settings.RentalTransponders);
            }
            else if (options.ReportType==1) report=RaceReports.EventStandings(options.RaceEvent!,options.ClassId,options.Scope);
            else if (options.ReportType==2) report=RaceReports.Lineups(options.RaceEvent!,options.ClassId,options.Scope,_settings.RentalTransponders);
            else report=RaceReports.Series(options.Series!,Events,options.ClassId,Drivers,Classes);
            new ReportPrintDialog(report) { Owner=this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this,"Could not create the report: "+ex.Message,"Print report",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
}
