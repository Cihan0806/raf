namespace Raf.Core;

/// <summary>Öğeleri ada, tarihe, boyuta ya da türe göre sıralar.</summary>
public static class LibrarySort
{
    /// <param name="added">Öğenin Raf'a girdiği an (tarihe göre sıralama bunu kullanır).</param>
    public static List<LibraryItem> Apply(IEnumerable<LibraryItem> items, SortKey key, bool descending, Func<LibraryItem, DateTime> added)
    {
        // Ad ve türe göre sıralamada klasörler, Dosya Gezgini'ndeki gibi dosyalardan önce gelir.
        IOrderedEnumerable<LibraryItem> ordered = key switch
        {
            SortKey.Date => items.OrderBy(added),
            SortKey.Size => items.OrderBy(i => i.IsDirectory ? -1 : i.Size),
            SortKey.Type => items.OrderBy(i => i.IsDirectory ? 0 : 1)
                                 .ThenBy(i => i.IsDirectory ? "" : Path.GetExtension(i.Name).ToLowerInvariant(), StringComparer.Ordinal),
            _ => items.OrderBy(i => i.IsDirectory ? 0 : 1),
        };

        var list = ordered.ThenBy(i => i.Name, NaturalComparer.Instance).ToList();
        if (descending) list.Reverse();
        return list;
    }
}

/// <summary>
/// Adları insanın beklediği gibi karşılaştırır: büyük/küçük ve Türkçe harf farkını yok sayar,
/// sayıları sayı olarak okur ("yedek-2", "yedek-10"dan önce gelir).
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var a = TextUtil.Fold(x ?? "");
        var b = TextUtil.Fold(y ?? "");
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int startA = i, startB = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;

                var numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                var numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                int byLength = numberA.Length.CompareTo(numberB.Length);
                if (byLength != 0) return byLength;
                int byDigits = numberA.CompareTo(numberB, StringComparison.Ordinal);
                if (byDigits != 0) return byDigits;
                continue;
            }

            int byChar = a[i].CompareTo(b[j]);
            if (byChar != 0) return byChar;
            i++;
            j++;
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
