namespace IMRCRaceControl.Models;

public enum AppTheme
{
    Dark,
    Light,
    Blue
}

public static class AppThemeExtensions
{
    public static string ToDisplay(this AppTheme theme) => theme switch
    {
        AppTheme.Dark => "Dark",
        AppTheme.Light => "Light",
        AppTheme.Blue => "Blue theme",
        _ => theme.ToString()
    };
}
