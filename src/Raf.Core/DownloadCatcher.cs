namespace Raf.Core;

/// <summary>
/// İnternetten indirilen dosyaları, hangi klasöre kaydedilirse kaydedilsin fark eder.
/// </summary>
/// <remarks>
/// Kullanıcının klasörleri ve diğer sürücüler alt klasörleriyle birlikte izlenir; ama yeni oluşan her
/// dosya değil, yalnızca Windows'un "internetten geldi" diye işaretlediği dosyalar yakalanır
/// (bkz. <see cref="ZoneInfo.HasMark"/>). Böylece programların kendi ürettiği dosyalara (derleme
/// çıktıları, oyun dosyaları, önbellekler) hiç dokunulmaz. Gelen klasörünün ve izlenen klasörlerin
/// doğrudan içine düşenler <see cref="InboxWatcher"/>'ın işidir; burada atlanır.
/// </remarks>
public sealed class DownloadCatcher : IDisposable
{
    // Bir derleme ya da kurulum saniyede binlerce dosya oluşturabilir; kuyruk bu kadarla sınırlı
    // kalır, fazlası bakılmadan geçilir.
    private const int MaxPending = 2000;

    // İndirilmiş bir arşivi açmak ya da bir klasörü kopyalamak onlarca işaretli dosyayı bir anda
    // oluşturur. Bunlar tek tek indirilmiş sayılmaz: kısa sürede bu kadardan fazlası yakalanmaz.
    private const int BurstLimit = 12;
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(10);

    private readonly RafConfig _cfg;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Vacated> _vacated = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _recent = new();
    private readonly object _gate = new();
    private HashSet<string> _handledElsewhere = new(StringComparer.OrdinalIgnoreCase);
    private string _rafRoot = "";
    private Timer? _timer;
    private int _ticking;

