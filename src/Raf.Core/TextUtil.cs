using System.Text;

namespace Raf.Core;

public static class TextUtil
{
    private static readonly char[] ListSeparators = { ',', ';', '\n', '\r', '\t', ' ' };
    private static readonly char[] PhraseSeparators = { ',', ';', '\n', '\r' };

    /// <summary>
    /// Büyük/küçük harf ve Türkçe harf farkını yok sayar: "MÜZİK", "Müzik" ve "muzik" aynı sonucu verir.
    /// Arama ve ad kuralları bu biçim üzerinden karşılaştırılır.
    /// </summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                'İ' or 'I' or 'ı' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ü' or 'ü' => 'u',
                'Ö' or 'ö' => 'o',
                'Ç' or 'ç' => 'c',
                _ => char.ToLowerInvariant(ch),
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Bir adın sorguya ne kadar iyi uyduğunu puanlar; küçük puan daha iyidir. Sorgunun her kelimesi
    /// adda geçmelidir. 0: ad sorguyla başlıyor; 1: addaki bir kelime sorguyla başlıyor
    /// ("google chrome" için "chr"); 2: yalnızca içinde geçiyor; -1: uymuyor.
    /// İkisi de <see cref="Fold"/> ile sadeleştirilmiş olmalıdır.
    /// </summary>
    public static int MatchScore(string foldedName, IReadOnlyList<string> foldedTokens)
    {
        if (foldedTokens.Count == 0) return -1;
        foreach (var token in foldedTokens)
        {
            if (!foldedName.Contains(token, StringComparison.Ordinal)) return -1;
        }

        var first = foldedTokens[0];
        if (foldedName.StartsWith(first, StringComparison.Ordinal)) return 0;
        for (int i = foldedName.IndexOf(first, StringComparison.Ordinal); i >= 0; i = foldedName.IndexOf(first, i + 1, StringComparison.Ordinal))
        {
            if (i > 0 && !char.IsLetterOrDigit(foldedName[i - 1])) return 1;
        }
        return 2;
    }

    /// <summary>"JAR", "*.jar" ve ".Jar" girişlerinin hepsini ".jar" yapar.</summary>
    public static string NormalizeExt(string ext)
    {
        ext = ext.Trim().TrimStart('*').ToLowerInvariant();
        if (ext.Length == 0) return "";
        return ext[0] == '.' ? ext : "." + ext;
    }

    /// <summary>Kullanıcının yazdığı uzantı listesini ayırır; boşluk da ayırıcıdır.</summary>
    public static List<string> ParseExtensions(string? text) =>
        (text ?? "").Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeExt)
            .Where(e => e.Length > 1)
            .Distinct()
            .ToList();

    /// <summary>Virgülle ayrılmış kelime/ifade listesini ayırır; ifadelerin içindeki boşluk korunur.</summary>
    public static List<string> ParsePhrases(string? text) =>
        (text ?? "").Split(PhraseSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
