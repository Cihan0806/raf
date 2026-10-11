using System.Globalization;

namespace Raf.Core;

/// <summary>Bir diskin doluluk durumu (bayt olarak boş ve toplam alan).</summary>
public readonly record struct DiskUsage(long Free, long Total)
{
    private const double Gigabyte = 1024d * 1024 * 1024;

    public long Used => Math.Max(0, Total - Free);

    /// <summary>Dolu kısmın oranı, 0 ile 1 arasında.</summary>
    public double UsedFraction => Total <= 0 ? 0 : Math.Clamp((double)Used / Total, 0, 1);

    /// <summary>Boş yer diskin onda birinden azsa true.</summary>
    public bool IsLow => Total > 0 && Free < Total / 10;

    /// <summary>Yolun durduğu diskin doluluğu; disk okunamıyorsa (ağ yolu, çıkarılmış sürücü) null.</summary>
    public static DiskUsage? Of(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            return drive.IsReady ? new DiskUsage(drive.TotalFreeSpace, drive.TotalSize) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Disk boyutunu kısa ve okunaklı yazar: "512 MB", "38,5 GB", "148 GB", "1,82 TB".</summary>
    public static string SizeText(long bytes, CultureInfo culture)
    {
        double gigabytes = bytes / Gigabyte;
        if (gigabytes < 1) return (gigabytes * 1024).ToString("0", culture) + " MB";
        if (gigabytes < 100) return gigabytes.ToString("0.#", culture) + " GB";
        if (gigabytes < 1024) return gigabytes.ToString("0", culture) + " GB";
        return (gigabytes / 1024).ToString("0.##", culture) + " TB";
    }
}
