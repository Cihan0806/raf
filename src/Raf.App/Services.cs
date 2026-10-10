using Raf.Core;

namespace Raf.App;

/// <summary>Uygulamanın tek örnekli parçaları; pencereler bunlara App.S üzerinden ulaşır.</summary>
public sealed class Services
{
    public required ConfigStore Store { get; init; }
    public required RafConfig Config { get; init; }
    public required Sorter Sorter { get; init; }
    public required InboxWatcher Watcher { get; init; }
    public required HotkeyManager Hotkey { get; init; }
    public required Launchables Launchables { get; init; }
    public TrayIcon? Tray { get; set; }
    public Views.SearchWidget? Widget { get; set; }

    /// <summary>Kategoriler, bağlantılar ya da izlenen klasörler değişti; görünümler yenilenmeli.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public void Save()
    {
        lock (Config) Store.Save(Config);
    }

    /// <summary>
    /// Raf'taki öğeleri ve her birinin Raf'a ne zaman girdiğini arka planda okur.
    /// Giriş zamanı taşıma geçmişinden gelir; geçmişte yoksa dosyanın kendi tarihi kullanılır.
    /// </summary>
    public Task<(List<LibraryItem> Items, Dictionary<string, DateTime> Added)> ScanAsync() => Task.Run(() =>
    {
        var items = Library.Scan(Config);
        var added = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in Sorter.HistorySnapshot().Where(r => !r.Undone))
            added.TryAdd(record.To, record.Time);
        return (items, added);
    });

    public string Inbox
    {
        get { lock (Config) return RafLayout.Inbox(Config); }
    }

    public bool IsWatching(string folder)
    {
        lock (Config) return Config.WatchFolders.Any(f => RafLayout.IsSame(f, folder));
    }

    /// <summary>Klasörü izlemeye alır. Başarılıysa null, değilse nedenini döndürür.</summary>
    public string? Watch(string folder)
    {
        lock (Config)
        {
            if (Config.WatchFolders.Any(f => RafLayout.IsSame(f, folder))) return null;
            var why = RafLayout.WhyNotWatchable(Config, folder);
            if (why is not null) return why;
            Config.WatchFolders.Add(RafLayout.Normalize(folder));
            Store.Save(Config);
        }
        Watcher.Start();
        NotifyChanged();
        return null;
    }

    public void Unwatch(string folder)
    {
        lock (Config)
        {
            Config.WatchFolders.RemoveAll(f => RafLayout.IsSame(f, folder));
            Store.Save(Config);
        }
        Watcher.Start();
        NotifyChanged();
    }
}
