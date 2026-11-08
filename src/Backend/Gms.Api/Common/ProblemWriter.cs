using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gms.Api.Common;

/// <summary>Opsi JSON tunggal (camelCase, tanpa null) untuk API dan Problem Details.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Menulis <c>application/problem+json</c> sesuai katalog di docs/Architecture/ApiConventions.md.</summary>
public static class ProblemWriter
{
    public static async Task WriteAsync(HttpContext context, GmsProblemDetails problem)
    {
        var withTrace = problem with { TraceId = Activity.Current?.Id ?? context.TraceIdentifier };
        context.Response.StatusCode = withTrace.Status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(withTrace, JsonDefaults.Options));
    }

    public static GmsProblemDetails From(ProblemException ex) => new()
    {
        Type = ex.Type,
        Title = ex.Title,
        Status = ex.Status,
        Detail = ex.Detail,
        Code = ex.Code,
        Errors = ex.Errors,
        Violations = ex.Violations,
        LockedUntil = ex.LockedUntil,
    };

    /// <summary>Memetakan pengecualian apa pun menjadi Problem Details tanpa membocorkan detail internal.</summary>
    public static GmsProblemDetails Map(Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case ProblemException pe:
                return From(pe);

            case DbUpdateConcurrencyException:
                return From(ProblemException.Concurrency());

            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg }:
                return new GmsProblemDetails
                {
                    Type = ProblemTypes.Duplicate,
                    Title = "Data sudah ada.",
                    Status = StatusCodes.Status409Conflict,
                    Detail = "Data dengan nilai yang sama sudah ada.",
                    Code = pg.ConstraintName,
                };

            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation } pg }:
                logger.LogWarning(exception, "Constraint database menolak perubahan: {Constraint}", pg.ConstraintName);
                return new GmsProblemDetails
                {
                    Type = ProblemTypes.RuleViolation,
                    Title = "Aturan bisnis dilanggar.",
                    Status = StatusCodes.Status409Conflict,
                    Detail = "Perubahan ditolak karena melanggar aturan integritas data.",
                    Code = pg.ConstraintName,
                };

            case BadHttpRequestException or JsonException:
                return new GmsProblemDetails
                {
                    Type = ProblemTypes.BadRequest,
                    Title = "Permintaan tidak valid.",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Isi permintaan tidak dapat dipahami.",
                };

            case NpgsqlException or TimeoutException:
                logger.LogError(exception, "Database tidak tersedia");
                return new GmsProblemDetails
                {
                    Type = ProblemTypes.ServiceUnavailable,
                    Title = "Layanan tidak tersedia.",
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail = "Layanan sementara tidak tersedia. Coba lagi sebentar lagi.",
                };

            default:
                logger.LogError(exception, "Galat tak tertangani");
                return new GmsProblemDetails
                {
                    Type = ProblemTypes.InternalError,
                    Title = "Terjadi kesalahan pada server.",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = "Terjadi kesalahan tak terduga. Laporkan traceId ini kepada administrator.",
                };
        }
    }

    /// <summary>Problem standar untuk respons kosong (404, 405, dst.).</summary>
    public static GmsProblemDetails ForStatus(int status) => status switch
    {
        StatusCodes.Status404NotFound => new GmsProblemDetails
        {
            Type = ProblemTypes.NotFound, Title = "Data tidak ditemukan.", Status = status, Detail = "Alamat atau data yang diminta tidak ditemukan.",
        },
        StatusCodes.Status401Unauthorized => new GmsProblemDetails
        {
            Type = ProblemTypes.SessionExpired, Title = "Sesi tidak valid.", Status = status, Detail = "Silakan masuk kembali.",
        },
        StatusCodes.Status403Forbidden => new GmsProblemDetails
        {
            Type = ProblemTypes.Forbidden, Title = "Akses ditolak.", Status = status, Detail = "Anda tidak memiliki izin untuk aksi ini.",
        },
        _ => new GmsProblemDetails
        {
            Type = ProblemTypes.BadRequest, Title = "Permintaan tidak valid.", Status = status,
        },
    };
}
