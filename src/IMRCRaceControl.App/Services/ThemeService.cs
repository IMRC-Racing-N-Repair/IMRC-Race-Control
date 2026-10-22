using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Services;

public static class ThemeService
{
    private sealed record Palette(
        string Page,
        string Panel,
        string PanelAlt,
        string PanelHover,
        string Input,
        string Border,
        string BorderStrong,
        string Text,
        string Muted,
        string Accent,
        string AccentDark,
        string Good,
        string Warn,
        string Danger,
        string Honeycomb);

    public static DashboardThemeColors GetDashboardDefaults(AppTheme theme) => theme switch
    {
        AppTheme.Light => new DashboardThemeColors(
            Background: "#F3F6FA",
            Panel: "#FFFFFF",
            Row: "#E8EEF5",
            AlternateRow: "#DDE6F0",
            Text: "#182330",
            PassingHighlight: "#2E78B8",
            FastestLapHighlight: "#FFF2B8",
            NewBestLapHighlight: "#CDEEDC",
            CheckInGlow: "#D83A3A",
            MinimumLapGlow: "#E67E22",
            PositionUp: "#2EAF62",
            PositionDown: "#D94A4A",
            PositionSame: "#D6A72C"),

        AppTheme.Blue => new DashboardThemeColors(
            Background: "#071522",
            Panel: "#0B2030",
            Row: "#102B40",
            AlternateRow: "#15364D",
            Text: "#F2F9FF",
            PassingHighlight: "#2997DA",
            FastestLapHighlight: "#3A4419",
            NewBestLapHighlight: "#0C5A4A",
            CheckInGlow: "#FF5555",
            MinimumLapGlow: "#FF9F43",
            PositionUp: "#2EBE75",
            PositionDown: "#E85B5B",
            PositionSame: "#D9B43B"),

        _ => new DashboardThemeColors(
            Background: "#101214",
            Panel: "#171A1D",
            Row: "#131619",
            AlternateRow: "#1B1F23",
            Text: "#F4F7FB",
            PassingHighlight: "#7E2E35",
            FastestLapHighlight: "#3A3212",
            NewBestLapHighlight: "#0E5B45",
            CheckInGlow: "#FF4B4B",
            MinimumLapGlow: "#FF9800",
            PositionUp: "#2FA866",
            PositionDown: "#C94848",
            PositionSame: "#B99226")
    };

    public static void Apply(AppTheme theme)
    {
        var palette = theme switch
        {
            AppTheme.Light => new Palette(
                "#F3F6FA", "#FFFFFF", "#E8EEF5", "#DCE6F0", "#FFFFFF",
                "#C3CFDC", "#8296AD", "#182330", "#58687A",
                "#C73640", "#9E2833", "#1EAA68", "#A66C00", "#C64141", "#9ABBD4"),

            AppTheme.Blue => new Palette(
                "#071522", "#0B2030", "#102B40", "#17415F", "#081B2A",
                "#24516E", "#3A789D", "#F2F9FF", "#9FC1D9",
                "#2997DA", "#155D8B", "#3AD48A", "#F3C65B", "#EF6A6A", "#2C6F98"),

            _ => new Palette(
                "#101214", "#171A1D", "#202428", "#2A3035", "#131619",
                "#30363C", "#626D77", "#F4F7FB", "#AAB6C7",
                "#E34B4B", "#7E2E35", "#42CE82", "#F1C75B", "#EF6A6A", "#365B78")
        };

        SetBrush("PageBrush", palette.Page);
        SetBrush("PanelBrush", palette.Panel);
        SetBrush("PanelAltBrush", palette.PanelAlt);
        SetBrush("PanelHoverBrush", palette.PanelHover);
        SetBrush("InputBrush", palette.Input);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("BorderStrongBrush", palette.BorderStrong);
        SetBrush("TextBrush", palette.Text);
        SetBrush("MutedBrush", palette.Muted);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentDarkBrush", palette.AccentDark);
        SetBrush("GoodBrush", palette.Good);
        SetBrush("WarnBrush", palette.Warn);
        SetBrush("DangerBrush", palette.Danger);
        SetBrush("HoneycombLineBrush", palette.Honeycomb);

        ApplyDashboardDefaults(theme);

        // Native WPF control fallbacks.
        SetBrush(SystemColors.WindowBrushKey, palette.Input);
        SetBrush(SystemColors.WindowTextBrushKey, palette.Text);
        SetBrush(SystemColors.ControlBrushKey, palette.PanelAlt);
        SetBrush(SystemColors.ControlTextBrushKey, palette.Text);
        SetBrush(SystemColors.HighlightBrushKey, palette.AccentDark);
        SetBrush(SystemColors.HighlightTextBrushKey, "#FFFFFF");
        SetBrush(SystemColors.GrayTextBrushKey, palette.Muted);
    }

