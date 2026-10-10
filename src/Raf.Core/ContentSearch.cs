using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Raf.Core;

/// <summary>
/// Dosyaların içinde arama ve metin okuma. Düz metin dosyalarını ve Word/Excel/PowerPoint
/// belgelerini (docx, xlsx, pptx) okur; PDF ve eski Office biçimlerini okumaz.
/// </summary>
public static class ContentSearch
{
    /// <summary>Bundan kısa sorgularda içerik aranmaz; neredeyse her dosya eşleşirdi.</summary>
    public const int MinQueryLength = 3;

    /// <summary>Bundan büyük dosyaların yalnızca baş kısmı okunur.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private const long MaxPartBytes = 32L * 1024 * 1024;
    private const int SnippetRadius = 60;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".xml", ".yml", ".yaml", ".ini", ".conf", ".cfg", ".properties", ".toml", ".env",
        ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".py", ".java", ".kt", ".cs", ".c", ".cpp", ".h", ".php", ".go", ".rs",
        ".sql", ".sh", ".bat", ".cmd", ".ps1", ".gradle", ".srt", ".url", ".ovpn",
    };

    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx", ".pptx" };

    private static readonly Regex XmlTag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex ParagraphEnd = new("</w:p>|</a:p>|</si>|<w:br[^>]*/>", RegexOptions.Compiled);

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Turkish;

    static ContentSearch()
    {
        // Türkçe Windows'ta Not Defteri'nin eski "ANSI" kaydı 1254 kod sayfasıdır; .NET bunu kayıt olmadan tanımaz.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Turkish = Encoding.GetEncoding(1254);
    }

    public sealed record Hit(LibraryItem Item, string Snippet);

    public static bool CanRead(string path)
    {
        var ext = Path.GetExtension(path);
        return TextExtensions.Contains(ext) || OfficeExtensions.Contains(ext);
    }

    /// <summary>
    /// Sorgunun (tek parça ifade olarak) içinde geçtiği dosyaları bulur. Büyük/küçük ve Türkçe harf farkı
    /// yok sayılır. Yalnızca Raf'taki dosyaların kendisine bakar; klasörlerin içine girmez.
    /// </summary>
    public static List<Hit> Find(IEnumerable<LibraryItem> items, string query, int limit, CancellationToken cancel = default)
    {
        var folded = TextUtil.Fold(query.Trim());
        var hits = new List<Hit>();
        if (folded.Length < MinQueryLength) return hits;

        foreach (var item in items)
        {
            if (cancel.IsCancellationRequested || hits.Count >= limit) break;
            if (item.IsDirectory || !CanRead(item.Path)) continue;

            var text = ReadText(item.Path);
            if (text is null) continue;

            // Fold harfleri bire bir eşler; bulunan konum asıl metinde de aynı yerdir.
            int index = TextUtil.Fold(text).IndexOf(folded, StringComparison.Ordinal);
            if (index >= 0) hits.Add(new Hit(item, Snippet(text, index, folded.Length)));
        }
        return hits;
    }

    /// <summary>Dosyanın metnini döndürür; okunamıyorsa ya da metin değilse null.</summary>
    public static string? ReadText(string path, int maxBytes = MaxBytes)
    {
        try
        {
            return OfficeExtensions.Contains(Path.GetExtension(path)) ? ReadOffice(path, maxBytes) : ReadPlain(path, maxBytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadPlain(string path, int maxBytes)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[(int)Math.Min(stream.Length, maxBytes)];
            stream.ReadExactly(bytes);
        }
        return Decode(bytes);
    }

    private static string? Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // İmzasız dosyada sıfır baytı varsa metin değildir.
        if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8192)) >= 0) return null;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Turkish.GetString(bytes);
        }
    }

    /// <summary>Office belgeleri zip içinde XML'dir; metin, etiketler atılarak çıkarılır.</summary>
    private static string ReadOffice(string path, int maxBytes)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var parts = ext switch
        {
            ".docx" => zip.Entries.Where(e => e.FullName == "word/document.xml"),
            ".xlsx" => zip.Entries.Where(e => e.FullName == "xl/sharedStrings.xml"),
            _ => zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                            .OrderBy(e => e.FullName, NaturalComparer.Instance),
        };

        var text = new StringBuilder();
        foreach (var part in parts)
        {
            // XML, içindeki metinden kat kat büyüktür; aşırı büyük parçalar atlanır.
            if (part.Length > MaxPartBytes) continue;

            using var reader = new StreamReader(part.Open(), Encoding.UTF8);
            var xml = ParagraphEnd.Replace(reader.ReadToEnd(), "\n");
            text.Append(WebUtility.HtmlDecode(XmlTag.Replace(xml, ""))).Append('\n');
            if (text.Length >= maxBytes) break;
        }
        return text.Length > maxBytes ? text.ToString(0, maxBytes) : text.ToString();
    }

    /// <summary>Eşleşmenin geçtiği satırdan, eşleşmenin iki yanını gösteren kısa bir parça.</summary>
    private static string Snippet(string text, int index, int length)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        int lineEnd = text.IndexOf('\n', index);
        if (lineEnd < 0) lineEnd = text.Length;

        int start = Math.Max(lineStart, index - SnippetRadius);
        int end = Math.Min(lineEnd, index + length + SnippetRadius);
        var snippet = text[start..end].Trim().Replace('\r', ' ').Replace('\t', ' ');
        return (start > lineStart ? "…" : "") + snippet + (end < lineEnd ? "…" : "");
    }
}
