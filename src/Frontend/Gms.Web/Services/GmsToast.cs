using AntDesign;

namespace Gms.Web.Services;

/// <summary>
/// Pemberitahuan singkat setelah aksi berhasil (Design.md 8.7: success menyebut dampaknya). Membungkus <see cref="IMessageService"/> AntDesign
/// agar halaman tidak bergantung pada pustaka UI. Tidak pernah melempar: kegagalan menampilkan toast tidak boleh menggagalkan aksi yang sudah berhasil.
/// </summary>
public interface IGmsToast
{
    void ShowSuccess(string text);

    void ShowInfo(string text);

    void ShowError(string text);
}

public sealed class GmsToast(IMessageService messages) : IGmsToast
{
    public void ShowSuccess(string text) => Safe(() => messages.Success(text, 4));

    public void ShowInfo(string text) => Safe(() => messages.Info(text, 4));

    public void ShowError(string text) => Safe(() => messages.Error(text, 8));

    private static void Safe(Action show)
    {
        try
        {
            show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ObjectDisposedException)
        {
            // kontainer AntDesign belum/tidak aktif (mis. prerender atau sirkuit tertutup)
        }
    }
}
