using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using IMRCRaceControl.Core;
namespace IMRCRaceControl.Dialogs;
public sealed class ReportPrintDialog : Window
{
    private readonly RaceReport _report;
    public ReportPrintDialog(RaceReport report)
    {
        _report=report; Title="Print preview — "+report.Title; Width=950; Height=750; MinWidth=650; MinHeight=450; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel(); Content=root;
        var toolbar=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(8) }; DockPanel.SetDock(toolbar,Dock.Top); root.Children.Add(toolbar);
        var print=new Button { Content="PRINT / SAVE AS PDF" }; toolbar.Children.Add(print); print.Click+=Print_Click;
        toolbar.Children.Add(new TextBlock { Text="Choose Microsoft Print to PDF in the printer list to save a PDF.", VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(10,0,0,0) });
        if (report.IsLineup && report.Sections.Count > 0)
        {
            var viewer = new DocumentViewer { Document = LineupPrintDocument.Build(report,816,1056), Background = Brushes.Gray };
            viewer.CommandBindings.Add(new CommandBinding(ApplicationCommands.Print, (_,e) => { e.Handled=true; Print_Click(this,new RoutedEventArgs()); }));
            root.Children.Add(viewer);
        }
        else root.Children.Add(new FlowDocumentReader { Document=BuildDocument(816,1056), ViewingMode=FlowDocumentReaderViewingMode.Page, IsFindEnabled=true, IsPrintEnabled=false, Background=Brushes.Gray });
    }
    private FlowDocument BuildDocument(double width,double height)
    {
        var doc=new FlowDocument { PageWidth=width, PageHeight=height, ColumnWidth=width, PagePadding=new Thickness(32), FontFamily=new FontFamily("Segoe UI"), FontSize=11, Foreground=Brushes.Black, Background=Brushes.White };
        doc.Blocks.Add(new BlockUIContainer(PrintBranding.Logo()));
        doc.Blocks.Add(new Paragraph(new Run(_report.Title)) { FontSize=22, FontWeight=FontWeights.Bold });
        doc.Blocks.Add(new Paragraph(new Run(_report.Subtitle+"\nPrinted "+DateTime.Now.ToString("g"))) { FontSize=10 });
        if (_report.Sections.Count==0) doc.Blocks.Add(new Paragraph(new Run(_report.EmptyMessage)));
        foreach (var section in _report.Sections)
        {
            doc.Blocks.Add(new Paragraph(new Run(section.Title)) { FontSize=15, FontWeight=FontWeights.Bold, KeepWithNext=true, Background=PrintBranding.Color(section.ClassColor), Padding=new Thickness(6), Margin=new Thickness(0,16,0,5) });
            if (section.Notes.Length>0) { var notes = ReportTextStyling.Paragraph(section.Notes,_report); notes.FontSize=10; notes.KeepWithNext=true; doc.Blocks.Add(notes); }
            // Small table batches repeat column headers on long lap/standing reports.
            foreach (var batch in section.Rows.Chunk(18).DefaultIfEmpty([]))
            {
                var table=new Table { CellSpacing=0, Margin=new Thickness(0,0,0,8) };
                var group=new TableRowGroup(); table.RowGroups.Add(group);
                var rowIndex = 0;
                TableRow Row(string[] values,bool header)
                {
                    var row=new TableRow();
                    var fill = header ? PrintBranding.Color(section.ClassColor) : rowIndex++ % 2 == 1 ? PrintBranding.Stripe(section.ClassColor) : Brushes.White;
                    foreach (var value in values) { var text = ReportTextStyling.Paragraph(value,_report); text.Margin=new Thickness(0); text.KeepTogether=true; row.Cells.Add(new TableCell(text) { Padding=new Thickness(4), BorderBrush=Brushes.LightGray, BorderThickness=new Thickness(0,0,0,0.5), FontWeight=header ? FontWeights.Bold : FontWeights.Normal, Background=fill }); }
                    return row;
                }
                group.Rows.Add(Row(section.Columns,true));
                foreach (var values in batch) group.Rows.Add(Row(values,false));
                doc.Blocks.Add(table);
            }
        }
        return doc;
    }
    private void Print_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var dialog=new PrintDialog();
            if (dialog.ShowDialog()!=true) return;
            IDocumentPaginatorSource doc = _report.IsLineup && _report.Sections.Count > 0
                ? LineupPrintDocument.Build(_report,dialog.PrintableAreaWidth,dialog.PrintableAreaHeight)
                : BuildDocument(dialog.PrintableAreaWidth,dialog.PrintableAreaHeight);
            dialog.PrintDocument(doc.DocumentPaginator,_report.Title);
        }
        catch (Exception ex) { MessageBox.Show(this,"Printing could not complete: "+ex.Message,"Print",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
}