    public static void ApplyDashboardDefaults(AppTheme theme)
    {
        var defaults = GetDashboardDefaults(theme);
        ApplyDashboardColors(defaults);
    }

    public static void ApplyDashboard(AppSettings settings)
    {
        var defaults = GetDashboardDefaults(settings.Theme);

        var colors = settings.DashboardColorsFollowTheme
            ? defaults
            : new DashboardThemeColors(
                NormalizeOrDefault(settings.DashboardBackgroundColor, defaults.Background),
                NormalizeOrDefault(settings.DashboardPanelColor, defaults.Panel),
                NormalizeOrDefault(settings.DashboardRowColor, defaults.Row),
                NormalizeOrDefault(settings.DashboardAlternateRowColor, defaults.AlternateRow),
                NormalizeOrDefault(settings.DashboardTextColor, defaults.Text),
                NormalizeOrDefault(settings.DashboardPassingHighlightColor, defaults.PassingHighlight),
                NormalizeOrDefault(settings.DashboardFastestLapHighlightColor, defaults.FastestLapHighlight),
                NormalizeOrDefault(settings.DashboardNewBestLapHighlightColor, defaults.NewBestLapHighlight),
                NormalizeOrDefault(settings.DashboardCheckInGlowColor, defaults.CheckInGlow),
                NormalizeOrDefault(settings.DashboardMinimumLapGlowColor, defaults.MinimumLapGlow),
                NormalizeOrDefault(settings.DashboardPositionUpColor, defaults.PositionUp),
                NormalizeOrDefault(settings.DashboardPositionDownColor, defaults.PositionDown),
                NormalizeOrDefault(settings.DashboardPositionSameColor, defaults.PositionSame));

        if (!settings.DashboardAlternateRowsEnabled)
            colors = colors with { AlternateRow = colors.Row };

        ApplyDashboardColors(colors);
        if (Application.Current is not null)
        {
            SetBrush("DriverCrossingBrush", NormalizeOrDefault(settings.DriverCrossingColor, "#35C76F"));
            Application.Current.Resources["DriverCrossingThickness"] = settings.DriverCrossingStyle == "Underline" ? new Thickness(0, 0, 0, 3) : new Thickness(2);
            Application.Current.Resources["FinishedDriverStyle"] = settings.FinishedDriverStyle;
            ApplyHighlightBrush("FinishedDriverBrush", "FinishedDriverContrastBrush", NormalizeOrDefault(settings.FinishedDriverColor, "#808080"));
        }
    }

    public static void ApplyDashboardColors(DashboardThemeColors colors)
    {
        SetBrush("DashboardBackgroundBrush", colors.Background);
        SetBrush("DashboardPanelBrush", colors.Panel);
        SetBrush("DashboardRowBrush", colors.Row);
        SetBrush("DashboardAlternateRowBrush", colors.AlternateRow);
        SetBrush("DashboardTextBrush", colors.Text);

        ApplySoftPassingHighlight(
            colors.Row,
            colors.PassingHighlight);

        ApplyHighlightBrush(
            "DashboardFastestLapHighlightBrush",
            "DashboardFastestLapHighlightTextBrush",
            colors.FastestLapHighlight);

        ApplyHighlightBrush(
            "DashboardNewBestLapHighlightBrush",
            "DashboardNewBestLapHighlightTextBrush",
            colors.NewBestLapHighlight);

        ApplyCheckInGlow(colors.CheckInGlow);
        ApplyMinimumLapGlow(colors.MinimumLapGlow);

        ApplyHighlightBrush(
            "DashboardPositionUpBrush",
            "DashboardPositionUpTextBrush",
            colors.PositionUp);

        ApplyHighlightBrush(
            "DashboardPositionDownBrush",
            "DashboardPositionDownTextBrush",
            colors.PositionDown);

        ApplyHighlightBrush(
            "DashboardPositionSameBrush",
            "DashboardPositionSameTextBrush",
            colors.PositionSame);
    }

