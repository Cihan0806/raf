namespace Raf.Core;

public static class ZoneInfo
{
    /// <summary>
    /// Dosya Windows tarafından "internetten geldi" diye işaretlenmiş mi? Tarayıcılar indirdikleri her
    /// dosyaya bu işareti koyar; programların kendi oluşturduğu dosyalarda bulunmaz.
    /// </summary>
    public static bool HasMark(string path)
    {
        try
        {
            // Çoğu dosyada işaret yoktur; önce var mı diye bakmak, açmayı deneyip hata yakalamaktan çok daha ucuzdur.
            if (!File.Exists(path + ":Zone.Identifier")) return false;

            // Dosyanın o sırada silinmesine ya da yeniden adlandırılmasına engel olmayacak biçimde açılır.
            using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (!line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase)) continue;
                // 3: İnternet, 4: Kısıtlı siteler. Yerel ağ ve güvenilen siteler (1, 2) indirme sayılmaz.
                return int.TryParse(line.AsSpan("ZoneId=".Length).Trim(), out int zone) && zone >= 3;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

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
