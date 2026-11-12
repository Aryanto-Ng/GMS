namespace Gms.Web.Infrastructure.Http;

/// <summary>Konfigurasi bagian <c>Api</c>: alamat API dan pengalih data tiruan (ADR-014).</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Alamat dasar API, mis. <c>https://localhost:5001</c>. Wajib bila <see cref="UseMock"/> false.</summary>
    public string BaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>true = semua antarmuka klien memakai <c>MockXxxApi</c> (data dalam memori) tanpa API/DB.</summary>
    public bool UseMock { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}
