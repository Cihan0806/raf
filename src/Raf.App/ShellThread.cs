using System.Collections.Concurrent;

namespace Raf.App;

/// <summary>
/// Windows kabuğunun betik nesneleriyle (Shell.Application) yapılan bütün işlerin yürüdüğü tek iş
/// parçacığı. Uygulama boyunca yaşar.
/// </summary>
/// <remarks>
/// Bu nesneler oluşturuldukları iş parçacığına bağlıdır, tür bilgilerini ise süreç genelinde
/// paylaşır. İki ayrı iş parçacığından kullanılınca biri öbürünün boşalmasını bekler; bekleneni
/// kapatınca da paylaşılan nesneler bırakılır ve bekleyen taraf "COM object that has been separated
/// from its underlying RCW" hatasıyla düşer (ilk açılışta uygulama listesi okunurken karşılama
/// ekranının Hızlı Erişim'e bakması gibi). Hepsi aynı ve hiç kapanmayan iş parçacığında olunca
/// paylaşılan bir şey kalmaz.
/// </remarks>
internal static class ShellThread
{
    private static readonly BlockingCollection<Action> Queue = new();

    static ShellThread()
    {
        var thread = new Thread(() =>
        {
            foreach (var work in Queue.GetConsumingEnumerable()) work();
        })
        { IsBackground = true, Name = "Raf.Kabuk" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>İşi kabuk iş parçacığında, sıraya girenlerin ardından çalıştırır; sonuç ya da hata dönen görevle gelir.</summary>
    public static Task<T> Run<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }
}
