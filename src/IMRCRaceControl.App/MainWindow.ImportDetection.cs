using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void OpenDetectedRaceImport(Window owner)
    {
        var picker = new OpenFileDialog
        {
            Title = "Select race data - automatically identify source",
            Filter = "Race databases or RCSP backups (*.db;*.sqlite;*.zip;*.adt)|*.db;*.sqlite;*.zip;*.adt|All files (*.*)|*.*"
        };
        if (picker.ShowDialog(owner) != true) return;
        ShowDetectedRaceImport(owner, picker.FileName);
    }

    private void OpenRcspDirectoryImport(Window owner)
    {
        var picker = new OpenFolderDialog { Title = "Select the RC Scoring Pro Data folder" };
        if (picker.ShowDialog(owner) == true) ShowDetectedRaceImport(owner, picker.FolderName);
    }

    private void ShowDetectedRaceImport(Window owner, string path)
    {
        RaceImportInspection inspection;
        try { inspection = RaceImportDetector.Inspect(path); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Unable to inspect this source. No data was changed.\n\n" + ex.Message,
                "Race data import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (inspection.Format == RaceImportFormat.LiveTime)
        {
            OpenLiveTimeImport(owner, path);
            return;
        }

        // An RCSP table inventory is not proof that records and race history
        // can be converted. Do not silently import approximate/incomplete data.
        var dialog = new Window
        {
            Owner = owner, Title = "Race data source identified",
            Width = 660, MaxHeight = SystemParameters.WorkArea.Height * .85,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = inspection.Format switch
        {
            RaceImportFormat.RcScoringPro => "RC SCORING PRO",
            RaceImportFormat.RaceControl => "IMRC RACE CONTROL",
            _ => "UNKNOWN DATABASE FORMAT"
        }, FontWeight = FontWeights.Bold, FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = inspection.Description,
            Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = inspection.Format == RaceImportFormat.RcScoringPro
                ? "Detected RC Scoring Pro data categories: Drivers, Classes, Class Assignments, Entries / Results, Practice, Practice Runs, and Tracks. These names identify tables, not validated records. Full Advantage record decoding, lap history reconciliation and selective import are not yet supported. Import is disabled to protect existing results."
                : inspection.Format == RaceImportFormat.RaceControl
                    ? "This is a native Race Control backup. Use RESTORE DATABASE in Saved Race Data for whole-database recovery. A selective merge of native backups is not available."
                    : "The file cannot be identified safely. Select the original backup or database.",
            TextWrapping = TextWrapping.Wrap
        });
        if (inspection.Tables.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "TABLE INVENTORY", FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 14, 0, 4) });
            panel.Children.Add(new TextBox { IsReadOnly = true, MinHeight = 65, MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = string.Join(", ", inspection.Tables), TextWrapping = TextWrapping.Wrap });
        }
        foreach (var note in inspection.Notes)
            panel.Children.Add(new TextBlock { Text = "• " + note,
                Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = "CLOSE", Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 110 };
        close.Click += (_, _) => dialog.Close();
        panel.Children.Add(close);
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.ShowDialog();
    }
}
