using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gms.Shared.Common;
using Gms.Shared.Problems;
using Gms.Web.Infrastructure.Auth;

namespace Gms.Web.Infrastructure.Http;

/// <summary>Opsi JSON yang sama dengan API: camelCase, enum sebagai string, null tidak ditulis.</summary>
public static class WebJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Pembangun query string: nilai null dilewati, tanggal <c>yyyy-MM-dd</c>, bool huruf kecil, enum sebagai nama.</summary>
public static class ApiQuery
{
    public static string Build(params (string Key, object? Value)[] pairs)
    {
        var parts = new List<string>();
        foreach (var (key, value) in pairs)
        {
            var text = value switch
            {
                null => null,
                string s when string.IsNullOrWhiteSpace(s) => null,
                bool b => b ? "true" : "false",
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Enum e => e.ToString(),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (text is not null)
            {
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
            }
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }
}

/// <summary>
/// Klien HTTP ke API (BFF). Melampirkan token sesi dari pengguna yang sedang login, dan mengubah SETIAP respons galat menjadi
/// <see cref="ApiException"/> berisi Problem Details (UI tidak pernah membaca status mentah). Kegagalan jaringan dilaporkan sebagai
/// 503 dengan pesan manusiawi agar halaman tetap menampilkan state error yang rapi.
/// </summary>
public sealed class GmsApiClient(IHttpClientFactory factory, ISessionTokenProvider tokens)
{
    public const string HttpClientName = "gms-api";

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Get, path, null, ct), ct);

    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Post, path, body, ct), ct);

    public async Task PostAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, ct);
    }

    public async Task<T> PutAsync<T>(string path, object? body, CancellationToken ct = default) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Put, path, body, ct), ct);

    public async Task PutAsync(string path, object? body, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, path, null, ct);
    }

    /// <summary>Mengunduh berkas (PDF, Excel, CSV). Nama berkas dari <c>Content-Disposition</c>.</summary>
    public async Task<FileContent> DownloadAsync(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "unduhan";
        return new FileContent(name, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", bytes);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var http = factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path);
        if (await tokens.GetTokenAsync() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, WebJson.Options), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ApiException(new GmsProblemDetails
            {
                Type = ProblemTypes.ServiceUnavailable, Status = (int)HttpStatusCode.ServiceUnavailable,
                Title = "Server tidak dapat dihubungi.", Detail = "Periksa koneksi lalu coba lagi. Bila berlanjut, hubungi administrator.",
            });
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new ApiException(await ParseProblemAsync(response, ct));
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default!;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, WebJson.Options, ct) ?? default!;
        }
    }

    /// <summary>Mengurai <c>application/problem+json</c>; badan lain (HTML, kosong) dipetakan menurut status HTTP.</summary>
    internal static async Task<GmsProblemDetails> ParseProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var problem = await JsonSerializer.DeserializeAsync<GmsProblemDetails>(await response.Content.ReadAsStreamAsync(ct), WebJson.Options, ct);
                if (problem is not null)
                {
                    return problem.Status == 0 ? problem with { Status = status } : problem;
                }
            }
            catch (JsonException)
            {
                // jatuh ke pemetaan generik di bawah
            }
        }

        return Generic(status);
    }

    public static GmsProblemDetails Generic(int status) => status switch
    {
        401 => new GmsProblemDetails { Type = ProblemTypes.SessionExpired, Status = 401, Title = "Sesi berakhir.", Detail = "Silakan masuk kembali." },
        403 => new GmsProblemDetails { Type = ProblemTypes.Forbidden, Status = 403, Title = "Akses ditolak.", Detail = "Anda tidak memiliki izin untuk aksi ini." },
        404 => new GmsProblemDetails { Type = ProblemTypes.NotFound, Status = 404, Title = "Data tidak ditemukan." },
        >= 500 => new GmsProblemDetails { Type = ProblemTypes.InternalError, Status = status, Title = "Terjadi kesalahan pada server.", Detail = "Coba lagi. Bila berlanjut, laporkan kepada administrator." },
        _ => new GmsProblemDetails { Type = ProblemTypes.BadRequest, Status = status, Title = "Permintaan tidak dapat diproses." },
    };
}
