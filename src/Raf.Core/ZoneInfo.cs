namespace Raf.Core;

public static class ZoneInfo
{
    /// <summary>
    /// Tarayıcıların indirilen dosyaya iliştirdiği "Zone.Identifier" bilgisinden kaynak adresi okur.
    /// Bilgi yoksa (ya da disk NTFS değilse) null döner.
    /// </summary>
    public static string? ReadSource(string path)
    {
        try
        {
            var urls = File.ReadAllLines(path + ":Zone.Identifier")
                .Where(l => l.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase)
                         || l.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
                .Select(l => l[(l.IndexOf('=') + 1)..].Trim())
                .Where(u => u.Length > 0)
                .ToList();
            return urls.Count == 0 ? null : string.Join(" ", urls);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
