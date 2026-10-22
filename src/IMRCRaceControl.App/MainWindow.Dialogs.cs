using System.Windows;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private ClassDefinition? ShowClassEditor(ClassDefinition? existing = null)
    {
        while (true)
        {
            var dialog = new ClassEditorDialog(existing) { Owner = this };
            if (dialog.ShowDialog() != true)
                return null;

            var duplicate = Classes.Any(c => c != existing &&
                string.Equals(c.Name, dialog.ClassName, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                MessageBox.Show(this, $"A class named '{dialog.ClassName}' already exists.",
                    "Class", MessageBoxButton.OK, MessageBoxImage.Information);
                continue;
            }

            var item = existing ?? new ClassDefinition();
            item.Name = dialog.ClassName;
            item.RaceFormat = dialog.RaceFormat;
            item.MainRaceFormat = dialog.MainRaceFormat;
            item.MainRaceMinutes = dialog.MainRaceMinutes;
            item.MainTargetLaps = dialog.MainTargetLaps;
            item.RaceMinutes = dialog.RaceMinutes;
            item.TargetLaps = dialog.TargetLaps;
            item.MinimumLapSeconds = dialog.MinimumLapSeconds;
            item.MaxRacersPerHeat = dialog.RacersPerHeat;
            item.MaxRacersPerMain = dialog.RacersPerMain;
            item.MainRules = dialog.MainRules.Select(r => r.Clone()).ToList();
            item.QualStartStyle = dialog.QualStartStyle;
            item.RaceStartStyle = dialog.RaceStartStyle;

            if (existing is null)
                Classes.Add(item);

            foreach (var ev in Events)
            {
                foreach (var session in ev.Sessions.Where(s => s.ClassId == item.Id))
                {
                    if (session.ProgressStatus != EventSessionStatus.Planned) continue;
                    session.RaceFormat = item.FormatFor(session.SessionType);
                    session.TargetLaps = item.LapsFor(session.SessionType);
                    session.ClassName = item.Name;
                    session.StartStyle = session.SessionType == SessionType.Qualifying
                        ? item.QualStartStyle
                        : item.RaceStartStyle;
                }
            }

            ClassGrid.Items.Refresh();
            RaceClassCombo.Items.Refresh();
            AssignmentClassCombo.Items.Refresh();
            RefreshAssignmentRows();
            RefreshEventClassChoices();
            UpdateClassDetail(item);
            return item;
        }
    }

    private DriverProfile? ShowDriverEditor(DriverProfile? existing = null)
    {
        var dialog = new DriverEditorDialog(existing, _settings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return null;

        var driver = existing ?? new DriverProfile();
        driver.FirstName = dialog.FirstName;
        driver.LastName = dialog.LastName;
        driver.Nickname = dialog.Nickname;
        driver.PhoneticName = dialog.PhoneticName;
        driver.SyncLegacyName();

        if (existing is null)
            Drivers.Add(driver);

        foreach (var ev in Events)
        {
            foreach (var entrant in ev.Sessions.SelectMany(s => s.Entrants).Where(e => e.DriverId == driver.Id))
            {
                entrant.DriverName = driver.FullName;
                entrant.Nickname = driver.Nickname;
            }
        }

        DriverGrid.Items.Refresh();
        UpdateDriverProfileDetail(driver);
        RefreshEventDriverChoices(driver.Id);
        return driver;
    }

    private void UpdateClassDetail(ClassDefinition? selected)
    {
        if (selected is null)
        {
            ClassDetailNameText.Text = "Select a class";
            ClassDetailMinutesText.Text = "—";
            ClassDetailMinLapText.Text = "—";
            ClassDetailHeatText.Text = "—";
            ClassDetailQualText.Text = "—";
            ClassDetailRaceText.Text = "—";
            return;
        }

        ClassDetailNameText.Text = selected.Name;
        ClassDetailMinutesText.Text = selected.RaceLengthDisplay;
        ClassDetailMinLapText.Text = $"{selected.MinimumLapSeconds:0.###} seconds";
        ClassDetailHeatText.Text = $"{selected.MaxRacersPerHeat} / heat • {selected.MaxRacersPerMain} default / main";
        ClassDetailQualText.Text = selected.QualStartStyle.ToDisplay();
        ClassDetailRaceText.Text = $"{selected.RaceStartStyle.ToDisplay()}\n{selected.MainRuleSummary}";
    }

    private void UpdateDriverProfileDetail(DriverProfile? driver)
    {
        if (driver is null)
        {
            DriverProfileUuidText.Text = "";
            DriverProfileTitleText.Text = "Select a driver";
            DriverProfileFirstNameText.Text = "—";
            DriverProfileLastNameText.Text = "—";
            DriverProfileNicknameText.Text = "—";
            return;
        }

        DriverProfileUuidText.Text = driver.Uuid;
        DriverProfileTitleText.Text = driver.DisplayName;
        DriverProfileFirstNameText.Text = string.IsNullOrWhiteSpace(driver.FirstName) ? "—" : driver.FirstName;
        DriverProfileLastNameText.Text = string.IsNullOrWhiteSpace(driver.LastName) ? "—" : driver.LastName;
        DriverProfileNicknameText.Text = string.IsNullOrWhiteSpace(driver.Nickname) ? "—" : driver.Nickname;
    }
}
