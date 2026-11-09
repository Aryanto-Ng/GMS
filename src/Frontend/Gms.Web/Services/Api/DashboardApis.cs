using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Dashboard;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Payroll;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IDashboardApi"/> ke API sungguhan: satu endpoint ringan per widget (<c>/api/v1/dashboard/{widgetId}</c>).</summary>
public sealed class HttpDashboardApi(GmsApiClient client) : IDashboardApi
{
    private Task<T> Get<T>(string id, CancellationToken ct) => client.GetAsync<T>($"/api/v1/dashboard/{id}", ct);

    public Task<PoLateWidget> GetPoLateAsync(CancellationToken ct = default) => Get<PoLateWidget>(DashboardWidgetIds.PoLate, ct);

    public Task<DueSoonWidget> GetDueSoonAsync(CancellationToken ct = default) => Get<DueSoonWidget>(DashboardWidgetIds.DueSoon, ct);

    public Task<PoNoCustomerNumberWidget> GetPoNoCustomerNumberAsync(CancellationToken ct = default) => Get<PoNoCustomerNumberWidget>(DashboardWidgetIds.PoNoCustomerNumber, ct);

    public Task<UnplannedDemandWidget> GetUnplannedDemandAsync(CancellationToken ct = default) => Get<UnplannedDemandWidget>(DashboardWidgetIds.UnplannedDemand, ct);

    public Task<WoRunningWidget> GetWoRunningAsync(CancellationToken ct = default) => Get<WoRunningWidget>(DashboardWidgetIds.WoRunning, ct);

    public Task<WoLateWidget> GetWoLateAsync(CancellationToken ct = default) => Get<WoLateWidget>(DashboardWidgetIds.WoLate, ct);

    public Task<OutputTodayWidget> GetOutputTodayAsync(CancellationToken ct = default) => Get<OutputTodayWidget>(DashboardWidgetIds.OutputToday, ct);

    public Task<VendorBalanceWidget> GetVendorBalanceAsync(CancellationToken ct = default) => Get<VendorBalanceWidget>(DashboardWidgetIds.VendorBalance, ct);

    public Task<UnhandedOverWidget> GetUnhandedOverAsync(CancellationToken ct = default) => Get<UnhandedOverWidget>(DashboardWidgetIds.UnhandedOver, ct);

    public Task<StockSummaryWidget> GetStockSummaryAsync(CancellationToken ct = default) => Get<StockSummaryWidget>(DashboardWidgetIds.StockSummary, ct);

    public Task<PayrollPeriodWidget> GetPayrollPeriodAsync(CancellationToken ct = default) => Get<PayrollPeriodWidget>(DashboardWidgetIds.PayrollPeriod, ct);

    public Task<SecurityWidget> GetSecurityAsync(CancellationToken ct = default) => Get<SecurityWidget>(DashboardWidgetIds.Security, ct);
}

