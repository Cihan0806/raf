using System.Collections.Concurrent;
using Raf.Core;

namespace Raf.Tests;

public class DownloadCatcherTests
{
    private sealed class Rig : IDisposable
    {
        public TempRaf Raf { get; } = new();
        public DownloadCatcher Catcher { get; }
        public string Elsewhere { get; }
        public ConcurrentQueue<string> Caught { get; } = new();
        public ConcurrentQueue<(string From, string To)> Moved { get; } = new();
        public ConcurrentQueue<string> Gone { get; } = new();

        public Rig(Action<TempRaf>? configure = null)
        {
            Elsewhere = Raf.Outside("baska-yer");
            configure?.Invoke(Raf);
            Catcher = new DownloadCatcher(Raf.Cfg)
            {
                Quiet = TimeSpan.FromMilliseconds(250),
                MarkWait = TimeSpan.FromSeconds(4),
            };
            Catcher.Caught += Caught.Enqueue;
            Catcher.Moved += (from, to) => Moved.Enqueue((from, to));
            Catcher.Gone += Gone.Enqueue;
            // Gerçek kökler yerine yalnızca testin klasörü izlenir.
            Catcher.Start(new[] { (Raf.Dir, true) });
        }

        public string At(params string[] parts) => Path.Combine(new[] { Elsewhere }.Concat(parts).ToArray());

        public void Dispose()
        {
            Catcher.Dispose();
            Raf.Dispose();
        }
    }

    /// <summary>Tarayıcının indirdiği dosyaya koyduğu "internetten geldi" işareti.</summary>
    private static void Mark(string path, int zone = 3) =>
        File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl=https://example.com/dosya\r\n");

    private static string Downloaded(string path)
    {
        TempRaf.Touch(path);
        Mark(path);
        return path;
    }

    private static bool Quiet(Rig rig, int ms = 1500)
    {
        Thread.Sleep(ms);
        return rig.Caught.IsEmpty;
    }

    [Fact]
    public void Baska_bir_klasore_inen_indirme_yakalanir()
    {
        using var rig = new Rig();

        var file = Downloaded(rig.At("projeler", "site", "logo.png"));

        Assert.True(TempRaf.WaitFor(() => rig.Caught.Contains(file)));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Isaretsiz_dosya_yakalanmaz()
    {
        using var rig = new Rig();

        TempRaf.Touch(rig.At("derleme-ciktisi.dll"));
        var local = TempRaf.Touch(rig.At("yerel-agdan.pdf"));
        Mark(local, zone: 1);

        Assert.True(Quiet(rig));
    }

    [Fact]
    public void Tarayici_indirmesi_bitince_ve_isareti_sonradan_yazilsa_da_yakalanir()
    {
        using var rig = new Rig();
        var partial = TempRaf.Touch(rig.At("film.mkv.crdownload"));
        Mark(partial);
        Assert.True(Quiet(rig, 900));

        // Tarayıcı önce asıl adına çevirir, işareti virüs taraması bitince yazar.
        var final = rig.At("film.mkv");
        File.Move(partial, final);
        File.Delete(final + ":Zone.Identifier");
        Assert.True(Quiet(rig, 900));
        Mark(final);

        Assert.True(TempRaf.WaitFor(() => rig.Caught.Contains(final)));
        Assert.Single(rig.Caught);
    }

    [Fact]
    public void Gecici_addan_cevrilen_dosya_isaret_gelmezse_birakilir()
    {
        using var rig = new Rig();
        var partial = TempRaf.Touch(rig.At("cikti.bin.tmp"));

        File.Move(partial, rig.At("cikti.bin"));

        Assert.True(Quiet(rig, 5500));
    }

    [Fact]
    public void Elle_yeniden_adlandirma_yeni_gelmis_sayilmaz()
    {
        using var rig = new Rig();
        // İzleme başlamadan önce de orada duran, eskiden indirilmiş bir dosya.
        rig.Catcher.Stop();
        var old = Downloaded(rig.At("eski-indirme.pdf"));
        rig.Catcher.Start(new[] { (rig.Raf.Dir, true) });

        File.Move(old, rig.At("yeni-ad.pdf"));

        Assert.True(Quiet(rig));
    }

    [Fact]
    public void Raf_in_ici_gelen_ve_izlenen_klasorlerin_dogrudan_ici_baskasinin_isidir()
    {
        string watched = "";
        using var rig = new Rig(raf =>
        {
            watched = raf.Outside("indirilenler");
            raf.Cfg.WatchFolders.Add(watched);
        });

        Downloaded(Path.Combine(rig.Raf.Inbox, "gelene-inen.pdf"));
        Downloaded(rig.Raf.InCat("Belgeler", "rafta-duran.pdf"));
        Downloaded(Path.Combine(watched, "izlenene-inen.pdf"));
        // İzlenen klasörün alt klasörüne InboxWatcher bakmaz; orası burada yakalanır.
        var nested = Downloaded(Path.Combine(watched, "alt", "icerideki.pdf"));

        Assert.True(TempRaf.WaitFor(() => rig.Caught.Contains(nested)));
        Thread.Sleep(600);
        Assert.Equal(new[] { nested }, rig.Caught);
    }

    [Fact]
    public void Gizli_dosyalar_arac_klasorleri_ve_geri_alinip_bekletilenler_yakalanmaz()
    {
        using var rig = new Rig();
        var held = rig.At("dokunma.pdf");
        rig.Raf.Cfg.Held.Add(held);

        Downloaded(held);
        Downloaded(rig.At(".git", "objects", "paket.zip"));
        Downloaded(rig.At("site", "node_modules", "lib", "index.js"));
        var hidden = Downloaded(rig.At("gizli.pdf"));
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);

        Assert.True(Quiet(rig));
    }

