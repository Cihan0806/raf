using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Raf.App.Views;
using Raf.Core;

namespace Raf.App;

/// <summary>
/// Komut satırı: --tray (pencere açmadan başla), --data &lt;klasör&gt; (ayarları ve Raf klasörünü orada tut).
/// </summary>
public partial class App : Application
{
    public static Services S { get; private set; } = null!;

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showSignal;
    private MainWindow? _main;
    private QuickBar? _quick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = ArgValue(e.Args, "--data");
        bool trayOnly = e.Args.Contains("--tray");

        // Aynı veri klasörüyle ikinci kez açılırsa yeni örnek başlamaz; çalışan örnek penceresini gösterir.
        var instance = dataDir is null
            ? "varsayilan"
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToLowerInvariant())))[..12];
        _mutex = new Mutex(initiallyOwned: true, $@"Local\Raf.Tek.{instance}", out _ownsMutex);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\Raf.Goster.{instance}");
        if (!_ownsMutex)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        FrontWindow.Start();
#if DEBUG
        if (ArgValue(e.Args, "--theme") is { } theme) Theme.Forced = theme != "light";
#endif
        Theme.Apply(this);
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        try
        {
            var store = new ConfigStore(dataDir);
            var cfg = store.Load();
            RafLayout.EnsureFolders(cfg);
            var sorter = new Sorter(cfg, store);
            S = new Services
            {
                Store = store,
                Config = cfg,
                Sorter = sorter,
                Watcher = new InboxWatcher(cfg, sorter),
                Catcher = new DownloadCatcher(cfg),
                Hotkey = new HotkeyManager { PauseInFullscreen = cfg.PauseHotkeyInFullscreen },
                Launchables = new Launchables(cfg),
            };
            // Uygulama listesi arka planda hazırlanır; ilk Alt+Space'e yetişsin diye hemen başlatılır.
            _ = S.Launchables.RefreshAsync(TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Raf klasörü hazırlanamadı:\n\n" + ex.Message, "Raf", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Kısayol tuşu küçük arama çubuğunu açar; tam pencere çubuktan, tepsi simgesinden
        // ya da Raf yeniden çalıştırılınca açılır.
        _main = new MainWindow();
        _quick = new QuickBar(_main.ShowLauncher, _main.ShowDefaultView);
        S.Widget = new SearchWidget((anchor, closed) => _quick.ShowBar(anchor, closed), _main.ShowLauncher);
        S.Widget.ApplySetting();
        S.Tray = new TrayIcon(_main, Quit);
        S.Hotkey.Pressed += _quick.Toggle;
        S.Watcher.Failed += (path, reason) =>
            Dispatcher.InvokeAsync(() => S.Tray?.Notify("Raf bir öğeyi taşıyamadı", $"{Path.GetFileName(path)}: {reason}"));
        S.Watcher.Start();

        S.Catcher.Caught += S.OnCaught;
        S.Catcher.Moved += S.OnCaughtMoved;
        S.Catcher.Gone += S.OnCaughtGone;
        S.Catcher.Failed += (path, reason) =>
            Dispatcher.InvokeAsync(() => S.Tray?.Notify("Raf bir öğeyi taşıyamadı", $"{Path.GetFileName(path)}: {reason}"));
        S.ApplyCatcher();

        if (!S.Hotkey.Register(S.Config.Hotkey))
        {
            S.Tray.Notify("Kısayol tuşu kullanılamıyor",
                $"{S.Config.Hotkey} başka bir program tarafından alınmış. Ayarlar'dan başka bir tuş seçebilirsin.");
        }

        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.InvokeAsync(() => _main.ShowLauncher());
        })
        { IsBackground = true, Name = "Raf.Goster" }.Start();

#if DEBUG
        if (ArgValue(e.Args, "--shot") is { } shotDir)
        {
            _shotMode = true;
            _main.ShowLauncher(activate: false);
            RunShots(shotDir);
            return;
        }
