namespace Raf.Core;

/// <summary>Raf klasörünün diskteki düzeni ve yol güvenlik kontrolleri.</summary>
public static class RafLayout
{
    public static string Inbox(RafConfig c) => Path.Combine(c.Root, c.InboxFolder);

    /// <summary>Kategorinin klasörü. Alt kategorinin klasörü, üst kategorinin klasörünün içindedir.</summary>
    public static string Dir(RafConfig c, Category category) => Path.Combine(ParentDir(c, c.Parent(category)), category.Folder);

    /// <summary>Verilen üst kategorinin altındaki kategorilerin klasörlerinin duracağı yer (üst yoksa Raf klasörü).</summary>
    public static string ParentDir(RafConfig c, Category? parent) =>
        parent is null ? c.Root : Path.Combine(c.Root, parent.Folder);

    /// <summary>Bir kategorinin klasöründe duran ama öğe olmayan klasörler: alt kategorilerinin klasörleri.</summary>
    public static HashSet<string> ChildDirs(RafConfig c, Category category) =>
        new(c.Children(category).Select(child => Normalize(Dir(c, child))), StringComparer.OrdinalIgnoreCase);

    public static void EnsureFolders(RafConfig c)
    {
        Directory.CreateDirectory(Inbox(c));
        foreach (var category in c.Categories)
            Directory.CreateDirectory(Dir(c, category));
    }

    public static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? ' ' : ch).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "Kategori" : cleaned;
    }

    /// <summary>
    /// Aynı üst kategorinin altındaki başka bir kategorinin (ana kategorilerde ayrıca Gelen klasörünün)
    /// kullanmadığı bir klasör adı üretir. O adda bir dosya varsa da başka ad seçilir.
    /// </summary>
    /// <param name="avoidExistingOnDisk">
    /// false ise o adda hazır duran bir klasör sahiplenilir (içindekiler kategorinin öğeleri olur);
    /// true ise boş bir ad aranır (yeniden adlandırma ve taşıma için).
    /// </param>
    public static string UniqueFolder(RafConfig c, string name, Category? parent, Category? except = null, bool avoidExistingOnDisk = false)
    {
        var baseName = SafeFolderName(name);
        var siblings = c.Categories.Where(x => x != except && c.Parent(x) == parent);
        var taken = new HashSet<string>(siblings.Select(x => x.Folder), StringComparer.OrdinalIgnoreCase);
        if (parent is null) taken.Add(c.InboxFolder);

        var dir = ParentDir(c, parent);
        bool Blocked(string candidate)
        {
            if (taken.Contains(candidate)) return true;
            var path = Path.Combine(dir, candidate);
            return File.Exists(path) || (avoidExistingOnDisk && Directory.Exists(path));
        }

        var result = baseName;
        for (int i = 2; Blocked(result); i++) result = $"{baseName} {i}";
        return result;
    }

    /// <summary>Hedef klasörde aynı adda bir şey varsa "ad (2).uzantı" biçiminde boş bir yol bulur.</summary>
    public static string UniquePath(string dir, string name, bool isDirectory)
    {
        var path = Path.Combine(dir, name);
        if (!Exists(path)) return path;

        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!Exists(path)) return path;
        }
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool IsSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>path, dir'in kendisi ya da içindeki bir şey mi?</summary>
    public static bool IsInside(string path, string dir)
    {
        var p = Normalize(path);
        var d = Normalize(dir);
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dosya ya da klasör adı olarak kullanılabilir mi? Kullanılamazsa nedenini döndürür.</summary>
    public static string? WhyNotValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bir ad yaz.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Adda şu karakterler olamaz:  \\ / : * ? \" < > |";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Ad nokta ya da boşlukla bitemez.";
        return null;
    }

    private static bool IsDriveRoot(string path)
    {
        var full = Normalize(path);
        return string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\', '/'), full.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ProtectedFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new[]
        {
            profile,
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        return folders.Where(f => !string.IsNullOrEmpty(f));
    }

    /// <summary>Klasör izlenebilirse null, izlenemezse nedenini döndürür.</summary>
    public static string? WhyNotWatchable(RafConfig c, string folder)
    {
        if (!Directory.Exists(folder)) return "Klasör bulunamadı.";
        if (IsDriveRoot(folder)) return "Bir sürücünün tamamı izlenemez.";
        if (IsInside(folder, c.Root)) return "Raf'ın kendi klasörü zaten düzenleniyor.";
        if (IsInside(c.Root, folder)) return "Bu klasör Raf klasörünü içeriyor.";

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (IsSame(folder, profile)) return "Kullanıcı klasörünün tamamı izlenemez.";

        foreach (var system in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var dir = Environment.GetFolderPath(system);
            if (dir.Length > 0 && IsInside(folder, dir)) return "Sistem klasörleri izlenemez.";
        }
        return null;
    }

    /// <summary>Öğe Raf'a taşınabilirse null, taşınamazsa nedenini döndürür.</summary>
    public static string? WhyNotMovable(RafConfig c, string path)
    {
        if (!Exists(path)) return "Öğe bulunamadı.";
        if (IsDriveRoot(path)) return "Bir sürücü taşınamaz.";
        if (IsInside(c.Root, path)) return "Bu klasör Raf klasörünü içeriyor.";
        if (IsStructural(c, path)) return "Bu, Raf'ın kendi klasörlerinden biri.";
        if (ProtectedFolders().Any(f => IsSame(path, f))) return "Windows'un özel klasörleri taşınamaz.";
        return null;
    }

    /// <summary>Yol, Gelen klasörü ya da bir kategori klasörü mü? Bunlar öğe değil, Raf'ın iskeletidir.</summary>
    public static bool IsStructural(RafConfig c, string path) =>
        IsSame(path, Inbox(c)) || c.Categories.Any(category => IsSame(path, Dir(c, category)));
}
