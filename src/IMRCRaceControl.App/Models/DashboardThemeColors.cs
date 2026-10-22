namespace IMRCRaceControl.Models;

public sealed record DashboardThemeColors(
    string Background,
    string Panel,
    string Row,
    string AlternateRow,
    string Text,
    string PassingHighlight,
    string FastestLapHighlight,
    string NewBestLapHighlight,
    string CheckInGlow,
    string MinimumLapGlow,
    string PositionUp,
    string PositionDown,
    string PositionSame);
