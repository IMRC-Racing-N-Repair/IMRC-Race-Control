using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMRCRaceControl.Core;
using IMRCRaceControl.Models;

namespace IMRCRaceControl.Dialogs;

internal static class UpdateAndLineupDialogs
{
    private static TextBlock Text(string value, double size = 14, bool bold = false, string brush = "TextBrush")
    {
        var text = new TextBlock { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty,brush);
        return text;
    }
    private static Window CreateWindow(Window owner,string title,double width,double height,out DockPanel root)
    {
        var window = new Window { Owner = owner, Title = title, Width = width, Height = height,
            MinWidth = 420, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        window.SetResourceReference(Control.BackgroundProperty,"PageBrush");
        window.SetResourceReference(Control.ForegroundProperty,"TextBrush");
        root = new DockPanel { Margin = new Thickness(18) };
        window.Content = root;
        return window;
    }
    private static void AddTop(DockPanel root,UIElement item) { DockPanel.SetDock(item,Dock.Top); root.Children.Add(item); }
    private static void AddBottom(DockPanel root,UIElement item) { DockPanel.SetDock(item,Dock.Bottom); root.Children.Add(item); }
    private static Border PanelBorder(UIElement content,string background)
    {
        var border = new Border { Child=content, Padding=new Thickness(10), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(5) };
        border.SetResourceReference(Border.BackgroundProperty,background);
        border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        return border;
    }
    private static StackPanel Actions() => new() { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };
    private static Button ActionButton(string label,bool primary=false)
    {
        var button=new Button { Content=label, MinWidth=150, Padding=new Thickness(14,8,14,8), FontSize=13 };
        if(primary) button.SetResourceReference(Control.BackgroundProperty,"AccentDarkBrush");
        return button;
    }

    internal static Window CreateConsecutiveWarning(Window owner,IReadOnlyList<ConsecutiveRaceDetector.Conflict> conflicts,
        IReadOnlyList<EventSessionDefinition>? currentOrder = null,
        IReadOnlyList<RaceOrderAssistant.Suggestion>? suggestions = null,
        Action<RaceOrderAssistant.Suggestion>? apply = null)
    {
        var window=CreateWindow(owner,"Consecutive race warning",860,490,out var root);
        var intro=new StackPanel { Margin=new Thickness(0,0,0,12) };
        intro.Children.Add(Text("BACK-TO-BACK RACES",21,true,"WarnBrush"));
        var count=conflicts.Select(c=>c.DriverId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        intro.Children.Add(Text($"{count} affected driver{(count==1?"":"s")} · {conflicts.Count} consecutive race conflict{(conflicts.Count==1?"":"s")}",14,true));
        intro.Children.Add(Text("Review the race pairs below. Change the lineup to give drivers more time, or continue with the current order.",13,false,"MutedBrush"));
        AddTop(root,intro);
        var actions=Actions();
        var change=ActionButton("CHANGE LINEUP",true); change.IsDefault=true; change.IsCancel=true;
        change.Click+=(_,_)=>window.DialogResult=false;
        var proceed=ActionButton("CONTINUE ANYWAY"); proceed.Click+=(_,_)=>window.DialogResult=true;
        if(currentOrder is not null && suggestions is not null && apply is not null)
        {
            var suggest=ActionButton("SUGGEST RACE ORDER");
            suggest.IsEnabled=suggestions.Count>0;
            suggest.ToolTip=suggestions.Count==0?"No better order found within locked races and race dependencies.":"Compare three alternative race orders.";
            suggest.Click+=(_,_) =>
            {
                var choice=ChooseOrder(window,currentOrder,suggestions,conflicts.Count);
                if(choice is null)return;
                apply(choice);
                window.DialogResult=false;
            };
            actions.Children.Add(suggest);
        }
        actions.Children.Add(change); actions.Children.Add(proceed); AddBottom(root,actions);
        var footer=Text(suggestions is {Count:0}
            ?"No better order found within race constraints. You can still edit manually or continue."
            :"Save checks the lineup again after each edit. Completed races remain locked.",12,false,"MutedBrush");
        footer.Margin=new Thickness(0,8,0,0); AddBottom(root,footer);
        var table=new DockPanel();
        AddTop(table,Row("DRIVER","FIRST RACE","NEXT RACE",true,0));
        var rows=new StackPanel();
        for(var i=0;i<conflicts.Count;i++)
        {
            var c=conflicts[i]; rows.Children.Add(Row(c.DriverName,c.FirstRace,c.SecondRace,false,i));
        }
        table.Children.Add(new ScrollViewer { Content=rows, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        root.Children.Add(PanelBorder(table,"PanelBrush"));
        return window;

        static Border Row(string driver,string first,string next,bool header,int i)
        {
            var grid=new Grid();
            foreach(var width in new[]{1.0,1.55,0.16,1.55})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width,GridUnitType.Star)});
            var values=new[]{driver,first,header?"":"→",next};
            for(var col=0;col<values.Length;col++)
            {
                var text=Text(values[col],header?11:13,header||col==0,header?"MutedBrush":col==2?"WarnBrush":"TextBrush");
                text.Margin=new Thickness(6,0,6,0); Grid.SetColumn(text,col); grid.Children.Add(text);
            }
            var border=new Border{Child=grid,Padding=new Thickness(2,8,2,8),BorderThickness=new Thickness(0,0,0,1)};
            border.SetResourceReference(Border.BackgroundProperty,header?"PanelAltBrush":i%2==0?"PanelBrush":"PanelAltBrush");
            border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
            return border;
        }
    }

    private static RaceOrderAssistant.Suggestion? ChooseOrder(Window owner,IReadOnlyList<EventSessionDefinition> current,
        IReadOnlyList<RaceOrderAssistant.Suggestion> suggestions,int originalConflicts)
    {
        var window=CreateWindow(owner,"Race Order Assistant",920,650,out var root);
        var heading=new StackPanel{Margin=new Thickness(0,0,0,10)};
        heading.Children.Add(Text("SUGGESTED RACE ORDER",21,true));
        heading.Children.Add(Text("Choose an alternative to preview. Driver rosters, combined races and race dependencies are preserved.",13,false,"MutedBrush"));
        AddTop(root,heading);
        var selector=new ComboBox{ItemsSource=suggestions.Select(s=>s.Name).ToList(),SelectedIndex=1,Margin=new Thickness(0,0,0,8)};
        AddTop(root,selector);
        var summary=Text("",14,true);summary.Margin=new Thickness(0,0,0,8);AddTop(root,summary);
        var actions=Actions();var cancel=ActionButton("CANCEL");cancel.IsCancel=true;
        var apply=ActionButton("APPLY TO PREVIEW",true);apply.IsDefault=true;
        RaceOrderAssistant.Suggestion? selected=null;
        cancel.Click+=(_,_)=>window.DialogResult=false;
        apply.Click+=(_,_)=>{selected=suggestions[selector.SelectedIndex];window.DialogResult=true;};
        actions.Children.Add(cancel);actions.Children.Add(apply);AddBottom(root,actions);
        var columns=new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new StackPanel{Margin=new Thickness(0,0,8,0)};
        var right=new StackPanel{Margin=new Thickness(8,0,0,0)};
        columns.Children.Add(left);Grid.SetColumn(right,1);columns.Children.Add(right);
        root.Children.Add(PanelBorder(new ScrollViewer{Content=columns,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},"PanelBrush"));
        void Fill(StackPanel panel,string title,IReadOnlyList<EventSessionDefinition> order)
        {
            panel.Children.Clear();panel.Children.Add(Text(title,15,true));
            var groups=order.GroupBy(s=>string.IsNullOrEmpty(s.CombinedRaceId)?"session:"+s.Id:"combined:"+s.CombinedRaceId).ToList();
            for(var i=0;i<groups.Count;i++)
            {
                var group=groups[i];var race=group.First();
                var row=Text($"{i+1}. Race {race.RaceNumber} · {string.Join(" + ",group.Select(s=>s.Name))}",13);
                row.Margin=new Thickness(4,5,4,5);
                panel.Children.Add(PanelBorder(row,i%2==0?"PanelAltBrush":"PanelBrush"));
            }
        }
        Fill(left,"CURRENT ORDER",current);
        void Refresh()
        {
            if(selector.SelectedIndex<0)return;
            var suggestion=suggestions[selector.SelectedIndex];
            summary.Text=$"{originalConflicts} → {suggestion.Score.Conflicts} conflicts · {suggestion.Score.AffectedDrivers} affected drivers · {suggestion.Score.MovedRaces} races change position";
            Fill(right,"SUGGESTED ORDER",suggestion.Order);
        }
        selector.SelectionChanged+=(_,_)=>Refresh();Refresh();
        window.ShowDialog();
        return selected;
    }

    internal static Window CreateWhatsNew(Window owner,Version current,Version? previous,IReadOnlyList<ReleaseChangeLog.Section> changes)
    {
        var window=CreateWindow(owner,"What's new in Race Control",760,560,out var root);
        var intro=new StackPanel { Margin=new Thickness(0,0,0,12) };
        intro.Children.Add(Text($"WHAT'S NEW · v{current.ToString(3)}",22,true));
        intro.Children.Add(Text(previous is null?"Recent release changes — previous installed version was not recorded."
            :$"Updated from v{previous.ToString(3)} to v{current.ToString(3)}",14,false,"MutedBrush"));
        AddTop(root,intro);
        var actions=Actions(); var done=ActionButton("GOT IT",true);done.IsDefault=true;done.Click+=(_,_)=>window.Close();
        actions.Children.Add(done);AddBottom(root,actions);
        var list=new StackPanel();
        foreach(var section in changes)
        {
            var header=Text($"VERSION {section.Version.ToString(3)}",15,true,"AccentBrush");
            header.Margin=new Thickness(0,8,0,5);list.Children.Add(header);
            foreach(var change in section.Changes)
            {
                var line=Text("•  "+change,13);
                line.Margin=new Thickness(0,2,4,5);list.Children.Add(line);
            }
        }
        if(changes.Count==0)list.Children.Add(Text("No release notes were recorded for this version.",13,false,"MutedBrush"));
        root.Children.Add(PanelBorder(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},"PanelBrush"));
        return window;
    }
}
