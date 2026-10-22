using System.Windows;

namespace IMRCRaceControl.Dialogs;

public partial class TransponderPromptDialog : Window
{
    public string Transponder => TransponderText.Text.Trim();

    public TransponderPromptDialog(string driverName, string current)
    {
        InitializeComponent();
        DriverText.Text = driverName;
        TransponderText.Text = current ?? string.Empty;
        Loaded += (_, _) =>
        {
            TransponderText.Focus();
            TransponderText.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
