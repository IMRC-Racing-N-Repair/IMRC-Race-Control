using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using IMRCRaceControl.Core;

namespace IMRCRaceControl.Dialogs;
internal static class LapMatrixPrintDocument
{
    public static FixedDocument Build(RaceReport report, double width, double height)
    {
        if(width<480 || height<400) throw new InvalidOperationException("Choose a larger paper size for race reports.");
        var document=new FixedDocument(); document.DocumentPaginator.PageSize=new Size(width,height);
        var available=width-56;
        var ink=new SolidColorBrush(Color.FromRgb(36,42,50));
        var stripe=new SolidColorBrush(Color.FromRgb(229,232,236));
        StackPanel body=null!;
        double used=0;
        TextBlock Text(string value,double size,bool bold=false) => new() { Text=value,FontFamily=new FontFamily("Segoe UI"),FontSize=size,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,Foreground=Brushes.Black,TextWrapping=TextWrapping.Wrap };
        double Measure(FrameworkElement element) { element.Measure(new Size(available,double.PositiveInfinity));return element.DesiredSize.Height; }
        void Add(FrameworkElement element) {used+=Measure(element);body.Children.Add(element);}
        bool Fits(double size) => used+size<=height-76;
        void NewPage()
        {
            var page=new FixedPage {Width=width,Height=height,Background=Brushes.White};
            body=new StackPanel {Width=available};FixedPage.SetLeft(body,28);FixedPage.SetTop(body,24);page.Children.Add(body);used=0;
            var heading=new Grid();heading.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(150)});heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.Children.Add(PrintBranding.Logo());
            var titles=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};
            var section=report.Sections.FirstOrDefault();
            var title=Text(section?.Title ?? report.Title,24,true);title.TextAlignment=TextAlignment.Right;titles.Children.Add(title);
            var round=Text(section?.RoundHeading ?? "",14,true);round.TextAlignment=TextAlignment.Right;titles.Children.Add(round);
            Grid.SetColumn(titles,1);heading.Children.Add(titles);Add(heading);
            var subtitle=Text(report.Subtitle,10);subtitle.Margin=new Thickness(0,4,0,10);Add(subtitle);
            var footer=Text($"Page {document.Pages.Count+1} • Cell: position / lap time; pace below • ** Breakout, not counted • — Unavailable",9);
            FixedPage.SetLeft(footer,28);FixedPage.SetTop(footer,height-38);footer.Width=available;page.Children.Add(footer);
            document.Pages.Add(new PageContent {Child=page});
        }
        FrameworkElement Caption(string caption) => new Border {Background=ink,Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,8,0,0),Child=new TextBlock {Text=caption,Foreground=Brushes.White,FontSize=11,FontWeight=FontWeights.Bold}};
        Grid Row(string[] cells,bool header,int index,bool matrix,int sectionIndex=0)
        {
            var font=matrix?(cells.Length<=5?13:11):10;
            var grid=new Grid {Width=available};
            for(var i=0;i<cells.Length;i++)
            {
                var columnWidth=matrix ? (i==0?new GridLength(32):new GridLength(1,GridUnitType.Star))
                    :new GridLength((cells.Length>=10?i==2:i==1)?2.7:1,GridUnitType.Star);
                grid.ColumnDefinitions.Add(new ColumnDefinition {Width=columnWidth});
            }
            for(var i=0;i<cells.Length;i++)
            {
                var text=Text(cells[i],font,header);text.TextAlignment=TextAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;
                FrameworkElement content=text;
                if(matrix && !header && i>0)
                {
                    report.LapDetails.TryGetValue((sectionIndex,int.Parse(cells[0]),i),out var detail);
                    var stack=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
                    var parts=cells[i].Split('\n');
                    if(parts.Length>1)
                    {
                        var rejected=Text(parts[0],9);rejected.TextAlignment=TextAlignment.Center;stack.Children.Add(rejected);
                        stack.Children.Add(new Border {Height=1,Background=Brushes.Gray,Margin=new Thickness(8,1,8,1)});
                    }
                    var lapText=Text(detail is null?parts[^1]:$"{detail.Position}/{parts[^1]}",font,detail?.BestLap==true);
                    lapText.TextAlignment=TextAlignment.Center;stack.Children.Add(lapText);
                    if(detail is not null)
                    {
                        var pace=Text(detail.Pace,Math.Max(9,font-2));pace.TextAlignment=TextAlignment.Center;stack.Children.Add(pace);
                    }
                    content=stack;
                }
                var border=new Border {Child=content,Padding=new Thickness(3,matrix?3:5,3,matrix?3:5),MinHeight=24,
                    BorderBrush=new SolidColorBrush(Color.FromRgb(160,166,174)),BorderThickness=new Thickness(i==0?1:0,header?1:0,1,1),
                    Background=header?new SolidColorBrush(Color.FromRgb(208,213,219)):index%2==1?stripe:Brushes.White};
                Grid.SetColumn(border,i);grid.Children.Add(border);
            }
            return grid;
        }
        void Table(ReportSection section,bool matrix,int sectionIndex=0)
        {
            FrameworkElement Header()=>Row(section.Columns,true,0,matrix,sectionIndex);
            var caption=matrix?"LAP ANALYSIS • POSITION / LAP TIME • PACE":section.Title;
            void StartTable(bool continued)
            {
                Add(Caption(caption+(continued?" • CONTINUED":"")));Add(Header());
            }
            var first=section.Rows.Count>0?Measure(Row(section.Rows[0],false,0,matrix,sectionIndex)):30;
            if(!Fits(Measure(Caption(caption))+Measure(Header())+first))NewPage();
            StartTable(false);
            if(section.Rows.Count==0) {Add(Text(section.Notes,11));return;}
            for(var index=0;index<section.Rows.Count;index++)
            {
                var row=Row(section.Rows[index],false,index,matrix,sectionIndex);
                if(!Fits(Measure(row))) {NewPage();StartTable(true);}
                if(!Fits(Measure(row)))throw new InvalidOperationException("Report row does not fit this paper size. Choose larger paper.");
                Add(row);
            }
        }
        NewPage();
        if(report.ResultsSummary is not null)Table(report.ResultsSummary,false);
        for(var i=0;i<report.Sections.Count;i++)Table(report.Sections[i],true,i);
        if(report.PriorQualifying is not null)Table(report.PriorQualifying,false);
        return document;
    }
}
