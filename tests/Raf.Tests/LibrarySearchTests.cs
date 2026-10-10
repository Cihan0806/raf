using Raf.Core;

namespace Raf.Tests;

public class LibrarySearchTests
{
    private static readonly DateTime Base = new(2026, 10, 1);

    // Sıra numarası büyüdükçe öğe daha yeni sayılır.
    private static readonly List<LibraryItem> Items = new()
    {
        File("rapor.pdf", 1),
        File("FATURA Ekim.PDF", 2),
        File("pdf okuma notları.txt", 3),
        File("FirinPlus.jar", 4),
        File("yeni-mod-1.21.jar", 5),
        File("Steam.lnk", 6),
        File("yedek.jar.bak", 7),
        File(".gitignore", 8),
        File("müzik listesi.txt", 9),
        new LibraryItem(@"C:\Raf\Diğer\pdf", "pdf", "x", IsDirectory: true, IsLink: false, Base.AddDays(10), 0),
        new LibraryItem(@"C:\Raf\Kod\mods.jar", "mods.jar", "x", IsDirectory: true, IsLink: false, Base.AddDays(11), 0),
    };

    private static LibraryItem File(string name, int day) =>
        new(@"C:\Raf\Kategori\" + name, name, "x", IsDirectory: false, IsLink: false, Base.AddDays(day), 1);

    private static LibrarySearch.Result Find(string query, int limit = 100) =>
        LibrarySearch.Find(Items, query, i => i.Added, limit);

    private static string[] Names(List<LibraryItem> items) => items.Select(i => i.Name).ToArray();

    [Theory]
    [InlineData("pdf")]
    [InlineData(".pdf")]
    [InlineData("*.pdf")]
    [InlineData("PDF")]
    [InlineData("  .Pdf ")]
    public void Yalnizca_uzanti_yazilinca_o_turdeki_dosyalar_en_yeni_once_gelir(string query)
    {
        var result = Find(query);

        Assert.Equal(".pdf", result.Extension);
        Assert.Equal(new[] { "FATURA Ekim.PDF", "rapor.pdf" }, Names(result.ByExtension));
    }

    [Fact]
    public void Noktasiz_uzanti_adinda_gecenleri_de_ayri_olarak_getirir()
    {
        var result = Find("pdf");

        Assert.Equal(new[] { "pdf", "pdf okuma notları.txt" }, Names(result.ByName));
    }

    [Fact]
    public void Noktali_uzanti_yalnizca_uzantiyla_eslesir()
    {
        var result = Find(".jar");

        Assert.Equal(new[] { "yeni-mod-1.21.jar", "FirinPlus.jar" }, Names(result.ByExtension));
        Assert.Empty(result.ByName);
    }

    [Fact]
    public void Klasorun_adindaki_nokta_uzanti_sayilmaz()
    {
        Assert.DoesNotContain("mods.jar", Names(Find(".jar").ByExtension));
        Assert.Contains("mods.jar", Names(Find("jar").ByName));
    }

    [Fact]
    public void Kisayollar_gizlenen_uzantisiyla_bulunur()
    {
        Assert.Equal(new[] { "Steam.lnk" }, Names(Find("lnk").ByExtension));
    }

    [Fact]
    public void Raf_ta_o_turde_dosya_yoksa_noktasiz_kelime_duz_ad_aramasidir()
    {
        var result = Find("mod");

        Assert.Null(result.Extension);
        Assert.Empty(result.ByExtension);
        Assert.Equal(new[] { "mods.jar", "yeni-mod-1.21.jar" }, Names(result.ByName));
    }

    [Fact]
    public void Kelime_ve_uzanti_birlikte_yazilabilir()
    {
        Assert.Equal(new[] { "FATURA Ekim.PDF" }, Names(Find("fatura .pdf").ByName));
        Assert.Equal(new[] { "yeni-mod-1.21.jar" }, Names(Find("yeni jar").ByName));
        Assert.Null(Find("fatura .pdf").Extension);
    }

    [Fact]
    public void Uzantisi_olmayan_noktali_sorgu_ad_aramasina_doner()
    {
        var result = Find(".git");

        Assert.Null(result.Extension);
        Assert.Equal(new[] { ".gitignore" }, Names(result.ByName));
    }

    [Fact]
    public void Ad_aramasi_turkce_harf_farkini_yok_sayar_ve_basta_eslesen_once_gelir()
    {
        Assert.Equal(new[] { "müzik listesi.txt" }, Names(Find("MUZIK").ByName));
        Assert.Equal(new[] { "yedek.jar.bak", "yeni-mod-1.21.jar" }, Names(Find("ye").ByName).Take(2).ToArray());
    }

    [Fact]
    public void Sonuc_sayisi_sinirla_kesilir_ve_uzanti_eslesmeleri_onceliklidir()
    {
        var result = Find("pdf", limit: 3);

        Assert.Equal(2, result.ByExtension.Count);
        Assert.Single(result.ByName);
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData("Brave", "brave", 0)]
    [InlineData("Brave", "BRA", 0)]
    [InlineData("Google Chrome", "chr", 1)]
    [InlineData("Bu bilgisayar", "bilg", 1)]
    [InlineData("Geri Dönüşüm Kutusu", "donusum kutu", 1)]
    [InlineData("IntelliJ IDEA 2026.2.0.1", "idea", 1)]
    [InlineData("Half-Life 2", "life", 1)]
    [InlineData("Minecraft Launcher", "craft", 2)]
    [InlineData("Minecraft Launcher", "launcher mine", 1)]
    [InlineData("Steam", "stream", -1)]
    [InlineData("Google Chrome", "chrome firefox", -1)]
    public void Ad_eslesmesi_basta_kelime_basinda_ve_icinde_diye_puanlanir(string name, string query, int expected)
    {
        var tokens = TextUtil.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(expected, TextUtil.MatchScore(TextUtil.Fold(name), tokens));
    }

    [Fact]
    public void Bos_sorgu_hicbir_adla_eslesmez()
    {
        Assert.Equal(-1, TextUtil.MatchScore("brave", Array.Empty<string>()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_sorgu_sonuc_dondurmez(string query)
    {
        Assert.Equal(0, Find(query).Count);
    }
}
