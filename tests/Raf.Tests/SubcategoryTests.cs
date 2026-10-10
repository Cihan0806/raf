using Raf.Core;

namespace Raf.Tests;

public class SubcategoryTests
{
    private static (Category Parent, Category Child) MinecraftWithMods(TempRaf raf)
    {
        var parent = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft", Extensions = { ".jar" } });
        var child = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Modlar", ParentId = parent.Id, NameContains = { "mod" } });
        return (parent, child);
    }

    [Fact]
    public void Alt_kategorinin_klasoru_ust_kategorinin_icinde_acilir()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);

        Assert.Equal(Path.Combine(raf.Cfg.Root, "Minecraft", "Modlar"), RafLayout.Dir(raf.Cfg, child));
        Assert.True(Directory.Exists(RafLayout.Dir(raf.Cfg, child)));
        Assert.Same(parent, raf.Cfg.Parent(child));
        Assert.Equal("Minecraft › Modlar", raf.Cfg.PathName(child));
        Assert.Equal(parent.Id, raf.Store.Load().Categories.First(c => c.Name == "Modlar").ParentId);
    }

    [Fact]
    public void Alt_kategorinin_kurali_ust_kategoriden_once_denenir()
    {
        using var raf = new TempRaf();
        MinecraftWithMods(raf);

        raf.Sorter.Sort(TempRaf.Touch(Path.Combine(raf.Inbox, "yeni-mod.jar")));
        raf.Sorter.Sort(TempRaf.Touch(Path.Combine(raf.Inbox, "FirinPlus.jar")));

        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Minecraft", "Modlar", "yeni-mod.jar")));
        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Minecraft", "FirinPlus.jar")));
        Assert.Equal("Minecraft › Modlar", raf.Sorter.HistorySnapshot().Last().CategoryName);
    }

    [Fact]
    public void Alt_kategori_klasoru_ust_kategorinin_ogesi_sayilmaz()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, parent), "FirinPlus.jar"));
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, child), "sodium.jar"));
        Directory.CreateDirectory(Path.Combine(RafLayout.Dir(raf.Cfg, parent), "dunya-yedegi"));

        var items = Library.Scan(raf.Cfg);

        Assert.Equal(new[] { "dunya-yedegi", "FirinPlus.jar" }, items.Where(i => i.CategoryId == parent.Id).Select(i => i.Name).OrderBy(n => n));
        Assert.Equal(new[] { "sodium.jar" }, items.Where(i => i.CategoryId == child.Id).Select(i => i.Name));
    }

    [Fact]
    public void Yeniden_uygulama_alt_kategori_klasorunu_oge_sanip_tasimaz()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);
        raf.Sorter.Sort(TempRaf.Touch(Path.Combine(raf.Inbox, "eklenti.jar")));
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, child), "sodium-mod.jar"));

        Assert.Empty(CategoryService.PlanReapply(raf.Cfg, raf.Sorter));
        Assert.True(Directory.Exists(RafLayout.Dir(raf.Cfg, child)));
        Assert.True(File.Exists(Path.Combine(RafLayout.Dir(raf.Cfg, parent), "eklenti.jar")));
    }

    [Fact]
    public void Ust_kategori_yeniden_adlandirilinca_alt_kategori_onunla_gider()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, child), "sodium.jar"));

        CategoryService.Update(raf.Cfg, raf.Store, parent, new Category { Name = "MC", Color = parent.Color, Extensions = { ".jar" } });

        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "MC", "Modlar", "sodium.jar")));
        Assert.False(Directory.Exists(Path.Combine(raf.Cfg.Root, "Minecraft")));
    }

    [Fact]
    public void Kategori_baska_bir_kategorinin_altina_icindekilerle_tasinir_ve_geri_cikarilir()
    {
        using var raf = new TempRaf();
        var parent = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft" });
        var maps = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Haritalar" });
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, maps), "spawn.schem"));

        CategoryService.Update(raf.Cfg, raf.Store, maps, new Category { Name = "Haritalar", Color = maps.Color, ParentId = parent.Id });
        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Minecraft", "Haritalar", "spawn.schem")));
        Assert.False(Directory.Exists(Path.Combine(raf.Cfg.Root, "Haritalar")));

        CategoryService.Update(raf.Cfg, raf.Store, maps, new Category { Name = "Haritalar", Color = maps.Color });
        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Haritalar", "spawn.schem")));
        Assert.Null(raf.Cfg.Parent(maps));
    }

    [Fact]
    public void Yalnizca_bir_seviye_ic_ice_olunur()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);
        var other = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Oyunlar" });

        // Alt kategorinin altına kategori eklenemez: ana kategori olur.
        var deep = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Fabric", ParentId = child.Id });
        Assert.Null(raf.Cfg.Parent(deep));

        // Alt kategorisi olan kategori başkasının altına giremez.
        CategoryService.Update(raf.Cfg, raf.Store, parent, new Category { Name = "Minecraft", Color = parent.Color, ParentId = other.Id });
        Assert.Null(raf.Cfg.Parent(parent));
        Assert.True(Directory.Exists(Path.Combine(raf.Cfg.Root, "Minecraft", "Modlar")));

        // Yedek kategori ("Diğer") kimsenin altına girmez.
        var fallback = raf.Cfg.Fallback;
        CategoryService.Update(raf.Cfg, raf.Store, fallback, new Category { Name = fallback.Name, Color = fallback.Color, ParentId = other.Id });
        Assert.Null(raf.Cfg.Parent(fallback));
    }

    [Fact]
    public void Ust_kategori_silinince_alt_kategoriler_ana_kategori_olur()
    {
        using var raf = new TempRaf();
        var (parent, child) = MinecraftWithMods(raf);
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, parent), "FirinPlus.jar"));
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, child), "sodium.jar"));

        CategoryService.Delete(raf.Cfg, raf.Store, raf.Sorter, parent);

        Assert.DoesNotContain(parent, raf.Cfg.Categories);
        Assert.Null(raf.Cfg.Parent(child));
        Assert.True(File.Exists(Path.Combine(raf.Cfg.Root, "Modlar", "sodium.jar")));
        Assert.True(File.Exists(raf.InCat("Diğer", "FirinPlus.jar")));
        Assert.False(Directory.Exists(Path.Combine(raf.Cfg.Root, "Minecraft")));
    }

    [Fact]
    public void Ayni_adli_hazir_klasor_alt_kategori_olarak_sahiplenilir()
    {
        using var raf = new TempRaf();
        var parent = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft" });
        TempRaf.Touch(Path.Combine(RafLayout.Dir(raf.Cfg, parent), "Modlar", "sodium.jar"));

        var child = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Modlar", ParentId = parent.Id });
        var items = Library.Scan(raf.Cfg);

        Assert.Equal("Modlar", child.Folder);
        Assert.DoesNotContain(items, i => i.CategoryId == parent.Id);
        Assert.Equal("sodium.jar", items.Single(i => i.CategoryId == child.Id).Name);
    }

    [Fact]
    public void Farkli_ust_kategorilerde_ayni_adli_alt_kategori_olabilir()
    {
        using var raf = new TempRaf();
        var a = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft" });
        var b = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "İş" });

        var first = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Yedek", ParentId = a.Id });
        var second = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Yedek", ParentId = b.Id });
        var third = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Yedek", ParentId = b.Id });

        Assert.Equal("Yedek", first.Folder);
        Assert.Equal("Yedek", second.Folder);
        Assert.Equal("Yedek 2", third.Folder);
    }

    [Fact]
    public void Siralama_ayni_seviyedeki_komsular_arasinda_yapilir()
    {
        using var raf = new TempRaf();
        var parent = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Minecraft" });
        var mods = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Modlar", ParentId = parent.Id });
        var maps = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Haritalar", ParentId = parent.Id });
        Assert.Equal(new[] { maps, mods }, raf.Cfg.Children(parent));

        CategoryService.Shift(raf.Cfg, raf.Store, maps, +1);
        Assert.Equal(new[] { mods, maps }, raf.Cfg.Children(parent));

        // En alttaki daha aşağı inmez; ana kategorilerin sırası alt kategorilerden etkilenmez.
        CategoryService.Shift(raf.Cfg, raf.Store, maps, +1);
        Assert.Equal(new[] { mods, maps }, raf.Cfg.Children(parent));
        Assert.Same(parent, raf.Cfg.TopLevel()[0]);
        Assert.Equal(new[] { parent, mods, maps }, raf.Cfg.TreeOrder().Take(3));
    }

    [Fact]
    public void Ustu_kaybolmus_alt_kategori_ana_kategori_gibi_davranir()
    {
        using var raf = new TempRaf();
        var orphan = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Yetim" });
        orphan.ParentId = "yok-boyle-biri";

        Assert.Null(raf.Cfg.Parent(orphan));
        Assert.Contains(orphan, raf.Cfg.TopLevel());
        Assert.Equal(Path.Combine(raf.Cfg.Root, "Yetim"), RafLayout.Dir(raf.Cfg, orphan));
    }
}