    [Fact]
    public void Arsiv_acmak_gibi_bir_anda_gelen_cok_sayida_dosyanin_hepsi_yakalanmaz()
    {
        using var rig = new Rig();

        for (int i = 0; i < 40; i++) Downloaded(rig.At("acilan-arsiv", $"dosya-{i}.txt"));

        Thread.Sleep(2500);
        Assert.InRange(rig.Caught.Count, 1, 12);
    }

    [Fact]
    public void Yakalanan_dosya_yeniden_adlandirilinca_ya_da_silinince_bildirilir()
    {
        using var rig = new Rig();
        var file = Downloaded(rig.At("rapor.pdf"));
        Assert.True(TempRaf.WaitFor(() => rig.Caught.Contains(file)));

        var renamed = rig.At("rapor-son.pdf");
        File.Move(file, renamed);
        Assert.True(TempRaf.WaitFor(() => rig.Moved.Contains((file, renamed))));

        File.Delete(renamed);
        Assert.True(TempRaf.WaitFor(() => rig.Gone.Contains(renamed)));
        Assert.Single(rig.Caught);
    }

    [Fact]
    public void Duzenleyip_kaydetme_yakalanmis_dosyayi_kaybettirmez()
    {
        using var rig = new Rig();
        var file = Downloaded(rig.At("sozlesme.docx"));
        Assert.True(TempRaf.WaitFor(() => rig.Caught.Contains(file)));

        // Belge düzenleyicilerin kaydetme biçimi: yenisini geçici adla yaz, eskisini kenara çek,
        // yenisini eski adıyla yerine koy, eskisini sil.
        var fresh = TempRaf.Touch(rig.At("~WRD0001.tmp"), "yeni içerik");
        var aside = rig.At("~WRL0002.tmp");
        File.Move(file, aside);
        File.Move(fresh, file);
        File.Delete(aside);

        Thread.Sleep(2000);
        Assert.Empty(rig.Gone);
        Assert.Empty(rig.Moved);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Onceki_calismadan_kalan_baglantilar_da_izlenir()
    {
        string linked = "";
        using var rig = new Rig(raf =>
        {
            linked = Downloaded(Path.Combine(raf.Outside("baska-yer"), "dunku-indirme.pdf"));
            CategoryService.AddLink(raf.Cfg, raf.Store, linked, raf.Cat("Belgeler"), auto: true);
        });

        File.Delete(linked);

        Assert.True(TempRaf.WaitFor(() => rig.Gone.Contains(linked)));
    }

    [Fact]
    public void Varsayilan_kokler_Raf_i_sistem_klasorlerini_ve_gizli_klasorleri_icermez()
    {
        using var raf = new TempRaf();

        var roots = DownloadCatcher.DefaultRoots(raf.Cfg).Select(r => r.Path).ToList();

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Contains(roots, r => RafLayout.IsSame(r, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
                                    || RafLayout.IsInside(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), r));
        Assert.DoesNotContain(roots, r => RafLayout.IsInside(r, Path.Combine(profile, "AppData")));
        Assert.DoesNotContain(roots, r => RafLayout.IsInside(r, Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.DoesNotContain(roots, r => RafLayout.IsInside(r, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.DoesNotContain(roots, r => RafLayout.IsInside(r, raf.Cfg.Root));
        Assert.DoesNotContain(roots, r => Path.GetFileName(r).StartsWith('.'));
    }
}

public class CaughtLinkTests
{
    [Fact]
    public void Kendiliginden_eklenen_baglanti_dosyasi_silinmisse_acilista_kalkar_elle_eklenen_kalir()
    {
        using var raf = new TempRaf();
        var auto = Path.Combine(raf.Outside("a"), "indirme.pdf");
        var manual = Path.Combine(raf.Outside("b"), "proje");
        CategoryService.AddLink(raf.Cfg, raf.Store, auto, raf.Cat("Belgeler"), auto: true);
        CategoryService.AddLink(raf.Cfg, raf.Store, manual, raf.Cat("Kod"));

        var reloaded = raf.Store.Load();

        Assert.Equal(new[] { RafLayout.Normalize(manual) }, reloaded.Links.Select(l => l.Path));
    }

    [Fact]
    public void Baglanti_yeni_yola_cevrilir_ve_kendiliginden_eklendigi_bilgisi_korunur()
    {
        using var raf = new TempRaf();
        var from = TempRaf.Touch(Path.Combine(raf.Outside("a"), "eski.pdf"));
        var to = Path.Combine(raf.Outside("a"), "yeni.pdf");
        CategoryService.AddLink(raf.Cfg, raf.Store, from, raf.Cat("Belgeler"), auto: true);
        File.Move(from, to);

        Assert.True(CategoryService.RetargetLink(raf.Cfg, raf.Store, from, to));
        Assert.False(CategoryService.RetargetLink(raf.Cfg, raf.Store, from, to));

        var link = Assert.Single(raf.Store.Load().Links);
        Assert.Equal(RafLayout.Normalize(to), link.Path);
        Assert.True(link.Auto);
        Assert.Equal(raf.Cat("Belgeler").Id, link.CategoryId);
    }

    [Fact]
    public void Indirme_isareti_yalnizca_internet_bolgesinde_sayilir()
    {
        using var raf = new TempRaf();
        var dir = raf.Outside("a");
        string File_(string name, string? zone)
        {
            var path = TempRaf.Touch(Path.Combine(dir, name));
            if (zone is not null) File.WriteAllText(path + ":Zone.Identifier", $"[ZoneTransfer]\r\nZoneId={zone}\r\n");
            return path;
        }

        Assert.True(ZoneInfo.HasMark(File_("internet.zip", "3")));
        Assert.True(ZoneInfo.HasMark(File_("kisitli.zip", "4")));
        Assert.False(ZoneInfo.HasMark(File_("yerel-ag.zip", "1")));
        Assert.False(ZoneInfo.HasMark(File_("isaretsiz.zip", null)));
        Assert.False(ZoneInfo.HasMark(Path.Combine(dir, "olmayan.zip")));
    }
}
