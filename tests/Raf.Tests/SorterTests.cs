using Raf.Core;

namespace Raf.Tests;

public class SorterTests
{
    [Fact]
    public void Dosya_kategori_klasorune_tasinir_ve_kaydedilir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"));

        var record = raf.Sorter.Sort(file);

        Assert.NotNull(record);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(raf.InCat("Belgeler", "rapor.pdf")));
        Assert.Equal("Belgeler", record!.CategoryName);
        Assert.Single(raf.Store.LoadHistory());
    }

    [Fact]
    public void Ayni_adda_dosya_varsa_ustune_yazmaz()
    {
        using var raf = new TempRaf();
        TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"), "eski");
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"), "yeni");

        raf.Sorter.Sort(file);

        Assert.Equal("eski", File.ReadAllText(raf.InCat("Belgeler", "rapor.pdf")));
        Assert.Equal("yeni", File.ReadAllText(raf.InCat("Belgeler", "rapor (2).pdf")));
    }

    [Fact]
    public void Noktali_klasor_adi_cakisinca_bozulmaz()
    {
        using var raf = new TempRaf();
        Directory.CreateDirectory(raf.InCat("Diğer", "apache-maven-3.9.16"));
        var folder = Path.Combine(raf.Inbox, "apache-maven-3.9.16");
        TempRaf.Touch(Path.Combine(folder, "pom.xml"));

        raf.Sorter.Sort(folder);

        Assert.True(File.Exists(Path.Combine(raf.InCat("Diğer", "apache-maven-3.9.16 (2)"), "pom.xml")));
    }

    [Fact]
    public void Zaten_dogru_kategorideki_dosya_yerinde_kalir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(raf.InCat("Belgeler", "rapor.pdf"));

        Assert.Null(raf.Sorter.Sort(file));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Geri_al_dosyayi_eski_yerine_koyar_ve_dokunma_listesine_alir()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"));
        var record = raf.Sorter.Sort(file)!;

        var error = raf.Sorter.Undo(record);

        Assert.Null(error);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(record.To));
        Assert.True(record.Undone);
        Assert.Contains(raf.Cfg.Held, h => RafLayout.IsSame(h, file));
        Assert.NotNull(raf.Sorter.Undo(record));
    }

    [Fact]
    public void Geri_alinan_dosya_elle_tasininca_dokunma_listesinden_cikar()
    {
        using var raf = new TempRaf();
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"));
        raf.Sorter.Undo(raf.Sorter.Sort(file)!);

        raf.Sorter.MoveTo(file, raf.Cat("Kod"));

        Assert.Empty(raf.Cfg.Held);
        Assert.True(File.Exists(raf.InCat("Kod", "rapor.pdf")));
    }

    [Fact]
    public void Indirme_adresi_site_kuralina_gore_yerlestirir()
    {
        using var raf = new TempRaf();
        CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Modlar", Sites = { "modrinth.com" } });
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "sodium.jar"));
        File.WriteAllText(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://cdn.modrinth.com/data/sodium.jar\r\n");

        raf.Sorter.Sort(file);

        Assert.True(File.Exists(raf.InCat("Modlar", "sodium.jar")));
    }
}

public class CategoryServiceTests
{
    [Fact]
    public void Yeni_kategori_en_uste_eklenir_ve_klasoru_acilir()
    {
        using var raf = new TempRaf();

        var cat = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft", Extensions = { ".jar" } });

        Assert.Same(cat, raf.Cfg.Categories[0]);
        Assert.True(Directory.Exists(RafLayout.Dir(raf.Cfg, cat)));
        Assert.Equal("Minecraft", raf.Store.Load().Categories[0].Name);
    }

    [Fact]
    public void Gelen_adinda_kategori_gelen_klasoruyle_cakismaz()
    {
        using var raf = new TempRaf();

        var cat = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Gelen" });

        Assert.False(RafLayout.IsSame(RafLayout.Dir(raf.Cfg, cat), raf.Inbox));
    }

