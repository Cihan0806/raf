using Raf.Core;

namespace Raf.Tests;

public class FileOpsTests
{
    [Fact]
    public void Yeniden_adlandirma_gecmise_yazilir_ve_geri_alinir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"));

        var record = raf.Sorter.Rename(file, "ekim raporu.pdf", raf.Cat("Belgeler"));

        Assert.True(File.Exists(raf.InCat("Belgeler", "ekim raporu.pdf")));
        Assert.False(File.Exists(file));
        Assert.True(record.Renamed);
        Assert.Equal("Belgeler", record.CategoryName);

        Assert.Null(raf.Sorter.Undo(record));
        Assert.True(File.Exists(file));
        Assert.Empty(raf.Cfg.Held);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b.pdf")]
    [InlineData("soru?.pdf")]
    [InlineData("nokta.")]
    [InlineData("rapor.pdf")]
    [InlineData("var.pdf")]
    public void Gecersiz_ayni_ya_da_kullanilan_ad_reddedilir(string newName)
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"), "asıl");
        TempRaf.Touch(raf.InCat("Belgeler", "var.pdf"), "diğer");

        Assert.ThrowsAny<IOException>(() => raf.Sorter.Rename(file, newName, raf.Cat("Belgeler")));

        Assert.Equal("asıl", File.ReadAllText(file));
        Assert.Equal("diğer", File.ReadAllText(raf.InCat("Belgeler", "var.pdf")));
        Assert.Empty(raf.Sorter.HistorySnapshot());
    }

    [Fact]
    public void Yalnizca_harf_buyuklugu_degisen_ad_dosyada_ve_klasorde_calisir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"));
        var folder = raf.InCat("Diğer", "proje");
        TempRaf.Touch(Path.Combine(folder, "a.txt"));

        raf.Sorter.Rename(file, "RAPOR.pdf", raf.Cat("Belgeler"));
        raf.Sorter.Rename(folder, "Proje", raf.Cat("Diğer"));

        Assert.Equal("RAPOR.pdf", Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName).Single());
        Assert.Equal("Proje", Directory.GetDirectories(Path.GetDirectoryName(folder)!).Select(Path.GetFileName).Single());
        Assert.True(File.Exists(Path.Combine(raf.InCat("Diğer", "Proje"), "a.txt")));
    }

    [Fact]
    public void Dokunma_listesindeki_dosyanin_adi_degisince_listedeki_yol_da_degisir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"));
        raf.Sorter.Undo(raf.Sorter.Sort(file)!);

        raf.Sorter.Rename(file, "bekleyen.pdf", null);

        Assert.True(RafLayout.IsSame(Path.Combine(raf.Inbox, "bekleyen.pdf"), raf.Cfg.Held.Single()));
    }

    [Fact]
    public void Baglantili_ogenin_adi_degisince_baglanti_onu_izler_geri_alinca_da()
    {
        using var raf = new TempRaf();
        var project = raf.Outside("webpanel");
        CategoryService.AddLink(raf.Cfg, raf.Store, project, raf.Cat("Kod"));

        var record = raf.Sorter.Rename(project, "mcpanel", raf.Cat("Kod"));
        Assert.Equal("mcpanel", Library.Scan(raf.Cfg).Single().Name);

        Assert.Null(raf.Sorter.Undo(record));
        Assert.Equal("webpanel", Library.Scan(raf.Cfg).Single().Name);
        Assert.True(Directory.Exists(project));
    }

    [Fact]
    public void Kopyalama_asil_ogeyi_yerinde_birakir_ve_ustune_yazmaz()
    {
        using var raf = new TempRaf();
        var outside = raf.Outside("masaustu");
        var file = TempRaf.Touch(Path.Combine(outside, "not.txt"), "asıl");
        var folder = Path.Combine(outside, "proje");
        TempRaf.Touch(Path.Combine(folder, "alt", "kod.py"));

        var first = raf.Sorter.CopyTo(file, raf.Cat("Belgeler"));
        var second = raf.Sorter.CopyTo(file, raf.Cat("Belgeler"));
        var copiedFolder = raf.Sorter.CopyTo(folder, raf.Cat("Kod"));

        Assert.True(File.Exists(file));
        Assert.Equal(raf.InCat("Belgeler", "not.txt"), first);
        Assert.Equal(raf.InCat("Belgeler", "not (2).txt"), second);
        Assert.True(File.Exists(Path.Combine(copiedFolder, "alt", "kod.py")));
        Assert.True(File.Exists(Path.Combine(folder, "alt", "kod.py")));
        Assert.Empty(raf.Sorter.HistorySnapshot());
    }

    [Fact]
    public void Kategoriler_arasi_tasimayi_geri_almak_dokunma_listesini_doldurmaz()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"));
        var record = raf.Sorter.MoveTo(file, raf.Cat("Kod"))!;

        Assert.Null(raf.Sorter.Undo(record));

        Assert.True(File.Exists(file));
        Assert.Empty(raf.Cfg.Held);
    }

    [Fact]
    public void Klasor_kendi_icindeki_kategoriye_tasinamaz_ve_kopyalanamaz()
    {
        using var raf = new TempRaf();
        var parent = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft" });
        var child = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Modlar", ParentId = parent.Id });
        var parentDir = RafLayout.Dir(raf.Cfg, parent);

        Assert.Throws<IOException>(() => raf.Sorter.MoveTo(parentDir, child));
        Assert.Throws<IOException>(() => raf.Sorter.CopyTo(parentDir, child));
        Assert.True(Directory.Exists(RafLayout.Dir(raf.Cfg, child)));
    }
}

