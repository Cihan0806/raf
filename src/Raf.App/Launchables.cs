using Raf.Core;

namespace Raf.App;

public enum LaunchKind
{
    /// <summary>Başlat menüsündeki bir uygulama.</summary>
    App,

    /// <summary>Bu bilgisayar, Geri Dönüşüm Kutusu gibi bir sistem yeri.</summary>
    Place,

    /// <summary>Masaüstünde duran bir dosya, klasör ya da kısayol.</summary>
    Desktop,
}

/// <param name="Target">Açmak için Windows'a verilecek ad: dosya yolu ya da "shell:..." adı.</param>
/// <param name="IconSource">Simgesinin isteneceği ad (dosya yolu ya da kabuk adı).</param>
public sealed record Launchable(string Name, string FoldedName, string Target, LaunchKind Kind, string IconSource, bool IsDirectory);

/// <summary>
/// Raf'ta olmayan ama hızlı aramada çıkması beklenen şeylerin listesi: kurulu uygulamalar, sistem
/// yerleri ve masaüstündekiler. Yalnızca okunur; hiçbir şey taşınmaz ya da Raf'a eklenmez.
/// </summary>
public sealed class Launchables
{
    private const string AppsFolder = "shell:AppsFolder";

    // Adlar Windows'tan o anki dilde okunur; okunamazsa buradaki ad kullanılır.
    private static readonly (string Target, string Name)[] Places =
    {
        ("shell:MyComputerFolder", "Bu bilgisayar"),
        ("shell:RecycleBinFolder", "Geri Dönüşüm Kutusu"),
        ("shell:ControlPanelFolder", "Denetim Masası"),
        ("shell:NetworkPlacesFolder", "Ağ"),
        ("shell:Desktop", "Masaüstü"),
        ("shell:Personal", "Belgeler"),
        ("shell:Downloads", "İndirilenler"),
        ("shell:My Pictures", "Resimler"),
        ("shell:My Music", "Müzik"),
        ("shell:My Video", "Videolar"),
    };

    private readonly RafConfig _cfg;
    private readonly object _gate = new();
    private volatile IReadOnlyList<Launchable> _items = Array.Empty<Launchable>();
    private DateTime _loadedAt = DateTime.MinValue;
    private Task? _loading;

    public Launchables(RafConfig cfg)
    {
        _cfg = cfg;
    }

    public IReadOnlyList<Launchable> Items => _items;

    /// <summary>Liste yenilendiğinde tetiklenir (kabuk iş parçacığından).</summary>
    public event Action? Updated;

    /// <summary>Liste <paramref name="maxAge"/> süresinden eskiyse arka planda yeniden okur.</summary>
    public Task RefreshAsync(TimeSpan maxAge)
    {
        lock (_gate)
        {
            if (_loading is { IsCompleted: false }) return _loading;
            if (DateTime.UtcNow - _loadedAt < maxAge) return Task.CompletedTask;

            return _loading = ShellThread.Run(() =>
            {
                try
                {
                    _items = _cfg.SearchApps ? Load() : Array.Empty<Launchable>();
                }
                catch (Exception)
                {
                    // Kabuk çağrıları Windows'un hata koduna göre çok farklı türde hata fırlatır. Liste
                    // okunamadıysa eski liste kalır; arama Raf'taki öğelerle çalışmaya devam eder.
                }
                _loadedAt = DateTime.UtcNow;
                Updated?.Invoke();
                return true;
            });
        }
    }

    private static List<Launchable> Load()
    {
        var list = new List<Launchable>();
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;

        // Başlat menüsünün gösterdiği her şey: klasik programlar ve Mağaza uygulamaları.
        var appNames = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (dynamic item in shell.NameSpace(AppsFolder).Items())
            {
                string name = item.Name;
                string id = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id)) continue;

                var target = AppsFolder + "\\" + id;
                var folded = TextUtil.Fold(name);
                appNames.Add(folded);
                list.Add(new Launchable(name, folded, target, LaunchKind.App, target, IsDirectory: false));
            }
        }
        catch (Exception)
        {
            // Uygulamalar okunamadıysa okunabilen kadarı kalır; yerler ve masaüstü yine listelenir.
        }

        foreach (var (target, fallbackName) in Places)
        {
            string name = fallbackName;
            string icon = target;
            try
            {
                dynamic? folder = shell.NameSpace(target);
                if (folder is null) continue;
                name = folder.Self.Name;
                icon = folder.Self.Path;
            }
            catch (Exception)
            {
                // Adı okunamayan yer, yedek adıyla listelenir.
            }
            list.Add(new Launchable(name, TextUtil.Fold(name), target, LaunchKind.Place, icon, IsDirectory: true));
        }

        foreach (var desktop in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            var dir = Environment.GetFolderPath(desktop);
            if (dir.Length == 0 || !Directory.Exists(dir)) continue;

            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

                bool isDir = info is DirectoryInfo;
                bool isShortcut = !isDir && info.Extension.ToLowerInvariant() is ".lnk" or ".url";
                var name = isShortcut ? Path.GetFileNameWithoutExtension(info.Name) : info.Name;
                var folded = TextUtil.Fold(name);
                // Masaüstündeki "Brave" kısayolu, uygulamalar listesindeki "Brave" ile aynı şeydir; iki kez çıkmasın.
                if (isShortcut && appNames.Contains(folded)) continue;

                list.Add(new Launchable(name, folded, info.FullName, LaunchKind.Desktop, info.FullName, isDir));
            }
        }
        return list;
    }
}
