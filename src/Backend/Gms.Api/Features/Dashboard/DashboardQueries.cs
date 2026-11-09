using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Dashboard;
using Gms.Shared.Enums;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Dashboard;

/// <summary>
/// Read model Beranda (FSD Beranda): hanya membaca, agregat di server, daftar dibatasi 5 baris. Setiap widget dimuat independen dan
/// izin dicek per widget (data widget yang tidak berhak tidak pernah dikirim, AC-BR-02). Definisi "hari ini" dan "7 hari" mengikuti zona
/// waktu aplikasi (AC-BR-18). PO dihitung pada status Confirmed/In Progress agar angka sama dengan daftar PO yang dituju saat diklik.
/// </summary>
public sealed class DashboardQueries(GmsDbContext db, ICurrentUser user, AppTimeZone zone, TimeProvider time)
{
    private const int TopN = 5;
    private static readonly PoStatus[] ActivePo = [PoStatus.Confirmed, PoStatus.InProgress];

    private DateOnly Today => zone.Today(time);

    /// <summary>Izin per widget (ApiConventions.md 4.9).</summary>
    public static string? PermissionFor(string widgetId) => widgetId switch
    {
        DashboardWidgetIds.PoLate or DashboardWidgetIds.DueSoon or DashboardWidgetIds.PoNoCustomerNumber or DashboardWidgetIds.UnplannedDemand
            => Permissions.CustomerPO.View,
        DashboardWidgetIds.WoRunning or DashboardWidgetIds.WoLate => Permissions.WorkOrder.View,
        DashboardWidgetIds.OutputToday => Permissions.ProductionOutput.View,
        DashboardWidgetIds.VendorBalance => Permissions.VendorJob.View,
        DashboardWidgetIds.UnhandedOver => Permissions.SerahTerimaFG.View,
        DashboardWidgetIds.StockSummary => Permissions.StokFG.View,
        DashboardWidgetIds.PayrollPeriod => Permissions.Payroll.View,
        DashboardWidgetIds.Security => Permissions.AccessManagement.AuditLog.View,
        _ => null,
    };

    public async Task<object> GetAsync(string widgetId, CancellationToken ct)
    {
        var permission = PermissionFor(widgetId) ?? throw ProblemException.NotFound("Widget");
        if (!user.HasPermission(permission))
        {
            throw ProblemException.Forbidden();
        }

        return widgetId switch
        {
            DashboardWidgetIds.PoLate => await PoLateAsync(ct),
            DashboardWidgetIds.DueSoon => await DueSoonAsync(ct),
            DashboardWidgetIds.PoNoCustomerNumber => await PoNoCustomerNumberAsync(ct),
            DashboardWidgetIds.UnplannedDemand => await UnplannedDemandAsync(ct),
            DashboardWidgetIds.WoRunning => await WoRunningAsync(ct),
            DashboardWidgetIds.WoLate => await WoLateAsync(ct),
            DashboardWidgetIds.OutputToday => await OutputTodayAsync(ct),
            DashboardWidgetIds.VendorBalance => await VendorBalanceAsync(ct),
            DashboardWidgetIds.UnhandedOver => await UnhandedOverAsync(ct),
            DashboardWidgetIds.StockSummary => await StockSummaryAsync(ct),
            DashboardWidgetIds.PayrollPeriod => await PayrollPeriodAsync(ct),
            _ => await SecurityAsync(ct),
        };
    }

    // ===== Penjualan =====

    private sealed class LateLine
    {
        public Guid PoId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateOnly DeliveryDate { get; set; }
        public int Shortage { get; set; }
    }

    public async Task<PoLateWidget> PoLateAsync(CancellationToken ct)
    {
        var today = Today;
        var lines = await (
            from l in db.PoLines.AsNoTracking()
            where l.DeliveryDate < today && l.DeliveredQty < l.Qty
            join p in db.PurchaseOrders.AsNoTracking() on l.PoId equals p.Id
            where ActivePo.Contains(p.Status)
            select new LateLine
            {
                PoId = p.Id, Code = p.Code, DeliveryDate = l.DeliveryDate, Shortage = l.Qty - l.DeliveredQty,
                CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            }).ToListAsync(ct);

        var grouped = lines.GroupBy(l => (l.PoId, l.Code, l.CustomerName))
            .Select(g => new PoLateItem(g.Key.PoId, g.Key.Code, g.Key.CustomerName, today.DayNumber - g.Min(l => l.DeliveryDate).DayNumber, g.Sum(l => l.Shortage)))
            .OrderByDescending(i => i.DaysLate).ThenBy(i => i.Code, StringComparer.Ordinal).ToList();
        return new PoLateWidget(grouped.Count, grouped.Sum(i => i.ShortageQty), grouped.Take(TopN).ToList());
    }

    public async Task<DueSoonWidget> DueSoonAsync(CancellationToken ct)
    {
        var today = Today;
        var until = today.AddDays(7);
        var rows = await (
            from l in db.PoLines.AsNoTracking()
            where l.DeliveryDate >= today && l.DeliveryDate <= until && l.DeliveredQty < l.Qty
            join p in db.PurchaseOrders.AsNoTracking() on l.PoId equals p.Id
            where ActivePo.Contains(p.Status)
            select new { l.PoId, Open = l.Qty - l.DeliveredQty }).ToListAsync(ct);
        return new DueSoonWidget(rows.Select(r => r.PoId).Distinct().Count(), rows.Sum(r => r.Open));
    }