    public DownloadCatcher(RafConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>Bir dosyanın "yazılması bitti" sayılması için değişmeden geçmesi gereken süre.</summary>
    public TimeSpan Quiet { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Tarayıcı, indirmeyi asıl adına çevirdikten sonra işaretini yazana kadar (virüs taraması sürerken)
    /// beklenecek en uzun süre.
    /// </summary>
    public TimeSpan MarkWait { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// true ise dosya başka bir program tarafından açıkken yakalanmaz, kapanması beklenir. Dosya
    /// taşınacaksa gerekir; yerinde bırakılacaksa gerekmez.
    /// </summary>
    public bool RequireExclusive { get; set; }

    /// <summary>İndirilmiş yeni bir dosya yerleşti: (yol). Arka plan iş parçacığından gelir.</summary>
    public event Action<string>? Caught;

    /// <summary>Daha önce yakalanmış bir dosyanın adı ya da yeri değişti: (eski yol, yeni yol).</summary>
    public event Action<string, string>? Moved;

    /// <summary>Daha önce yakalanmış bir dosya artık yok: (yol).</summary>
    public event Action<string>? Gone;

    /// <summary>Yakalanan dosya işlenirken hata oldu: (yol, neden).</summary>
    public event Action<string, string>? Failed;

    private sealed class Pending
    {
        public DateTime LastEvent;
        public DateTime MarkDeadline;
        public long Size = -1;
        public DateTime SizeSeen;
    }

    private readonly record struct Vacated(DateTime At, string? MovedTo);

    private enum Verdict { Wait, Drop, Ready }

    /// <param name="roots">İzlenecek klasörler ve alt klasörlerinin de izlenip izlenmeyeceği; verilmezse <see cref="DefaultRoots"/>.</param>
    public void Start(IEnumerable<(string Path, bool Recursive)>? roots = null)
    {
        Stop();

        List<string> tracked;
        lock (_cfg)
        {
            _rafRoot = RafLayout.Normalize(_cfg.Root);
            _handledElsewhere = new HashSet<string>(
                _cfg.WatchFolders.Append(RafLayout.Inbox(_cfg)).Select(RafLayout.Normalize),
                StringComparer.OrdinalIgnoreCase);
            tracked = _cfg.Links.Where(l => l.Auto).Select(l => RafLayout.Normalize(l.Path)).ToList();
        }
        lock (_gate)
        {
            foreach (var path in tracked) _tracked.Add(path);
        }

        foreach (var (path, recursive) in roots ?? DefaultRoots(_cfg)) Watch(path, recursive);
        _timer = new Timer(_ => Tick(), null, 500, 500);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        lock (_gate)
        {
            _pending.Clear();
            _vacated.Clear();
            _tracked.Clear();
            _recent.Clear();
        }
    }

    public void Dispose() => Stop();

    /// <summary>Yakalanmış bir dosyayı izlemeyi bırakır (kullanıcı bağlantısını kaldırdıysa).</summary>
    public void Forget(string path)
    {
        lock (_gate) _tracked.Remove(RafLayout.Normalize(path));
    }

    /// <summary>
    /// İndirme inebilecek yerler: kullanıcı klasörünün içindeki klasörler (Masaüstü, Belgeler,
    /// İndirilenler, OneDrive…), sistem sürücüsündeki diğer klasörler ve öteki sabit sürücüler.
    /// Windows'un, programların ve gizli klasörlerin (AppData gibi) içi izlenmez. Çalışırken açılan
    /// yeni üst düzey klasörler bir sonraki başlatmada listeye girer.
    /// </summary>
    public static List<(string Path, bool Recursive)> DefaultRoots(RafConfig cfg)
    {
        string rafRoot;
        lock (cfg) rafRoot = cfg.Root;

        var roots = new List<(string, bool)>();
        void AddChildren(string parent, IReadOnlyCollection<string> skip)
        {
            try
            {
                foreach (var dir in new DirectoryInfo(parent).EnumerateDirectories())
                {
                    if ((dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (dir.Name[0] is '$' or '.' || skip.Any(s => RafLayout.IsSame(s, dir.FullName))) continue;
                    if (RafLayout.IsInside(dir.FullName, rafRoot)) continue;
                    roots.Add((dir.FullName, true));
                }
                // Klasörün doğrudan içine kaydedilen dosyalar için.
                roots.Add((parent, false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Okunamayan klasör izlenmez.
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "";
        var notForUsers = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
        }
            .Select(Environment.GetFolderPath)
            .Append(Path.GetDirectoryName(profile) ?? "")
            .Append(Path.Combine(systemDrive, "PerfLogs"))
            .Append(Path.Combine(systemDrive, "Recovery"))
            .Append(Path.Combine(systemDrive, "Windows.old"))
            .Where(p => p.Length > 0)
            .ToList();

        // AppData çoğu bilgisayarda gizlidir ama gizliliği kaldırılmış olabilir; adıyla da dışarıda tutulur.
        var appData = new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData }
            .Select(f => Path.GetDirectoryName(Environment.GetFolderPath(f)) ?? "")
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Directory.Exists(profile)) AddChildren(profile, appData);

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            if (RafLayout.IsSame(root, systemDrive)) AddChildren(root, notForUsers);
            // Sistem sürücüsü dışındakilerde gürültü azdır; sürücünün tamamı tek izleyiciyle izlenir.
            else roots.Add((root, true));
        }
        return roots;
    }

    private void Watch(string folder, bool recursive)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                InternalBufferSize = 64 * 1024,
                // Yalnızca dosya oluşturma, silme ve yeniden adlandırma; içerik değişiklikleri gelmez.
                NotifyFilter = NotifyFilters.FileName,
            };
            watcher.Created += (_, e) => OnArrival(e.FullPath, fromTemp: false);
            watcher.Renamed += (_, e) => OnRenamed(e.OldFullPath, e.FullPath);
            watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // İzlenemeyen klasör (erişim yok, sürücü çıkarılmış) atlanır; diğerleri izlenmeye devam eder.
        }
    }

    /// <summary>
    /// Yol, yakalanabilecek bir dosyanın yolu mu? İzlenen ağaçlardaki her dosya olayı için çağrılır;
    /// diske dokunmaz.
    /// </summary>
    private bool IsCandidate(string path)
    {
        if (InboxWatcher.IsTemp(Path.GetFileName(path))) return false;
        if (RafLayout.IsInside(path, _rafRoot)) return false;

        var dir = Path.GetDirectoryName(path);
        if (dir is null || _handledElsewhere.Contains(dir)) return false;

        // Adı noktayla başlayan klasörler (.git, .gradle, .venv…) ve node_modules araçların kendi
        // alanıdır; içlerinde bir anda on binlerce dosya oluşur.
        return !path.Contains(@"\.", StringComparison.Ordinal)
            && !path.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(@"\$RECYCLE.BIN\", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(@"\System Volume Information\", StringComparison.OrdinalIgnoreCase);
    }

    private void OnArrival(string path, bool fromTemp)
    {
        if (!IsCandidate(path)) return;

        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_pending.TryGetValue(path, out var pending))
            {
                if (_pending.Count >= MaxPending) return;
                _pending[path] = pending = new Pending();
            }
            pending.LastEvent = now;
            if (fromTemp) pending.MarkDeadline = now + MarkWait;
        }
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        lock (_gate)
        {
            if (_tracked.Contains(oldPath))
            {
                // Hemen karar verilmez: belge düzenleyiciler kaydederken asıl dosyayı geçici bir ada
                // çevirip yenisini eski adıyla yerine koyar. Kim nerede kaldı, ortalık durulunca bakılır.
                _vacated[oldPath] = new Vacated(DateTime.UtcNow, newPath);
                return;
            }
        }

        // Tarayıcılar indirme bitince geçici dosyayı asıl adına çevirir; kullanıcının elle yaptığı
        // yeniden adlandırma ise "yeni geldi" sayılmaz.
        if (InboxWatcher.IsTemp(Path.GetFileName(oldPath))) OnArrival(newPath, fromTemp: true);
    }

    private void OnDeleted(string path)
    {
        lock (_gate)
        {
            if (_tracked.Contains(path)) _vacated.TryAdd(path, new Vacated(DateTime.UtcNow, null));
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            SettleVacated(now);

            KeyValuePair<string, Pending>[] snapshot;
            lock (_gate) snapshot = _pending.ToArray();

            foreach (var (path, pending) in snapshot)
            {
                var verdict = Check(path, pending, now);
                if (verdict == Verdict.Wait) continue;

                lock (_gate)
                {
                    _pending.Remove(path);
                    if (verdict == Verdict.Drop) continue;

                    while (_recent.Count > 0 && now - _recent.Peek() > BurstWindow) _recent.Dequeue();
                    if (_recent.Count >= BurstLimit) continue;
                    _recent.Enqueue(now);
                    _tracked.Add(path);
                }
                Raise(path, () => Caught?.Invoke(path));
            }
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    /// <summary>Yakalanmış dosyalardan yeri değişenleri ve silinenleri, ortalık durulduktan sonra bildirir.</summary>
    private void SettleVacated(DateTime now)
    {
        KeyValuePair<string, Vacated>[] due;
        lock (_gate)
        {
            due = _vacated.Where(v => now - v.Value.At >= Quiet + Quiet).ToArray();
            foreach (var (path, _) in due) _vacated.Remove(path);
        }

        foreach (var (path, vacated) in due)
        {
            // Aynı adla yerine bir dosya konmuşsa (düzenleyip kaydetme) dosya hâlâ odur.
            if (File.Exists(path)) continue;

            if (vacated.MovedTo is { } movedTo && File.Exists(movedTo) && IsCandidate(movedTo))
            {
                lock (_gate)
                {
                    _tracked.Remove(path);
                    _tracked.Add(movedTo);
                }
                Raise(path, () => Moved?.Invoke(path, movedTo));
            }
            else
            {
                lock (_gate) _tracked.Remove(path);
                Raise(path, () => Gone?.Invoke(path));
            }
        }
    }

    private void Raise(string path, Action notify)
    {
        try
        {
            notify();
        }
        catch (Exception e)
        {
            // Zamanlayıcı iş parçacığında yakalanmayan hata uygulamayı kapatır; bildir ve sıradakine geç.
            Failed?.Invoke(path, e.Message);
        }
    }

    private Verdict Check(string path, Pending pending, DateTime now)
    {
        if (now - pending.LastEvent < Quiet) return Verdict.Wait;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System)) != 0) return Verdict.Drop;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Verdict.Drop;
        }

        // Buradan sonrası yalnızca indirilmiş dosyalar için: başka programların yeni oluşturduğu
        // dosyalar açılmaz, kilitlenmez.
        if (!ZoneInfo.HasMark(path)) return now < pending.MarkDeadline ? Verdict.Wait : Verdict.Drop;

        lock (_cfg)
        {
            if (_cfg.Held.Any(h => RafLayout.IsSame(h, path))) return Verdict.Drop;
        }

        try
        {
            // Firefox "ad.zip" adında boş bir yer tutucu açıp asıl veriyi "ad.zip.part" içine yazar.
            if (InboxWatcher.HasTempSibling(path)) return Verdict.Wait;

            long size = new FileInfo(path).Length;
            if (size != pending.Size)
            {
                pending.Size = size;
                pending.SizeSeen = now;
                return Verdict.Wait;
            }
            if (now - pending.SizeSeen < Quiet) return Verdict.Wait;

            if (RequireExclusive)
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            return Verdict.Ready;
        }
        catch (FileNotFoundException)
        {
            return Verdict.Drop;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Verdict.Wait;
        }
    }
}
