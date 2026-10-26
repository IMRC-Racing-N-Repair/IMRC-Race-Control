using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Controls;

public partial class RentalTransponderView : UserControl
{
    private AppSettings? _settings;
    private Func<Action, bool, Task>? _commit;
    private Point _dragStart;
    private string? _dragNumber;
    private bool _refreshing;
    private const string DragFormat = "IMRC.RentalTransponderNumber";
    private sealed record RentalRow(string Id, string Number, string Nickname, string LabelHex, string AssignedTo);
    public RentalTransponderView() { InitializeComponent(); }
    public void Configure(AppSettings settings, ObservableCollection<DetectedTransponder> decoded, IEnumerable<RaceDayEventDefinition> events,
        Func<Action, bool, Task> commit, string? activeEventId)
    {
        _settings = settings; _commit = commit;
        DecoderGrid.ItemsSource = decoded;
        var chosen = (EventPicker.SelectedItem as RaceDayEventDefinition)?.Id ?? activeEventId;
        EventPicker.ItemsSource = events;
        EventPicker.SelectedItem = events.FirstOrDefault(x => x.Id == chosen) ?? events.FirstOrDefault(x => x.Status == EventStatus.Active);
        Refresh(); RefreshClasses();
    }
    private string? SelectedId => (InventoryGrid.SelectedItem as RentalRow)?.Id;
    private void Refresh(string? selectedId = null)
    {
        if (_settings is null) return;
        selectedId ??= SelectedId;
        _refreshing = true;
        try
        {
            InventoryGrid.ItemsSource = _settings.RentalTransponders.Inventory.Select(r => new RentalRow(r.Id, r.Number, r.Nickname, r.LabelHex,
                string.Join(", ", _settings.RentalTransponders.History.Where(h => h.RentalId == r.Id && h.ReturnedAt is null).Select(h => h.DriverName).Distinct()))).ToList();
            InventoryGrid.SelectedItem = ((IEnumerable<RentalRow>)InventoryGrid.ItemsSource).FirstOrDefault(x => x.Id == selectedId);
            InventoryGrid.FontSize = _settings.RentalTransponders.LabelFontSize;
            InventoryGrid.RowHeight = _settings.RentalTransponders.RowHeight;
        }
        finally { _refreshing = false; }
        RefreshHistory();
    }
    private void RefreshHistory()
    {
        if (_settings is null || HistoryGrid is null) return;
        var id = SelectedId;
        var rental = _settings.RentalTransponders.Inventory.FirstOrDefault(x => x.Id == id);
        SelectedLabel.Text = rental is null ? "Select a rental to assign it or view its history" : $"{rental.Nickname} • {rental.Number}";
        HistoryGrid.ItemsSource = id is null ? null : RentalTransponderRegistry.History(_settings, id, LastThree.IsChecked == true ? 3 : null);
    }
    private void RentalSelected(object sender, SelectionChangedEventArgs e) { if (!_refreshing) RefreshHistory(); }
    private void HistoryFilterChanged(object sender, RoutedEventArgs e) => RefreshHistory();
    private async Task Change(Action mutation, bool assignments = false, string? selectId = null)
    {
        if (_commit is null) return;
        IsEnabled = false;
        try { await _commit(mutation, assignments); StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); StatusText.Text = "Saved."; }
        catch (Exception ex) { StatusText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); StatusText.Text = ex.Message; }
        finally { IsEnabled = true; Refresh(selectId); }
    }
    private async void AddClicked(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        string? added = null;
        await Change(() => { added = RentalTransponderRegistry.Add(_settings, NumberInput.Text, NicknameInput.Text).Id; });
        if (added is not null && _settings.RentalTransponders.Inventory.Any(x => x.Id == added)) { Refresh(added); NumberInput.Clear(); NicknameInput.Clear(); }
    }
    private void AssignmentContextChanged(object sender, SelectionChangedEventArgs e) => RefreshClasses();
    private void RefreshClasses()
    {
        if (_settings is null || ClassPicker is null) return;
        var selected = (ClassPicker.SelectedItem as ClassDefinition)?.Id;
        var ev = EventPicker.SelectedItem as RaceDayEventDefinition;
        var classes = _settings.Classes.Where(x => ev?.ClassIds.Contains(x.Id) == true).ToList();
        ClassPicker.ItemsSource = classes;
        ClassPicker.SelectedItem = classes.FirstOrDefault(x => x.Id == selected) ?? classes.FirstOrDefault();
        RefreshDrivers();
    }
    private void ClassChanged(object sender, SelectionChangedEventArgs e) => RefreshDrivers();
    private void RefreshDrivers()
    {
        if (_settings is null || DriverPicker is null) return;
        var selected = (DriverPicker.SelectedItem as DriverProfile)?.Id;
        var ev = EventPicker.SelectedItem as RaceDayEventDefinition;
        var cls = ClassPicker.SelectedItem as ClassDefinition;
        var ids = ev?.DriverAssignments.Where(a => a.ClassId == cls?.Id).Select(a => a.DriverId).ToHashSet() ?? [];
        var drivers = _settings.Drivers.Where(d => ids.Contains(d.Id)).OrderBy(d => d.DisplayName).ToList();
        DriverPicker.ItemsSource = drivers;
        DriverPicker.SelectedItem = drivers.FirstOrDefault(x => x.Id == selected) ?? drivers.FirstOrDefault();
    }
    private async void AssignClicked(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SelectedId is not string id || EventPicker.SelectedItem is not RaceDayEventDefinition ev ||
            ClassPicker.SelectedItem is not ClassDefinition cls || DriverPicker.SelectedItem is not DriverProfile driver)
        { StatusText.Text = "Select a rental, event, class and registered racer. Add racers to the event roster first if needed."; return; }
        await Change(() => RentalTransponderRegistry.Assign(_settings, id, ev.Id, driver.Id, cls.Id), true, id);
    }
    private async void ReturnClicked(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SelectedId is not string id) { StatusText.Text = "Select the rental being returned."; return; }
        await Change(() => RentalTransponderRegistry.Return(_settings, id), true, id);
    }
    private void DecoderMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(DecoderGrid); _dragNumber = null;
        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node is not null && node is not DataGridRow) node = VisualTreeHelper.GetParent(node);
        if (node is DataGridRow { Item: DetectedTransponder hit }) _dragNumber = hit.TransponderId;
    }
    private void DecoderMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNumber is null) return;
        var p = e.GetPosition(DecoderGrid);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject(); data.SetData(DragFormat, _dragNumber); data.SetText(_dragNumber); _dragNumber = null;
        DragDrop.DoDragDrop(DecoderGrid, data, DragDropEffects.Copy);
    }
    private void DecoderDoubleClick(object sender, MouseButtonEventArgs e)
    { if (DecoderGrid.SelectedItem is DetectedTransponder row) { NumberInput.Text = row.TransponderId; NumberInput.Focus(); } }
    private void NumberDragOver(object sender, DragEventArgs e)
    { e.Effects = e.Data.GetDataPresent(DragFormat) || e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void NumberDropped(object sender, DragEventArgs e)
    {
        NumberInput.Text = (e.Data.GetData(DragFormat) ?? e.Data.GetData(DataFormats.UnicodeText))?.ToString()?.Trim() ?? "";
        NumberInput.Focus(); e.Handled = true;
        if (ReferenceEquals(sender, InventoryGrid)) AddClicked(sender, new RoutedEventArgs());
        else StatusText.Text = "Number filled. Choose Add rental to save it.";
    }
    private async void SettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;
        var id = SelectedId;
        var item = _settings.RentalTransponders.Inventory.FirstOrDefault(x => x.Id == id);
        var dialog = new RentalAppearanceDialog(_settings, item) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        await Change(() =>
        {
            if (item is not null) RentalTransponderRegistry.SetLabel(_settings, item.Id, dialog.Color, dialog.Number, dialog.CustomNickname);
            _settings.RentalTransponders.LabelFontSize = dialog.LabelSize;
            _settings.RentalTransponders.RowHeight = Math.Max(dialog.RowSize, dialog.LabelSize + 12);
        }, false, id);
    }
}
