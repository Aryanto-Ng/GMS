namespace Gms.Web.Services;

/// <summary>
/// Pemberitahuan singkat setelah aksi (Design.md 10 Toast): sukses menyebut dampaknya; pemanggil memberi jenis SECARA EKSPLISIT.
/// Tidak pernah melempar: kegagalan menampilkan toast tidak boleh menggagalkan aksi yang sudah berhasil.
/// </summary>
public interface IGmsToast
{
    void ShowSuccess(string text);

    void ShowInfo(string text);

    void ShowError(string text);
}

public enum ToastKind { Success, Info, Error }

public sealed record ToastItem(Guid Id, ToastKind Kind, string Text);

/// <summary>Toast per sirkuit. <see cref="ToastHost"/> berlangganan <see cref="Changed"/>; setiap toast hilang otomatis (galat lebih lama).</summary>
public sealed class GmsToast : IGmsToast
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(2800);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(4);
    private readonly List<ToastItem> _items = [];
    private readonly object _lock = new();

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }
    }

    public void ShowSuccess(string text) => Add(ToastKind.Success, text, Short);

    public void ShowInfo(string text) => Add(ToastKind.Info, text, Short);

    public void ShowError(string text) => Add(ToastKind.Error, text, Long);

    public void Dismiss(Guid id)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Id == id);
        }

        Raise();
    }

    private void Add(ToastKind kind, string text, TimeSpan life)
    {
        var item = new ToastItem(Guid.NewGuid(), kind, text);
        lock (_lock)
        {
            _items.Add(item);
        }

        Raise();
        _ = Task.Delay(life).ContinueWith(_ => Dismiss(item.Id), TaskScheduler.Default);
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // sirkuit sudah tertutup
        }
    }
}
