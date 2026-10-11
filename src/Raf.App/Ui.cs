using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Raf.App;

/// <summary>Görünümlerin ortak kullandığı küçük yardımcılar.</summary>
internal static class Ui
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Dictionary<string, Brush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Brush FallbackBrush = Frozen(Color.FromRgb(0x8A, 0x8F, 0x98));

    /// <summary>"#RRGGBB" metninden fırça üretir; geçersiz metinde gri döner.</summary>
    public static Brush BrushOf(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return FallbackBrush;
        if (Brushes.TryGetValue(hex, out var brush)) return brush;
        try
        {
            brush = Frozen((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            brush = FallbackBrush;
        }
        Brushes[hex] = brush;
        return brush;
    }

    /// <summary>Kategori simgesinin onaltılık kodunu ("E8B9") simge yazı tipindeki karaktere çevirir.</summary>
    public static string Glyph(string? hexCode)
    {
        if (!int.TryParse(hexCode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code) || code is < 0x20 or > 0xFFFF)
            code = int.Parse(Raf.Core.Defaults.DefaultIcon, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((char)code).ToString();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static string SizeText(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : value.ToString(value < 10 ? "0.#" : "0", Turkish) + " " + units[unit];
    }

    /// <summary>Disk boyutu: "38,5 GB", "148 GB", "1,82 TB".</summary>
    public static string DiskSizeText(long bytes) => Raf.Core.DiskUsage.SizeText(bytes, Turkish);

    public static string WhenText(DateTime time)
    {
        var today = DateTime.Today;
        if (time.Date == today) return "bugün " + time.ToString("HH:mm", Turkish);
        if (time.Date == today.AddDays(-1)) return "dün " + time.ToString("HH:mm", Turkish);
        return time.ToString(time.Year == today.Year ? "d MMMM HH:mm" : "d MMMM yyyy", Turkish);
    }

    /// <summary>Olayın çıktığı öğeden yukarı doğru çıkıp T türündeki ilk atayı bulur.</summary>
    public static T? Ancestor<T>(object? source) where T : DependencyObject
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    public static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Descendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
