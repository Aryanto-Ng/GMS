using Gms.Shared.Problems;
using Microsoft.AspNetCore.Components;

namespace Gms.Web.Infrastructure.Http;

/// <summary>
/// Penanganan galat API yang seragam untuk halaman: 401 → kembali ke login (dengan alasan dan returnUrl); selainnya dikembalikan sebagai
/// Problem Details untuk dirender komponen (banner/ringkasan/state). Halaman tidak perlu membaca status HTTP mentah.
/// </summary>
public sealed class ApiErrorHandler(NavigationManager navigation)
{
    /// <summary>true bila galat adalah sesi berakhir dan navigasi ke login sudah dipicu.</summary>
    public bool TryHandleSessionEnd(GmsProblemDetails problem)
    {
        if (ProblemPresenter.Classify(problem) != ProblemKind.Unauthorized)
        {
            return false;
        }

        var returnUrl = "/" + navigation.ToBaseRelativePath(navigation.Uri);
        navigation.NavigateTo($"/account/session-ended?reason={ProblemPresenter.SessionEndReason(problem)}&returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
        return true;
    }

    /// <summary>Menjalankan aksi; mengembalikan null bila berhasil atau sesi berakhir (sudah dialihkan), selain itu Problem Details.</summary>
    public async Task<GmsProblemDetails?> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (ApiException ex)
        {
            return TryHandleSessionEnd(ex.Problem) ? null : ex.Problem;
        }
    }
}