public class LibrarySortTests
{
    private static readonly DateTime Base = new(2026, 10, 1);

    private static readonly List<LibraryItem> Items = new()
    {
        File("yedek-10.zip", day: 1, size: 500),
        File("yedek-2.zip", day: 5, size: 100),
        File("Ödev.docx", day: 3, size: 300),
        File("ayar.yml", day: 4, size: 50),
        new LibraryItem(@"C:\Raf\x\zz-klasör", "zz-klasör", "x", IsDirectory: true, IsLink: false, Base.AddDays(2), 0),
    };

    private static LibraryItem File(string name, int day, long size) =>
        new(@"C:\Raf\x\" + name, name, "x", IsDirectory: false, IsLink: false, Base.AddDays(day), size);

    private static string[] Sorted(SortKey key, bool descending) =>
        LibrarySort.Apply(Items, key, descending, i => i.Added).Select(i => i.Name).ToArray();

    [Fact]
    public void Ada_gore_klasorler_once_sayilar_sayi_olarak_turkce_harfler_yerinde()
    {
        Assert.Equal(new[] { "zz-klasör", "ayar.yml", "Ödev.docx", "yedek-2.zip", "yedek-10.zip" }, Sorted(SortKey.Name, descending: false));
        Assert.Equal(new[] { "yedek-10.zip", "yedek-2.zip", "Ödev.docx", "ayar.yml", "zz-klasör" }, Sorted(SortKey.Name, descending: true));
    }

    [Fact]
    public void Tarihe_gore_en_yeni_once()
    {
        Assert.Equal(new[] { "yedek-2.zip", "ayar.yml", "Ödev.docx", "zz-klasör", "yedek-10.zip" }, Sorted(SortKey.Date, descending: true));
    }

    [Fact]
    public void Boyuta_gore_en_buyuk_once()
    {
        Assert.Equal(new[] { "yedek-10.zip", "Ödev.docx", "yedek-2.zip", "ayar.yml", "zz-klasör" }, Sorted(SortKey.Size, descending: true));
    }

    [Fact]
    public void Ture_gore_uzantilar_bir_arada_klasorler_once()
    {
        Assert.Equal(new[] { "zz-klasör", "Ödev.docx", "ayar.yml", "yedek-2.zip", "yedek-10.zip" }, Sorted(SortKey.Type, descending: false));
    }
}
