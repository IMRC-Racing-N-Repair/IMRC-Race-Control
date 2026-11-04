namespace IMRCRaceControl.Models;

public enum AppTheme
{
    Dark,
    Light,
    Blue,
    Thanksgiving,
    Halloween,
    Christmas
}

public static class AppThemeExtensions
{
    public static string ToDisplay(this AppTheme theme) => theme switch
    {
        AppTheme.Dark => "Dark",
        AppTheme.Light => "Light",
        AppTheme.Blue => "Blue theme",
        AppTheme.Thanksgiving => "Thanksgiving",
        AppTheme.Halloween => "Halloween",
        AppTheme.Christmas => "Christmas",
        _ => theme.ToString()
    };
}
