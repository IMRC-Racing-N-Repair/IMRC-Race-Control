using System.Windows;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl.Dialogs;

public partial class DriverEditorDialog : Window
{
    private readonly LapSpeechService _speech = new();
    private readonly AppSettings _speechSettings;
    public string PhoneticName => PhoneticNameText.Text.Trim();
    public string FirstName => FirstNameText.Text.Trim();
    public string LastName => LastNameText.Text.Trim();
    public string Nickname => NicknameText.Text.Trim();

    public DriverEditorDialog(DriverProfile? driver = null, AppSettings? settings = null)
    {
        InitializeComponent();
        _speechSettings = settings ?? new AppSettings();

        if (driver is null)
        {
            DialogTitleText.Text = "ADD DRIVER";
        }
        else
        {
            DialogTitleText.Text = $"EDIT DRIVER • {driver.DisplayName}";
            FirstNameText.Text = driver.FirstName;
            LastNameText.Text = driver.LastName;
            NicknameText.Text = driver.Nickname;
            PhoneticNameText.Text = driver.PhoneticName;
        }

        Closed += (_, _) => _speech.Dispose();
        Loaded += (_, _) => FirstNameText.Focus();
    }

    private void TestName_Click(object sender, RoutedEventArgs e)
    {
        var driver = new DriverProfile { FirstName = FirstName, LastName = LastName,
            Nickname = Nickname, PhoneticName = PhoneticName };
        if (string.IsNullOrWhiteSpace(driver.SpokenName)) return;
        _speech.Stop();
        _speech.ResetFailure();
        if (!_speech.Enqueue(driver.SpokenName, _speechSettings.NameSpeechRate,
                _speechSettings.LapSpeechVolume, _speechSettings.LapSpeechVoice))
            MessageBox.Show(this, _speech.LastError ?? "Enter a name to test.", "Test pronunciation");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FirstName))
        {
            MessageBox.Show(this,
                "First name is required.",
                "Driver",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            FirstNameText.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