    public static bool DashboardColorsMatchDefaults(
        AppTheme theme,
        string? background,
        string? panel,
        string? row,
        string? alternateRow,
        string? text,
        string? passingHighlight,
        string? fastestLapHighlight,
        string? newBestLapHighlight,
        string? checkInGlow,
        string? minimumLapGlow,
        string? positionUp,
        string? positionDown,
        string? positionSame)
    {
        var defaults = GetDashboardDefaults(theme);
        return ColorEquals(background, defaults.Background) &&
               ColorEquals(panel, defaults.Panel) &&
               ColorEquals(row, defaults.Row) &&
               ColorEquals(alternateRow, defaults.AlternateRow) &&
               ColorEquals(text, defaults.Text) &&
               ColorEquals(passingHighlight, defaults.PassingHighlight) &&
               ColorEquals(fastestLapHighlight, defaults.FastestLapHighlight) &&
               ColorEquals(newBestLapHighlight, defaults.NewBestLapHighlight) &&
               ColorEquals(checkInGlow, defaults.CheckInGlow) &&
               ColorEquals(minimumLapGlow, defaults.MinimumLapGlow) &&
               ColorEquals(positionUp, defaults.PositionUp) &&
               ColorEquals(positionDown, defaults.PositionDown) &&
               ColorEquals(positionSame, defaults.PositionSame);
    }

    private static void ApplyCheckInGlow(string glowColor)
    {
        if (Application.Current is null || !TryParseColor(glowColor, out var color))
            return;

        Application.Current.Resources["DashboardCheckInGlowBrush"] = new SolidColorBrush(color);
        Application.Current.Resources["DashboardCheckInGlowEffect"] = new DropShadowEffect
        {
            Color = color,
            BlurRadius = 18,
            ShadowDepth = 0,
            Opacity = 0.95
        };
    }

    private static void ApplyMinimumLapGlow(string glowColor)
    {
        if (Application.Current is null || !TryParseColor(glowColor, out var color))
            return;

        Application.Current.Resources["DashboardMinimumLapGlowBrush"] = new SolidColorBrush(color);
        Application.Current.Resources["DashboardMinimumLapGlowEffect"] = new DropShadowEffect
        {
            Color = color,
            BlurRadius = 20,
            ShadowDepth = 0,
            Opacity = 0.95
        };
    }

    private static void ApplySoftPassingHighlight(string rowColor, string highlightColor)
    {
        if (Application.Current is null ||
            !TryParseColor(rowColor, out var row) ||
            !TryParseColor(highlightColor, out var highlight))
            return;

        // A solid blended color is softer than a hard replacement but remains
        // obvious on WPF DataGrid rows. Using opacity on the brush itself can
        // become nearly invisible depending on the underlying row/template.
        var blended = Blend(row, highlight, 0.48);
        Application.Current.Resources["DashboardPassingHighlightBrush"] = new SolidColorBrush(blended);
        Application.Current.Resources["DashboardPassingHighlightAccentBrush"] = new SolidColorBrush(highlight);
        Application.Current.Resources["DashboardPassingHighlightTextBrush"] =
            new SolidColorBrush(ContrastText(blended));
    }

    private static Color Blend(Color background, Color foreground, double foregroundAmount)
    {
        var amount = Math.Clamp(foregroundAmount, 0.0, 1.0);
        static byte Mix(byte a, byte b, double t) =>
            (byte)Math.Clamp((int)Math.Round(a + ((b - a) * t)), 0, 255);

        return Color.FromRgb(
            Mix(background.R, foreground.R, amount),
            Mix(background.G, foreground.G, amount),
            Mix(background.B, foreground.B, amount));
    }

    private static void ApplyHighlightBrush(
        string backgroundKey,
        string textKey,
        string value,
        double opacity = 1.0)
    {
        if (Application.Current is null || !TryParseColor(value, out var highlight))
            return;

        var background = new SolidColorBrush(highlight)
        {
            Opacity = Math.Clamp(opacity, 0.0, 1.0)
        };

        Application.Current.Resources[backgroundKey] = background;
        Application.Current.Resources[textKey] = new SolidColorBrush(ContrastText(highlight));
    }

    public static bool IsValidColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TryParseColor(value, out _);

    private static string NormalizeOrDefault(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && TryParseColor(value, out _) ? value.Trim() : fallback;

    private static bool ColorEquals(string? left, string right)
    {
        if (!TryParseColor(left, out var a) || !TryParseColor(right, out var b))
            return false;
        return a == b;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var converted = ColorConverter.ConvertFromString(value.Trim());
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // Validation caller decides how to surface an invalid color.
        }

        return false;
    }

    private static Color ContrastText(Color background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.62 ? Colors.Black : Colors.White;
    }

    private static void SetBrush(object key, string hex)
    {
        if (Application.Current is null)
            return;

        var converted = ColorConverter.ConvertFromString(hex);
        if (converted is Color color)
            Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
