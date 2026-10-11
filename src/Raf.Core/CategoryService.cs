namespace Raf.Core;

/// <summary>Kategori ekleme, düzenleme, silme ve sıralama; klasörleri de ayarlarla birlikte günceller.</summary>
public static class CategoryService
{
    /// <summary>
    /// Yeni kategoriyi kendi seviyesinin en üstüne ekler; böylece hazır tür kategorilerinden önce denenir.
    /// draft.ParentId bir ana kategoriyi gösteriyorsa alt kategori olarak eklenir.
    /// </summary>
    public static Category Add(RafConfig cfg, ConfigStore store, Category draft)
    {
        lock (cfg)
        {
            var parent = ValidParent(cfg, draft, draft.ParentId);
            draft.ParentId = parent?.Id ?? "";
            draft.Folder = RafLayout.UniqueFolder(cfg, draft.Name, parent);
            draft.IsFallback = false;
            if (string.IsNullOrWhiteSpace(draft.Icon)) draft.Icon = Defaults.DefaultIcon;

            cfg.Categories.Insert(0, draft);
            Directory.CreateDirectory(RafLayout.Dir(cfg, draft));
            store.Save(cfg);
        }
        return draft;
    }

    /// <summary>
    /// Kategoriyi günceller. Adı ya da üst kategorisi değiştiyse klasörü içindekilerle birlikte
    /// yeni yerine taşınır; alt kategorileri de klasörün içinde olduğu için onunla gider.
    /// </summary>
    public static void Update(RafConfig cfg, ConfigStore store, Category category, Category draft)
    {
        lock (cfg)
        {
            var oldParent = cfg.Parent(category);
            var newParent = ValidParent(cfg, category, draft.ParentId);
            bool parentChanged = oldParent != newParent;
            bool nameChanged = !string.Equals(category.Name, draft.Name, StringComparison.Ordinal);

            if (parentChanged || nameChanged)
            {
                var oldDir = RafLayout.Dir(cfg, category);
                var wanted = RafLayout.SafeFolderName(draft.Name);
                bool keepFolderName = !parentChanged && string.Equals(wanted, category.Folder, StringComparison.OrdinalIgnoreCase);
                if (!keepFolderName)
                {
                    var folder = RafLayout.UniqueFolder(cfg, draft.Name, newParent, category, avoidExistingOnDisk: true);
                    var newDir = Path.Combine(RafLayout.ParentDir(cfg, newParent), folder);
                    Directory.CreateDirectory(RafLayout.ParentDir(cfg, newParent));
                    if (Directory.Exists(oldDir)) Directory.Move(oldDir, newDir);
                    else Directory.CreateDirectory(newDir);
                    category.Folder = folder;
                }
                category.ParentId = newParent?.Id ?? "";
                category.Name = draft.Name;
            }

            category.Color = draft.Color;
            if (!string.IsNullOrWhiteSpace(draft.Icon)) category.Icon = draft.Icon;
            category.Extensions = draft.Extensions;
            category.NameContains = draft.NameContains;
            category.Sites = draft.Sites;
            category.MatchAll = draft.MatchAll;
            store.Save(cfg);
        }
    }

    /// <summary>
    /// Bir kategorinin bağlanabileceği üst kategoriyi doğrular. Yalnızca bir seviye iç içe olunur:
    /// üst, kendisi ana kategori olmalı; alt kategorisi olan bir kategori başkasının altına giremez.
    /// </summary>
    private static Category? ValidParent(RafConfig cfg, Category category, string parentId)
    {
        if (string.IsNullOrEmpty(parentId) || category.IsFallback) return null;
        var parent = cfg.Categories.FirstOrDefault(c => c.Id == parentId);
        if (parent is null || parent == category || cfg.Parent(parent) is not null) return null;
        if (cfg.Children(category).Count > 0) return null;
        return parent;
    }

