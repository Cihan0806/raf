namespace Raf.Core;

/// <summary>
/// Gelen klasörünü (ve eklenen diğer klasörleri) izler; yazılması biten dosyaları kurallara göre taşır.
/// Gelen klasöründeki her şey düzenlenir. Ek klasörlerde yalnızca izleme başladıktan sonra gelenlere dokunulur.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    private static readonly string[] TempExtensions =
    {
        ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".bc!", ".aria2",
    };

    private const int MaxFailures = 40;

    private readonly RafConfig _cfg;
    private readonly Sorter _sorter;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Timer? _timer;
    private int _ticking;

    public InboxWatcher(RafConfig cfg, Sorter sorter)
    {
        _cfg = cfg;
        _sorter = sorter;
    }

    /// <summary>Bir dosyanın "yazılması bitti" sayılması için değişmeden geçmesi gereken süre.</summary>
    public TimeSpan Quiet { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Bir öğe taşınamadığında: (yol, neden).</summary>
    public event Action<string, string>? Failed;

    private sealed class Pending
    {
        public DateTime LastEvent;
        public long Size = -1;
        public DateTime SizeSeen;
        public int Failures;
    }

    private enum Verdict { Wait, Drop, Ready }

    public void Start()
    {
        Stop();

        string inbox;
        List<string> extras;
        lock (_cfg)
        {
            inbox = RafLayout.Inbox(_cfg);
            extras = _cfg.WatchFolders.Where(f => RafLayout.WhyNotWatchable(_cfg, f) is null).ToList();
        }

        Directory.CreateDirectory(inbox);
        Watch(inbox, isInbox: true);
        foreach (var folder in extras) Watch(folder, isInbox: false);

        ScanInbox(inbox);
        _timer = new Timer(_ => Tick(), null, 400, 400);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        lock (_gate) _pending.Clear();
    }

    public void Dispose() => Stop();

    private void Watch(string folder, bool isInbox)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Created += (_, e) => OnEvent(folder, isInbox, e.FullPath, arrival: true);
        // Tarayıcılar indirme bitince geçici dosyayı asıl adına çevirir; kullanıcının elle yaptığı
        // yeniden adlandırma ise "yeni geldi" sayılmaz.
        watcher.Renamed += (_, e) => OnEvent(folder, isInbox, e.FullPath, arrival: IsTemp(Path.GetFileName(e.OldFullPath)));
        watcher.Changed += (_, e) => OnEvent(folder, isInbox, e.FullPath, arrival: false);
        watcher.Error += (_, _) => { if (isInbox) ScanInbox(folder); };
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void OnEvent(string folder, bool isInbox, string fullPath, bool arrival)
    {
        var relative = Path.GetRelativePath(folder, fullPath);
        int separator = relative.IndexOfAny(new[] { '\\', '/' });
        bool nested = separator >= 0;
        var top = Path.Combine(folder, nested ? relative[..separator] : relative);

        lock (_gate)
        {
            if (_pending.TryGetValue(top, out var existing))
            {
                existing.LastEvent = DateTime.UtcNow;
                return;
            }
            // Ek klasörlerde yalnızca doğrudan klasöre yeni düşenler alınır; eski bir alt klasörün
            // içinde olup bitenler o klasörün taşınmasına yol açmaz.
            if (isInbox || (arrival && !nested))
                _pending[top] = new Pending { LastEvent = DateTime.UtcNow };
        }
    }

    private void ScanInbox(string inbox)
    {
        try
        {
            var now = DateTime.UtcNow;
            lock (_gate)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(inbox))
                    _pending.TryAdd(entry, new Pending { LastEvent = now });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(inbox, e.Message);
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            KeyValuePair<string, Pending>[] snapshot;
            lock (_gate) snapshot = _pending.ToArray();

            var now = DateTime.UtcNow;
            foreach (var (path, pending) in snapshot)
            {
                var verdict = Check(path, pending, now);
                if (verdict == Verdict.Wait) continue;

                lock (_gate) _pending.Remove(path);
                if (verdict == Verdict.Drop) continue;

                try
                {
                    _sorter.Sort(path, auto: true);
                }
                catch (IOException) when (++pending.Failures < MaxFailures)
                {
                    pending.LastEvent = now;
                    lock (_gate) _pending[path] = pending;
                }
                catch (Exception e)
                {
                    // Zamanlayıcı iş parçacığında yakalanmayan hata uygulamayı kapatır; bildir ve sıradakine geç.
                    Failed?.Invoke(path, e.Message);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    private Verdict Check(string path, Pending pending, DateTime now)
    {
        if (IsTemp(Path.GetFileName(path))) return Verdict.Drop;

        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) return Verdict.Drop;

        lock (_cfg)
        {
            if (_cfg.Held.Any(h => RafLayout.IsSame(h, path))) return Verdict.Drop;
            if (RafLayout.IsInside(_cfg.Root, path)) return Verdict.Drop;
        }

        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0) return Verdict.Drop;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Verdict.Wait;
        }

        if (now - pending.LastEvent < Quiet) return Verdict.Wait;

        try
        {
            if (isDir)
            {
                bool stillDownloading = Directory
                    .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Any(f => IsTemp(Path.GetFileName(f)));
                return stillDownloading ? Verdict.Wait : Verdict.Ready;
            }

            // Firefox "ad.zip" adında boş bir yer tutucu açıp asıl veriyi "ad.zip.part" içine yazar.
            if (HasTempSibling(path)) return Verdict.Wait;

            long size = new FileInfo(path).Length;
            if (size != pending.Size)
            {
                pending.Size = size;
                pending.SizeSeen = now;
                return Verdict.Wait;
            }
            if (now - pending.SizeSeen < Quiet) return Verdict.Wait;

            // Başka bir program hâlâ yazıyorsa dosya paylaşımsız açılamaz.
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return Verdict.Ready;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Verdict.Wait;
        }
    }

    /// <summary>Dosyanın yanında, aynı adla başlayan yarım bir indirme (ör. "ad.zip.part") duruyor mu?</summary>
    public static bool HasTempSibling(string path) => TempExtensions.Any(t => File.Exists(path + t));

    public static bool IsTemp(string name) =>
        TempExtensions.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase))
        || name.StartsWith("~$", StringComparison.Ordinal)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
}