    [Fact]
    public void Ad_degisince_klasor_icindekilerle_birlikte_yeniden_adlandirilir()
    {
        using var raf = new TempRaf();
        var cat = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Oyun" });
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, cat), "kayit.dat"));

        CategoryService.Update(raf.Cfg, raf.Store, cat, new Category { Name = "Oyunlar", Color = cat.Color });

        Assert.Equal("Oyunlar", cat.Folder);
        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Oyunlar", "kayit.dat")));
        Assert.False(Directory.Exists(Path.Combine(raf.Cfg.Root, "Oyun")));
    }

    [Fact]
    public void Silinen_kategorinin_icindekiler_digere_gecer()
    {
        using var raf = new TempRaf();
        var cat = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Geçici" });
        var dir = RafLayout.Dir(raf.Cfg, cat);
        TempRaf.Touch(Path.Combine(dir, "not.txt"));
        CategoryService.AddLink(raf.Cfg, raf.Store, raf.Outside("proje"), cat);

        CategoryService.Delete(raf.Cfg, raf.Store, raf.Sorter, cat);

        Assert.DoesNotContain(cat, raf.Cfg.Categories);
        Assert.False(Directory.Exists(dir));
        Assert.True(File.Exists(raf.InCat("Diğer", "not.txt")));
        Assert.Equal(raf.Cfg.Fallback.Id, raf.Cfg.Links.Single().CategoryId);
    }

    [Fact]
    public void Yeniden_uygulama_yeni_kurala_uyanlari_bulur_elle_konulanlara_dokunmaz()
    {
        using var raf = new TempRaf();
        var auto = raf.Sorter.Sort(TempRaf.Touch(Path.Combine(raf.Inbox, "eklenti.jar")))!;
        var manual = raf.Sorter.MoveTo(TempRaf.Touch(Path.Combine(raf.Inbox, "ozel.jar")), raf.Cat("Belgeler"))!;
        var important = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Önemli" });
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, important), "elle.jar"));

        var minecraft = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft", Extensions = { ".jar" } });
        var plan = CategoryService.PlanReapply(raf.Cfg, raf.Sorter);

        var only = Assert.Single(plan);
        Assert.Equal(auto.To, only.Path);
        Assert.Same(minecraft, only.Target);
        Assert.True(File.Exists(manual.To));
    }

    [Fact]
    public void Baglanti_ogeyi_yerinden_oynatmaz()
    {
        using var raf = new TempRaf();
        var project = raf.Outside("webpanel");
        TempRaf.Touch(Path.Combine(project, "index.js"));

        CategoryService.AddLink(raf.Cfg, raf.Store, project, raf.Cat("Kod"));
        var item = Library.Scan(raf.Cfg).Single();

        Assert.True(item.IsLink);
        Assert.True(item.IsDirectory);
        Assert.Equal(raf.Cat("Kod").Id, item.CategoryId);
        Assert.True(File.Exists(Path.Combine(project, "index.js")));
    }
}

public class RafLayoutTests
{
    [Fact]
    public void Raf_klasorunu_iceren_ya_da_icindeki_klasor_izlenemez()
    {
        using var raf = new TempRaf();

        Assert.NotNull(RafLayout.WhyNotWatchable(raf.Cfg, raf.Cfg.Root));
        Assert.NotNull(RafLayout.WhyNotWatchable(raf.Cfg, raf.Inbox));
        Assert.NotNull(RafLayout.WhyNotWatchable(raf.Cfg, raf.Dir));
        Assert.NotNull(RafLayout.WhyNotWatchable(raf.Cfg, Path.GetPathRoot(raf.Dir)!));
        Assert.Null(RafLayout.WhyNotWatchable(raf.Cfg, raf.Outside("indirilenler")));
    }

    [Fact]
    public void Raf_klasorunu_iceren_klasor_ve_ozel_klasorler_tasinamaz()
    {
        using var raf = new TempRaf();

        Assert.NotNull(RafLayout.WhyNotMovable(raf.Cfg, raf.Dir));
        Assert.NotNull(RafLayout.WhyNotMovable(raf.Cfg, raf.Inbox));
        Assert.NotNull(RafLayout.WhyNotMovable(raf.Cfg, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
        Assert.Null(RafLayout.WhyNotMovable(raf.Cfg, raf.Outside("proje")));
    }

    [Fact]
    public void Benzer_adli_komsu_klasor_icinde_sayilmaz()
    {
        Assert.False(RafLayout.IsInside(@"C:\Raf2\a.txt", @"C:\Raf"));
        Assert.True(RafLayout.IsInside(@"C:\Raf\Gelen\a.txt", @"C:\raf\"));
    }
}
