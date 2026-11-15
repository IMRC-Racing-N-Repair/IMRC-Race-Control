using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private PracticeCardViewModel? _practiceCardBeingAssigned;
    private readonly Dictionary<string, (string DriverId, string ClassId)> _practiceRentalAssignments =
        new(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<EventDriverAssignment> PracticeEventAssignments =>
        _activeEvent is { Status: EventStatus.Active } raceDay ? raceDay.DriverAssignments : [];

    private static bool HasPracticeTag(string? value, string tag) =>
        SplitTransponders(value).Contains(tag, StringComparer.OrdinalIgnoreCase);

    private bool CanAssignPracticeCard(PracticeCardViewModel card) =>
        _practiceActive && !card.IsSaving && PracticeCards.Contains(card) &&
        card.DriverId == PracticeTracker.UnknownId(card.TransponderId);

    private void PracticeCards_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ItemsControl control || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(control, source) is not ContentPresenter { Content: PracticeCardViewModel card } container ||
            !CanAssignPracticeCard(card))
            return;

        var menu = new ContextMenu { PlacementTarget = container, Placement = PlacementMode.MousePoint };
        var assign = new MenuItem { Header = "Assign driver and class…" };
        assign.Click += (_, _) => ShowPracticeAssignment(card);
        menu.Items.Add(assign);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ShowPracticeAssignment(PracticeCardViewModel card)
    {
        if (!CanAssignPracticeCard(card) || _practiceCardBeingAssigned is not null)
            return;

        if (Drivers.Count == 0 || Classes.Count == 0)
        {
            MessageBox.Show(this, "Add a driver and a class before assigning this transponder.", "Assign practice transponder");
            return;
        }

        var tag = card.TransponderId;
        var isRental = _settings.RentalTransponders.Inventory.Any(t =>
            string.Equals(t.Number, tag, StringComparison.OrdinalIgnoreCase));
        var dialog = new Window
        {
            Owner = this, Title = "Assign practice transponder", Width = 450, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = $"Transponder {tag}", FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = isRental
                ? "This rental assignment applies until Practice is stopped. Recorded laps stay on the card."
                : "Save this transponder to the driver's class. Recorded laps stay on the card.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12)
        });

        var driverBox = new ComboBox { ItemsSource = Drivers.OrderBy(d => d.DisplayName).ToList(), DisplayMemberPath = nameof(DriverProfile.DisplayName), IsTextSearchEnabled = true };
        var classBox = new ComboBox { ItemsSource = Classes.OrderBy(c => c.Name).ToList(), DisplayMemberPath = nameof(ClassDefinition.Name), IsTextSearchEnabled = true };
        panel.Children.Add(new Label { Content = "_Driver", Target = driverBox });
        panel.Children.Add(driverBox);
        panel.Children.Add(new Label { Content = "_Class", Target = classBox });
        panel.Children.Add(classBox);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "CANCEL", IsCancel = true };
        var save = new Button { Content = "ASSIGN", IsDefault = true, IsEnabled = false };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        var saving = false;
        void UpdateAction() => save.IsEnabled = !saving && driverBox.SelectedItem is DriverProfile && classBox.SelectedItem is ClassDefinition;
        driverBox.SelectionChanged += (_, _) => UpdateAction();
        classBox.SelectionChanged += (_, _) => UpdateAction();
        dialog.Closing += (_, e) => e.Cancel = saving;
        save.Click += async (_, _) =>
        {
            if (saving || driverBox.SelectedItem is not DriverProfile driver || classBox.SelectedItem is not ClassDefinition cls)
                return;

            saving = true;
            UpdateAction();
            cancel.IsEnabled = driverBox.IsEnabled = classBox.IsEnabled = false;
            try
            {
                await AssignPracticeCardAsync(card, driver, cls, isRental);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                return;
            }
            finally
            {
                saving = false;
                cancel.IsEnabled = driverBox.IsEnabled = classBox.IsEnabled = true;
                UpdateAction();
            }
            dialog.DialogResult = true;
        };

        _practiceCardBeingAssigned = card;
        try { dialog.ShowDialog(); }
        finally { _practiceCardBeingAssigned = null; }
    }

    private async Task AssignPracticeCardAsync(PracticeCardViewModel card, DriverProfile driver, ClassDefinition cls, bool isRental)
    {
        if (!CanAssignPracticeCard(card))
            throw new InvalidOperationException("This practice card is no longer available to assign.");

        var tag = card.TransponderId;
        var conflict = _driverAssignments
            .Select(a => (a.DriverId, a.TransponderId))
            .Concat(PracticeEventAssignments.Select(a => (a.DriverId, a.TransponderId)))
            .FirstOrDefault(a => a.DriverId != driver.Id && HasPracticeTag(a.TransponderId, tag));
        if (conflict.DriverId is not null)
        {
            var owner = Drivers.FirstOrDefault(d => d.Id == conflict.DriverId)?.DisplayName ?? "another driver";
            throw new InvalidOperationException($"Transponder {tag} is already assigned to {owner}.");
        }

        if (isRental)
        {
            _practiceRentalAssignments[tag] = (driver.Id, cls.Id);
        }
        else
        {
            var matches = _driverAssignments.Where(a => a.DriverId == driver.Id && a.ClassId == cls.Id).ToList();
            var existing = matches.FirstOrDefault(a => HasPracticeTag(a.TransponderId, tag)) ?? matches.FirstOrDefault();
            var assignment = existing ?? new DriverClassAssignment { DriverId = driver.Id, ClassId = cls.Id };
            var previous = assignment.TransponderId;
            if (!HasPracticeTag(previous, tag))
                assignment.TransponderId = string.Join("; ", SplitTransponders(previous).Append(tag));
            if (existing is null)
                _driverAssignments.Add(assignment);

            try { await CaptureAndSaveSettingsAsync(reportFailureToCaller: true); }
            catch
            {
                if (existing is null) _driverAssignments.Remove(assignment);
                else assignment.TransponderId = previous;
                _settings.DriverClassAssignments = _driverAssignments.ToList();
                throw;
            }
        }

        card.ResolveDriver(driver.Id, driver.DisplayName, cls.Name);
        RefreshAssignmentRows();
        FooterStatusText.Text = $"Transponder {tag} assigned to {driver.DisplayName} • {cls.Name}.";
    }
}
