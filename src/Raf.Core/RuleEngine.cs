namespace Raf.Core;

public static class RuleEngine
{
    /// <summary>
    /// Kategorileri sırayla dener ve uyan ilkini döndürür; hiçbiri uymazsa yedek kategoriyi verir.
    /// </summary>
    /// <param name="source">Dosyanın indirildiği adres (biliniyorsa).</param>
    public static Category Classify(IReadOnlyList<Category> categories, string name, bool isDirectory = false, string? source = null)
    {
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var foldedName = TextUtil.Fold(stem);
        var ext = isDirectory ? "" : TextUtil.NormalizeExt(Path.GetExtension(name));
        var src = source?.ToLowerInvariant();

        foreach (var category in categories)
        {
            if (!category.IsFallback && Matches(category, foldedName, ext, src))
                return category;
        }
        return categories.First(c => c.IsFallback);
    }

    private static bool Matches(Category c, string foldedName, string ext, string? source)
    {
        bool hasExt = c.Extensions.Count > 0;
        bool hasName = c.NameContains.Count > 0;
        bool hasSite = c.Sites.Count > 0;
        if (!hasExt && !hasName && !hasSite) return false;

        bool extOk = hasExt && ext.Length > 0 && c.Extensions.Any(e => TextUtil.NormalizeExt(e) == ext);
        bool nameOk = hasName && c.NameContains.Any(k => k.Trim().Length > 0 && foldedName.Contains(TextUtil.Fold(k.Trim())));
        bool siteOk = hasSite && source is not null && c.Sites.Any(s => s.Trim().Length > 0 && source.Contains(s.Trim().ToLowerInvariant()));

        if (c.MatchAll)
            return (!hasExt || extOk) && (!hasName || nameOk) && (!hasSite || siteOk);
        return extOk || nameOk || siteOk;
    }
}
