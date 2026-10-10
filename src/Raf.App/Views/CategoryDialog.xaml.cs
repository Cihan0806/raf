using System.Windows;
using System.Windows.Controls;
using Raf.Core;

namespace Raf.App.Views;

/// <summary>Kategori ekleme ve düzenleme. Kaydedilince Result, yeni değerleri taşıyan bir taslaktır.</summary>
public partial class CategoryDialog : ChromeWindow
{
    private readonly RafConfig _cfg;
    private readonly Category? _editing;
    private string _color;
    private string _icon;
    private string _parentId;

    public Category? Result { get; private set; }

    /// <param name="parentId">Yeni kategori için önceden seçili gelecek üst kategori (yoksa boş).</param>
    public CategoryDialog(RafConfig cfg, Category? editing, string parentId = "")
    {
        InitializeComponent();
        // Seçenekler çoğalınca pencere küçük ekrana sığmayabilir; sığmayan kısım kaydırılır.
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        _cfg = cfg;
        _editing = editing;

        List<string> usedColors;
        List<Category> possibleParents;
        bool hasChildren;
        lock (cfg)
        {
            usedColors = cfg.Categories.Select(c => c.Color).ToList();
            possibleParents = cfg.TopLevel().Where(c => c != editing && !c.IsFallback).ToList();
            hasChildren = editing is not null && cfg.Children(editing).Count > 0;
            _parentId = editing is null ? parentId : cfg.Parent(editing)?.Id ?? "";
        }

        _color = editing?.Color
            ?? Defaults.Palette.FirstOrDefault(p => !usedColors.Contains(p, StringComparer.OrdinalIgnoreCase))
            ?? Defaults.Palette[0];
        _icon = string.IsNullOrWhiteSpace(editing?.Icon) ? Defaults.DefaultIcon : editing.Icon;

        BuildParentChips(possibleParents, locked: hasChildren || editing?.IsFallback == true);
        BuildIconSwatches();
        BuildColorSwatches();

        if (editing is null)
        {
            Heading.Text = "Yeni kategori";
        }
        else
        {
            Heading.Text = $"“{editing.Name}” kategorisini düzenle";
            NameBox.Text = editing.Name;
            ExtensionsBox.Text = string.Join("  ", editing.Extensions);
            WordsBox.Text = string.Join(", ", editing.NameContains);
            SitesBox.Text = string.Join(", ", editing.Sites);
            AllRadio.IsChecked = editing.MatchAll;

            if (editing.IsFallback)
            {
                RulesPanel.Visibility = Visibility.Collapsed;
                FallbackNote.Visibility = Visibility.Visible;
            }
        }

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>
    /// "Yok" ve her ana kategori için bir seçenek. Alt kategorisi olan bir kategori (ve "Diğer")
    /// başkasının altına giremediği için onlarda bölüm gizlenir.
    /// </summary>
    private void BuildParentChips(List<Category> parents, bool locked)
    {
        if (locked || parents.Count == 0)
        {
            ParentPanel.Visibility = Visibility.Collapsed;
            _parentId = "";
            return;
        }
        if (parents.All(p => p.Id != _parentId)) _parentId = "";

        void AddChip(string id, object content)
        {
            var chip = new RadioButton
            {
                GroupName = "Parent",
                Style = (Style)FindResource("Chip"),
                Content = content,
                IsChecked = id == _parentId,
            };
            chip.Checked += (_, _) => _parentId = id;
            ParentChips.Children.Add(chip);
        }

        AddChip("", "Yok (ana kategori)");
        foreach (var parent in parents)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock
            {
                Text = Ui.Glyph(parent.Icon),
                Style = (Style)FindResource("Icon"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = Ui.BrushOf(parent.Color),
            });
            label.Children.Add(new TextBlock { Text = parent.Name });
            AddChip(parent.Id, label);
        }
    }

    private void BuildIconSwatches()
    {
        var icons = Defaults.Icons.ToList();
        if (!icons.Contains(_icon, StringComparer.OrdinalIgnoreCase)) icons.Add(_icon);
        foreach (var icon in icons)
        {
            var swatch = new RadioButton
            {
                GroupName = "Icon",
                Style = (Style)FindResource("IconSwatch"),
                Content = Ui.Glyph(icon),
                Foreground = Ui.BrushOf(_color),
                IsChecked = string.Equals(icon, _icon, StringComparison.OrdinalIgnoreCase),
            };
            swatch.Checked += (_, _) => _icon = icon;
            IconSwatches.Children.Add(swatch);
        }
    }

    private void BuildColorSwatches()
    {
        var palette = Defaults.Palette.ToList();
        if (!palette.Contains(_color, StringComparer.OrdinalIgnoreCase)) palette.Add(_color);
        foreach (var color in palette)
        {
            var swatch = new RadioButton
            {
                GroupName = "Color",
                Style = (Style)FindResource("Swatch"),
                Background = Ui.BrushOf(color),
                IsChecked = string.Equals(color, _color, StringComparison.OrdinalIgnoreCase),
                ToolTip = color,
            };
            swatch.Checked += (_, _) =>
            {
                // Simgeler seçilen renkle boyanır; kenar çubuğunda nasıl duracağı hemen görülür.
                _color = color;
                foreach (RadioButton iconSwatch in IconSwatches.Children) iconSwatch.Foreground = Ui.BrushOf(color);
            };
            Swatches.Children.Add(swatch);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("Kategoriye bir ad ver.");
            return;
        }

        // Aynı ad, farklı üst kategorilerin altında kullanılabilir; aynı yerde iki kez kullanılamaz.
        bool taken;
        lock (_cfg)
        {
            taken = _cfg.Categories.Any(c => c != _editing
                && (_cfg.Parent(c)?.Id ?? "") == _parentId
                && TextUtil.Fold(c.Name) == TextUtil.Fold(name));
        }
        if (taken)
        {
            ShowError($"Burada “{name}” adında bir kategori zaten var.");
            return;
        }

        bool rules = _editing?.IsFallback != true;
        Result = new Category
        {
            Name = name,
            ParentId = _parentId,
            Color = _color,
            Icon = _icon,
            Extensions = rules ? TextUtil.ParseExtensions(ExtensionsBox.Text) : new(),
            NameContains = rules ? TextUtil.ParsePhrases(WordsBox.Text) : new(),
            Sites = rules ? TextUtil.ParsePhrases(SitesBox.Text) : new(),
            MatchAll = rules && AllRadio.IsChecked == true,
        };
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        NameBox.Focus();
    }
}
