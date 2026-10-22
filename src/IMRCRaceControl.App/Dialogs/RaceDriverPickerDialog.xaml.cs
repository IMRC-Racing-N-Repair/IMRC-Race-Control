using System.Windows;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

public partial class RaceDriverPickerDialog : Window
{
    public DriverProfile? SelectedDriver => DriverCombo.SelectedItem as DriverProfile;
    public string Transponder => TransponderText.Text.Trim();
    public bool CreateNewDriver { get; private set; }

    public RaceDriverPickerDialog(IReadOnlyList<DriverProfile> availableDrivers)
    {
        InitializeComponent();
        DriverCombo.ItemsSource = availableDrivers;
        if (availableDrivers.Count > 0)
            DriverCombo.SelectedIndex = 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDriver is null)
        {
            MessageBox.Show(this, "Select a driver or choose Create New Racer.",
                "Add racer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void CreateNew_Click(object sender, RoutedEventArgs e)
    {
        CreateNewDriver = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
