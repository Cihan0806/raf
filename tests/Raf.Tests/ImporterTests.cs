using Raf.Core;

namespace Raf.Tests;

public class ImporterTests
{
    /// <summary>Masaüstü gibi dağınık bir klasör: dosyalar, bir kısayol, bir klasör ve dokunulmaması gerekenler.</summary>
    private static string Messy(TempRaf raf, string name = "masaustu")
    {
        var dir = raf.Outside(name);
        TempRaf.Touch(Path.Combine(dir, "tatil.jpg"));
        TempRaf.Touch(Path.Combine(dir, "fatura.pdf"));
        TempRaf.Touch(Path.Combine(dir, "Oyun.lnk"));
        TempRaf.Touch(Path.Combine(dir, "notlar.xyz"));
        TempRaf.Touch(Path.Combine(dir, "proje", "kod.cs"));
        return dir;
    }

    private static ImportResult Import(TempRaf raf, bool linkFolders, params string[] folders) =>
        Importer.Apply(raf.Cfg, raf.Store, raf.Sorter, Importer.Plan(raf.Cfg, raf.Sorter, folders, linkFolders));

    [Fact]
    public void Plan_dosyalari_kurallara_gore_klasorleri_baglanti_olarak_ayirir()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);

        var plan = Importer.Plan(raf.Cfg, raf.Sorter, new[] { dir }, linkFolders: true);

        Assert.Equal(
            new[] { "fatura.pdf → Belgeler", "notlar.xyz → Diğer", "Oyun.lnk → Kısayollar", "proje → Klasörler", "tatil.jpg → Resimler" },
            plan.Select(e => $"{e.Name} → {e.TargetName}"));
        Assert.Equal(ImportAction.Link, plan.Single(e => e.Name == "proje").Action);
        Assert.All(plan.Where(e => !e.IsDirectory), e => Assert.Equal(ImportAction.Move, e.Action));
    }

    [Fact]
    public void Plan_hicbir_seyi_degistirmez()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);

        Importer.Plan(raf.Cfg, raf.Sorter, new[] { dir }, linkFolders: true);

        Assert.True(File.Exists(Path.Combine(dir, "tatil.jpg")));
        Assert.Empty(raf.Cfg.Links);
        Assert.Empty(raf.Sorter.HistorySnapshot());
        Assert.DoesNotContain(raf.Cfg.Categories, c => c.Name == Importer.FolderCategoryName);
    }

    [Fact]
    public void Iceri_alinca_dosyalar_tasinir_klasor_yerinde_kalip_baglanir()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);

        var result = Import(raf, linkFolders: true, dir);

        Assert.Empty(result.Errors);
        Assert.True(File.Exists(raf.InCat("Resimler", "tatil.jpg")));
        Assert.True(File.Exists(raf.InCat("Belgeler", "fatura.pdf")));
        Assert.True(File.Exists(raf.InCat("Kısayollar", "Oyun.lnk")));
        Assert.True(File.Exists(raf.InCat("Diğer", "notlar.xyz")));
        Assert.False(File.Exists(Path.Combine(dir, "tatil.jpg")));

        // Klasör yerinden oynamaz; Raf'ta "Klasörler" kategorisinde görünür.
        Assert.True(File.Exists(Path.Combine(dir, "proje", "kod.cs")));
        var folders = raf.Cat(Importer.FolderCategoryName);
        var link = Assert.Single(raf.Cfg.Links);
        Assert.Equal(folders.Id, link.CategoryId);
        Assert.Contains(Library.Scan(raf.Cfg), i => i.Name == "proje" && i.CategoryId == folders.Id);
    }

    [Fact]
    public void Klasorler_kategorisi_kuralsiz_acilir_ve_Diger_in_hemen_ustunde_durur()
    {
        using var raf = new TempRaf();
        Import(raf, linkFolders: true, Messy(raf));

        var folders = raf.Cat(Importer.FolderCategoryName);
        Assert.False(folders.HasRules);
        Assert.Equal(raf.Cfg.Categories.Count - 2, raf.Cfg.Categories.IndexOf(folders));
        Assert.True(raf.Cfg.Categories[^1].IsFallback);
        Assert.Contains(raf.Store.Load().Categories, c => c.Name == Importer.FolderCategoryName);
    }

    [Fact]
    public void Klasorler_kategorisi_varsa_yenisi_acilmaz()
    {
        using var raf = new TempRaf();
        var existing = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "klasörler" });

        Import(raf, linkFolders: true, Messy(raf));

        Assert.Single(raf.Cfg.Categories, c => TextUtil.Fold(c.Name) == TextUtil.Fold(Importer.FolderCategoryName));
        Assert.Equal(existing.Id, Assert.Single(raf.Cfg.Links).CategoryId);
    }

    [Fact]
    public void Klasorler_istenmezse_plana_girmez_ve_kategori_acilmaz()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);

        var result = Import(raf, linkFolders: false, dir);

        Assert.Empty(result.Linked);
        Assert.Empty(raf.Cfg.Links);
        Assert.DoesNotContain(raf.Cfg.Categories, c => c.Name == Importer.FolderCategoryName);
        Assert.Equal(4, result.Moved.Count);
    }

    [Fact]
    public void Gizli_dosyalar_zaten_bagli_klasorler_ve_yarim_indirmeler_alinmaz()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);
        var hidden = TempRaf.Touch(Path.Combine(dir, "gizli.txt"));
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        TempRaf.Touch(Path.Combine(dir, "film.mkv.crdownload"));
        CategoryService.AddLink(raf.Cfg, raf.Store, Path.Combine(dir, "proje"), raf.Cat("Kod"));

        var plan = Importer.Plan(raf.Cfg, raf.Sorter, new[] { dir }, linkFolders: true);

        Assert.DoesNotContain(plan, e => e.Name is "gizli.txt" or "proje");
        var partial = plan.Single(e => e.Name == "film.mkv.crdownload");
        Assert.Equal(ImportAction.Skip, partial.Action);
        Assert.False(string.IsNullOrEmpty(partial.Reason));

        Importer.Apply(raf.Cfg, raf.Store, raf.Sorter, plan);
        Assert.True(File.Exists(Path.Combine(dir, "film.mkv.crdownload")));
        Assert.True(File.Exists(hidden));
        // Önceden eklenmiş bağlantının kategorisi değişmez.
        Assert.Equal(raf.Cat("Kod").Id, Assert.Single(raf.Cfg.Links).CategoryId);
    }

    [Fact]
    public void Raf_in_kendi_klasoru_ve_icindekiler_iceri_alinmaz()
    {
        using var raf = new TempRaf();
        TempRaf.Touch(raf.InCat("Belgeler", "zaten-rafta.pdf"));

        // Raf klasörünü içeren klasör kaynak seçilirse Raf klasörü listelenmez.
        var around = Importer.Plan(raf.Cfg, raf.Sorter, new[] { raf.Dir }, linkFolders: true);
        Assert.DoesNotContain(around, e => RafLayout.IsSame(e.Path, raf.Cfg.Root));

        // Raf'ın içindeki bir klasör kaynak seçilirse hiçbir şey planlanmaz.
        var inside = Importer.Plan(raf.Cfg, raf.Sorter, new[] { RafLayout.Dir(raf.Cfg, raf.Cat("Belgeler")), raf.Inbox }, linkFolders: true);
        Assert.Empty(inside);
    }

    [Fact]
    public void Iki_kaynaktan_gelen_ayni_adli_dosyalarin_ikisi_de_korunur()
    {
        using var raf = new TempRaf();
        var first = raf.Outside("masaustu");
        var second = raf.Outside("indirilenler");
        TempRaf.Touch(Path.Combine(first, "rapor.pdf"), "bir");
        TempRaf.Touch(Path.Combine(second, "rapor.pdf"), "iki");

        var result = Import(raf, linkFolders: true, first, second);

        Assert.Empty(result.Errors);
        Assert.Equal("bir", File.ReadAllText(raf.InCat("Belgeler", "rapor.pdf")));
        Assert.Equal("iki", File.ReadAllText(raf.InCat("Belgeler", "rapor (2).pdf")));
    }

    [Fact]
    public void Ayni_klasor_iki_kez_secilirse_ogeler_bir_kez_planlanir()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);

        var plan = Importer.Plan(raf.Cfg, raf.Sorter, new[] { dir, dir + Path.DirectorySeparatorChar }, linkFolders: true);

        Assert.Equal(5, plan.Count);
    }

    [Fact]
    public void Tasimalar_kendiliginden_sayilir_ki_kurallar_degisince_yeniden_uygulanabilsin()
    {
        using var raf = new TempRaf();
        var dir = raf.Outside("masaustu");
        TempRaf.Touch(Path.Combine(dir, "sodium.jar"));

        var result = Import(raf, linkFolders: true, dir);
        Assert.All(result.Moved, r => Assert.True(r.Auto));
        Assert.True(File.Exists(raf.InCat("Programlar", "sodium.jar")));

        var minecraft = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft", Extensions = { ".jar" } });
        var replan = CategoryService.PlanReapply(raf.Cfg, raf.Sorter);

        var (path, target) = Assert.Single(replan);
        Assert.Equal(raf.InCat("Programlar", "sodium.jar"), path);
        Assert.Same(minecraft, target);
    }

    [Fact]
    public void Kaybolan_oge_isi_durdurmaz_nedeni_bildirilir()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);
        var plan = Importer.Plan(raf.Cfg, raf.Sorter, new[] { dir }, linkFolders: true);
        File.Delete(Path.Combine(dir, "fatura.pdf"));
        Directory.Delete(Path.Combine(dir, "proje"), recursive: true);

        var progress = new List<int>();
        var result = Importer.Apply(raf.Cfg, raf.Store, raf.Sorter, plan, progress.Add);

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.StartsWith("fatura.pdf:"));
        Assert.Contains(result.Errors, e => e.StartsWith("proje:"));
        Assert.Equal(3, result.Moved.Count);
        Assert.Empty(raf.Cfg.Links);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, progress);
    }

    [Fact]
    public void Geri_alinca_dosyalar_eski_yerine_doner_baglantilar_kalkar()
    {
        using var raf = new TempRaf();
        var dir = Messy(raf);
        var result = Import(raf, linkFolders: true, dir);

        var errors = Importer.Undo(raf.Cfg, raf.Store, raf.Sorter, result);

        Assert.Empty(errors);
        foreach (var name in new[] { "tatil.jpg", "fatura.pdf", "Oyun.lnk", "notlar.xyz" })
            Assert.True(File.Exists(Path.Combine(dir, name)), name);
        Assert.False(File.Exists(raf.InCat("Resimler", "tatil.jpg")));
        Assert.Empty(raf.Cfg.Links);
        Assert.True(Directory.Exists(Path.Combine(dir, "proje")));
        Assert.All(raf.Sorter.HistorySnapshot(), r => Assert.True(r.Undone));
        Assert.DoesNotContain(Library.Scan(raf.Cfg), i => !i.IsDirectory || i.Name == "proje");
    }
}
