namespace Gms.Web.Services.Tabs;

/// <summary>
/// ID tab (Design.md 6: layar + nomor dokumen). Aturan umum berbasis path agar tidak perlu daftar per halaman:
/// "/" = <c>home</c>; segmen dipisah ":" dan akhiran "/edit" dibuang, sehingga detail dan mode Ubah satu dokumen berbagi SATU tab
/// (<c>/purchase-orders/{id}</c> dan <c>/purchase-orders/{id}/edit</c>), dokumen baru satu tab per layar (<c>/purchase-orders/new</c>),
/// dan daftar satu tab per layar. Query string tidak membentuk tab baru (filter berada di dalam tab).
/// </summary>
public static class TabIdentity
{
    public static string For(Type pageType, IReadOnlyDictionary<string, object?> routeValues, string path)
    {
        _ = pageType;
        _ = routeValues;
        return FromPath(path);
    }

    public static string FromPath(string path)
    {
        var trimmed = path.Trim('/').ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            return TabManager.HomeId;
        }

        if (trimmed.EndsWith("/edit", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^5];
        }

        return trimmed.Replace('/', ':');
    }
}
