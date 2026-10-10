using System.Text.Json;
using Raf.Core;

namespace Raf.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Hazir_kategorilerin_her_birinin_kendi_simgesi_var()
    {
        var icons = Defaults.Categories().Select(c => c.Icon).ToList();

        Assert.All(icons, icon => Assert.False(string.IsNullOrWhiteSpace(icon)));
        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Fact]
    public void Simgesi_olmayan_eski_ayar_dosyasi_adina_gore_simge_alir()
    {
        using var raf = new TempRaf();
        var old = new
        {
            Root = raf.Cfg.Root,
            Categories = new object[]
            {
                new { Name = "Resimler", Folder = "Resimler" },
                new { Name = "Minecraft", Folder = "Minecraft" },
                new { Name = "Diğer", Folder = "Diğer", IsFallback = true },
            },
        };
        File.WriteAllText(raf.Store.ConfigPath, JsonSerializer.Serialize(old));

        var loaded = raf.Store.Load();

        Assert.Equal("E8B9", loaded.Categories[0].Icon);
        Assert.Equal(Defaults.DefaultIcon, loaded.Categories[1].Icon);
        Assert.Equal("E712", loaded.Categories[2].Icon);
    }

    [Fact]
    public void Yeni_kategori_secilen_simgeyi_korur_secilmediyse_klasor_simgesi_alir()
    {
        using var raf = new TempRaf();

        var game = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Oyun", Icon = "E7FC" });
        var plain = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "Sade" });

        Assert.Equal("E7FC", raf.Store.Load().Find(game.Id)!.Icon);
        Assert.Equal(Defaults.DefaultIcon, plain.Icon);
    }

    [Fact]
    public void Duzenleme_simgeyi_degistirir_bos_gelirse_eskisini_korur()
    {
        using var raf = new TempRaf();
        var cat = CategoryService.Add(raf.Cfg, raf.Store, new Category { Name = "İş", Icon = "E821" });

        CategoryService.Update(raf.Cfg, raf.Store, cat, new Category { Name = "İş", Color = cat.Color, Icon = "E734" });
        Assert.Equal("E734", cat.Icon);

        CategoryService.Update(raf.Cfg, raf.Store, cat, new Category { Name = "İş", Color = cat.Color });
        Assert.Equal("E734", cat.Icon);
    }
}