#endif
        if (!trayOnly) _main.ShowLauncher();
    }

    public void Quit()
    {
        PrepareExit();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        PrepareExit();
        base.OnSessionEnding(e);
    }

    /// <summary>Pencereler normalde kapatılınca yalnızca gizlenir; çıkarken gerçekten kapanmalarına izin verir.</summary>
    private void PrepareExit()
    {
        _main?.PrepareExit();
        if (_quick is not null) _quick.Exiting = true;
        if (S?.Widget is not null) S.Widget.Exiting = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        FrontWindow.Stop();
        if (S is not null)
        {
            S.Watcher.Dispose();
            S.Catcher.Dispose();
            S.Hotkey.Dispose();
            S.Tray?.Dispose();
        }
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Dispatcher.InvokeAsync(() => Theme.Apply(this));
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Beklenmeyen bir hata arka planda çalışan düzenlemeyi durdurmasın: kaydet, göster, devam et.
        e.Handled = true;
        string logPath = "";
        try
        {
            logPath = Path.Combine(S?.Store.DataDir ?? Path.GetTempPath(), "hata.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logPath = "";
        }

#if DEBUG
        // Görüntü modunda pencere açıp beklemek yerine çık; hata hata.log'dan okunur.
        if (_shotMode)
        {
            Quit();
            return;
        }
#endif
        var where = logPath.Length > 0 ? $"\n\nAyrıntı şuraya yazıldı:\n{logPath}" : "";
        MessageDialog.Info(_main, "Beklenmeyen bir hata oluştu", e.Exception.Message + where);
    }

    private static string? ArgValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

#if DEBUG
    private bool _shotMode;

    /// <summary>Geliştirme yardımcısı: her ekranın görüntüsünü klasöre kaydedip çıkar.</summary>
    private async void RunShots(string dir)
    {
        Directory.CreateDirectory(dir);
        var main = _main!;

        async Task Shot(string name, Window? window = null)
        {
            await Task.Delay(900);
            Snap(window ?? main, Path.Combine(dir, name + ".png"));
        }

        await Task.Delay(1800);
        await Shot("1-tumu");
        main.DebugGo("first-category");
        await Shot("2-kategori");
        main.DebugGo("search:mod");
        await Shot("3-arama");
        main.DebugGo("search:");

        main.DebugGo("details");
        await Shot("13-ayrinti-listesi");
        main.DebugGo("all");
        main.DebugGo("tiles");
        main.DebugGo("preview");
        main.DebugGo("select:manzara.jpg");
        await Shot("14-onizleme-resim");
        main.DebugGo("select:toplantı notları.txt");
        await Shot("15-onizleme-metin");
        main.DebugGo("search:santral");
        await Task.Delay(1200);
        main.DebugGo("select:hizmet sözleşmesi.docx");
        await Shot("16-icerik-arama");
        main.DebugGo("search:");
        main.DebugGo("no-preview");

        main.DebugGo("categories");
        await Shot("4-kategoriler");
        main.DebugGo("history");
        await Shot("5-gecmis");
        main.DebugGo("settings");
        await Shot("6-ayarlar");

        // Boş Raf'ta (ilk açılış) alt kategori yoktur; ilk kategori gösterilir.
        var edited = S.Config.Categories.FirstOrDefault(c => c.ParentId.Length > 0) ?? S.Config.Categories[0];
        var edit = new CategoryDialog(S.Config, edited) { Owner = main, ShowActivated = false };
        edit.Show();
        await Shot("7-kategori-duzenle", edit);
        edit.Close();

        var ask = new MessageDialog("“webpanel” Raf'a nasıl eklensin?",
            "Taşı: öğe şu anki yerinden alınır ve kurallara göre uygun kategoriye konur.\n\nBağlantı ekle: öğe olduğu yerde kalır, Raf'ta yalnızca kısayolu görünür.",
            new[] { "Taşı", "Bağlantı ekle", "Vazgeç" }) { Owner = main, ShowActivated = false };
        ask.Show();
        await Shot("8-soru", ask);
        ask.Close();

        var quick = _quick!;
        await quick.DebugShow("");
        await Shot("9-cubuk-bos", quick);
        await quick.DebugShow("mod");
        await Shot("10-cubuk-arama", quick);
        await quick.DebugShow("pdf");
        await Shot("11-cubuk-uzanti", quick);
        await quick.DebugShow("yokboyle");
        await Shot("12-cubuk-sonuc-yok", quick);
        await quick.DebugShow("brave");
        await Shot("17-cubuk-uygulama", quick);
        await quick.DebugShow("bilg");
        await Shot("18-cubuk-sistem", quick);
        if (S.Widget is not null) await Shot("19-kutucuk", S.Widget);
        await quick.DebugShow("", compact: true);
        await Shot("20-kutucuk-acik-bos", quick);
        await quick.DebugShow("st", compact: true);
        await Shot("21-kutucuk-acik-sonuc", quick);
        await quick.DebugShow("raf");
        await Shot("22-cubuk-raf", quick);
        quick.Hide();

        // İçeri alma, örnek bir "masaüstü" klasörüyle baştan sona: önizleme, uygulama, geri alma.
        var messy = Path.Combine(S.Store.DataDir, "ornek-masaustu");
        Directory.CreateDirectory(Path.Combine(messy, "proje"));
        Directory.CreateDirectory(Path.Combine(messy, "okul ödevleri"));
        foreach (var name in new[] { "tatil.jpg", "fatura.pdf", "Oyun.lnk", "notlar.xyz", "kurulum.exe", "film.mkv.crdownload" })
            File.WriteAllText(Path.Combine(messy, name), "örnek");

        // Gerçek kaynak listesiyle (masaüstü ve bilinen klasörler) yalnızca görünüm; hiçbir şey içeri alınmaz.
        var real = new ImportDialog { Owner = main, ShowActivated = false };
        real.Show();
        await Task.Delay(1500);
        await Shot("26-iceri-al-gercek", real);
        real.Close();

        main.DebugGo("all");
        _ = Dispatcher.InvokeAsync(() => main.ImportExisting(new[] { messy }, activate: false));
        await Task.Delay(1200);
        var import = Windows.OfType<ImportDialog>().First();
        await Shot("23-iceri-al", import);
        import.DebugImport();
        await Task.Delay(1500);
        await Shot("24-iceri-alindi");
        File.WriteAllLines(Path.Combine(dir, "iceri-alma.txt"), new[]
        {
            "iceri alindiktan sonra kaynakta kalanlar: " + string.Join(", ", Directory.GetFileSystemEntries(messy).Select(Path.GetFileName).Order()),
            "baglantilar: " + string.Join(", ", S.Config.Links.Select(l => Path.GetFileName(l.Path)).Order()),
        });
        main.DebugGo("snack-undo");
        await Task.Delay(1500);
        await Shot("25-iceri-alma-geri-alindi");
        File.AppendAllLines(Path.Combine(dir, "iceri-alma.txt"), new[]
        {
            "geri alindiktan sonra kaynakta: " + string.Join(", ", Directory.GetFileSystemEntries(messy).Select(Path.GetFileName).Order()),
            "baglantilar: " + string.Join(", ", S.Config.Links.Select(l => Path.GetFileName(l.Path)).Order()),
        });

        Quit();
    }

    private static void Snap(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
#endif
}
