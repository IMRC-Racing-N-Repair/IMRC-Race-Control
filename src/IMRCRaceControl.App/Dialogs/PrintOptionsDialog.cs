using System.Windows;
using System.Windows.Controls;
using IMRCRaceControl.Models;
using IMRCRaceControl.Core;
namespace IMRCRaceControl.Dialogs;
public sealed class PrintOptionsDialog : Window
{
    private readonly ComboBox _print = new() { DisplayMemberPath="Label" };
    private readonly ComboBox _event = new() { DisplayMemberPath="DisplayName" };
    private readonly ComboBox _series = new() { DisplayMemberPath="DisplayName" };
    private readonly ComboBox _driver = new() { DisplayMemberPath="DisplayName" };
    private readonly ComboBox _class = new() { DisplayMemberPath="ClassName" };
    private readonly DatePicker _from = new() { SelectedDate=DateTime.Today };
    private readonly DatePicker _to = new() { SelectedDate=DateTime.Today };
    public int ReportType => (_print.SelectedItem as PrintReportChoice)?.ReportType ?? -1;
    public EventReportScope Scope => (_print.SelectedItem as PrintReportChoice)?.Scope ?? new();
    public RaceDayEventDefinition? RaceEvent => _event.SelectedItem as RaceDayEventDefinition;
    public RaceSeriesDefinition? Series => _series.SelectedItem as RaceSeriesDefinition;
    public DriverProfile? Driver => _driver.SelectedItem as DriverProfile;
    public string? ClassId => (_class.SelectedItem as SeriesClassChoice)?.ClassId is { Length:>0 } id ? id : null;
    public DateTime From => _from.SelectedDate!.Value.Date;
    public DateTime Through => _to.SelectedDate!.Value.Date;
    public PrintOptionsDialog(IReadOnlyList<RaceDayEventDefinition> events,IReadOnlyList<RaceSeriesDefinition> series,IReadOnlyList<DriverProfile> drivers,RaceDayEventDefinition? activeEvent,RaceSeriesDefinition? activeSeries,bool practice, bool seriesTab = false, IReadOnlyList<ClassDefinition>? classes = null)
    {
        Title="Print reports"; Width=570; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(18) }; Content=panel;
        var fields=new Dictionary<Control,StackPanel>();
        void Add(string label,Control control)
        {
            var field=new StackPanel();
            field.Children.Add(new TextBlock { Text=label, Margin=new Thickness(0,10,0,3),FontWeight=control==_print ? FontWeights.Bold : FontWeights.Normal });
            field.Children.Add(control); fields.Add(control,field); panel.Children.Add(field);
        }
        Add("Event",_event); _event.ItemsSource=events.OrderByDescending(e=>e.EventDate).ToList(); _event.SelectedItem=activeEvent ?? events.FirstOrDefault();
        Add("Round / report to print",_print);
        _print.ToolTip="Choose a round lineup. Completed-results choices appear once all non-skipped races in that round and class selection are complete.";
        Add("Class",_class);
        Add("Series",_series); _series.ItemsSource=series; _series.SelectedItem=activeSeries ?? series.FirstOrDefault();
        Add("Driver",_driver); _driver.ItemsSource=drivers.OrderBy(d=>d.DisplayName).ToList(); _driver.SelectedIndex=drivers.Count>0 ? 0 : -1;
        Add("Practice from",_from); Add("Practice through",_to);
        var info=new TextBlock { Margin=new Thickness(0,10,0,0),TextWrapping=TextWrapping.Wrap }; panel.Children.Add(info);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0) };
        var cancel=new Button { Content="CANCEL",IsCancel=true }; var preview=new Button { Content="PREVIEW / PRINT" };
        buttons.Children.Add(cancel);buttons.Children.Add(preview);panel.Children.Add(buttons);
        bool updating=false;
        void UpdateFields(bool resetClass)
        {
            var prior=resetClass ? null : ClassId;
            var selectedEvents=ReportType==3 ? events.Where(e=>Series?.EventIds.Contains(e.Id)==true) : events.Where(e=>e.Id==RaceEvent?.Id);
            var choices=new List<SeriesClassChoice> { new("","ALL CLASSES") };
            choices.AddRange(selectedEvents.SelectMany(e=>e.Sessions.Select(s=>new SeriesClassChoice(s.ClassId,s.ClassName))
                .Concat(e.DriverAssignments.Select(a=>new SeriesClassChoice(a.ClassId,classes?.FirstOrDefault(c=>c.Id==a.ClassId)?.Name ?? "Class"))))
                .GroupBy(c=>c.ClassId).Select(g=>g.First()));
            _class.ItemsSource=choices;_class.SelectedItem=choices.FirstOrDefault(c=>c.ClassId==prior) ?? choices[0];
            fields[_event].Visibility=ReportType is 1 or 2 or -1 ? Visibility.Visible : Visibility.Collapsed;
            fields[_class].Visibility=ReportType is 1 or 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
            fields[_series].Visibility=ReportType==3 ? Visibility.Visible : Visibility.Collapsed;
            foreach(var control in new Control[] {_driver,_from,_to}) fields[control].Visibility=ReportType==0 ? Visibility.Visible : Visibility.Collapsed;
            preview.IsEnabled=ReportType>=0;
            info.Text=ReportType<0 ? "No reports are available. Save a round, or add a driver or series first." :
                ReportType is 1 or 2 ? $"{Scope.Label} • {(_class.SelectedItem as SeriesClassChoice)?.ClassName}\nCompleted results appear when this round's races are finished." : "";
        }
        void RebuildChoices(bool resetClass=false, bool initial=false)
        {
            if (updating) return;
            updating=true;
            try
            {
                var previous=_print.SelectedItem as PrintReportChoice;
                var classId=resetClass || ReportType is not (1 or 2) ? null : ClassId;
                var choices=PrintReportChoice.Available(RaceEvent,classId,drivers.Count>0,series.Count>0);
                _print.ItemsSource=choices;
                _print.SelectedItem=choices.FirstOrDefault(c=>c==previous)
                    ?? (initial ? choices.FirstOrDefault(c=>c.ReportType==(practice ? 0 : seriesTab ? 3 : 2)) : null)
                    ?? choices.FirstOrDefault(c=>c.ReportType==2) ?? choices.FirstOrDefault();
                UpdateFields(resetClass);
            }
            finally { updating=false; }
        }
        _event.SelectionChanged+=(_,_)=>RebuildChoices(resetClass:true);
        _class.SelectionChanged+=(_,_)=>RebuildChoices();
        _series.SelectionChanged+=(_,_)=>RebuildChoices(resetClass:true);
        _print.SelectionChanged+=(_,_)=>
        {
            if(updating)return;
            updating=true;
            try { UpdateFields(resetClass:ReportType is not (1 or 2)); }
            finally { updating=false; }
            RebuildChoices();
        };
        RebuildChoices(initial:true);
        preview.Click+=(_,_)=> {
            if (ReportType<0 || ReportType==0 && (Driver is null || _from.SelectedDate is null || _to.SelectedDate is null || Through<From) || ReportType is 1 or 2 && RaceEvent is null || ReportType==3 && Series is null)
            { MessageBox.Show(this,"Choose an available report, event, series or driver and valid practice dates.");return; }
            DialogResult=true;
        };
    }
}
