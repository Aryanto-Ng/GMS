using Gms.Domain.Payroll;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>WO Released siap dipakai uji Production Output. Routing: CUT (Internal) → EMB (Vendor) → SEW (Internal).</summary>
public sealed record ProdSetup(PoWorld World, PoDto Po, WoDto Wo, int Cut, int Emb, int Sew, Guid LineS, Guid LineM);

public static class ProdHelpers
{
    private const string Base = "/api/v1/production";

    public static async Task<ProdSetup> NewProdSetupAsync(this TestHost h, int poQty = 100, int woQtySmall = 60, int woQtyMedium = 40)
    {
        var (w, po, wo) = await h.NewReleasedWoAsync(poQty, woQtySmall, woQtyMedium);
        var progress = (await h.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/progress", h.AdminToken)).Ok().As<WoProgressDto>();
        var s = wo.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id;
        var m = wo.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).Id;
        return new ProdSetup(w, po, wo, progress.Steps[0].Seq, progress.Steps[1].Seq, progress.Steps[2].Seq, s, m);
    }

    public static async Task<VendorDto> NewVendorAsync(this TestHost h, string? name = null) =>
        (await h.Api.PostAsync("/api/v1/master/vendors", new { name = name ?? "Vendor " + MasterHelpers.Code("") }, h.AdminToken)).Ok("vendor").As<VendorDto>();

