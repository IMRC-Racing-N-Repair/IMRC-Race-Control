using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Controls;
public sealed class EventRegistrationView : UserControl
{
    private readonly ListBox _drivers = new() { DisplayMemberPath="Label" };
    private readonly TextBox _search = new();
    private readonly StackPanel _classes = new();
    private readonly TextBlock _selected = new() { FontSize=17,FontWeight=FontWeights.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) };
    private readonly Button _newDriver = new() { Content="+ NEW DRIVER" }, _newClass=new() { Content="+ CLASS" };
    private RaceDayEventDefinition? _event;
    private List<DriverProfile> _profiles=[]; private List<ClassDefinition> _definitions=[]; private List<DriverClassAssignment> _defaults=[];
    private Func<string,string,string,bool,string,Task>? _save;
    private bool _refreshing, _saving;
    private sealed record DriverRow(string Id,string Label);
    public EventRegistrationView()
    {
        var root=new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(.8,GridUnitType.Star) });root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(10) });root.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.8,GridUnitType.Star) });
        var left=new DockPanel { Margin=new Thickness(7) };var top=new StackPanel();
        top.Children.Add(new TextBlock { Text="DRIVERS",FontSize=15,FontWeight=FontWeights.Bold });
        top.Children.Add(_search);_search.ToolTip="Search drivers by name or nickname";top.Children.Add(_newDriver);
        DockPanel.SetDock(top,Dock.Top);left.Children.Add(top);left.Children.Add(_drivers);root.Children.Add(left);
        var right=new DockPanel { Margin=new Thickness(7) };Grid.SetColumn(right,2);root.Children.Add(right);
        var header=new StackPanel();header.Children.Add(_selected);
        header.Children.Add(new TextBlock { Text="Select classes and enter a transponder for each. Changes save automatically.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,6) });header.Children.Add(_newClass);
        DockPanel.SetDock(header,Dock.Top);right.Children.Add(header);DockPanel.SetDock(_status,Dock.Bottom);right.Children.Add(_status);
        right.Children.Add(new ScrollViewer { Content=_classes,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        left.SetResourceReference(Panel.BackgroundProperty,"PanelBrush");right.SetResourceReference(Panel.BackgroundProperty,"PanelBrush");Content=root;
        _search.TextChanged+=(_,_)=>RefreshDrivers();_drivers.SelectionChanged+=(_,_)=> { if(!_refreshing)RefreshClasses(); };
    }
    public void Configure(RaceDayEventDefinition? ev,IEnumerable<DriverProfile> drivers,IEnumerable<ClassDefinition> classes,IEnumerable<DriverClassAssignment> defaults,
        Func<string,string,string,bool,string,Task> save,Action newDriver,Action newClass)
    {
        _event=ev;_profiles=drivers.ToList();_definitions=classes.ToList();_defaults=defaults.ToList();_save=save;
        _newDriver.Command=null;_newClass.Command=null;
        NewDriver=newDriver;NewClass=newClass;
        if(!_wired) { _wired=true;_newDriver.Click+=(_,_)=>NewDriver?.Invoke();_newClass.Click+=(_,_)=>NewClass?.Invoke(); }
        _newDriver.IsEnabled=_newClass.IsEnabled=ev?.Status==EventStatus.Active;
        RefreshDrivers();
    }
    private bool _wired;private Action? NewDriver,NewClass;
    public void SelectDriver(string id) { _search.Clear();_drivers.SelectedItem=_drivers.Items.Cast<DriverRow>().FirstOrDefault(x=>x.Id==id); }
    private void RefreshDrivers()
    {
        if(_saving)return;
        var selected=(_drivers.SelectedItem as DriverRow)?.Id;
        _refreshing=true;
        var query=_search.Text.Trim();
        _drivers.ItemsSource=_profiles.Where(d=>(d.DisplayName+" "+d.FullName).Contains(query,StringComparison.OrdinalIgnoreCase)).OrderBy(d=>d.DisplayName)
            .Select(d=>new DriverRow(d.Id,$"{d.DisplayName}  •  {_event?.DriverAssignments.Count(a=>a.DriverId==d.Id) ?? 0} classes")).ToList();
        _drivers.SelectedItem=_drivers.Items.Cast<DriverRow>().FirstOrDefault(x=>x.Id==selected) ?? _drivers.Items.Cast<DriverRow>().FirstOrDefault();
        _refreshing=false;RefreshClasses();
    }
    private void RefreshClasses()
    {
        if(_saving)return;
        _classes.Children.Clear();
        var driver=_drivers.SelectedItem as DriverRow;
        _selected.Text=driver is null ? "Select a driver" : _profiles.First(d=>d.Id==driver.Id).DisplayName+" • CLASSES & TRANSPONDERS";
        if(driver is null || _event is null)return;
        var eventId=_event.Id;
        foreach(var cls in _definitions.OrderBy(c=>c.Name))
        {
            var assignment=_event.DriverAssignments.FirstOrDefault(a=>a.DriverId==driver.Id && a.ClassId==cls.Id);
            var defaults=_defaults.Where(a=>a.DriverId==driver.Id && a.ClassId==cls.Id).Select(a=>a.TransponderId).Where(t=>!string.IsNullOrWhiteSpace(t)).Distinct();
            var row=new Grid { Margin=new Thickness(0,5,0,5) };row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(180) });
            var check=new CheckBox { Content=cls.Name,IsChecked=assignment is not null,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6) };
            var number=new TextBox { Text=assignment?.TransponderId ?? string.Join(";",defaults),IsEnabled=assignment is not null,ToolTip="Transponder number for this driver and class (optional)",Margin=new Thickness(6) };
            var field=new StackPanel();field.Children.Add(new TextBlock { Text="Transponder",Margin=new Thickness(6,0,0,0),FontSize=10 });field.Children.Add(number);
            Grid.SetColumn(field,1);row.Children.Add(check);row.Children.Add(field);row.IsEnabled=_event.Status==EventStatus.Active;
            var border=new Border { Child=row,BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(cls.PrintColor)),BorderThickness=new Thickness(4,0,0,0),Margin=new Thickness(0,4,0,4) };_classes.Children.Add(border);
            check.Click+=async(_,_)=>await Save(eventId,driver.Id,cls.Id,check.IsChecked==true,number.Text);
            number.LostKeyboardFocus+=async(_,_)=>
            {
                if(_saving || _event?.Id!=eventId)return;
                var current=_event?.DriverAssignments.FirstOrDefault(a=>a.DriverId==driver.Id && a.ClassId==cls.Id);
                if(current is not null && current.TransponderId!=number.Text.Trim())await Save(eventId,driver.Id,cls.Id,true,number.Text);
            };
        }
        _status.Text=_event.Status==EventStatus.Completed ? "Completed event: use Reopen Event to edit registrations." : "Saved races stay in place. Add new registrants to their race in Open Rounds / Mains, then reprint.";
    }
    private async Task Save(string eventId,string driver,string cls,bool included,string number)
    {
        if(_save is null || _saving)return;
        _saving=true;IsEnabled=false;
        string? error=null;
        try { await _save(eventId,driver,cls,included,number); }catch(Exception ex){error=ex.Message;}
        finally { _saving=false;IsEnabled=true;RefreshDrivers();if(error is not null)_status.Text=error; }
    }
    internal static void ExportPreview(string output)
    {
        var driver=new DriverProfile { FirstName="Alex",LastName="Rivera" };
        var second=new DriverProfile { FirstName="Jordan",LastName="Lee" };
        var classes=new[] { new ClassDefinition { Id="stock",Name="21.5 Stock",PrintColor="#009FE3" },new ClassDefinition { Id="mod",Name="Modified Touring",PrintColor="#EF5350" },new ClassDefinition { Id="novice",Name="Novice",PrintColor="#24BD60" } };
        var ev=new RaceDayEventDefinition { Name="Club Racing",ClassIds=["stock","mod"],DriverAssignments=[new() { DriverId=driver.Id,ClassId="stock",TransponderId="1000001" },new() { DriverId=driver.Id,ClassId="mod",TransponderId="6001201" }] };
        var view=new EventRegistrationView();
        view.Configure(ev,[driver,second],classes,[],(eventId,d,c,i,t)=> { IMRCRaceControl.Core.EventRegistration.Set(ev,d,c,i,t);return Task.CompletedTask; },()=>{},()=>{});
        var window=new Window { Width=1100,Height=560,Content=view };window.Show();
        window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(view);
        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using(var file=System.IO.File.Create(System.IO.Path.Combine(output,"Event-Driver-Registration.png")))png.Save(file);
        window.Close();
    }
}