    /// <summary>
    /// Kategoriyi siler; içindekiler yedek kategoriye ("Diğer") taşınır, hiçbir dosya silinmez.
    /// Alt kategorileri silinmez, ana kategori olur.
    /// </summary>
    public static void Delete(RafConfig cfg, ConfigStore store, Sorter sorter, Category category)
    {
        if (category.IsFallback) throw new InvalidOperationException("Yedek kategori silinemez.");

        Category fallback;
        string dir;
        lock (cfg)
        {
            foreach (var child in cfg.Children(category))
            {
                var childDir = RafLayout.Dir(cfg, child);
                var folder = RafLayout.UniqueFolder(cfg, child.Name, parent: null, child, avoidExistingOnDisk: true);
                if (Directory.Exists(childDir)) Directory.Move(childDir, Path.Combine(cfg.Root, folder));
                child.ParentId = "";
                child.Folder = folder;
            }
            store.Save(cfg);

            fallback = cfg.Fallback;
            dir = RafLayout.Dir(cfg, category);
        }

        foreach (var entry in Library.EntriesOf(cfg, category))
            sorter.MoveTo(entry, fallback);
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);

        lock (cfg)
        {
            foreach (var link in cfg.Links.Where(l => l.CategoryId == category.Id))
                link.CategoryId = fallback.Id;
            cfg.Categories.Remove(category);
            store.Save(cfg);
        }
    }

    /// <summary>Kategoriyi, aynı seviyedeki komşularının arasında bir sıra yukarı ya da aşağı alır.</summary>
    public static void Shift(RafConfig cfg, ConfigStore store, Category category, int delta)
    {
        lock (cfg)
        {
            var parent = cfg.Parent(category);
            var siblings = cfg.Categories.Where(c => cfg.Parent(c) == parent).ToList();
            int index = siblings.IndexOf(category);
            int target = index + delta;
            if (index < 0 || target < 0 || target >= siblings.Count) return;

            int a = cfg.Categories.IndexOf(category);
            int b = cfg.Categories.IndexOf(siblings[target]);
            (cfg.Categories[a], cfg.Categories[b]) = (cfg.Categories[b], cfg.Categories[a]);
            store.Save(cfg);
        }
    }

    /// <summary>
    /// Kurallar değiştikten sonra yer değiştirmesi gereken dosyaları bulur (taşımaz).
    /// Kullanıcının elle yerleştirdikleri ve kuralı olmayan kategorilerdekiler olduğu yerde bırakılır.
    /// </summary>
    public static List<(string Path, Category Target)> PlanReapply(RafConfig cfg, Sorter sorter)
    {
        List<Category> categories;
        lock (cfg) categories = cfg.Categories.ToList();

        var manual = new HashSet<string>(
            sorter.HistorySnapshot().Where(r => !r.Auto && !r.Undone).Select(r => r.To),
            StringComparer.OrdinalIgnoreCase);

        var plan = new List<(string, Category)>();
        foreach (var category in categories)
        {
            if (!category.HasRules && !category.IsFallback) continue;

            foreach (var entry in Library.EntriesOf(cfg, category))
            {
                if (manual.Contains(entry)) continue;
                var target = sorter.Classify(entry);
                if (target.Id != category.Id) plan.Add((entry, target));
            }
        }
        return plan;
    }

    /// <summary>Bir yolu yerinden oynatmadan bir kategoriye bağlar. Zaten bağlıysa kategorisini günceller.</summary>
    /// <param name="auto">Bağlantıyı kullanıcı değil Raf ekliyorsa true (bkz. <see cref="LinkItem.Auto"/>).</param>
    public static void AddLink(RafConfig cfg, ConfigStore store, string path, Category category, bool auto = false)
    {
        lock (cfg)
        {
            var existing = cfg.Links.FirstOrDefault(l => RafLayout.IsSame(l.Path, path));
            if (existing is not null) existing.CategoryId = category.Id;
            else cfg.Links.Add(new LinkItem { Path = RafLayout.Normalize(path), CategoryId = category.Id, Added = DateTime.Now, Auto = auto });
            store.Save(cfg);
        }
    }

    /// <summary>Bağlı bir öğenin yolu değiştiyse bağlantıyı yeni yola çevirir. Bağlantı yoksa bir şey yapmaz.</summary>
    public static bool RetargetLink(RafConfig cfg, ConfigStore store, string from, string to)
    {
        lock (cfg)
        {
            var link = cfg.Links.FirstOrDefault(l => RafLayout.IsSame(l.Path, from));
            if (link is null) return false;
            link.Path = RafLayout.Normalize(to);
            store.Save(cfg);
            return true;
        }
    }

    public static bool RemoveLink(RafConfig cfg, ConfigStore store, string path)
    {
        lock (cfg)
        {
            if (cfg.Links.RemoveAll(l => RafLayout.IsSame(l.Path, path)) == 0) return false;
            store.Save(cfg);
            return true;
        }
    }
}
