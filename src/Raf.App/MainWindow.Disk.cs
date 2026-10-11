using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Raf.Core;

namespace Raf.App;

// Üst şeritteki disk çubuğu: Raf klasörünün durduğu diskte ne kadar yer kaldığını gösterir.
public partial class MainWindow
{
    private const double DiskTextPadding = 20;

    private DispatcherTimer? _diskTimer;
    private string _diskFull = "";
    private string _diskShort = "";

    private void InitDiskBar()
    {
        DiskHost.SizeChanged += (_, _) => FitDiskText();
        // Dolu kısım düz bir dikdörtgendir; çubuğun yuvarlak köşelerinden taşmasın diye kırpılır.
        DiskInner.SizeChanged += (_, _) =>
            DiskInner.Clip = new RectangleGeometry(new Rect(0, 0, DiskInner.ActualWidth, DiskInner.ActualHeight), 7, 7);

        IsVisibleChanged += (_, _) => { if (IsVisible) UpdateDisk(); };
        _diskTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _diskTimer.Tick += (_, _) => { if (IsVisible) UpdateDisk(); };
        _diskTimer.Start();
    }

    private void UpdateDisk()
    {
        string root;
        lock (_s.Config) root = _s.Config.Root;

        if (DiskUsage.Of(root) is not { Total: > 0 } disk)
        {
            _diskFull = "";
            DiskBar.Visibility = Visibility.Collapsed;
            return;
        }

        _diskShort = $"{Ui.DiskSizeText(disk.Free)} boş";
        _diskFull = $"{_diskShort}  ·  {Ui.DiskSizeText(disk.Used)} dolu";
        DiskUsedColumn.Width = new GridLength(disk.UsedFraction, GridUnitType.Star);
        DiskFreeColumn.Width = new GridLength(1 - disk.UsedFraction, GridUnitType.Star);
        DiskFill.SetResourceReference(Border.BackgroundProperty, disk.IsLow ? "DangerBrush" : "AccentBrush");
        FitDiskText();
    }

    /// <summary>Pencere daraldıkça önce "dolu" kısmı atılır; o da sığmazsa çubuk gizlenir.</summary>
    private void FitDiskText()
    {
        if (_diskFull.Length == 0) return;

        double room = Math.Min(DiskHost.ActualWidth - DiskBar.Margin.Left - DiskBar.Margin.Right, DiskBar.MaxWidth) - DiskTextPadding;
        string? text = Fits(_diskFull, room) ? _diskFull : Fits(_diskShort, room) ? _diskShort : null;

        DiskBar.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        if (text is not null) DiskText.Text = text;
    }

    private bool Fits(string text, double room)
    {
        var typeface = new Typeface(DiskText.FontFamily, DiskText.FontStyle, DiskText.FontWeight, DiskText.FontStretch);
        var measured = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            DiskText.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return measured.Width <= room;
    }
}
