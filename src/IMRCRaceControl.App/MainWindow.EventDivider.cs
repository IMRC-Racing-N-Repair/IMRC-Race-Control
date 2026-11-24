using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private void RestoreEventRoundsDivider()
    {
        var share = _settings.EventRoundsHeightShare;
        if (!double.IsFinite(share) || share <= 0 || share >= 1) share = 0.25;
        EventRegistrationRow.Height = new GridLength(1-share,GridUnitType.Star);
        _eventRoundsHeight = new GridLength(share,GridUnitType.Star);
        if (EventRoundsSection.Visibility != Visibility.Collapsed) EventRoundsRow.Height = _eventRoundsHeight;
    }

    private void EventRoundsSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!e.Canceled) RememberEventRoundsDivider();
    }
    private void EventRoundsSplitter_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down) RememberEventRoundsDivider();
    }
    private void RememberEventRoundsDivider()
    {
        if (EventRoundsSection.Visibility != Visibility.Visible) return;
        var total = EventRegistrationRow.ActualHeight + EventRoundsRow.ActualHeight;
        if (total <= 0 || !double.IsFinite(total)) return;
        var share = EventRoundsRow.ActualHeight / total;
        if (share <= 0 || share >= 1) return;
        _settings.EventRoundsHeightShare = share;
        RestoreEventRoundsDivider();
        _ = CaptureAndSaveSettingsAsync();
    }

    private void CheckEventRoundsDivider()
    {
        var original = _settings.EventRoundsHeightShare;
        try
        {
            foreach (var share in new[] { 0.25, 0.55, 0.75 })
            {
                _settings.EventRoundsHeightShare = share;
                RestoreEventRoundsDivider();
                if (Math.Abs(EventRegistrationRow.Height.Value-(1-share)) > 0.0001 ||
                    Math.Abs(EventRoundsRow.Height.Value-share) > 0.0001)
                    throw new InvalidOperationException("Saved event divider ratio did not restore.");
            }
        }
        finally { _settings.EventRoundsHeightShare = original; RestoreEventRoundsDivider(); }
    }
}
