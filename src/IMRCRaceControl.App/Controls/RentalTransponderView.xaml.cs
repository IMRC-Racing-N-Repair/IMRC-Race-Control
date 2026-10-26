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
    private sealed record RentalRow(string Id, string Number, string Nickname, string LabelHex);
    public RentalTransponderView() { InitializeComponent(); }
    public void Configure(AppSettings settings, ObservableCollection<DetectedTransponder> decoded, IEnumerable<RaceDayEventDefinition> events,
        Func<Action, bool, Task> commit, string? activeEventId)
    {
        _settings = settings; _commit = commit;
        DecoderGrid.ItemsSource = decoded;
        Refresh();
    }
    private string? SelectedId => (InventoryGrid.SelectedItem as RentalRow)?.Id;
    private void Refresh(string? selectedId = null)
    {
        if (_settings is null) return;
        selectedId ??= SelectedId;
        _refreshing = true;
        try
        {
            InventoryGrid.ItemsSource = _settings.RentalTransponders.Inventory.Select(r => new RentalRow(r.Id, r.Number, r.Nickname, r.LabelHex)).ToList();
            InventoryGrid.SelectedItem = ((IEnumerable<RentalRow>)InventoryGrid.ItemsSource).FirstOrDefault(x => x.Id == selectedId);
        }
        finally { _refreshing = false; }
        RefreshHistory();
    }
    private void RefreshHistory()
    {
        if (_settings is null || HistoryGrid is null) return;
        var id = SelectedId;
        var rental = _settings.RentalTransponders.Inventory.FirstOrDefault(x => x.Id == id);
        SelectedLabel.Text = rental is null ? "Select a rental to view its history" : $"{rental.Nickname} • {rental.Number}";
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
        var dialog = new RentalAppearanceDialog(_settings) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        await Change(() => dialog.Apply(_settings));
    }
    private async void EditNicknameClicked(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SelectedId is not string id) { StatusText.Text = "Select a rental to edit its nickname."; return; }
        var rental = _settings.RentalTransponders.Inventory.Single(x => x.Id == id);
        var dialog = new RentalNicknameDialog(_settings, rental) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        await Change(() => RentalTransponderRegistry.SetLabel(_settings, id, dialog.LabelColor, dialog.LabelNumber, dialog.CustomNickname), false, id);
    }
}
