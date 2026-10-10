using Raf.App.Views;
using Raf.Core;

namespace Raf.App;

// Var olan dosyaları içeri alma: pencereyi açar, sonucu bildirir ve hepsini birden geri almayı sunar.
public partial class MainWindow
{
    // Toplu işlerde her öğe için ayrı bildirim çıkmasın diye; iş bitince tek bir özet gösterilir.
    private bool _quietMoves;

    /// <param name="folders">Verilirse kaynak olarak yalnızca bunlar sunulur.</param>
    internal void ImportExisting(IReadOnlyList<string>? folders = null, bool activate = true)
    {
        ImportResult? result;
        _quietMoves = true;
        try { result = ImportDialog.Show(this, folders, activate); }
        finally { _quietMoves = false; }
        if (result is null) return;

        // "Klasörler" kategorisi açılmış, bağlantılar eklenmiş olabilir.
        _s.NotifyChanged();

        int count = result.Moved.Count + result.Linked.Count;
        if (count > 0)
        {
            ShowSnackAction(count == 1 ? "1 öğe içeri alındı." : $"{count} öğe içeri alındı.", () => UndoImport(result));
        }
        if (result.Errors.Count > 0)
        {
            MessageDialog.Info(this, "Bazı öğeler içeri alınamadı", string.Join("\n", result.Errors));
        }
    }

    private async void UndoImport(ImportResult result)
    {
        List<string> errors;
        _quietMoves = true;
        try { errors = await Task.Run(() => Importer.Undo(_s.Config, _s.Store, _s.Sorter, result)); }
        finally { _quietMoves = false; }

        _s.NotifyChanged();
        int count = result.Moved.Count + result.Linked.Count - errors.Count;
        ShowSnack(count == 1 ? "1 öğe eski yerine döndü." : $"{count} öğe eski yerine döndü.", null);
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler geri alınamadı", string.Join("\n", errors));
    }

    private void Import_Click(object sender, System.Windows.RoutedEventArgs e) => ImportExisting();
}
