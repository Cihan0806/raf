using Raf.Core;

namespace Raf.Tests;

/// <summary>Her test için geçici bir veri klasörü ve onun içinde bir Raf kurar; bitince siler.</summary>
public sealed class TempRaf : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "raf-test-" + Guid.NewGuid().ToString("N")[..8]);
    public ConfigStore Store { get; }
    public RafConfig Cfg { get; }
    public Sorter Sorter { get; }

    public TempRaf()
    {
        Store = new ConfigStore(Dir);
        Cfg = Store.Load();
        RafLayout.EnsureFolders(Cfg);
        Sorter = new Sorter(Cfg, Store);
    }

    public string Inbox => RafLayout.Inbox(Cfg);

    public Category Cat(string name) => Cfg.Categories.First(c => c.Name == name);

    public string InCat(string category, string file) => Path.Combine(RafLayout.Dir(Cfg, Cat(category)), file);

    /// <summary>Raf'ın dışında, izlenebilecek boş bir klasör açar.</summary>
    public string Outside(string name)
    {
        var path = Path.Combine(Dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string Touch(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public static bool WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(50);
        }
        return condition();
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
