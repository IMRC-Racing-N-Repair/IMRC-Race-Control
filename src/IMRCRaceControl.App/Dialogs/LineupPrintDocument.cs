using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;

internal static class LineupPrintDocument
{
    // Measure using the real printer's available size. Every physical sheet repeats
    // the round heading; a race that spans pages repeats its race and column headings.
    public static FixedDocument Build(RaceReport report, double width, double height)
    {
        if (width < 240 || height < 320)
            throw new InvalidOperationException("Choose a larger paper size for race lineups.");
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(width,height);
        var contentWidth = width-64;
        var contentHeight = height-52;
        var printedAt = DateTime.Now.ToString("g");
        StackPanel panel = new();
        double used = 0;
        string currentRound = "";

        TextBlock Text(string value, double size, bool bold = false, bool centered = false) =>
            ReportTextStyling.Block(value, report, size, bold, centered);
        double Measure(FrameworkElement element)
        {
            element.Measure(new Size(contentWidth,double.PositiveInfinity));
            return element.DesiredSize.Height;
        }
        void Add(FrameworkElement element) { used+=Measure(element); panel.Children.Add(element); }
        void NewPage(string round)
        {
            currentRound=round;
            var page=new FixedPage { Width=width, Height=height, Background=Brushes.White };
            panel=new StackPanel { Width=contentWidth }; used=0;
            FixedPage.SetLeft(panel,32); FixedPage.SetTop(panel,24);page.Children.Add(panel);
            Add(PrintBranding.Logo());
            var title=Text(round,26,true,true);title.Margin=new Thickness(0,0,0,7);Add(title);
            var subtitle=Text(report.Subtitle+" • Printed "+printedAt,10,false,true);
            subtitle.Margin=new Thickness(0,0,0,12);Add(subtitle);
            var footer=Text($"Page {document.Pages.Count+1}",9);
            FixedPage.SetLeft(footer,32);FixedPage.SetTop(footer,height-22);page.Children.Add(footer);
            document.Pages.Add(new PageContent { Child=page });
        }
        Border TableRow(string[] values, bool heading, string color, bool alternate = false)
        {
            var grid=new Grid();
            var weights=new[] {0.5,2.1,1.45,0.55};
            for(var i=0;i<values.Length;i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(i<weights.Length ? weights[i] : 1,GridUnitType.Star) });
                var text=Text(values[i],12,heading);text.Margin=new Thickness(5,4,5,4);
                Grid.SetColumn(text,i);grid.Children.Add(text);
            }
            return new Border { Child=grid, Background=heading ? PrintBranding.Color(color) : alternate ? PrintBranding.Stripe(color) : Brushes.White,
                BorderBrush=Brushes.LightGray, BorderThickness=new Thickness(0,0,0,0.5) };
        }
        FrameworkElement RaceTitle(ReportSection section,bool continued)
        {
            var grid = new Grid { Margin = new Thickness(0,12,0,6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (section.RaceNumber > 0)
            {
                var number = Text($"RACE\n#{section.RaceNumber}",18,true,true);
                number.Margin = new Thickness(8,4,8,4);
                grid.Children.Add(new Border { Child = number, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0,0,8,0) });
            }
            var title = section.Title;
            var prefix = $"RACE #{section.RaceNumber} - ";
            if (title.StartsWith(prefix)) title = title[prefix.Length..];
            var text = Text(title+(continued ? " (continued)" : ""),20,true);
            text.Margin = new Thickness(8); text.VerticalAlignment = VerticalAlignment.Center;
            var band = new Border { Child = text, Background = PrintBranding.Color(section.ClassColor) };
            Grid.SetColumn(band,1); grid.Children.Add(band); return grid;
        }
        foreach(var section in report.Sections)
        {
            if(document.Pages.Count == 0 || currentRound!=section.RoundHeading) NewPage(section.RoundHeading);
            var title=RaceTitle(section,false);
            var columns=TableRow(section.Columns,true,section.ClassColor);
            var firstRow=section.Rows.Count>0 ? Measure(TableRow(section.Rows[0],false,section.ClassColor)) : 0;
            if(used+Measure(title)+Measure(columns)+firstRow>contentHeight) NewPage(section.RoundHeading);
            Add(title);Add(columns);
            var rowIndex = 0;
            foreach(var values in section.Rows)
            {
                var row=TableRow(values,false,section.ClassColor,rowIndex++ % 2 == 1);
                if(used+Measure(row)>contentHeight)
                {
                    NewPage(section.RoundHeading);
                    Add(RaceTitle(section,true));Add(TableRow(section.Columns,true,section.ClassColor));
                }
                if(used+Measure(row)>contentHeight)
                    throw new InvalidOperationException("A lineup row is too tall for this paper size. Choose a larger page or shorten the driver name.");
                Add(row);
            }
        }
        return document;
    }
}
