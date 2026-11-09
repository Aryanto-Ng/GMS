using Gms.Shared.Problems;

namespace Gms.Web.Infrastructure.Http;

/// <summary>Jenis tampilan galat menurut pemetaan status/Problem Details (_Foundation-UI.md 7).</summary>
public enum ProblemKind
{
    /// <summary>400/422: tandai field dan ringkasan.</summary>
    Validation,

    /// <summary>401: kembali ke login.</summary>
    Unauthorized,

    /// <summary>403: halaman akses ditolak.</summary>
    Forbidden,

    /// <summary>404: data tidak ditemukan.</summary>
    NotFound,

    /// <summary>409 konkurensi: "Data telah diubah pengguna lain" + Muat ulang.</summary>
    Concurrency,

    /// <summary>409 aturan bisnis/status/duplikat/periode tertutup: jelaskan penahan.</summary>
    Business,

    /// <summary>5xx/503: galat umum dengan traceId dan Coba lagi.</summary>
    Server,
}

public sealed record ProblemView(ProblemKind Kind, string Title, string? Detail, string? TraceId, bool CanRetry);

/// <summary>Memetakan <see cref="GmsProblemDetails"/> ke teks dan jenis tampilan. Satu tempat agar semua halaman bereaksi sama.</summary>
public static class ProblemPresenter
{
    public static ProblemView Describe(GmsProblemDetails problem)
    {
        var kind = Classify(problem);
        return kind switch
        {
            ProblemKind.Validation => new(kind, "Data belum valid", problem.Detail ?? "Periksa kembali isian yang ditandai.", problem.TraceId, false),
            ProblemKind.Unauthorized => new(kind, UnauthorizedTitle(problem), problem.Detail ?? "Silakan masuk kembali.", problem.TraceId, false),
            ProblemKind.Forbidden => new(kind, "Akses ditolak", "Anda tidak memiliki izin untuk aksi ini.", problem.TraceId, false),
            ProblemKind.NotFound => new(kind, "Data tidak ditemukan", problem.Detail ?? "Data mungkin sudah dihapus atau dipindahkan.", problem.TraceId, false),
            ProblemKind.Concurrency => new(kind, "Data telah diubah pengguna lain", "Muat ulang untuk melihat versi terbaru, lalu ulangi perubahan Anda.", problem.TraceId, false),
            ProblemKind.Business => new(kind, problem.Title ?? "Aksi tidak dapat dilakukan", problem.Detail, problem.TraceId, false),
            _ => new(kind, "Terjadi kesalahan pada server", "Coba lagi. Bila berlanjut, laporkan kepada administrator dengan menyebut ID rujukan.", problem.TraceId, true),
        };
    }

    public static ProblemKind Classify(GmsProblemDetails problem) => problem.Type switch
    {
        ProblemTypes.ConcurrencyConflict => ProblemKind.Concurrency,
        ProblemTypes.ValidationFailed or ProblemTypes.BadRequest => ProblemKind.Validation,
        ProblemTypes.SessionExpired or ProblemTypes.SessionReplaced or ProblemTypes.AccountDisabled or ProblemTypes.InvalidCredentials => ProblemKind.Unauthorized,
        ProblemTypes.Forbidden or ProblemTypes.MustChangePassword => ProblemKind.Forbidden,
        ProblemTypes.NotFound => ProblemKind.NotFound,
        ProblemTypes.StateConflict or ProblemTypes.RuleViolation or ProblemTypes.Duplicate or ProblemTypes.PeriodClosed or ProblemTypes.ImportInvalid => ProblemKind.Business,
        _ => problem.Status switch
        {
            401 => ProblemKind.Unauthorized,
            403 => ProblemKind.Forbidden,
            404 => ProblemKind.NotFound,
            400 or 422 => ProblemKind.Validation,
            409 => ProblemKind.Business,
            _ => ProblemKind.Server,
        },
    };

    private static string UnauthorizedTitle(GmsProblemDetails problem) => problem.Type switch
    {
        ProblemTypes.SessionReplaced => "Sesi digantikan",
        ProblemTypes.AccountDisabled => "Akun dinonaktifkan",
        _ => "Sesi berakhir",
    };

    /// <summary>Kode alasan untuk <c>/account/session-ended</c> (pesan di halaman login).</summary>
    public static string SessionEndReason(GmsProblemDetails problem) => problem.Type switch
    {
        ProblemTypes.SessionReplaced => "session-replaced",
        ProblemTypes.AccountDisabled => "account-disabled",
        _ => "session-expired",
    };
}