    public async Task<PoNoCustomerNumberWidget> PoNoCustomerNumberAsync(CancellationToken ct) =>
        new(await db.PurchaseOrders.AsNoTracking().CountAsync(
            p => p.CustomerPoNumber == null && (p.Status == PoStatus.Draft || p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress), ct));

    public async Task<UnplannedDemandWidget> UnplannedDemandAsync(CancellationToken ct)
    {
        var rows = await (
            from l in db.PoLines.AsNoTracking()
            join p in db.PurchaseOrders.AsNoTracking() on l.PoId equals p.Id
            where ActivePo.Contains(p.Status) && l.Qty - l.DeliveredQty - l.AllocatedQty - l.InProductionQty > 0
            select new { l.PoId, Remaining = l.Qty - l.DeliveredQty - l.AllocatedQty - l.InProductionQty }).ToListAsync(ct);
        return new UnplannedDemandWidget(rows.Select(r => r.PoId).Distinct().Count(), rows.Sum(r => r.Remaining));
    }

    // ===== Produksi =====

    private static readonly WoStatus[] RunningStatuses = [WoStatus.Released, WoStatus.InProgress];

    private sealed class WoRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string FgCode { get; set; } = string.Empty;
        public DateOnly? TargetDate { get; set; }
        public int Total { get; set; }
        public int LastOutput { get; set; }
    }

    public async Task<WoRunningWidget> WoRunningAsync(CancellationToken ct)
    {
        var counts = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WoStatus.Released || w.Status == WoStatus.InProgress || w.Status == WoStatus.Completed)
            .GroupBy(w => w.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var rows = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status == WoStatus.Released || w.Status == WoStatus.InProgress)
            .OrderBy(w => w.TargetDate == null).ThenBy(w => w.TargetDate).ThenBy(w => w.Code)
            .Take(TopN)
            .Select(w => new WoRow
            {
                Id = w.Id, Code = w.Code, FgCode = w.FgCode, TargetDate = w.TargetDate,
                Total = db.WoLines.Where(l => l.WoId == w.Id).Sum(l => (int?)l.Qty) ?? 0,
                LastOutput = db.WoStepBalances
                    .Where(b => db.WoLines.Any(l => l.Id == b.WoLineId && l.WoId == w.Id)
                                && b.Seq == db.WoRoutingSnapshots.Where(r => r.WoId == w.Id).Max(r => (int?)r.Seq))
                    .Sum(b => (int?)b.Qty) ?? 0,
            }).ToListAsync(ct);

        return new WoRunningWidget(
            counts.GetValueOrDefault(WoStatus.Released), counts.GetValueOrDefault(WoStatus.InProgress), counts.GetValueOrDefault(WoStatus.Completed),
            rows.Select(r => new WoProgressItem(r.Id, r.Code, r.FgCode, r.Total <= 0 ? 0 : Math.Round(r.LastOutput * 100m / r.Total, 1), r.TargetDate)).ToList());
    }

    public async Task<WoLateWidget> WoLateAsync(CancellationToken ct)
    {
        var today = Today;
        return new WoLateWidget(await db.WorkOrders.AsNoTracking().CountAsync(
            w => w.TargetDate < today && (w.Status == WoStatus.Draft || w.Status == WoStatus.Released || w.Status == WoStatus.InProgress), ct));
    }

    public async Task<OutputTodayWidget> OutputTodayAsync(CancellationToken ct)
    {
        var today = Today;
        var rows = await (
            from l in db.OutputEntryLines.AsNoTracking()
            join e in db.OutputEntries.AsNoTracking() on l.EntryId equals e.Id
            where e.WorkDate == today && e.Status == DocStatus.Active
            join r in db.WoRoutingSnapshots.AsNoTracking() on new { e.WoId, e.Seq } equals new { r.WoId, r.Seq }
            select new { r.ProcessName, l.Qty }).ToListAsync(ct);
        var byProcess = rows.GroupBy(r => r.ProcessName).Select(g => new ProcessQty(g.Key, g.Sum(x => x.Qty)))
            .OrderByDescending(p => p.Qty).ThenBy(p => p.ProcessName, StringComparer.Ordinal).ToList();
        return new OutputTodayWidget(byProcess.Sum(p => p.Qty), byProcess);
    }

    public async Task<VendorBalanceWidget> VendorBalanceAsync(CancellationToken ct)
    {
        var rows = await (
            from l in db.VendorMovementLines.AsNoTracking()
            join m in db.VendorMovements.AsNoTracking() on l.MovementId equals m.Id
            where m.Status == DocStatus.Active
            join w in db.WorkOrders.AsNoTracking() on m.WoId equals w.Id
            where w.Status != WoStatus.Closed && w.Status != WoStatus.Cancelled
            join v in db.Vendors.AsNoTracking() on m.VendorId equals v.Id
            select new { v.Name, m.Kind, l.Qty }).ToListAsync(ct);
        var byVendor = rows.GroupBy(r => r.Name)
            .Select(g => new VendorQty(g.Key, g.Where(x => x.Kind == VendorMovementKind.Send).Sum(x => x.Qty) - g.Where(x => x.Kind == VendorMovementKind.Receive).Sum(x => x.Qty)))
            .Where(v => v.Qty > 0).OrderByDescending(v => v.Qty).ThenBy(v => v.VendorName, StringComparer.Ordinal).ToList();
        return new VendorBalanceWidget(byVendor.Sum(v => v.Qty), byVendor.Take(TopN).ToList());
    }

    // ===== Gudang =====

    private sealed class UnhandedRow
    {
        public Guid WoId { get; set; }
        public string WoCode { get; set; } = string.Empty;
        public int Qty { get; set; }
    }

    public async Task<UnhandedOverWidget> UnhandedOverAsync(CancellationToken ct)
    {
        var rows = await db.WoLines.AsNoTracking()
            .Join(db.WorkOrders.AsNoTracking().Where(w => w.Status != WoStatus.Closed && w.Status != WoStatus.Cancelled && w.Status != WoStatus.Draft),
                l => l.WoId, w => w.Id, (l, w) => new { Line = l, Wo = w })
            .Select(x => new UnhandedRow
            {
                WoId = x.Wo.Id, WoCode = x.Wo.Code,
                Qty = (db.WoStepBalances
                           .Where(b => b.WoLineId == x.Line.Id && b.Seq == db.WoRoutingSnapshots.Where(r => r.WoId == x.Wo.Id).Max(r => (int?)r.Seq))
                           .Select(b => b.Qty).FirstOrDefault()) - x.Line.HandedOverQty,
            }).ToListAsync(ct);

        var byWo = rows.Where(r => r.Qty > 0).GroupBy(r => (r.WoId, r.WoCode))
            .Select(g => new UnhandedItem(g.Key.WoId, g.Key.WoCode, g.Sum(x => x.Qty)))
            .OrderByDescending(i => i.Qty).ThenBy(i => i.WoCode, StringComparer.Ordinal).ToList();
        return new UnhandedOverWidget(byWo.Sum(i => i.Qty), byWo.Take(TopN).ToList());
    }

    public async Task<StockSummaryWidget> StockSummaryAsync(CancellationToken ct)
    {
        var onHand = await db.StockBalances.AsNoTracking().SumAsync(s => (int?)s.OnHand, ct) ?? 0;
        var allocated = await db.StockBalances.AsNoTracking().SumAsync(s => (int?)s.Allocated, ct) ?? 0;
        var perFg = await (
            from s in db.StockBalances.AsNoTracking()
            join f in db.Fgs.AsNoTracking() on s.FgId equals f.Id
            select new { f.Code, Available = s.OnHand - s.Allocated }).ToListAsync(ct);
        var top = perFg.GroupBy(x => x.Code).Select(g => new FgAvailable(g.Key, g.Sum(x => x.Available)))
            .Where(f => f.Available > 0).OrderByDescending(f => f.Available).ThenBy(f => f.FgCode, StringComparer.Ordinal).Take(TopN).ToList();
        return new StockSummaryWidget(onHand, allocated, onHand - allocated, top);
    }

    // ===== Penggajian & Administrasi =====

    public async Task<PayrollPeriodWidget> PayrollPeriodAsync(CancellationToken ct)
    {
        var today = Today;
        var open = await db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Status == PayrollStatus.Open, ct);
        if (open is null)
        {
            var last = await db.PayrollPeriods.AsNoTracking().OrderByDescending(p => p.EndDate).FirstOrDefaultAsync(ct);
            var suggested = last is not null
                ? last.EndDate.AddDays(1)
                : await db.OutputEntries.AsNoTracking().Where(e => e.Status == DocStatus.Active).MinAsync(e => (DateOnly?)e.WorkDate, ct);
            return new PayrollPeriodWidget(false, null, null, null, null, 0, 0m, 0, suggested);
        }

        var lines = await PayrollTotalsAsync(open.StartDate, open.EndDate, ct);
        return new PayrollPeriodWidget(true, open.Id, open.Code, open.StartDate, open.EndDate, lines.Count, lines.Sum(t => t.Paid),
            open.EndDate.DayNumber - today.DayNumber, null);
    }

    private async Task<List<Payroll.WorkerTotal>> PayrollTotalsAsync(DateOnly start, DateOnly end, CancellationToken ct) =>
        Payroll.PayrollCalculator.Totals(await new Payroll.PayrollCalculator(db).LoadLiveAsync(start, end, ct));

    public async Task<SecurityWidget> SecurityAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var since = now.AddHours(-24);
        var locked = await db.Users.AsNoTracking().CountAsync(u => u.IsActive && u.LockoutEnd != null && u.LockoutEnd > now, ct);
        var failed = await db.AuditLogs.AsNoTracking().CountAsync(a => a.EventType == "LoginFailed" && a.OccurredAt >= since, ct);
        return new SecurityWidget(locked, failed);
    }
}