/// <summary>Widget tiruan (ADR-014) yang diturunkan dari data modul tiruan lain agar angka konsisten dengan daftar/filter di modul tujuan.</summary>
public sealed class MockDashboardApi(
    MockPoApi pos, MockWoApi wos, MockProductionApi production, MockInventoryApi inventory, MockPayrollApi payroll, MockAccessStore access) : IDashboardApi
{
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    private async Task<List<PoDto>> ActivePosAsync(PoListQuery q)
    {
        var list = new List<PoDto>();
        foreach (var item in (await pos.ListAsync(q with { PageSize = 200 })).Items) { list.Add(await pos.GetAsync(item.Id)); }
        return list.Where(p => p.Status is PoStatus.Confirmed or PoStatus.InProgress).ToList();
    }

    public async Task<PoLateWidget> GetPoLateAsync(CancellationToken ct = default)
    {
        var today = Today();
        var late = await ActivePosAsync(new PoListQuery { Terlambat = true });
        var items = late.Select(p =>
        {
            var lateLines = p.Lines.Where(l => l.IsLate).ToList();
            return new PoLateItem(p.Id, p.Code, p.CustomerName, today.DayNumber - lateLines.Min(l => l.DeliveryDate).DayNumber, lateLines.Sum(l => l.Qty - l.DeliveredQty));
        }).OrderByDescending(i => i.DaysLate).ToList();
        return new PoLateWidget(items.Count, items.Sum(i => i.ShortageQty), items.Take(5).ToList());
    }

    public async Task<DueSoonWidget> GetDueSoonAsync(CancellationToken ct = default)
    {
        var today = Today();
        var due = await ActivePosAsync(new PoListQuery { JatuhTempo7 = true });
        return new DueSoonWidget(due.Count, due.Sum(p => p.Lines.Where(l => l.DeliveryDate >= today && l.DeliveryDate <= today.AddDays(7)).Sum(l => l.Qty - l.DeliveredQty)));
    }

    public async Task<PoNoCustomerNumberWidget> GetPoNoCustomerNumberAsync(CancellationToken ct = default) =>
        new((await ActivePosAsync(new PoListQuery { PoCustomerKosong = true })).Count);

    public async Task<UnplannedDemandWidget> GetUnplannedDemandAsync(CancellationToken ct = default)
    {
        var open = await ActivePosAsync(new PoListQuery { SisaRencana = true });
        return new UnplannedDemandWidget(open.Count, open.Sum(p => p.RemainingPlanQty));
    }

    public async Task<WoRunningWidget> GetWoRunningAsync(CancellationToken ct = default)
    {
        var all = (await wos.ListAsync(new WoListQuery { PageSize = 200 }, ct)).Items;
        var running = all.Where(w => w.Status is WoStatus.Released or WoStatus.InProgress).OrderBy(w => w.TargetDate ?? DateOnly.MaxValue).Take(5)
            .Select(w => new WoProgressItem(w.Id, w.Code, w.FgCode, w.ProgressPercent, w.TargetDate)).ToList();
        return new WoRunningWidget(all.Count(w => w.Status == WoStatus.Released), all.Count(w => w.Status == WoStatus.InProgress), all.Count(w => w.Status == WoStatus.Completed), running);
    }

    public async Task<WoLateWidget> GetWoLateAsync(CancellationToken ct = default) => new((await wos.ListAsync(new WoListQuery { Terlambat = true }, ct)).TotalCount);

    public async Task<OutputTodayWidget> GetOutputTodayAsync(CancellationToken ct = default)
    {
        var rows = (await production.ListEntriesAsync(new EntryListQuery { Kind = EntryKind.Output, HariIni = true, PageSize = 500 }, ct)).Items;
        return new OutputTodayWidget(rows.Sum(r => r.TotalQty), rows.GroupBy(r => r.ProcessName).Select(g => new ProcessQty(g.Key, g.Sum(r => r.TotalQty))).OrderByDescending(p => p.Qty).ToList());
    }

    public async Task<VendorBalanceWidget> GetVendorBalanceAsync(CancellationToken ct = default)
    {
        var rows = (await production.ListEntriesAsync(new EntryListQuery { PageSize = 500 }, ct)).Items.Where(r => r.Kind is EntryKind.VendorSend or EntryKind.VendorReceive).ToList();
        var byVendor = rows.GroupBy(r => r.VendorName ?? "—").Select(g => new VendorQty(g.Key, g.Sum(r => r.Kind == EntryKind.VendorSend ? r.TotalQty : -r.TotalQty))).Where(v => v.Qty > 0).OrderByDescending(v => v.Qty).ToList();
        return new VendorBalanceWidget(byVendor.Sum(v => v.Qty), byVendor.Take(3).ToList());
    }

    public async Task<UnhandedOverWidget> GetUnhandedOverAsync(CancellationToken ct = default)
    {
        var items = new List<UnhandedItem>();
        foreach (var w in (await wos.ListAsync(new WoListQuery { PageSize = 200 }, ct)).Items.Where(w => w.Status is WoStatus.InProgress or WoStatus.Completed))
        {
            var dto = await wos.GetAsync(w.Id, ct);
            if (dto.UnhandedQty > 0) { items.Add(new UnhandedItem(w.Id, w.Code, dto.UnhandedQty)); }
        }

        return new UnhandedOverWidget(items.Sum(i => i.Qty), items.OrderByDescending(i => i.Qty).Take(5).ToList());
    }

    public async Task<StockSummaryWidget> GetStockSummaryAsync(CancellationToken ct = default)
    {
        var stock = await inventory.ListStockAsync(new StockQuery { PageSize = 500 }, ct);
        var top = stock.Rows.Items.GroupBy(r => r.FgCode).Select(g => new FgAvailable(g.Key, g.Sum(r => r.Available))).OrderByDescending(f => f.Available).Take(5).ToList();
        return new StockSummaryWidget(stock.OnHandTotal, stock.AllocatedTotal, stock.AvailableTotal, top);
    }

    public async Task<PayrollPeriodWidget> GetPayrollPeriodAsync(CancellationToken ct = default)
    {
        var defaults = await payroll.GetNextPeriodDefaultsAsync(ct);
        var open = (await payroll.ListPeriodsAsync(ct)).FirstOrDefault(p => p.Status == PayrollStatus.Open);
        return open is null
            ? new PayrollPeriodWidget(false, null, null, null, null, 0, 0, 0, defaults.StartDate)
            : new PayrollPeriodWidget(true, open.Id, open.Code, open.StartDate, open.EndDate, open.WorkerCount, open.TotalPaid, Math.Max(0, open.EndDate.DayNumber - Today().DayNumber), null);
    }

    public async Task<SecurityWidget> GetSecurityAsync(CancellationToken ct = default)
    {
        var users = access.ListUsers(new UserListQueryProxy().Query);
        var audit = access.ListAudit(new Gms.Shared.Access.AuditLogQuery { From = Today().AddDays(-1), To = Today(), EventType = "LoginFailed", PageSize = 1 });
        return await Task.FromResult(new SecurityWidget(users.TotalCount, audit.TotalCount));
    }

    private sealed class UserListQueryProxy
    {
        public Gms.Shared.Access.UserListQuery Query { get; } = new() { Status = UserStatus.Locked, PageSize = 1 };
    }
}
