using System.Collections.ObjectModel;
using System.Windows;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl;

public partial class MainWindow
{
    public ObservableCollection<DetectedTransponder> DetectedTransponders { get; } = new();

    private void ObserveDecoderPassing(DecoderPassing passing, string source)
    {
        var row = DetectedTransponders.FirstOrDefault(t => t.TransponderId == passing.TransponderId);
        if (row is null)
        {
            row = new DetectedTransponder { TransponderId = passing.TransponderId };
            DetectedTransponders.Insert(0, row);
            // Reader history is diagnostic only; never remove a scoring record.
            if (DetectedTransponders.Count > 1000) DetectedTransponders.RemoveAt(DetectedTransponders.Count - 1);
        }
        var assignment = _driverAssignments.FirstOrDefault(a =>
            SplitTransponders(a.TransponderId).Contains(passing.TransponderId, StringComparer.OrdinalIgnoreCase));
        var driver = assignment is null ? null : Drivers.FirstOrDefault(d => d.Id == assignment.DriverId);
        row.Observe(passing.Timestamp, source, driver?.DisplayName, passing.Hits, passing.Strength);
        DetectedTransponderGrid.SelectedItem ??= row;
        LastDetectedTransponderText.Text = $"LAST TRANSPONDER: {passing.TransponderId}";
        RegisterResolvedTransponder(passing.TransponderId, source, passing.Timestamp);
    }

    private void CopyDetectedTransponder_Click(object sender, RoutedEventArgs e)
    {
        if (DetectedTransponderGrid.SelectedItem is DetectedTransponder tag)
            CopyTransponderNumber(tag.TransponderId);
    }

    internal void CopyTransponderNumber(string id)
    {
        try
        {
            Clipboard.SetText(id);
            FooterStatusText.Text = $"Copied transponder {id}. Paste it into a driver's transponder assignment.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, $"The clipboard is busy. Transponder number: {id}", "Copy transponder");
        }
    }

    private void ClearDetectedTransponders_Click(object sender, RoutedEventArgs e)
    {
        DetectedTransponders.Clear();
        LastDetectedTransponderText.Text = "Pass a transponder over the loop to read its number.";
    }
}
