namespace Raf.Core;

/// <summary>Öğeleri kategorilere taşır, her taşımayı kaydeder ve geri alabilir.</summary>
public sealed class Sorter
{
    private readonly RafConfig _cfg;
    private readonly ConfigStore _store;
    private readonly List<MoveRecord> _history;

    public Sorter(RafConfig cfg, ConfigStore store)
    {
        _cfg = cfg;
        _store = store;
        _history = store.LoadHistory();
    }

    /// <summary>Bir taşıma, yeniden adlandırma ya da geri alma sonrası tetiklenir. Arka plan iş parçacığından gelebilir.</summary>
    public event Action<MoveRecord>? Moved;

    public List<MoveRecord> HistorySnapshot()
    {
        lock (_history) return _history.ToList();
    }

    public Category Classify(string path)
    {
        bool isDir = Directory.Exists(path);
        var source = isDir ? null : ZoneInfo.ReadSource(path);
        lock (_cfg) return RuleEngine.Classify(_cfg.PriorityOrder(), Path.GetFileName(RafLayout.Normalize(path)), isDir, source);
    }

    /// <summary>Öğeyi kurallara göre bulduğu kategoriye taşır. Zaten oradaysa null döner.</summary>
    public MoveRecord? Sort(string path, bool auto = true) => MoveTo(path, Classify(path), auto);

    public MoveRecord? MoveTo(string path, Category target, bool auto = false)
    {
        string destDir;
        string targetName;
        lock (_cfg)
        {
            destDir = RafLayout.Dir(_cfg, target);
            targetName = _cfg.PathName(target);
        }

        path = RafLayout.Normalize(path);
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) throw new FileNotFoundException("Öğe bulunamadı.", path);
        if (RafLayout.IsSame(Path.GetDirectoryName(path)!, destDir)) return null;
        if (isDir && RafLayout.IsInside(destDir, path)) throw new IOException("Bir klasör kendi içine taşınamaz.");

        Directory.CreateDirectory(destDir);
        var dest = RafLayout.UniquePath(destDir, Path.GetFileName(path), isDir);
        MoveEntry(path, dest, isDir);

        var record = new MoveRecord
        {
            From = path,
            To = dest,
            CategoryId = target.Id,
            CategoryName = targetName,
            Time = DateTime.Now,
            Auto = auto,
        };
        Remember(record);
        Unhold(path);

