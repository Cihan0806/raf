using System.Text.Encodings.Web;
using System.Text.Json;

namespace Raf.Core;

/// <summary>Ayarları ve taşıma geçmişini JSON olarak saklar.</summary>
public sealed class ConfigStore
{
    private const int HistoryLimit = 500;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly bool _customDir;

    public string DataDir { get; }
    public string ConfigPath => Path.Combine(DataDir, "config.json");
    public string HistoryPath => Path.Combine(DataDir, "gecmis.json");

    /// <param name="dataDir">Boş bırakılırsa %APPDATA%\Raf kullanılır.</param>
    public ConfigStore(string? dataDir = null)
    {
        _customDir = !string.IsNullOrWhiteSpace(dataDir);
        DataDir = _customDir
            ? Path.GetFullPath(dataDir!)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Raf");
    }

    public RafConfig Load()
    {
        Directory.CreateDirectory(DataDir);

        RafConfig? cfg = null;
        if (File.Exists(ConfigPath))
        {
            try { cfg = JsonSerializer.Deserialize<RafConfig>(File.ReadAllText(ConfigPath), Json); }
            catch (JsonException)
            {
                // Bozuk dosyayı silmeden kenara al; varsayılanlarla devam et.
                File.Copy(ConfigPath, ConfigPath + ".bozuk", overwrite: true);
            }
        }

        bool isNew = cfg is null;
        cfg ??= new RafConfig { Categories = Defaults.Categories() };

        if (string.IsNullOrWhiteSpace(cfg.Root))
        {
            cfg.Root = _customDir
                ? Path.Combine(DataDir, "Raf")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Raf");
        }
        if (string.IsNullOrWhiteSpace(cfg.InboxFolder)) cfg.InboxFolder = "Gelen";

        if (!cfg.Categories.Any(c => c.IsFallback))
            cfg.Categories.Add(Defaults.Fallback());
        foreach (var extra in cfg.Categories.Where(c => c.IsFallback).Skip(1).ToList())
            extra.IsFallback = false;
        foreach (var category in cfg.Categories.Where(c => string.IsNullOrWhiteSpace(c.Folder)))
            category.Folder = RafLayout.UniqueFolder(cfg, category.Name, cfg.Parent(category), category);
        // Simgeler sonradan eklendi; eski ayar dosyalarındaki kategoriler adına göre simge alır.
        foreach (var category in cfg.Categories.Where(c => string.IsNullOrWhiteSpace(c.Icon)))
            category.Icon = Defaults.IconFor(category.Name);

        cfg.Held.RemoveAll(h => !RafLayout.Exists(h));

        if (isNew) Save(cfg);
        return cfg;
    }

    public void Save(RafConfig cfg) => WriteAtomic(ConfigPath, JsonSerializer.Serialize(cfg, Json));

    public List<MoveRecord> LoadHistory()
    {
        if (!File.Exists(HistoryPath)) return new();
        try { return JsonSerializer.Deserialize<List<MoveRecord>>(File.ReadAllText(HistoryPath), Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public void SaveHistory(List<MoveRecord> history)
    {
        if (history.Count > HistoryLimit) history.RemoveRange(HistoryLimit, history.Count - HistoryLimit);
        WriteAtomic(HistoryPath, JsonSerializer.Serialize(history, Json));
    }

    private void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(DataDir);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
