using System.IO.Compression;
using System.Text;
using Raf.Core;

namespace Raf.Tests;

public sealed class ContentSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "raf-icerik-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<LibraryItem> _items = new();

    public ContentSearchTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string Add(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        _items.Add(new LibraryItem(path, name, "x", IsDirectory: false, IsLink: false, DateTime.Now, bytes.Length));
        return path;
    }

    private string AddText(string name, string text, Encoding? encoding = null) =>
        Add(name, (encoding ?? new UTF8Encoding(false)).GetPreamble().Concat((encoding ?? new UTF8Encoding(false)).GetBytes(text)).ToArray());

    private void AddOffice(string name, string entry, string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        Add(name, buffer.ToArray());
    }

    private string[] Found(string query, int limit = 50) =>
        ContentSearch.Find(_items, query, limit).Select(h => h.Item.Name).ToArray();

    [Fact]
    public void Metin_dosyasinin_icinde_turkce_harf_ve_buyukluk_farki_gozetmeden_bulur()
    {
        AddText("toplanti.txt", "Pazartesi\r\nMÜŞTERİ görüşmesi saat 14:00'te, Kadıköy ofisinde.\r\nSalı boş.");
        AddText("alisveris.txt", "süt, ekmek, yumurta");

        Assert.Equal(new[] { "toplanti.txt" }, Found("musteri gorusmesi"));
        Assert.Equal(new[] { "alisveris.txt" }, Found("EKMEK"));
        Assert.Empty(Found("peynir"));
    }

    [Fact]
    public void Parca_eslesen_satiri_gosterir()
    {
        AddText("toplanti.txt", "Pazartesi\r\nMüşteri görüşmesi saat 14:00'te.\r\nSalı boş.");

        var hit = Assert.Single(ContentSearch.Find(_items, "saat 14", 10));

        Assert.Equal("Müşteri görüşmesi saat 14:00'te.", hit.Snippet);
    }

    [Fact]
    public void Uzun_satirda_parca_eslesmenin_cevresiyle_sinirlanir()
    {
        AddText("uzun.txt", new string('a', 500) + " iğne " + new string('b', 500));

        var snippet = Assert.Single(ContentSearch.Find(_items, "iğne", 10)).Snippet;

        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("iğne", snippet);
        Assert.True(snippet.Length < 160);
    }

    [Fact]
    public void Eski_ansi_ve_utf16_kayitlari_da_okur()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Add("ansi.txt", Encoding.GetEncoding(1254).GetBytes("Şifre yenileme günü: çarşamba"));
        AddText("utf16.txt", "Ürün listesi: ığdır kayısısı", Encoding.Unicode);
        AddText("imzali.txt", "Öğle arası", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(new[] { "ansi.txt" }, Found("çarşamba"));
        Assert.Equal(new[] { "utf16.txt" }, Found("kayısısı"));
        Assert.Equal(new[] { "imzali.txt" }, Found("öğle"));
        Assert.Equal("Şifre yenileme günü: çarşamba", ContentSearch.ReadText(Path.Combine(_dir, "ansi.txt")));
    }

    [Fact]
    public void Word_excel_ve_powerpoint_belgelerinin_metnini_okur()
    {
        AddOffice("teklif.docx", "word/document.xml",
            "<w:document><w:body><w:p><w:r><w:t>Sayın yetkili,</w:t></w:r></w:p><w:p><w:r><w:t>Santral kurulumu &amp; bakım</w:t></w:r><w:r><w:t> teklifimiz ektedir.</w:t></w:r></w:p></w:body></w:document>");
        AddOffice("fiyat.xlsx", "xl/sharedStrings.xml", "<sst><si><t>Ürün</t></si><si><t>IP telefon seti</t></si></sst>");
        AddOffice("sunum.pptx", "ppt/slides/slide1.xml", "<p:sld><a:p><a:r><a:t>Yıllık hedefler</a:t></a:r></a:p></p:sld>");

        Assert.Equal(new[] { "teklif.docx" }, Found("kurulumu & bakım teklifimiz"));
        Assert.Equal(new[] { "fiyat.xlsx" }, Found("ip telefon"));
        Assert.Equal(new[] { "sunum.pptx" }, Found("yillik hedefler"));
        Assert.Equal("Santral kurulumu & bakım teklifimiz ektedir.", Assert.Single(ContentSearch.Find(_items, "santral", 10)).Snippet);
    }

    [Fact]
    public void Metin_olmayan_dosyalari_ve_klasorleri_atlar()
    {
        Add("resim.png", Encoding.UTF8.GetBytes("içinde gizli kelime var"));
        Add("ikili.txt", new byte[] { 0x67, 0x69, 0x7A, 0x6C, 0x69, 0x00, 0x01, 0x02 });
        Add("bozuk.docx", Encoding.UTF8.GetBytes("zip değil ama gizli kelime"));
        _items.Add(new LibraryItem(_dir, "gizli klasör.txt", "x", IsDirectory: true, IsLink: false, DateTime.Now, 0));

        Assert.Empty(Found("gizli"));
        Assert.False(ContentSearch.CanRead("rapor.pdf"));
        Assert.True(ContentSearch.CanRead("NOTLAR.TXT"));
    }

    [Fact]
    public void Cok_kisa_sorguda_icerik_aranmaz()
    {
        AddText("not.txt", "ab abc");

        Assert.Empty(Found("ab"));
        Assert.Empty(Found("  ab  "));
        Assert.Single(Found("abc"));
    }

    [Fact]
    public void Sonuc_siniri_ve_iptal_calisir()
    {
        for (int i = 0; i < 5; i++) AddText($"not-{i}.txt", "ortak kelime");

        Assert.Equal(2, Found("ortak", limit: 2).Length);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Empty(ContentSearch.Find(_items, "ortak", 50, cancelled.Token));
    }

    [Fact]
    public void Buyuk_dosyanin_yalnizca_bas_kismi_okunur()
    {
        var path = AddText("buyuk.log", "başta" + new string('x', 5000) + "sonda");

        Assert.Contains("başta", ContentSearch.ReadText(path, maxBytes: 100));
        Assert.DoesNotContain("sonda", ContentSearch.ReadText(path, maxBytes: 100));
    }
}