        Moved?.Invoke(record);
        return record;
    }

    /// <summary>Öğeyi bulunduğu yerde yeniden adlandırır; geçmişe yazılır ve geri alınabilir.</summary>
    /// <exception cref="IOException">Ad geçersizse ya da o adda başka bir öğe varsa.</exception>
    public MoveRecord Rename(string path, string newName, Category? category)
    {
        path = RafLayout.Normalize(path);
        newName = newName.Trim();
        if (RafLayout.WhyNotValidName(newName) is { } why) throw new IOException(why);

        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) throw new FileNotFoundException("Öğe bulunamadı.", path);

        var dest = Path.Combine(Path.GetDirectoryName(path)!, newName);
        bool caseOnly = string.Equals(path, dest, StringComparison.OrdinalIgnoreCase);
        if (string.Equals(path, dest, StringComparison.Ordinal)) throw new IOException("Ad değişmedi.");
        if (!caseOnly && RafLayout.Exists(dest)) throw new IOException($"“{newName}” adında bir öğe zaten var.");

        if (isDir && caseOnly)
        {
            // Klasörde yalnızca harf büyüklüğü değişiyorsa doğrudan taşıma "aynı yol" diye reddedilir.
            var temp = RafLayout.UniquePath(Path.GetDirectoryName(path)!, newName + ".raf-gecici", isDirectory: true);
            Directory.Move(path, temp);
            Directory.Move(temp, dest);
        }
        else
        {
            MoveEntry(path, dest, isDir);
        }

        string categoryName = "";
        if (category is not null)
        {
            lock (_cfg) categoryName = _cfg.PathName(category);
        }
        var record = new MoveRecord
        {
            From = path,
            To = dest,
            CategoryId = category?.Id ?? "",
            CategoryName = categoryName,
            Time = DateTime.Now,
            Renamed = true,
        };
        Remember(record);
        RetargetLink(path, dest);
        lock (_cfg)
        {
            // Dokunma listesindeki bir dosyanın adı değişirse listedeki yolu da onunla birlikte güncellenir.
            int held = _cfg.Held.FindIndex(h => RafLayout.IsSame(h, path));
            if (held >= 0)
            {
                _cfg.Held[held] = dest;
                _store.Save(_cfg);
            }
        }

        Moved?.Invoke(record);
        return record;
    }

    /// <summary>Öğenin bir kopyasını kategoriye koyar; asıl öğe yerinde kalır. Kopyanın yolunu döndürür.</summary>
    public string CopyTo(string path, Category target)
    {
        string destDir;
        lock (_cfg) destDir = RafLayout.Dir(_cfg, target);

        path = RafLayout.Normalize(path);
        bool isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) throw new FileNotFoundException("Öğe bulunamadı.", path);
        if (isDir && RafLayout.IsInside(destDir, path)) throw new IOException("Bir klasör kendi içine kopyalanamaz.");

        Directory.CreateDirectory(destDir);
        var dest = RafLayout.UniquePath(destDir, Path.GetFileName(path), isDir);
        if (isDir) CopyDirectory(path, dest);
        else File.Copy(path, dest);
        return dest;
    }

    /// <summary>Taşımayı ya da yeniden adlandırmayı geri alır. Başarılıysa null, değilse kullanıcıya gösterilecek nedeni döndürür.</summary>
    public string? Undo(MoveRecord record)
    {
        if (record.Undone) return "Bu işlem zaten geri alınmış.";

        bool isDir = Directory.Exists(record.To);
        if (!isDir && !File.Exists(record.To)) return "Öğe artık bırakıldığı yerde değil.";

        var originalDir = Path.GetDirectoryName(record.From)!;
        string back;
        try
        {
            Directory.CreateDirectory(originalDir);
            back = RafLayout.UniquePath(originalDir, Path.GetFileName(record.From), isDir);

            // İzlenen bir klasöre dönüyorsa önce "dokunma" listesine al; yoksa izleme, dosya eski
            // yerine döner dönmez onu yeniden taşır.
            lock (_cfg)
            {
                bool watched = RafLayout.IsSame(originalDir, RafLayout.Inbox(_cfg))
                            || _cfg.WatchFolders.Any(f => RafLayout.IsSame(f, originalDir));
                if (watched)
                {
                    _cfg.Held.Add(back);
                    _store.Save(_cfg);
                }
            }
            MoveEntry(record.To, back, isDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Geri alınamadı: " + e.Message;
        }

        RetargetLink(record.To, back);
        lock (_history)
        {
            record.Undone = true;
            _store.SaveHistory(_history);
        }
        Moved?.Invoke(record);
        return null;
    }

    /// <summary>
    /// Yerinden oynatılmadan Raf'ta gösterilen bir öğenin (bağlantının) yolu değiştiyse bağlantıyı
    /// yeni yola çevirir; yoksa öğe Raf'tan kaybolurdu.
    /// </summary>
    private void RetargetLink(string from, string to)
    {
        lock (_cfg)
        {
            var link = _cfg.Links.FirstOrDefault(l => RafLayout.IsSame(l.Path, from));
            if (link is null) return;
            link.Path = to;
            _store.Save(_cfg);
        }
    }

    private void Remember(MoveRecord record)
    {
        lock (_history)
        {
            _history.Insert(0, record);
            _store.SaveHistory(_history);
        }
    }

    private void Unhold(string path)
    {
        lock (_cfg)
        {
            if (_cfg.Held.RemoveAll(h => RafLayout.IsSame(h, path)) > 0) _store.Save(_cfg);
        }
    }

    private static void MoveEntry(string from, string to, bool isDirectory)
    {
        if (!isDirectory)
        {
            File.Move(from, to);
            return;
        }

        if (string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(from, to);
            return;
        }

        // Directory.Move sürücüler arasında çalışmaz: önce kopyala, kopya tamamsa kaynağı kaldır.
        CopyDirectory(from, to);
        Directory.Delete(from, recursive: true);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
