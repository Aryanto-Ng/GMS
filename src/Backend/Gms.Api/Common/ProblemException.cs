using Gms.Shared.Common;
using Gms.Shared.Problems;

namespace Gms.Api.Common;

/// <summary>
/// Galat bisnis yang dipetakan ke RFC 7807 Problem Details oleh <see cref="ProblemWriter"/>.
/// Handler melempar ini; jangan membocorkan detail internal pada pesan.
/// </summary>
public sealed class ProblemException : Exception
{
    public ProblemException(int status, string type, string title, string? detail = null, string? code = null)
        : base(detail ?? title)
    {
        Status = status;
        Type = type;
        Title = title;
        Detail = detail;
        Code = code;
    }

    public int Status { get; }
    public string Type { get; }
    public string Title { get; }
    public string? Detail { get; }
    public string? Code { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
    public IReadOnlyList<RuleViolation>? Violations { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }

    public static ProblemException Validation(ErrorBag errors) =>
        new(StatusCodes.Status422UnprocessableEntity, ProblemTypes.ValidationFailed, "Data tidak valid.",
            "Periksa kembali isian yang ditandai.")
        {
            Errors = errors.ToDictionary(),
        };

    public static ProblemException Validation(string path, string message) =>
        Validation(new ErrorBag().Add(path, message));

    public static ProblemException NotFound(string what) =>
        new(StatusCodes.Status404NotFound, ProblemTypes.NotFound, "Data tidak ditemukan.", $"{what} tidak ditemukan.");

    public static ProblemException Duplicate(string path, string message) =>
        new(StatusCodes.Status409Conflict, ProblemTypes.Duplicate, "Data sudah ada.", message)
        {
            Errors = new Dictionary<string, string[]> { [path] = [message] },
        };

    public static ProblemException StateConflict(string message, string? code = null) =>
        new(StatusCodes.Status409Conflict, ProblemTypes.StateConflict, "Status tidak mengizinkan aksi ini.", message, code);

    public static ProblemException Rule(string code, string message, IReadOnlyList<RuleViolation>? violations = null) =>
        new(StatusCodes.Status409Conflict, ProblemTypes.RuleViolation, "Aturan bisnis dilanggar.", message, code)
        {
            Violations = violations,
        };

    public static ProblemException Concurrency() =>
        new(StatusCodes.Status409Conflict, ProblemTypes.ConcurrencyConflict, "Data telah diubah pengguna lain.",
            "Data telah diubah oleh pengguna lain. Muat ulang lalu coba lagi.");

    public static ProblemException Forbidden(string? detail = null) =>
        new(StatusCodes.Status403Forbidden, ProblemTypes.Forbidden, "Akses ditolak.",
            detail ?? "Anda tidak memiliki izin untuk aksi ini.");

    public static ProblemException InvalidCredentials() =>
        new(StatusCodes.Status401Unauthorized, ProblemTypes.InvalidCredentials, "Login gagal.",
            "Username atau password salah.");

    public static ProblemException AccountLocked(DateTimeOffset? until) =>
        new(StatusCodes.Status401Unauthorized, ProblemTypes.AccountLocked, "Akun terkunci.",
            "Terlalu banyak percobaan. Coba lagi nanti atau hubungi Administrator.")
        {
            LockedUntil = until,
        };

    public static ProblemException MustChangePassword() =>
        new(StatusCodes.Status403Forbidden, ProblemTypes.MustChangePassword, "Wajib mengganti password.",
            "Anda harus mengganti password sebelum melanjutkan.");
}

/// <summary>Pengumpul galat validasi per field (kunci = jalur field seperti <c>lines[2].qty</c>).</summary>
public sealed class ErrorBag
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public ErrorBag Add(string path, string message)
    {
        if (!_errors.TryGetValue(path, out var list))
        {
            list = [];
            _errors[path] = list;
        }

        list.Add(message);
        return this;
    }

    public void ThrowIfAny()
    {
        if (Any)
        {
            throw ProblemException.Validation(this);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}
