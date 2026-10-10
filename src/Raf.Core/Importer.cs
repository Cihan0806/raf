namespace Raf.Core;

public enum ImportAction
{
    /// <summary>Kurallara göre bulunan kategoriye taşınır.</summary>
    Move,

    /// <summary>Yerinde kalır; Raf'ta bağlantı olarak görünür.</summary>
    Link,

    /// <summary>Dokunulmaz; nedeni <see cref="ImportEntry.Reason"/> içindedir.</summary>
    Skip,
}

/// <param name="Target">Gideceği kategori. Bağlantıların kategorisi henüz açılmadıysa null'dır.</param>
/// <param name="TargetName">Kullanıcıya gösterilecek kategori adı.</param>
public sealed record ImportEntry(string Path, string Name, bool IsDirectory, ImportAction Action, Category? Target, string TargetName, string? Reason = null);

public sealed record ImportResult(IReadOnlyList<MoveRecord> Moved, IReadOnlyList<string> Linked, IReadOnlyList<string> Errors);

/// <summary>
/// Raf kurulmadan önce birikmiş olanları içeri alır: seçilen klasörlerin en üst düzeyindeki dosyalar
/// kurallara göre kategorilere taşınır; klasörler yerinde bırakılıp bağlantı olarak eklenir, çünkü
/// yollarına başka programlar bağlı olabilir (proje klasörleri gibi).
/// </summary>
public static class Importer
{
    /// <summary>Bağlanan klasörlerin gösterildiği, kuralı olmayan kategori; yoksa içeri alırken açılır.</summary>
    public const string FolderCategoryName = "Klasörler";

    /// <summary>Neyin nereye gideceğini çıkarır; hiçbir şeyi değiştirmez.</summary>
    /// <param name="linkFolders">false ise klasörler plana hiç girmez.</param>
    public static List<ImportEntry> Plan(RafConfig cfg, Sorter sorter, IEnumerable<string> folders, bool linkFolders)
    {
        Category? folderCategory;
        HashSet<string> linked;
        string root;
        lock (cfg)
        {
            folderCategory = FindFolderCategory(cfg);
            linked = new HashSet<string>(cfg.Links.Select(l => RafLayout.Normalize(l.Path)), StringComparer.OrdinalIgnoreCase);
            root = cfg.Root;
        }

        var plan = new List<ImportEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder) || RafLayout.IsInside(folder, root)) continue;

            List<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            foreach (var info in entries.OrderBy(i => i.Name, NaturalComparer.Instance))
            {
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;

                var path = RafLayout.Normalize(info.FullName);
                if (!seen.Add(path) || linked.Contains(path)) continue;
                // Raf'ın kendi klasörü (ya da onu içeren bir klasör) içeri alınacak bir şey değil.
                if (RafLayout.IsInside(root, path)) continue;

                if (info is DirectoryInfo)
                {
                    if (linkFolders) plan.Add(new ImportEntry(path, info.Name, true, ImportAction.Link, folderCategory, FolderCategoryName));
                    continue;
                }

                string? why = InboxWatcher.IsTemp(info.Name) ? "Geçici dosya ya da yarım kalmış indirme." : null;
                if (why is null)
                {
                    lock (cfg) why = RafLayout.WhyNotMovable(cfg, path);
                }
                if (why is not null)
                {
                    plan.Add(new ImportEntry(path, info.Name, false, ImportAction.Skip, null, "", why));
                    continue;
                }

                var target = sorter.Classify(path);
                string targetName;
                lock (cfg) targetName = cfg.PathName(target);
                plan.Add(new ImportEntry(path, info.Name, false, ImportAction.Move, target, targetName));
            }
        }
        return plan;
    }

    /// <summary>Planı uygular. Taşınamayan öğe işi durdurmaz; nedeni sonuçta döner.</summary>
    /// <param name="progress">Her öğeden sonra, o ana kadar işlenen öğe sayısıyla çağrılır.</param>
    public static ImportResult Apply(RafConfig cfg, ConfigStore store, Sorter sorter, IEnumerable<ImportEntry> plan, Action<int>? progress = null)
    {
        var moved = new List<MoveRecord>();
        var linked = new List<string>();
        var errors = new List<string>();
        Category? folderCategory = null;
        int done = 0;

        foreach (var entry in plan.Where(e => e.Action != ImportAction.Skip))
        {
            try
            {
                if (entry.Action == ImportAction.Move)
                {
                    // Kurala göre yerleştiği için "kendiliğinden" sayılır: kurallar sonradan değişirse
                    // "kuralları yeniden uygula" bu öğeleri de yeni yerine götürür.
                    if (sorter.MoveTo(entry.Path, entry.Target!, auto: true) is { } record) moved.Add(record);
                }
                else
                {
                    if (!Directory.Exists(entry.Path)) throw new DirectoryNotFoundException("Klasör bulunamadı.");
                    folderCategory ??= entry.Target ?? EnsureFolderCategory(cfg, store);
                    CategoryService.AddLink(cfg, store, entry.Path, folderCategory);
                    linked.Add(entry.Path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{entry.Name}: {e.Message}");
            }
            progress?.Invoke(++done);
        }
        return new ImportResult(moved, linked, errors);
    }

    /// <summary>
    /// İçeri almayı geri alır: taşınanlar eski yerlerine döner, eklenen bağlantılar kaldırılır.
    /// Geri alınamayan öğelerin nedenlerini döndürür.
    /// </summary>
    public static List<string> Undo(RafConfig cfg, ConfigStore store, Sorter sorter, ImportResult result)
    {
        var errors = new List<string>();
        foreach (var record in result.Moved.Reverse())
        {
            if (sorter.Undo(record) is { } why) errors.Add($"{Path.GetFileName(record.To)}: {why}");
        }
        foreach (var path in result.Linked) CategoryService.RemoveLink(cfg, store, path);
        return errors;
    }

    private static Category? FindFolderCategory(RafConfig cfg)
    {
        var wanted = TextUtil.Fold(FolderCategoryName);
        return cfg.Categories.FirstOrDefault(c => TextUtil.Fold(c.Name) == wanted);
    }

    private static Category EnsureFolderCategory(RafConfig cfg, ConfigStore store)
    {
        lock (cfg)
        {
            if (FindFolderCategory(cfg) is { } existing) return existing;
        }

        var category = CategoryService.Add(cfg, store, new Category { Name = FolderCategoryName, Color = "#D9A521", Icon = Defaults.DefaultIcon });
        lock (cfg)
        {
            // Yeni kategoriler en üste eklenir; kuralı olmayan bu kategori listenin sonunda, "Diğer"in üstünde dursun.
            cfg.Categories.Remove(category);
            cfg.Categories.Insert(cfg.Categories.IndexOf(cfg.Fallback), category);
            store.Save(cfg);
        }
        return category;
    }
}
