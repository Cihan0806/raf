using System.Globalization;
using Raf.Core;

namespace Raf.Tests;

public class DiskUsageTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [Theory]
    [InlineData(512L * 1024 * 1024, "512 MB")]
    [InlineData(Gb, "1 GB")]
    [InlineData(38 * Gb + Gb / 2, "38,5 GB")]
    [InlineData(64 * Gb, "64 GB")]
    [InlineData(148 * Gb + Gb / 4, "148 GB")]
    [InlineData(931 * Gb, "931 GB")]
    [InlineData(1024 * Gb, "1 TB")]
    [InlineData(1863 * Gb, "1,82 TB")]
    [InlineData(0L, "0 MB")]
    public void Boyut_kisa_ve_okunakli_yazilir(long bytes, string expected)
    {
        Assert.Equal(expected, DiskUsage.SizeText(bytes, Turkish));
    }

    [Fact]
    public void Dolu_alan_ve_oran_bos_alandan_hesaplanir()
    {
        var disk = new DiskUsage(Free: 150 * Gb, Total: 500 * Gb);

        Assert.Equal(350 * Gb, disk.Used);
        Assert.Equal(0.7, disk.UsedFraction, 3);
        Assert.False(disk.IsLow);
    }

    [Fact]
    public void Bos_yer_onda_birin_altina_inince_az_sayilir()
    {
        Assert.True(new DiskUsage(Free: 40 * Gb, Total: 500 * Gb).IsLow);
        Assert.False(new DiskUsage(Free: 50 * Gb, Total: 500 * Gb).IsLow);
    }

    [Fact]
    public void Tutarsiz_degerler_orani_sinirlarin_disina_cikarmaz()
    {
        Assert.Equal(0, new DiskUsage(Free: 0, Total: 0).UsedFraction);
        Assert.Equal(0, new DiskUsage(Free: 600 * Gb, Total: 500 * Gb).UsedFraction);
        Assert.Equal(0, new DiskUsage(Free: 600 * Gb, Total: 500 * Gb).Used);
        Assert.False(new DiskUsage(Free: 0, Total: 0).IsLow);
    }

    [Fact]
    public void Bir_klasorun_durdugu_disk_okunur()
    {
        var disk = DiskUsage.Of(Path.GetTempPath());

        Assert.NotNull(disk);
        Assert.True(disk.Value.Total > 0);
        Assert.InRange(disk.Value.Free, 0, disk.Value.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\\olmayan-sunucu\paylasim\klasor")]
    public void Diski_okunamayan_yol_icin_bilgi_donmez(string path)
    {
        Assert.Null(DiskUsage.Of(path));
    }
}
