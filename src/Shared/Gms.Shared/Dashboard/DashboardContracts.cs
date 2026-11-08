namespace Gms.Shared.Dashboard;

/// <summary>Id widget pada rute <c>/dashboard/{widgetId}</c> (ApiConventions.md bagian 4.9).</summary>
public static class DashboardWidgetIds
{
    public const string PoLate = "po-late";
    public const string DueSoon = "due-soon";
    public const string PoNoCustomerNumber = "po-no-customer-number";
    public const string UnplannedDemand = "unplanned-demand";
    public const string WoRunning = "wo-running";
    public const string WoLate = "wo-late";
    public const string OutputToday = "output-today";
    public const string VendorBalance = "vendor-balance";
    public const string UnhandedOver = "unhanded-over";
    public const string StockSummary = "stock-summary";
    public const string PayrollPeriod = "payroll-period";
    public const string Security = "security";
}

public sealed record PoLateItem(Guid PoId, string Code, string CustomerName, int DaysLate, int ShortageQty);

public sealed record PoLateWidget(int PoCount, int ShortageQty, IReadOnlyList<PoLateItem> Items);

public sealed record DueSoonWidget(int PoCount, int Qty);

public sealed record PoNoCustomerNumberWidget(int PoCount);

public sealed record UnplannedDemandWidget(int PoCount, int Qty);

public sealed record WoProgressItem(Guid Id, string Code, string FgCode, decimal ProgressPercent, DateOnly? TargetDate);

public sealed record WoRunningWidget(
    int Released,
    int InProgress,
    int CompletedAwaitingClose,
    IReadOnlyList<WoProgressItem> Items);

public sealed record WoLateWidget(int Count);

public sealed record ProcessQty(string ProcessName, int Qty);

public sealed record OutputTodayWidget(int TotalQty, IReadOnlyList<ProcessQty> ByProcess);

public sealed record VendorQty(string VendorName, int Qty);

public sealed record VendorBalanceWidget(int TotalQty, IReadOnlyList<VendorQty> ByVendor);

public sealed record UnhandedItem(Guid WoId, string WoCode, int Qty);

public sealed record UnhandedOverWidget(int TotalQty, IReadOnlyList<UnhandedItem> Items);

public sealed record FgAvailable(string FgCode, int Available);

public sealed record StockSummaryWidget(int OnHand, int Allocated, int Available, IReadOnlyList<FgAvailable> TopFgs);

public sealed record PayrollPeriodWidget(
    bool HasOpenPeriod,
    Guid? PeriodId,
    string? Code,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int WorkerCount,
    decimal ProvisionalTotal,
    int DaysLeft,
    DateOnly? SuggestedNextStart);

public sealed record SecurityWidget(int LockedAccounts, int FailedLogins24h);

/// <summary>Setiap widget dimuat independen; kegagalan satu widget tidak memengaruhi yang lain.</summary>
public interface IDashboardApi
{
    Task<PoLateWidget> GetPoLateAsync(CancellationToken ct = default);
    Task<DueSoonWidget> GetDueSoonAsync(CancellationToken ct = default);
    Task<PoNoCustomerNumberWidget> GetPoNoCustomerNumberAsync(CancellationToken ct = default);
    Task<UnplannedDemandWidget> GetUnplannedDemandAsync(CancellationToken ct = default);
    Task<WoRunningWidget> GetWoRunningAsync(CancellationToken ct = default);
    Task<WoLateWidget> GetWoLateAsync(CancellationToken ct = default);
    Task<OutputTodayWidget> GetOutputTodayAsync(CancellationToken ct = default);
    Task<VendorBalanceWidget> GetVendorBalanceAsync(CancellationToken ct = default);
    Task<UnhandedOverWidget> GetUnhandedOverAsync(CancellationToken ct = default);
    Task<StockSummaryWidget> GetStockSummaryAsync(CancellationToken ct = default);
    Task<PayrollPeriodWidget> GetPayrollPeriodAsync(CancellationToken ct = default);
    Task<SecurityWidget> GetSecurityAsync(CancellationToken ct = default);
}
