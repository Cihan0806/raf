using Raf.Core;

namespace Raf.Tests;

public class RuleEngineTests
{
    private static List<Category> WithUserCategory(Category user)
    {
        var list = Defaults.Categories();
        list.Insert(0, user);
        return list;
    }

    [Theory]
    [InlineData("rapor.pdf", "Belgeler")]
    [InlineData("FOTO.JPG", "Resimler")]
    [InlineData("film.mkv", "Videolar")]
    [InlineData("paket.tar.gz", "Arşivler")]
    [InlineData("kurulum.exe", "Programlar")]
    [InlineData("Steam.lnk", "Kısayollar")]
    [InlineData("ayar.yml", "Kod")]
    [InlineData("bilinmeyen.xyz", "Diğer")]
    [InlineData("uzantisiz", "Diğer")]
    public void Kuralsiz_dosya_turune_gore_yerlesir(string name, string expected)
    {
        Assert.Equal(expected, RuleEngine.Classify(Defaults.Categories(), name).Name);
    }

    [Fact]
    public void Ustteki_kategori_once_denenir()
    {
        var minecraft = new Category { Name = "Minecraft", Extensions = { ".jar" } };
        var cats = WithUserCategory(minecraft);

        Assert.Equal("Minecraft", RuleEngine.Classify(cats, "FirinPlus.jar").Name);

        cats.Remove(minecraft);
        cats.Add(minecraft);
        Assert.Equal("Programlar", RuleEngine.Classify(cats, "FirinPlus.jar").Name);
    }

    [Fact]
    public void Ad_kurali_turkce_harf_ve_buyukluk_farkini_yok_sayar()
    {
        var cats = WithUserCategory(new Category { Name = "İş", NameContains = { "teklif", "müşteri" } });

        Assert.Equal("İş", RuleEngine.Classify(cats, "TEKLİF-2026.pdf").Name);
        Assert.Equal("İş", RuleEngine.Classify(cats, "musteri listesi.xlsx").Name);
        Assert.Equal("Belgeler", RuleEngine.Classify(cats, "fatura.pdf").Name);
    }

    [Fact]
    public void Ad_kurali_uzantiya_bakmaz()
    {
        var cats = WithUserCategory(new Category { Name = "Zipli", NameContains = { "zip" } });

        Assert.Equal("Arşivler", RuleEngine.Classify(cats, "yedek.zip").Name);
        Assert.Equal("Zipli", RuleEngine.Classify(cats, "zip-notlari.txt").Name);
    }

    [Fact]
    public void Hepsi_uymali_seciliyse_tek_olcut_yetmez()
    {
        var cats = WithUserCategory(new Category
        {
            Name = "Faturalar",
            Extensions = { ".pdf" },
            NameContains = { "fatura" },
            MatchAll = true,
        });

        Assert.Equal("Faturalar", RuleEngine.Classify(cats, "ekim fatura.pdf").Name);
        Assert.Equal("Belgeler", RuleEngine.Classify(cats, "rapor.pdf").Name);
        Assert.Equal("Resimler", RuleEngine.Classify(cats, "fatura.png").Name);
    }

    [Fact]
    public void Site_kurali_indirme_adresine_bakar()
    {
        var cats = WithUserCategory(new Category { Name = "Modlar", Sites = { "modrinth.com" } });

        Assert.Equal("Modlar", RuleEngine.Classify(cats, "sodium.jar", source: "https://cdn.MODRINTH.com/data/x.jar").Name);
        Assert.Equal("Programlar", RuleEngine.Classify(cats, "sodium.jar", source: "https://example.org/x.jar").Name);
        Assert.Equal("Programlar", RuleEngine.Classify(cats, "sodium.jar").Name);
    }

    [Fact]
    public void Klasorler_uzanti_kuralina_takilmaz_ama_ad_kuralina_uyar()
    {
        var cats = WithUserCategory(new Category { Name = "Minecraft", NameContains = { "mods" } });

        Assert.Equal("Diğer", RuleEngine.Classify(cats, "arsiv.zip", isDirectory: true).Name);
        Assert.Equal("Minecraft", RuleEngine.Classify(cats, "mods21.4", isDirectory: true).Name);
    }

    [Fact]
    public void Kurali_olmayan_kategori_hicbir_seyi_kapmaz()
    {
        var cats = WithUserCategory(new Category { Name = "Önemli" });

        Assert.Equal("Belgeler", RuleEngine.Classify(cats, "not.txt").Name);
    }

    [Theory]
    [InlineData("JAR, *.zip;.Rar  mp4", ".jar|.zip|.rar|.mp4")]
    [InlineData("", "")]
    [InlineData(".pdf .pdf", ".pdf")]
    public void Uzanti_listesi_serbest_yazimdan_ayrisir(string input, string expected)
    {
        Assert.Equal(expected, string.Join("|", TextUtil.ParseExtensions(input)));
    }

    [Fact]
    public void Kelime_listesinde_ifadenin_icindeki_bosluk_korunur()
    {
        Assert.Equal(new[] { "iş takip", "teklif" }, TextUtil.ParsePhrases(" iş takip , teklif,, "));
    }
}
