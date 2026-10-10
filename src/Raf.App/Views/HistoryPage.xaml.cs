using System.Windows;
using System.Windows.Controls;
using Raf.Core;

namespace Raf.App.Views;

public partial class HistoryPage : UserControl
{
    private const int MaxRows = 200;

    public HistoryPage()
    {
        InitializeComponent();
    }

    private static Services S => App.S;

    public sealed class Row
    {
        public required MoveRecord Record { get; init; }
        public required string Name { get; init; }
        public required string Detail { get; init; }
        public required string When { get; init; }
        public required string Tip { get; init; }
        public Visibility UndoVisibility { get; init; }
        public Visibility UndoneVisibility { get; init; }
    }

    public void Refresh()
    {
        var inbox = S.Inbox;
        var rows = S.Sorter.HistorySnapshot().Take(MaxRows).Select(record =>
        {
            var fromDir = Path.GetDirectoryName(record.From) ?? "";
            var source = RafLayout.IsSame(fromDir, inbox) ? "Gelen" : Path.GetFileName(fromDir);
            return new Row
            {
                Record = record,
                Name = Path.GetFileName(record.To),
                Detail = $"{source}  →  {record.CategoryName}" + (record.Auto ? "" : "   ·   elle taşındı"),
                When = Ui.WhenText(record.Time),
                Tip = $"Eski yeri: {record.From}\nYeni yeri: {record.To}",
                UndoVisibility = record.Undone ? Visibility.Collapsed : Visibility.Visible,
                UndoneVisibility = record.Undone ? Visibility.Visible : Visibility.Collapsed,
            };
        }).ToList();

        Rows.ItemsSource = rows;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var record = ((Row)((FrameworkElement)sender).Tag).Record;
        var error = S.Sorter.Undo(record);
        if (error is not null) MessageDialog.Info(Window.GetWindow(this), "Geri alınamadı", error);
        Refresh();
    }
}
