using System.Globalization;
using System.Windows;

namespace IMRCRaceControl.Dialogs;

public partial class EventEditorDialog : Window
{
    public string EventName { get; private set; } = "Race Day";
    public DateTime EventDate { get; private set; } = DateTime.Today;

    public EventEditorDialog()
    {
        InitializeComponent();
        EventDateText.Text = DateTime.Now.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        Loaded += (_, _) =>
        {
            EventNameText.Focus();
            EventNameText.SelectAll();
        };
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = EventNameText.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter an event name.", "New event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var formats = new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd" };
        if (!DateTime.TryParseExact(EventDateText.Text.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            MessageBox.Show(this, "Enter the date as MM/DD/YYYY.", "New event",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        EventName = name;
        EventDate = date.Date;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
