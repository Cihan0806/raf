using System.Windows;
using System.Windows.Controls;
using Raf.Core;

namespace Raf.App.Views;

/// <summary>Seçili öğenin büyük görüntüsünü, bilgilerini ve (metinse) içeriğinin başını gösteren yan bölme.</summary>
public partial class PreviewPane : UserControl
{
    private const int TextPreviewBytes = 48 * 1024;
    private const int TextPreviewLines = 60;
    private const int TextPreviewChars = 3500;

    private ItemVM? _current;
    private int _version;

    public PreviewPane()
    {
        InitializeComponent();
    }

    public event Action<ItemVM>? OpenRequested;
    public event Action<ItemVM>? RevealRequested;

    public void Show(IReadOnlyList<ItemVM> selected)
    {
        int version = ++_version;
        _current = selected.Count == 1 ? selected[0] : null;

        if (_current is null)
        {
            Empty.Text = selected.Count > 1 ? $"{selected.Count} öğe seçili." : "Bir öğe seç; önizlemesi burada görünür.";
            Empty.Visibility = Visibility.Visible;
            Details.Visibility = Visibility.Collapsed;
            return;
        }

        var vm = _current;
        Empty.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;
        Details.ScrollToTop();

        NameText.Text = vm.Item.Name;
        KindText.Text = vm.Item.IsDirectory ? vm.TypeText : $"{vm.TypeText}  ·  {vm.SizeText}";
        CategoryIcon.Text = vm.CategoryGlyph;
        CategoryIcon.Foreground = vm.CategoryDot;
        CategoryText.Text = vm.CategoryName;
        DateText.Text = vm.DateText;
        PathText.Text = vm.Item.IsLink ? "Bağlantı, asıl yeri:\n" + vm.Item.Path : vm.Item.Path;

        SnippetText.Text = vm.Snippet;
        SnippetPanel.Visibility = vm.Snippet.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Büyük görüntü gelene kadar listedeki küçük simge durur.
        Picture.Source = vm.Icon;
        IconLoader.RequestLarge(vm.Item.Path, image => Dispatcher.InvokeAsync(() =>
        {
            if (version == _version && image is not null) Picture.Source = image;
        }));

        TextPanel.Visibility = Visibility.Collapsed;
        if (!vm.Item.IsDirectory && ContentSearch.CanRead(vm.Item.Path)) LoadText(vm.Item.Path, version);
    }

    private async void LoadText(string path, int version)
    {
        var text = await Task.Run(() => ContentSearch.ReadText(path, TextPreviewBytes));
        if (version != _version || string.IsNullOrWhiteSpace(text)) return;

        var lines = text.Replace("\r\n", "\n").Split('\n').Take(TextPreviewLines);
        var preview = string.Join("\n", lines).TrimEnd();
        bool cut = preview.Length > TextPreviewChars || preview.Length < text.TrimEnd().Length;
        if (preview.Length > TextPreviewChars) preview = preview[..TextPreviewChars];

        TextPreview.Text = cut ? preview + "\n…" : preview;
        TextPanel.Visibility = Visibility.Visible;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) OpenRequested?.Invoke(_current);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) RevealRequested?.Invoke(_current);
    }
}
