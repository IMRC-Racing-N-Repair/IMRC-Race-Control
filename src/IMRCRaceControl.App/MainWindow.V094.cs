using System.Windows;

namespace IMRCRaceControl;

public partial class MainWindow
{
    public static readonly DependencyProperty PracticeGridColumnsProperty =
        DependencyProperty.Register(
            nameof(PracticeGridColumns),
            typeof(int),
            typeof(MainWindow),
            new PropertyMetadata(5));

    public static readonly DependencyProperty PracticeGridRowsProperty =
        DependencyProperty.Register(
            nameof(PracticeGridRows),
            typeof(int),
            typeof(MainWindow),
            new PropertyMetadata(2));

    public int PracticeGridColumns
    {
        get => (int)GetValue(PracticeGridColumnsProperty);
        set => SetValue(PracticeGridColumnsProperty, Math.Clamp(value, 4, 6));
    }

    public int PracticeGridRows
    {
        get => (int)GetValue(PracticeGridRowsProperty);
        set => SetValue(PracticeGridRowsProperty, Math.Max(2, value));
    }
}