    public static async Task DeactivateWorkerAsync(this TestHost h, Guid workerId)
    {
        await using var db = h.CreateDbContext();
        await db.Workers.Where(x => x.Id == workerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
    }

    public static async Task DeactivateVendorAsync(this TestHost h, Guid vendorId)
    {
        await using var db = h.CreateDbContext();
        await db.Vendors.Where(x => x.Id == vendorId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
    }

    public static async Task InsertPeriodAsync(this TestHost h, DateOnly start, DateOnly end, PayrollStatus status = PayrollStatus.Closed)
    {
        await using var db = h.CreateDbContext();
        db.PayrollPeriods.Add(new PayrollPeriod
        {
            Code = MasterHelpers.Code("PRD-"), StartDate = start, EndDate = end, Status = status,
            ClosedAt = status == PayrollStatus.Closed ? DateTimeOffset.UtcNow : null, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public static IEnumerable<WoLineQty> Cells(params (Guid Line, int Qty)[] cells) => cells.Select(c => new WoLineQty(c.Line, c.Qty));

    public static OutputRow Row(Guid worker, params (Guid Line, int Qty)[] cells) => new(worker, Cells(cells).ToList());

    public static Task<ApiResponse> PostOutputsAsync(this TestHost h, ProdSetup s, int seq, IEnumerable<OutputRow> rows,
        DateOnly? date = null, bool confirm = false, string? token = null) =>
        h.Api.PostAsync($"{Base}/outputs", new SaveOutputsRequest(s.Wo.Id, seq, date ?? Today(), rows.ToList(), confirm), token ?? h.AdminToken);

    /// <summary>Simpan output satu pekerja dan pastikan sukses; mengembalikan id entri.</summary>
    public static async Task<Guid> OutAsync(this TestHost h, ProdSetup s, int seq, Guid worker, params (Guid Line, int Qty)[] cells)
    {
        var result = (await h.PostOutputsAsync(s, seq, [Row(worker, cells)], confirm: true)).Ok($"output langkah {seq}").As<SaveOutputsResult>();
        return result.EntryIds.Single();
    }

    public static Task<ApiResponse> PostVendorAsync(this TestHost h, ProdSetup s, VendorMovementKind kind, Guid vendor,
        IEnumerable<(Guid Line, int Qty)> cells, DateOnly? date = null, string? docNo = null, string? token = null) =>
        h.Api.PostAsync($"{Base}/vendor-movements",
            new SaveVendorMovementRequest(s.Wo.Id, s.Emb, vendor, kind, date ?? Today(), docNo, null, Cells(cells.ToArray()).ToList()), token ?? h.AdminToken);

    public static async Task<Guid> SendAsync(this TestHost h, ProdSetup s, Guid vendor, params (Guid Line, int Qty)[] cells) =>
        Id((await h.PostVendorAsync(s, VendorMovementKind.Send, vendor, cells)).Ok("kirim vendor"));

    public static async Task<Guid> ReceiveAsync(this TestHost h, ProdSetup s, Guid vendor, params (Guid Line, int Qty)[] cells) =>
        Id((await h.PostVendorAsync(s, VendorMovementKind.Receive, vendor, cells)).Ok("terima vendor"));

    private static Guid Id(ApiResponse response) => System.Text.Json.JsonDocument.Parse(response.Body).RootElement.GetProperty("id").GetGuid();

    public static Task<ApiResponse> VoidAsync(this TestHost h, Guid id, string? reason = "salah input", bool vendor = false, string? token = null) =>
        h.Api.PostAsync($"{Base}/{(vendor ? "vendor-movements" : "entries")}/{id}/void", new Gms.Shared.Common.ReasonRequest(reason ?? "", 0), token ?? h.AdminToken);

    public static async Task<ImpactPreview> PreviewVoidAsync(this TestHost h, Guid id, bool vendor = false) =>
        (await h.Api.PostAsync($"{Base}/{(vendor ? "vendor-movements" : "entries")}/{id}/void/preview", new { }, h.AdminToken)).Ok("preview void").As<ImpactPreview>();

    public static async Task<EntryDetailDto> GetEntryAsync(this TestHost h, Guid id) =>
        (await h.Api.GetAsync($"{Base}/entries/{id}", h.AdminToken)).Ok().As<EntryDetailDto>();

    public static async Task<Gms.Shared.Common.PagedResult<EntryListItem>> ListEntriesAsync(this TestHost h, string query = "", string? token = null) =>
        (await h.Api.GetAsync($"{Base}/entries?pageSize=100{query}", token ?? h.AdminToken)).Ok().As<Gms.Shared.Common.PagedResult<EntryListItem>>();

    public static async Task<(int Qty, int Sent)> BalanceAsync(this TestHost h, Guid woLineId, int seq)
    {
        await using var db = h.CreateDbContext();
        var b = await db.WoStepBalances.AsNoTracking().SingleOrDefaultAsync(x => x.WoLineId == woLineId && x.Seq == seq);
        return (b?.Qty ?? 0, b?.SentQty ?? 0);
    }

    /// <summary>
    /// Invarian ledger (FSD 2.2, ADR-004): saldo proyeksi = jumlah entri aktif, dan rantai
    /// Output(n−1) ≥ Terkirim(n) ≥ Diterima(n) ≥ Kum(n+1) berlaku untuk setiap baris WO. Dipanggil setelah tes konkurensi.
    /// </summary>
    public static async Task AssertLedgerInvariantAsync(this TestHost h, ProdSetup s)
    {
        await using var db = h.CreateDbContext();
        var lines = await db.WoLines.AsNoTracking().Where(l => l.WoId == s.Wo.Id).ToListAsync();
        foreach (var line in lines)
        {
            int Output(int seq) => db.OutputEntryLines.Where(l => l.WoLineId == line.Id && db.OutputEntries.Any(e => e.Id == l.EntryId && e.Seq == seq && e.Status == DocStatus.Active)).Sum(l => (int?)l.Qty) ?? 0;
            int Vendor(VendorMovementKind kind) => db.VendorMovementLines.Where(l => l.WoLineId == line.Id && db.VendorMovements.Any(m => m.Id == l.MovementId && m.Status == DocStatus.Active && m.Kind == kind)).Sum(l => (int?)l.Qty) ?? 0;

            var cut = Output(s.Cut);
            var sent = Vendor(VendorMovementKind.Send);
            var received = Vendor(VendorMovementKind.Receive);
            var sew = Output(s.Sew);

            Assert.Equal((cut, 0), await h.BalanceAsync(line.Id, s.Cut));
            Assert.Equal((received, sent), await h.BalanceAsync(line.Id, s.Emb));
            Assert.Equal((sew, 0), await h.BalanceAsync(line.Id, s.Sew));
            Assert.True(sent <= cut && received <= sent && sew <= received, $"rantai terlanggar: cut {cut}, sent {sent}, received {received}, sew {sew}");
        }
    }
}
