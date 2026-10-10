using Raf.Core;

namespace Raf.Tests;

public class InboxWatcherTests
{
    private static InboxWatcher Start(TempRaf raf)
    {
        var watcher = new InboxWatcher(raf.Cfg, raf.Sorter) { Quiet = TimeSpan.FromMilliseconds(300) };
        watcher.Start();
        return watcher;
    }

    [Fact]
    public void Gelene_dusen_dosya_kategorisine_tasinir()
    {
        using var raf = new TempRaf();
        using var watcher = Start(raf);

        TempRaf.Touch(Path.Combine(raf.Inbox, "tatil.png"));

        Assert.True(TempRaf.WaitFor(() => File.Exists(raf.InCat("Resimler", "tatil.png"))));
        Assert.True(raf.Sorter.HistorySnapshot().Single().Auto);
    }

    [Fact]
    public void Acilista_gelende_bekleyenler_de_duzenlenir()
    {
        using var raf = new TempRaf();
        TempRaf.Touch(Path.Combine(raf.Inbox, "eski.zip"));

        using var watcher = Start(raf);

        Assert.True(TempRaf.WaitFor(() => File.Exists(raf.InCat("Arşivler", "eski.zip"))));
    }

    [Fact]
    public void Indirme_bitmeden_dosyaya_dokunulmaz()
    {
        using var raf = new TempRaf();
        using var watcher = Start(raf);
        var partial = Path.Combine(raf.Inbox, "film.mp4.crdownload");

        TempRaf.Touch(partial);
        Thread.Sleep(1500);
        Assert.True(File.Exists(partial));

        File.Move(partial, Path.Combine(raf.Inbox, "film.mp4"));
        Assert.True(TempRaf.WaitFor(() => File.Exists(raf.InCat("Videolar", "film.mp4"))));
    }

    [Fact]
    public void Baska_program_yazarken_bekler_birakinca_tasir()
    {
        using var raf = new TempRaf();
        using var watcher = Start(raf);
        var file = Path.Combine(raf.Inbox, "kayit.txt");

        using (var writer = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            writer.WriteByte(1);
            writer.Flush();
            Thread.Sleep(1500);
            Assert.True(File.Exists(file));
        }

        Assert.True(TempRaf.WaitFor(() => File.Exists(raf.InCat("Belgeler", "kayit.txt"))));
    }

    [Fact]
    public void Geri_alinan_dosya_yeniden_tasinmaz()
    {
        using var raf = new TempRaf();
        using var watcher = Start(raf);
        var file = TempRaf.Touch(Path.Combine(raf.Inbox, "rapor.pdf"));
        Assert.True(TempRaf.WaitFor(() => raf.Sorter.HistorySnapshot().Count == 1));

        Assert.Null(raf.Sorter.Undo(raf.Sorter.HistorySnapshot()[0]));
        Thread.Sleep(1500);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Ek_klasorde_eskilere_dokunmaz_yalnizca_yeni_geleni_tasir()
    {
        using var raf = new TempRaf();
        var downloads = raf.Outside("indirilenler");
        var old = TempRaf.Touch(Path.Combine(downloads, "eski.pdf"));
        var oldFolder = Path.Combine(downloads, "eski-klasor");
        TempRaf.Touch(Path.Combine(oldFolder, "a.txt"));
        raf.Cfg.WatchFolders.Add(downloads);
        using var watcher = Start(raf);

        // Eski dosyayı düzenlemek, eski klasörün içine yazmak ve elle yeniden adlandırmak "yeni geldi" sayılmaz.
        File.AppendAllText(old, "ek");
        TempRaf.Touch(Path.Combine(oldFolder, "b.txt"));
        var renamed = Path.Combine(downloads, "eski-yeni-ad.pdf");
        File.Move(old, renamed);
        TempRaf.Touch(Path.Combine(downloads, "yeni.pdf"));

        Assert.True(TempRaf.WaitFor(() => File.Exists(raf.InCat("Belgeler", "yeni.pdf"))));
        Thread.Sleep(1200);
        Assert.True(File.Exists(renamed));
        Assert.True(File.Exists(Path.Combine(oldFolder, "b.txt")));
        Assert.Single(raf.Sorter.HistorySnapshot());
    }

    [Fact]
    public void Gelene_dusen_klasor_butun_olarak_tasinir()
    {
        using var raf = new TempRaf();
        using var watcher = Start(raf);
        var folder = Path.Combine(raf.Inbox, "proje");

        TempRaf.Touch(Path.Combine(folder, "bir.txt"));
        TempRaf.Touch(Path.Combine(folder, "alt", "iki.txt"));

        var moved = raf.InCat("Diğer", "proje");
        Assert.True(TempRaf.WaitFor(() => File.Exists(Path.Combine(moved, "alt", "iki.txt"))));
        Assert.True(File.Exists(Path.Combine(moved, "bir.txt")));
        Assert.False(Directory.Exists(folder));
    }
}
