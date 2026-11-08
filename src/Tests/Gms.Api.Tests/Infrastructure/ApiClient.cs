using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using Gms.Api.Common;
using Gms.Shared.Problems;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>Hasil panggilan HTTP: status, isi mentah, dan Problem Details bila galat.</summary>
public sealed record ApiResponse(HttpStatusCode Status, string Body, string? ContentType)
{
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    public T As<T>() =>
        JsonSerializer.Deserialize<T>(Body, JsonDefaults.Options)
        ?? throw new InvalidOperationException($"Respons kosong/tidak dapat dibaca sebagai {typeof(T).Name}: {Body}");

    public GmsProblemDetails Problem =>
        ContentType?.Contains("problem+json", StringComparison.OrdinalIgnoreCase) == true
            ? JsonSerializer.Deserialize<GmsProblemDetails>(Body, JsonDefaults.Options)!
            : throw new InvalidOperationException($"Respons bukan Problem Details ({(int)Status}): {Body}");

    /// <summary>Memastikan sukses; bila gagal, pesan menyertakan isi respons agar diagnosis mudah.</summary>
    public ApiResponse Ok(string? because = null)
    {
        if (!IsSuccess)
        {
            throw new Xunit.Sdk.XunitException($"Diharapkan sukses{(because is null ? "" : " (" + because + ")")} tetapi {(int)Status}: {Body}");
        }

        return this;
    }
}

public sealed class ApiClient(HttpClient http)
{
    public Task<ApiResponse> GetAsync(string path, string? token = null) => SendAsync(HttpMethod.Get, path, null, token);

    public Task<ApiResponse> PostAsync(string path, object? body = null, string? token = null) =>
        SendAsync(HttpMethod.Post, path, body, token);

    public Task<ApiResponse> PutAsync(string path, object? body, string? token = null) =>
        SendAsync(HttpMethod.Put, path, body, token);

    public Task<ApiResponse> DeleteAsync(string path, string? token = null) => SendAsync(HttpMethod.Delete, path, null, token);

    /// <summary>Unggah berkas multipart (impor CSV). Isi berkas dikirim apa adanya (byte).</summary>
    public async Task<ApiResponse> PostFileAsync(string path, string fileName, byte[] content, string? token, string fieldName = "file")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, fieldName, fileName);
        request.Content = form;

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResponse(response.StatusCode, text, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Mengunduh berkas mentah (template CSV, PDF, dst.).</summary>
    public async Task<(HttpStatusCode Status, byte[] Bytes, string? ContentType, string? FileName)> DownloadAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync(),
            response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentDisposition?.FileName);
    }

    public async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            var json = body is string raw ? raw : JsonSerializer.Serialize(body, JsonDefaults.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResponse(response.StatusCode, text, response.Content.Headers.ContentType?.MediaType);
    }
}
