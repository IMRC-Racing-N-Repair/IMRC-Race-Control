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
        if(width<480 || height<400) throw new InvalidOperationException("Choose a larger paper size for lap reports.");
        var document=new FixedDocument(); document.DocumentPaginator.PageSize=new Size(width,height);
        var available=width-56;
        TextBlock Text(string value,double size,bool bold=false) => new() { Text=value,FontFamily=new FontFamily("Segoe UI"),FontSize=size,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,Foreground=Brushes.Black,TextWrapping=TextWrapping.Wrap };
        foreach(var section in report.Sections)
        {
            var font=section.Columns.Length<=5?16:12;
            Grid Row(string[] cells,bool header,int index)
            {
                var grid=new Grid { Width=available };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(42) });
                for(var i=1;i<cells.Length;i++)grid.ColumnDefinitions.Add(new ColumnDefinition());
                for(var i=0;i<cells.Length;i++)
                {
                    var text=Text(cells[i],font,header);text.TextAlignment=TextAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;
                    var border=new Border { Child=text,Padding=new Thickness(3,5,3,5),MinHeight=28,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(i==0?1:0,header?1:0,1,1),Background=header?Brushes.LightGray:index%2==1?new SolidColorBrush(Color.FromRgb(229,232,236)):Brushes.White };
                    Grid.SetColumn(border,i);grid.Children.Add(border);
                }
                return grid;
            }
            int offset=0;
            do
            {
                var page=new FixedPage { Width=width,Height=height,Background=Brushes.White };
                var body=new StackPanel { Width=available };FixedPage.SetLeft(body,28);FixedPage.SetTop(body,24);page.Children.Add(body);
                double used=0;
                double Measure(FrameworkElement e) {e.Measure(new Size(available,double.PositiveInfinity));return e.DesiredSize.Height;}
                void Add(FrameworkElement e) {used+=Measure(e);body.Children.Add(e);}
                var heading=new Grid();heading.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(170)});heading.ColumnDefinitions.Add(new ColumnDefinition());
                heading.Children.Add(PrintBranding.Logo());var title=Text(report.Title,22,true);title.HorizontalAlignment=HorizontalAlignment.Right;title.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);heading.Children.Add(title);Add(heading);
                var subtitle=Text(report.Subtitle,10);subtitle.Margin=new Thickness(0,5,0,12);Add(subtitle);
                Add(Row(section.Columns,true,0));
                if(section.Rows.Count==0)Add(Text(section.Notes,12));
                var pageStart=offset;
                while(offset<section.Rows.Count)
                {
                    var row=Row(section.Rows[offset],false,offset);
                    if(used+Measure(row)>height-78)break;
                    Add(row);offset++;
                }
                var footer=Text($"Page {document.Pages.Count+1} • Lap times in seconds • — No recorded lap",9);FixedPage.SetLeft(footer,28);FixedPage.SetTop(footer,height-30);page.Children.Add(footer);
                document.Pages.Add(new PageContent {Child=page});
                if(section.Rows.Count>0 && offset==pageStart)throw new InvalidOperationException("Driver names do not fit this paper size.");
            } while(offset<section.Rows.Count);
        }
        return document;
    }
}
