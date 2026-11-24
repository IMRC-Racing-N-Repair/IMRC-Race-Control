using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMRCRaceControl.Core;
using IMRCRaceControl.Dialogs;
using IMRCRaceControl.Models;
using IMRCRaceControl.Services;

namespace IMRCRaceControl;

public partial class MainWindow
{
    private static void CheckThemedReleaseDialogs(string output)
    {
        var owner=new Window { Width=1000,Height=700,ShowInTaskbar=false };
        owner.Show();
        try
        {
            var current=ReleaseNotesService.CurrentVersion;
            var notes=ReleaseNotesService.GetChanges(current,new Version(0,17,16));
            if(notes.Count<2 || notes[0].Version!=current)throw new InvalidDataException("Bundled cumulative release notes missing.");
            foreach(var theme in new[]{AppTheme.Light,AppTheme.Thanksgiving,AppTheme.Dark})
            {
                ThemeService.Apply(theme);
                var conflicts=new[]{new ConsecutiveRaceDetector.Conflict("a","Driver A","Race 1: Stock Q1","Race 2: Grom Q1"),
                    new ConsecutiveRaceDetector.Conflict("b","Driver B","Race 2: Grom Q1","Race 3: Touring Q1")};
                var warning=UpdateAndLineupDialogs.CreateConsecutiveWarning(owner,conflicts);
                var release=UpdateAndLineupDialogs.CreateWhatsNew(owner,current,new Version(0,17,16),notes);
                foreach(var window in new[]{warning,release})
                {
                    try
                    {
                        window.Show();window.UpdateLayout();
                        var text=Visuals(window).OfType<TextBlock>().ToList();
                        var expected=(SolidColorBrush)window.FindResource("TextBrush");
                        var sample=text.FirstOrDefault(t=>window==warning?t.Text=="Driver A":t.Text.StartsWith("•  "));
                        if(sample?.Foreground is not SolidColorBrush actual || actual.Color!=expected.Color)
                            throw new InvalidDataException("Dialog content does not use active theme text.");
                        if(!Visuals(window).OfType<ScrollViewer>().Any())throw new InvalidDataException("Dialog lacks scrollable details.");
                    }
                    finally { window.Close(); }
                }
            }
            File.WriteAllText(Path.Combine(output,"Release-Dialog-Checks.txt"),"PASS: bundled version range and themed warning/update dialogs in Light, Thanksgiving and Dark.");
        }
        finally { owner.Close(); }
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i);yield return child;
                foreach(var nested in Visuals(child))yield return nested;
            }
        }
    }
}
