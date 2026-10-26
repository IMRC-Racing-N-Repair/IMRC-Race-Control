using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;
internal static class LineupPrintDocument
{
    public static FixedDocument Build(RaceReport report, double width, double height)
    {
        if (width < 480 || height < 400) throw new InvalidOperationException("Choose a larger paper size for race lineups.");
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(width,height);
        var contentWidth = width-56;
        var contentHeight = height-84;
        var printedAt = DateTime.Now.ToString("g");
        StackPanel panel = new(); double used = 0; string currentRound = "";
        var rule = new SolidColorBrush(Color.FromRgb(185,190,197));
        TextBlock Text(string value, double size, bool bold = false, bool centered = false) => ReportTextStyling.Block(value,report,size,bold,centered);
        double Measure(FrameworkElement element) { element.Measure(new Size(contentWidth,double.PositiveInfinity)); return element.DesiredSize.Height; }
        void Add(FrameworkElement element) { used += Measure(element); panel.Children.Add(element); }
        void NewPage(string round)
        {
            currentRound=round;
            var page=new FixedPage { Width=width, Height=height, Background=Brushes.White };
            panel=new StackPanel { Width=contentWidth }; used=0;
            FixedPage.SetLeft(panel,28); FixedPage.SetTop(panel,24);page.Children.Add(panel);
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(180) }); header.ColumnDefinitions.Add(new ColumnDefinition());
            header.Children.Add(PrintBranding.Logo());
            var titles = new StackPanel { VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(12,0,0,8) };
            var eyebrow=Text("RACE LINEUP",9,true); eyebrow.TextAlignment=TextAlignment.Right; titles.Children.Add(eyebrow);
            var title=Text(round,24,true); title.TextAlignment=TextAlignment.Right; titles.Children.Add(title);
            Grid.SetColumn(titles,1); header.Children.Add(titles); Add(header);
            var subtitle=Text(report.Subtitle,10); subtitle.Margin=new Thickness(0,0,0,9); Add(subtitle);
            Add(new Border { Height=2, Background=Brushes.Black, Margin=new Thickness(0,0,0,3) });
            var legend=Text("Best qual: best completed laps/time; (round). Last round: prior run for mains.   SOF/SR: performance / safety.   * Starting ratings.",8);
            FixedPage.SetLeft(legend,28);FixedPage.SetTop(legend,height-43);page.Children.Add(legend);
            var footer=new Grid { Width=contentWidth };
            footer.Children.Add(Text("Printed "+printedAt,8));
            var pageNumber=Text($"Page {document.Pages.Count+1}",8);pageNumber.HorizontalAlignment=HorizontalAlignment.Right;footer.Children.Add(pageNumber);
            FixedPage.SetLeft(footer,28);FixedPage.SetTop(footer,height-27);page.Children.Add(footer);
            document.Pages.Add(new PageContent { Child=page });
        }
        Border TableRow(string[] values,bool heading,bool alternate=false)
        {
            var grid=new Grid(); var weights=new[] {.42,2.2,1.2,1.7,.95};
            for(var i=0;i<values.Length;i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(i<weights.Length ? weights[i] : 1,GridUnitType.Star) });
                var text=Text(values[i],heading ? 10 : 11.5,heading);
                text.Margin=new Thickness(6,5,6,5); text.VerticalAlignment=VerticalAlignment.Center;
                if(i==0) text.TextAlignment=TextAlignment.Center;
                if(i==values.Length-1) text.TextAlignment=TextAlignment.Right;
                Grid.SetColumn(text,i);grid.Children.Add(text);
            }
            return new Border { Child=grid, Background=heading ? Brushes.WhiteSmoke : alternate ? PrintBranding.Stripe("") : Brushes.White,
                BorderBrush=rule, BorderThickness=new Thickness(0,0,0,heading ? 1 : .5) };
        }
        FrameworkElement RaceTitle(ReportSection section,bool continued)
        {
            var grid=new Grid { Margin=new Thickness(0,12,0,6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });grid.ColumnDefinitions.Add(new ColumnDefinition());
            var badge=new StackPanel { Margin=new Thickness(6,3,6,3) };
            badge.Children.Add(Text("RACE",8,true,true));badge.Children.Add(Text(section.RaceNumber.ToString(),25,true,true));
            grid.Children.Add(new Border { Child=badge, Width=48, Background=PrintBranding.Color(section.ClassColor), CornerRadius=new CornerRadius(3), Margin=new Thickness(0,0,10,0) });
            var title=section.Title; var prefix=$"RACE #{section.RaceNumber} - ";if(title.StartsWith(prefix))title=title[prefix.Length..];
            var label=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            label.Children.Add(Text(title+(continued ? " (continued)" : ""),18,true));
            var detail=Text($"{section.Rows.Count} drivers • {section.RoundHeading}",9);detail.Margin=new Thickness(0,3,0,0);label.Children.Add(detail);
            Grid.SetColumn(label,1);grid.Children.Add(label);return grid;
        }
        foreach(var section in report.Sections)
        {
            if(document.Pages.Count==0 || currentRound!=section.RoundHeading)NewPage(section.RoundHeading);
            var title=RaceTitle(section,false);var columns=TableRow(section.Columns,true);
            var firstRow=section.Rows.Count>0 ? Measure(TableRow(section.Rows[0],false)) : 0;
            if(used+Measure(title)+Measure(columns)+firstRow>contentHeight)NewPage(section.RoundHeading);
            Add(title);Add(columns);var index=0;
            foreach(var values in section.Rows)
            {
                var row=TableRow(values,false,index++ % 2==1);
                if(used+Measure(row)>contentHeight) { NewPage(section.RoundHeading);Add(RaceTitle(section,true));Add(TableRow(section.Columns,true)); }
                if(used+Measure(row)>contentHeight)throw new InvalidOperationException("A lineup row is too tall for this paper size. Choose a larger page or shorten the driver name.");
                Add(row);
            }
        }
        return document;
    }
}
