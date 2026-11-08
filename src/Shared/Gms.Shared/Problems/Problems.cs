using Gms.Shared.Common;

namespace Gms.Shared.Problems;

/// <summary>Tipe Problem Details (RFC 7807) berupa URN. Katalog: docs/Architecture/ApiConventions.md bagian 2.</summary>
public static class ProblemTypes
{
    private const string Prefix = "urn:gms:problem:";

    public const string ValidationFailed = Prefix + "validation-failed";
    public const string BadRequest = Prefix + "bad-request";
    public const string InvalidCredentials = Prefix + "invalid-credentials";
    public const string AccountLocked = Prefix + "account-locked";
    public const string SessionExpired = Prefix + "session-expired";
    public const string SessionReplaced = Prefix + "session-replaced";
    public const string AccountDisabled = Prefix + "account-disabled";
    public const string MustChangePassword = Prefix + "must-change-password";
    public const string Forbidden = Prefix + "forbidden";
    public const string NotFound = Prefix + "not-found";
    public const string ConcurrencyConflict = Prefix + "concurrency-conflict";
    public const string Duplicate = Prefix + "duplicate";
    public const string StateConflict = Prefix + "state-conflict";
    public const string RuleViolation = Prefix + "rule-violation";
    public const string PeriodClosed = Prefix + "period-closed";
    public const string ImportInvalid = Prefix + "import-invalid";
    public const string InternalError = Prefix + "internal-error";
    public const string ServiceUnavailable = Prefix + "service-unavailable";
}

/// <summary>Kode mesin spesifik (anggota <c>code</c>) yang dipakai UI untuk perilaku khusus.</summary>
public static class ProblemCodes
{
    public const string OutputCumulativeExceeded = "output.cumulative-exceeded";
    public const string OutputBelowDownstream = "output.below-downstream";

    /// <summary>Output proses pertama melebihi Qty WO: klien harus meminta konfirmasi lalu mengirim ulang dengan ConfirmExceedWoQty = true.</summary>
    public const string OutputExceedsWoQtyNeedsConfirmation = "output.exceeds-wo-qty-needs-confirmation";
    public const string VendorSentExceeded = "vendor.sent-exceeded";
    public const string VendorReceivedExceeded = "vendor.received-exceeded";
    public const string StockInsufficient = "stock.insufficient";
    public const string AllocationExceeded = "allocation.exceeded";
    public const string PoRemainingPlanExceeded = "po.remaining-plan-exceeded";
    public const string PoQtyBelowCommitted = "po.qty-below-committed";
    public const string PoLineInUse = "po.line-in-use";
    public const string DeliveryExceedsAllocation = "delivery.exceeds-allocation";
    public const string FgNotReady = "fg.not-ready";
    public const string WoStatusInvalid = "wo.status-invalid";
    public const string PayrollOpenPeriodExists = "payroll.open-period-exists";
    public const string PayrollNotContiguous = "payroll.not-contiguous";
    public const string PayrollStartAfterOldestOutput = "payroll.start-after-oldest-output";
    public const string LastAdministrator = "access.last-administrator";
    public const string CannotDeactivateSelf = "access.cannot-deactivate-self";

    // Master Data
    public const string MasterInUse = "master.in-use";
    public const string FgSizeHasTariffs = "fg.size-has-tariffs";
    public const string FgStepHasTariffs = "fg.step-has-tariffs";
    public const string FgCopyNeedsConfirm = "fg.copy-needs-confirm";
    public const string FgHasOpenDocuments = "fg.has-open-documents";
    public const string ProcessTypeLocked = "process.type-locked";
    public const string ImportFileInvalid = "import.file-invalid";
    public const string SystemRoleImmutable = "access.system-role-immutable";
    public const string RoleInUse = "access.role-in-use";
}

/// <summary>Satu baris galat impor (nomor baris berbasis 1 pada berkas).</summary>
public sealed record ImportRowProblem(int RowNumber, string? Key, string Message);

/// <summary>Representasi Problem Details yang di-parse klien dari <c>application/problem+json</c>.</summary>
public sealed record GmsProblemDetails
{
    public string Type { get; init; } = ProblemTypes.InternalError;
    public string? Title { get; init; }
    public int Status { get; init; }
    public string? Detail { get; init; }
    public string? Code { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
    public IReadOnlyList<RuleViolation>? Violations { get; init; }
    public IReadOnlyList<ImportRowProblem>? Rows { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
}

/// <summary>Dilempar oleh klien bertipe ketika API mengembalikan galat. Mengandung Problem Details untuk dirender UI.</summary>
public sealed class ApiException : Exception
{
    public ApiException(GmsProblemDetails problem)
        : base(problem?.Detail ?? problem?.Title ?? "Terjadi kesalahan.")
    {
        Problem = problem ?? new GmsProblemDetails();
    }

    public GmsProblemDetails Problem { get; }

    public int StatusCode => Problem.Status;
}
