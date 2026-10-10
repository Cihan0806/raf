namespace Raf.Core;

/// <summary>Raf'taki öğelerde ada ve uzantıya göre arama.</summary>
public static class LibrarySearch
{
    /// <param name="Extension">Sorgu tek başına bir uzantıysa o uzantı (".pdf"); değilse null.</param>
    /// <param name="ByExtension">Uzantısı tam olarak eşleşenler, en yeni önce.</param>
    /// <param name="ByName">Adı eşleşen diğerleri.</param>
    public sealed record Result(string? Extension, List<LibraryItem> ByExtension, List<LibraryItem> ByName)
    {
        public int Count => ByExtension.Count + ByName.Count;
    }

    private readonly record struct Entry(LibraryItem Item, string Name, string Extension);

    /// <summary>
    /// Boşlukla ayrılan her kelime öğenin adında (uzantı dahil) geçmelidir. Nokta ya da yıldızla
    /// başlayan kelime (".pdf", "*.pdf") yalnızca uzantıyla eşleşir. Sorgu tek bir uzantıdan ibaretse
    /// (noktasız "pdf" de, Raf'ta o türde dosya varsa) o türdeki dosyalar ayrı ve önce listelenir.
    /// </summary>
    public static Result Find(IReadOnlyList<LibraryItem> items, string query, Func<LibraryItem, DateTime> added, int limit)
    {
        var tokens = TextUtil.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new Result(null, new(), new());

        var entries = items
            .Select(i => new Entry(i, TextUtil.Fold(i.Name), i.IsDirectory ? "" : TextUtil.Fold(Path.GetExtension(i.Name))))
            .ToList();

        string? extension = null;
        if (tokens.Length == 1)
        {
            var candidate = TextUtil.NormalizeExt(tokens[0]);
            if (candidate.Length > 1 && (IsDotted(tokens[0]) || entries.Any(e => e.Extension == candidate)))
                extension = candidate;
        }

        var hits = entries.Where(e => tokens.All(t => Matches(e, t))).ToList();
        if (hits.Count == 0 && tokens.Any(IsDotted))
        {
            // ".git" gibi, hiçbir dosyanın uzantısı olmayan noktalı sorgu: düz ad aramasına dön.
            extension = null;
            hits = entries.Where(e => tokens.All(t => e.Name.Contains(t, StringComparison.Ordinal))).ToList();
        }

        var byExtension = extension is null
            ? new List<LibraryItem>()
            : hits.Where(e => e.Extension == extension)
                .OrderByDescending(e => added(e.Item))
                .Take(limit)
                .Select(e => e.Item)
                .ToList();

        var byName = hits.Where(e => extension is null || e.Extension != extension)
            .OrderBy(e => e.Name.StartsWith(tokens[0], StringComparison.Ordinal) ? 0 : 1)
            .ThenByDescending(e => added(e.Item))
            .Take(Math.Max(0, limit - byExtension.Count))
            .Select(e => e.Item)
            .ToList();

        return new Result(extension, byExtension, byName);
    }

    private static bool IsDotted(string token) => token[0] is '.' or '*';

    private static bool Matches(Entry entry, string token)
    {
        if (IsDotted(token))
        {
            var extension = TextUtil.NormalizeExt(token);
            return extension.Length > 1 && entry.Extension == extension;
        }
        return entry.Name.Contains(token, StringComparison.Ordinal);
    }
}
