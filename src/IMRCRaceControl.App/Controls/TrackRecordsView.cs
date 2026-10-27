using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using IMRCRaceControl.Core;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Controls;

public sealed partial class TrackRecordsView : UserControl
{
    private sealed record Choice(string? Id, string Name);
    private readonly ComboBox _classes = new() { DisplayMemberPath="Name", MinWidth=200, MaxWidth=320 };
    private readonly TextBox _count = new() { Text="10", Width=64, VerticalContentAlignment=VerticalAlignment.Center };
    private readonly StackPanel _records = new();
    private readonly TextBlock _status = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,12) };
    private readonly Button _print = new() { Content="PRINT / PDF" };
    private Func<(TrackProfile Track, List<RaceDayEventDefinition> Events, List<ClassDefinition> Classes)>? _source;
    private RaceReport? _report;
    private bool _refreshing;

    public TrackRecordsView()
    {
        var root = new DockPanel { Margin=new Thickness(18) }; Content=root;
        var top = new StackPanel(); DockPanel.SetDock(top,Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text="TRACK RECORDS", FontSize=24, FontWeight=FontWeights.Bold });
        top.Children.Add(new TextBlock { Text="Fastest valid lap per driver, ranked within each class. Completed qualifying and mains only; breakout laps are excluded.", TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,6,0,12) });
        var filters = new WrapPanel(); top.Children.Add(filters);
        void Field(string title,FrameworkElement control)
        {
            var field=new StackPanel { Margin=new Thickness(0,0,12,8) };
            field.Children.Add(new TextBlock { Text=title, Margin=new Thickness(0,0,0,4) }); field.Children.Add(control); filters.Children.Add(field);
        }
        Field("Class",_classes); Field("Top per class",_count);
        var refresh=new Button { Content="REFRESH", VerticalAlignment=VerticalAlignment.Bottom, Margin=new Thickness(0,0,8,8) };
        filters.Children.Add(refresh); _print.VerticalAlignment=VerticalAlignment.Bottom; _print.Margin=new Thickness(0,0,0,8); filters.Children.Add(_print);
        top.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content=_records, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        _classes.SelectionChanged+=(_,_)=>Render(); _count.TextChanged+=(_,_)=>Render(); refresh.Click+=(_,_)=>Refresh();
        Loaded+=(_,_)=>Refresh();
        _print.Click+=(_,_)=> { if(_report is not null) new ReportPrintDialog(_report) { Owner=Window.GetWindow(this) }.ShowDialog(); };
    }
    public void Configure(Func<(TrackProfile Track,List<RaceDayEventDefinition> Events,List<ClassDefinition> Classes)> source) { _source=source; Refresh(); }
    public void Refresh()
    {
        if(_source is null) return;
        var data=_source(); var selected=(_classes.SelectedItem as Choice)?.Id;
        _refreshing=true;
        var choices=new List<Choice> { new(null,"All classes") };
        choices.AddRange(TrackRecords.Calculate(data.Track,data.Events,data.Classes,1).Select(c=>new Choice(c.Id,c.Name)));
        _classes.ItemsSource=choices; _classes.SelectedItem=choices.FirstOrDefault(c=>c.Id==selected)??choices[0];
        _refreshing=false; Render();
    }
    private void Render()
    {
        if(_refreshing || _source is null) return;
        _records.Children.Clear(); _report=null; _print.IsEnabled=false;
        if(!int.TryParse(_count.Text,out var count) || count<1) { _status.Text="Enter a whole number of at least 1 for the records per class."; return; }
        var data=_source();
        var classes=TrackRecords.Calculate(data.Track,data.Events,data.Classes,count,(_classes.SelectedItem as Choice)?.Id);
        _report=TrackRecords.Report(data.Track.Name,classes,count); _print.IsEnabled=classes.Count>0;
        var unassigned=data.Events.Count(e=>e.Track is null && e.Sessions.Any(s=>s.ProgressStatus==EventSessionStatus.Completed));
        _status.Text=$"{(string.IsNullOrWhiteSpace(data.Track.Name)?"Your track":data.Track.Name)} • {classes.Sum(c=>c.Records.Count)} records • Top {count} per class"+
            (unassigned>0 ? $"\n{unassigned} older event(s) have no track assigned. Select an event in Events, then use Track details → Assign saved track to selected event to include its records." : "");
        if(classes.Count==0) _records.Children.Add(new TextBlock { Text=_report.EmptyMessage, TextWrapping=TextWrapping.Wrap });
        foreach(var c in classes)
        {
            var heading=new TextBlock { Text=c.Name.ToUpperInvariant(), FontSize=18, FontWeight=FontWeights.Bold, Margin=new Thickness(10,4,0,4) };
            _records.Children.Add(new Border { Child=heading, BorderThickness=new Thickness(5,0,0,0), BorderBrush=(Brush)new BrushConverter().ConvertFromString(c.Color)!, Margin=new Thickness(0,12,0,8) });
            if(c.Records.Count==0) { _records.Children.Add(new TextBlock { Text="No completed race laps recorded at this track yet.", Margin=new Thickness(10,0,0,16) }); continue; }
            var grid=new DataGrid { ItemsSource=c.Records, AutoGenerateColumns=false, IsReadOnly=true, CanUserAddRows=false, CanUserSortColumns=false, CanUserReorderColumns=false, HeadersVisibility=DataGridHeadersVisibility.Column, GridLinesVisibility=DataGridGridLinesVisibility.Horizontal, Margin=new Thickness(0,0,0,16) };
            grid.SetResourceReference(BackgroundProperty,"PageBrush"); grid.SetResourceReference(ForegroundProperty,"TextBrush");
            grid.SetResourceReference(DataGrid.RowBackgroundProperty,"PageBrush"); grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"PanelAltBrush");
            void Col(string title,string path,DataGridLength width)=>grid.Columns.Add(new DataGridTextColumn { Header=title, Binding=new Binding(path), Width=width });
            Col("#","Rank",45); Col("DRIVER","Driver",new DataGridLength(2,DataGridLengthUnitType.Star));
            Col("LAP TIME (s)","TimeDisplay",110); Col("EVENT DATE","DateDisplay",110); Col("EVENT","Event",new DataGridLength(2,DataGridLengthUnitType.Star));
            _records.Children.Add(grid);
        }
    }
}
