namespace Raf.Core;

/// <summary>Raf'ta görünen öğeleri diskten okur. Asıl kaynak klasörlerin kendisidir; ayrı bir veritabanı yoktur.</summary>
public static class Library
{
    /// <summary>Gelen klasöründe bekleyen öğelerin sözde kategori kimliği.</summary>
    public const string InboxId = "@gelen";

    public static List<LibraryItem> Scan(RafConfig cfg)
    {
        string inbox;
        List<(string Id, string Dir, HashSet<string> ChildDirs)> categories;
        List<LinkItem> links;
        lock (cfg)
        {
            inbox = RafLayout.Inbox(cfg);
            categories = cfg.Categories.Select(c => (c.Id, RafLayout.Dir(cfg, c), RafLayout.ChildDirs(cfg, c))).ToList();
            links = cfg.Links.ToList();
        }

        var items = new List<LibraryItem>();
        AddFolder(items, inbox, InboxId, skip: null);
        foreach (var (id, dir, childDirs) in categories) AddFolder(items, dir, id, childDirs);

        foreach (var link in links)
        {
            var info = Info(link.Path);
            if (info is null) continue;
            bool isDir = info is DirectoryInfo;
            items.Add(new LibraryItem(link.Path, info.Name, link.CategoryId, isDir, true, link.Added, isDir ? 0 : ((FileInfo)info).Length));
        }
        return items;
    }

    /// <summary>Bir kategori klasöründeki öğelerin yolları; alt kategori klasörleri öğe sayılmaz.</summary>
    public static List<string> EntriesOf(RafConfig cfg, Category category)
    {
        string dir;
        HashSet<string> childDirs;
        lock (cfg)
        {
            dir = RafLayout.Dir(cfg, category);
            childDirs = RafLayout.ChildDirs(cfg, category);
        }
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFileSystemEntries(dir).Where(e => !childDirs.Contains(RafLayout.Normalize(e))).ToList();
    }

    private static void AddFolder(List<LibraryItem> items, string dir, string categoryId, HashSet<string>? skip)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (InboxWatcher.IsTemp(info.Name)) continue;

                bool isDir = info is DirectoryInfo;
                if (isDir && skip is not null && skip.Contains(RafLayout.Normalize(info.FullName))) continue;

                var added = info.CreationTime > info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
                items.Add(new LibraryItem(info.FullName, info.Name, categoryId, isDir, false, added, isDir ? 0 : ((FileInfo)info).Length));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Okunamayan klasör boş sayılır; diğer kategoriler gösterilmeye devam eder.
        }
    }

    private static FileSystemInfo? Info(string path)
    {
        if (Directory.Exists(path)) return new DirectoryInfo(path);
        if (File.Exists(path)) return new FileInfo(path);
        return null;
    }
}
