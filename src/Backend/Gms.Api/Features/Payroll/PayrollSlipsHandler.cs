using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Documents;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Payroll;

/// <summary>
/// Slip gaji PDF (AC-PY-28..32): satu pekerja atau semua (urut Kode Pekerja, tiap pekerja di halaman baru). Periode Closed bersumber dari
/// snapshot dan identik dengan rekap; periode Open diberi watermark BELUM FINAL. Setiap pencetakan tercatat di audit.
/// </summary>
public sealed class PayrollSlipsHandler(
    GmsDbContext db, PayrollQueries queries, ICurrentUser user, IAuditWriter audit, TimeProvider time, AppTimeZone zone, IOptions<AppOptions> options)
{
    public async Task<FileContent> HandleAsync(Guid periodId, Guid? workerId, bool includeDaily, CancellationToken ct)
    {
        var (period, lines) = await queries.LoadAsync(periodId, ct);
        var ids = lines.Select(l => l.WorkerId).Distinct().ToList();
        if (workerId is { } single)
        {
            if (!ids.Contains(single))
            {
                throw ProblemException.NotFound("Pekerja pada periode ini");
            }

            ids = [single];
        }

        var workers = ids
            .Select(id => PayrollCalculator.WorkerDetail(lines, id))
            .OrderBy(d => d.WorkerCode, StringComparer.Ordinal)
            .Select(d => new SlipWorker(d.WorkerCode, d.WorkerName, d.TotalQty, d.SumBeforeRounding, d.Paid, d.ByWo, d.ByDay, d.Adjustments))
            .ToList();

        var printedBy = await db.Users.AsNoTracking().Where(u => u.Id == user.Id).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? user.UserName ?? "-";
        var provisional = period.Status == PayrollStatus.Open;
        var model = new SlipModel(
            period.Code, period.StartDate, period.EndDate, provisional, options.Value.Konveksi.Name, options.Value.Konveksi.Address,
            printedBy, zone.Now(time), includeDaily, workers);
        var bytes = PayrollSlipPdfBuilder.Build(model, options.Value.Pdf.FontDirectory);

        audit.Add(AuditEvents.PayrollSlipPrinted, "PayrollPeriod", periodId.ToString(), null,
            new { period.Code, worker = workerId?.ToString() ?? "semua", workers = workers.Count, includeDaily, provisional });
        await db.SaveChangesAsync(ct);

        var name = $"slip-{period.Code}{(workerId is null ? "-semua" : "-" + workers[0].WorkerCode)}{(provisional ? "-BELUM-FINAL" : string.Empty)}.pdf";
        return new FileContent(name, "application/pdf", bytes);
    }
}
