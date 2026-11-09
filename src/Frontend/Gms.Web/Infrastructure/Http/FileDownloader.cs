using Gms.Shared.Common;
using Microsoft.JSInterop;

namespace Gms.Web.Infrastructure.Http;

/// <summary>Menyimpan berkas hasil API (template CSV, PDF, Excel) ke perangkat pengguna melalui modul JS kecil.</summary>
public sealed class FileDownloader(IJSRuntime js)
{
    public async Task SaveAsync(FileContent file)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "/download.js");
        await module.InvokeVoidAsync("save", file.FileName, file.ContentType, Convert.ToBase64String(file.Bytes));
    }
}
