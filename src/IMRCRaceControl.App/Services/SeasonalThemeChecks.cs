using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using IMRCRaceControl.Models;
namespace IMRCRaceControl.Services;
internal static class SeasonalThemeChecks
{
    internal static void Run(string output)
    {
        double Light(Color color)
        {
            double Channel(byte v){var s=v/255d;return s<=0.04045?s/12.92:Math.Pow((s+0.055)/1.055,2.4);}
            return 0.2126*Channel(color.R)+0.7152*Channel(color.G)+0.0722*Channel(color.B);
        }
        Color Parse(string hex)=>(Color)ColorConverter.ConvertFromString(hex)!;
        double Contrast(Color a,Color b){var x=Light(a);var y=Light(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
        if((int)AppTheme.Dark!=0 || (int)AppTheme.Light!=1 || (int)AppTheme.Blue!=2)throw new InvalidDataException("Existing theme settings changed identity.");
        foreach(var theme in new[]{AppTheme.Thanksgiving,AppTheme.Halloween,AppTheme.Christmas})
        {
            ThemeService.Apply(theme);
            Color Brush(string key)=>((SolidColorBrush)Application.Current.FindResource(key)).Color;
            foreach(var surface in new[]{"PageBrush","PanelBrush","PanelAltBrush","PanelHoverBrush","InputBrush"})
                foreach(var ink in new[]{"TextBrush","MutedBrush"})
                    if(Contrast(Brush(ink),Brush(surface))<4.5)throw new InvalidDataException($"{theme}: {ink} unreadable on {surface}.");
            if(Contrast(Colors.White,Brush("AccentBrush"))<4.5 || Contrast(Colors.White,Brush("AccentDarkBrush"))<4.5)throw new InvalidDataException($"{theme}: accent button contrast too low.");
            var defaults=ThemeService.GetDashboardDefaults(theme);
            if(defaults.Row==defaults.AlternateRow || Contrast(Parse(defaults.Text),Parse(defaults.Row))<4.5 || Contrast(Parse(defaults.Text),Parse(defaults.AlternateRow))<4.5)throw new InvalidDataException($"{theme}: dashboard rows unreadable.");
            var restored=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings {Theme=theme}))!;
            if(restored.Theme!=theme || theme.ToDisplay()!=theme.ToString())throw new InvalidDataException("Seasonal theme did not survive saving.");
            restored.DashboardColorsFollowTheme=false;restored.DashboardBackgroundColor="#123456";
            ThemeService.ApplyDashboard(restored);
            if(Brush("DashboardBackgroundBrush")!=Parse("#123456"))throw new InvalidDataException("Seasonal theme overwrote a custom dashboard color.");
        }
        File.WriteAllText(Path.Combine(output,"Seasonal-Theme-Checks.txt"),"PASS: holiday palettes; text and button contrast; alternate dashboard rows; existing theme identity; persistence; custom dashboard override.");
    }
}
