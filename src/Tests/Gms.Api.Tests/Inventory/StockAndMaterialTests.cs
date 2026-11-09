using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Inventory;

/// <summary>Stok FG, kartu stok, penyesuaian, impor saldo awal, dan serah terima material (FSD Serah Terima & Stok FG 4.3–4.6).</summary>
[Collection(ApiCollection.Name)]
public sealed class StockAndMaterialTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private static string LedgerUrl(PoWorld w, SizeDto size, string extra = "") =>
        $"/api/v1/stock/ledger?fgId={w.Fg.Fg.Id}&colorId={w.Color.Id}&sizeId={size.Id}{extra}";

    private async Task<StockListResult> ListAsync(string query, string? token = null) =>
        (await H.Api.GetAsync($"/api/v1/stock?pageSize=50{query}", token ?? H.AdminToken)).Ok().As<StockListResult>();

    private async Task AdjustOkAsync(PoWorld w, SizeDto size, int qty, string reason = "stok awal", DateOnly? date = null) =>
        (await H.Api.PostAsync("/api/v1/stock/adjustments",
            new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, size.Id, qty, date ?? Today(), reason), H.AdminToken)).Ok("penyesuaian");

    // ===== Daftar stok =====

    [Fact]
    public async Task Daftar_stok_menampilkan_fisik_teralokasi_tersedia_total_dan_filter()
    {
        var w = await H.NewPoWorldAsync();
        await AdjustOkAsync(w, w.Small, 50);
        await AdjustOkAsync(w, w.Medium, 20);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        (await H.Api.PostAsync($"/api/v1/po-lines/{po.PoLineId(w.Small)}/allocate", new AllocateRequest(30, null), H.AdminToken)).Ok();

        var all = await ListAsync($"&fgId={w.Fg.Fg.Id}");
        Assert.Equal((70, 30, 40), (all.OnHandTotal, all.AllocatedTotal, all.AvailableTotal));
        Assert.Equal(2, all.Rows.TotalCount);
        var small = all.Rows.Items.Single(r => r.ColorSize.SizeId == w.Small.Id);
        Assert.Equal((50, 30, 20, w.Fg.Fg.Code), (small.OnHand, small.Allocated, small.Available, small.FgCode));
        Assert.Equal([w.Small.Id, w.Medium.Id], all.Rows.Items.Select(r => r.ColorSize.SizeId)); // urut Size

        Assert.Equal(1, (await ListAsync($"&fgId={w.Fg.Fg.Id}&sizeId={w.Medium.Id}")).Rows.TotalCount);
        Assert.Equal(2, (await ListAsync($"&colorId={w.Color.Id}&fgId={w.Fg.Fg.Id}")).Rows.TotalCount);
        Assert.Equal(2, (await ListAsync($"&q={w.Fg.Fg.Code}")).Rows.TotalCount);
        Assert.Equal(0, (await ListAsync("&q=tidak-ada-fg-ini")).Rows.TotalCount);
        Assert.Equal(2, (await ListAsync($"&customerId={w.Customer.Id}")).Rows.TotalCount);

        await AdjustOkAsync(w, w.Medium, -20, "habis");
        Assert.Equal(1, (await ListAsync($"&fgId={w.Fg.Fg.Id}")).Rows.TotalCount); // hanyaAdaStok bawaan true
        Assert.Equal(2, (await ListAsync($"&fgId={w.Fg.Fg.Id}&hanyaAdaStok=false")).Rows.TotalCount);

        var page = (await H.Api.GetAsync($"/api/v1/stock?fgId={w.Fg.Fg.Id}&hanyaAdaStok=false&pageSize=1&page=2", H.AdminToken)).Ok().As<StockListResult>();
        Assert.Equal((2, 1), (page.Rows.TotalCount, page.Rows.Items.Count));
    }

    [Fact]
    public async Task Stok_dan_kartu_stok_dilindungi_permission_lihat()
    {
        var w = await H.NewPoWorldAsync();
        await AdjustOkAsync(w, w.Small, 5);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.StokFG.View)).Id);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/stock", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync(LedgerUrl(w, w.Small), none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.Api.GetAsync(LedgerUrl(w, w.Small), viewer.Token)).IsSuccess);
        ProblemAssert.Is(await H.Api.PostAsync("/api/v1/stock/adjustments", new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 1, Today(), "x"), viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    // ===== Kartu stok =====

    [Fact]
    public async Task Kartu_stok_menampilkan_saldo_berjalan_sumber_alasan_pembuat_dan_paging_serta_filter_tanggal()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        var w = s.World;
        await AdjustOkAsync(w, w.Small, 10, "saldo awal", Today(-5));
        await H.ProduceAsync(s, 30);
        var handover = await H.HandoverAsync(s, (s.LineS, 30));
        await AdjustOkAsync(w, w.Small, -4, "rusak gudang", Today(-1));

        var ledger = (await H.Api.GetAsync(LedgerUrl(w, w.Small), H.AdminToken)).Ok().As<StockLedgerResult>();

        Assert.Equal((36, 30, 6), (ledger.Header.OnHand, ledger.Header.Allocated, ledger.Header.Available));
        Assert.Equal([StockLedgerKind.Adjustment, StockLedgerKind.HandoverReceipt, StockLedgerKind.Adjustment], ledger.Rows.Items.Select(r => r.Kind));
        Assert.Equal([-4, 30, 10], ledger.Rows.Items.Select(r => r.Qty));
        Assert.Equal([36, 40, 10], ledger.Rows.Items.Select(r => r.RunningBalance));
        var receipt = ledger.Rows.Items[1];
        Assert.Equal((handover.Id, "FgHandover"), (receipt.SourceId!.Value, receipt.SourceType));
        Assert.Contains(s.Wo.Code, receipt.SourceLabel);
        Assert.Equal("rusak gudang", ledger.Rows.Items[0].Reason);
        Assert.False(string.IsNullOrEmpty(ledger.Rows.Items[0].CreatedByName));

        var page2 = (await H.Api.GetAsync(LedgerUrl(w, w.Small, "&pageSize=1&page=2"), H.AdminToken)).Ok().As<StockLedgerResult>();
        Assert.Equal((3, 1, 40), (page2.Rows.TotalCount, page2.Rows.Items.Count, page2.Rows.Items[0].RunningBalance)); // saldo berjalan tetap global

        var filtered = (await H.Api.GetAsync(LedgerUrl(w, w.Small, $"&from={Today(-2)}&to={Today()}"), H.AdminToken)).Ok().As<StockLedgerResult>();
        Assert.Equal(2, filtered.Rows.TotalCount);
        Assert.Equal([36, 40], filtered.Rows.Items.Select(r => r.RunningBalance)); // saldo berjalan bukan hanya dari entri terfilter
    }

    [Fact]
    public async Task Kartu_stok_kombinasi_valid_tanpa_riwayat_nol_dan_kombinasi_tidak_dikenal_404()
    {
        var w = await H.NewPoWorldAsync();

        var empty = (await H.Api.GetAsync(LedgerUrl(w, w.Small), H.AdminToken)).Ok().As<StockLedgerResult>();
        Assert.Equal((0, 0, 0), (empty.Header.OnHand, empty.Header.Allocated, empty.Rows.TotalCount));

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/stock/ledger?fgId={Guid.NewGuid()}&colorId={w.Color.Id}&sizeId={w.Small.Id}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== Penyesuaian =====

    [Fact]
    public async Task Penyesuaian_positif_negatif_dan_kombinasi_tanpa_riwayat_tercatat_di_ledger_dan_audit()
    {
        var w = await H.NewPoWorldAsync();

        await AdjustOkAsync(w, w.Small, 25, "temuan stok");
        await AdjustOkAsync(w, w.Small, -5, "rusak");

        Assert.Equal((20, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await H.AssertStockConsistentAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        await using var db = H.CreateDbContext();
        var audit = await db.AuditLogs.Where(a => a.EventType == "StockAdjusted" && a.TargetId!.StartsWith(w.Fg.Fg.Id.ToString())).ToListAsync();
        Assert.Equal(2, audit.Count);
    }

    [Fact]
    public async Task Penyesuaian_yang_membuat_stok_tersedia_negatif_ditolak_dan_pratinjau_menjelaskan_tanpa_menyimpan()
    {
        var w = await H.NewPoWorldAsync();
        await AdjustOkAsync(w, w.Small, 50);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        (await H.Api.PostAsync($"/api/v1/po-lines/{po.PoLineId(w.Small)}/allocate", new AllocateRequest(40, null), H.AdminToken)).Ok();
        var request = new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, -11, Today(), "kurangi");

        var preview = (await H.Api.PostAsync("/api/v1/stock/adjustments/preview", request, H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.False(preview.CanProceed);
        Assert.Contains(preview.Rows, r => r is { Metric: "Stok Tersedia", Before: 10, After: -1, Ok: false });

        var problem = ProblemAssert.Is(await H.Api.PostAsync("/api/v1/stock/adjustments", request, H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.StockInsufficient, problem.Code);
        Assert.Contains("Lepas alokasi", problem.Detail);
        Assert.Equal((50, 40), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));

        (await H.Api.PostAsync("/api/v1/stock/adjustments", request with { Qty = -10 }, H.AdminToken)).Ok("tepat sampai tersedia nol");
        var okPreview = (await H.Api.PostAsync("/api/v1/stock/adjustments/preview", request with { Qty = 5 }, H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.True(okPreview.CanProceed);
        Assert.Equal((40, 40), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
    }

    [Fact]
    public async Task Validasi_penyesuaian_qty_alasan_tanggal_dan_kombinasi()
    {
        var w = await H.NewPoWorldAsync();
        var otherSize = await H.NewSizeAsync();
        Task<ApiResponse> Post(AdjustStockRequest r) => H.Api.PostAsync("/api/v1/stock/adjustments", r, H.AdminToken);
        var ok = new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 5, Today(), "uji");

        Assert.Contains("qty", ProblemAssert.Is(await Post(ok with { Qty = 0 }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("qty", ProblemAssert.Is(await Post(ok with { Qty = 2_000_000 }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("reason", ProblemAssert.Is(await Post(ok with { Reason = " " }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("date", ProblemAssert.Is(await Post(ok with { Date = Today(1) }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("fgId", ProblemAssert.Is(await Post(ok with { FgId = Guid.NewGuid() }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("colorId", ProblemAssert.Is(await Post(ok with { ColorId = Guid.NewGuid() }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("sizeId", ProblemAssert.Is(await Post(ok with { SizeId = otherSize.Id }), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(ok)).Status);
    }

    [Fact]
    public async Task Penyesuaian_paralel_tidak_pernah_membuat_stok_negatif()
    {
        var w = await H.NewPoWorldAsync();
        await AdjustOkAsync(w, w.Small, 10);
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await H.Api.PostAsync("/api/v1/stock/adjustments", new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, -1, Today(), "ambil"), H.AdminToken);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal((0, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await H.AssertStockConsistentAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
    }

    // ===== Impor saldo awal =====

    [Fact]
    public async Task Impor_dryRun_bawaan_tidak_menulis_dan_commit_menambah_saldo_awal_dengan_alasan_otomatis()
    {
        var w = await H.NewPoWorldAsync();
        string Row(string size, int qty, string date = "") => $"{w.Fg.Fg.Code},{w.Color.Code},{size},{qty},{date}";
        var csv = Row(w.Small.Code, 40) + "\n" + Row(w.Medium.Code, 15, Today(-3).ToString("yyyy-MM-dd")) + "\n" + Row(w.Small.Code, 10);

        var dry = (await H.UploadOpeningAsync(csv, dryRun: true)).Ok().As<ImportResultDto>();
        Assert.Equal((3, 3, 0, false), (dry.TotalRows, dry.ValidRows, dry.ErrorRows, dry.Committed));
        Assert.Equal((0, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));

        var omitted = await H.Api.PostFileAsync("/api/v1/stock/opening-balance/import", "x.csv",
            System.Text.Encoding.UTF8.GetBytes("fg_code,color_code,size_code,qty,date\n" + Row(w.Small.Code, 1)), H.AdminToken);
        Assert.False(omitted.Ok().As<ImportResultDto>().Committed); // dryRun bawaan true

        var real = (await H.UploadOpeningAsync(csv, dryRun: false)).Ok().As<ImportResultDto>();
        Assert.True(real.Committed);
        Assert.Equal((50, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id)); // dua baris kombinasi sama dijumlahkan
        Assert.Equal((15, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id));

        var ledger = (await H.Api.GetAsync(LedgerUrl(w, w.Medium), H.AdminToken)).Ok().As<StockLedgerResult>();
        var entry = Assert.Single(ledger.Rows.Items);
        Assert.Equal((StockLedgerKind.OpeningBalance, Today(-3), "Saldo awal (import)"), (entry.Kind, entry.EntryDate, entry.Reason));
        await H.AssertStockConsistentAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);

        var again = (await H.UploadOpeningAsync(Row(w.Small.Code, 5), dryRun: true)).Ok().As<ImportResultDto>();
        Assert.Contains("sudah ada saldo awal", again.Rows[0].Message); // peringatan, tetap valid
        Assert.True(again.Rows[0].IsValid);
    }

    [Fact]
    public async Task Impor_semua_atau_tidak_sama_sekali_dengan_pesan_per_baris()
    {
        var w = await H.NewPoWorldAsync();
        var otherSize = await H.NewSizeAsync();
        var inactiveColor = await H.NewColorAsync();
        await using (var db = H.CreateDbContext())
        {
            await db.Colors.Where(c => c.Id == inactiveColor.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.IsActive, false));
        }

        var fg = w.Fg.Fg.Code;
        var csv = string.Join("\n",
            $"{fg},{w.Color.Code},{w.Small.Code},10,",              // valid
            $"TIDAK-ADA,{w.Color.Code},{w.Small.Code},10,",         // FG tak ada
            $"{fg},{inactiveColor.Code},{w.Small.Code},10,",        // color nonaktif
            $"{fg},{w.Color.Code},{otherSize.Code},10,",            // size bukan Size Berlaku
            $"{fg},{w.Color.Code},{w.Small.Code},0,",               // qty 0
            $"{fg},{w.Color.Code},{w.Small.Code},abc,",             // qty bukan angka
            $"{fg},{w.Color.Code},{w.Small.Code},5,2026-13-45",     // tanggal rusak
            $"{fg},{w.Color.Code},{w.Small.Code},5,{Today(2):yyyy-MM-dd}"); // masa depan

        var result = (await H.UploadOpeningAsync(csv, dryRun: false)).Ok().As<ImportResultDto>();

        Assert.Equal((8, 1, 7, false), (result.TotalRows, result.ValidRows, result.ErrorRows, result.Committed));
        Assert.Contains("FG tidak ditemukan", result.Rows[1].Message);
        Assert.Contains("Color nonaktif", result.Rows[2].Message);
        Assert.Contains("Size Berlaku", result.Rows[3].Message);
        Assert.Contains("Qty", result.Rows[4].Message);
        Assert.Contains("Qty", result.Rows[5].Message);
        Assert.Contains("yyyy-MM-dd", result.Rows[6].Message);
        Assert.Contains("masa depan", result.Rows[7].Message);
        Assert.Equal((0, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id)); // baris valid pun tidak disimpan
    }

    [Fact]
    public async Task Impor_menolak_header_salah_dan_dilindungi_permission_serta_template_dapat_diunduh()
    {
        var w = await H.NewPoWorldAsync();
        var importer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.StokFG.View, Permissions.StokFG.Import)).Id);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.StokFG.View)).Id);

        var wrongHeader = await H.UploadOpeningAsync($"{w.Fg.Fg.Code},{w.Color.Code},{w.Small.Code},1", dryRun: true, header: "kode,warna,ukuran,jumlah");
        ProblemAssert.Is(wrongHeader, HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);

        ProblemAssert.Is(await H.UploadOpeningAsync($"{w.Fg.Fg.Code},{w.Color.Code},{w.Small.Code},1,", dryRun: true, token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.UploadOpeningAsync($"{w.Fg.Fg.Code},{w.Color.Code},{w.Small.Code},1,", dryRun: true, token: importer.Token)).IsSuccess);

        var template = await H.Api.GetAsync("/api/v1/stock/opening-balance/import-template", importer.Token);
        Assert.True(template.IsSuccess);
        Assert.Contains("fg_code,color_code,size_code,qty,date", template.Body);
        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/stock/opening-balance/import-template", viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    // ===== Serah terima material =====

    private async Task<(MaterialDto Material, PoWorld World, PoDto Po, Gms.Shared.WorkOrders.WoDto Wo)> MaterialSetupAsync()
    {
        var (w, po, wo) = await H.NewWoSetupAsync();
        return (await H.NewMaterialAsync(), w, po, wo);
    }

    private Task<ApiResponse> PostMaterialAsync(Guid woId, IEnumerable<MaterialHandoverLine> lines, DateOnly? date = null, string? docNo = null, string? token = null) =>
        H.Api.PostAsync("/api/v1/handovers/material", new SaveMaterialHandoverRequest(woId, date ?? Today(), docNo, "catatan", lines.ToList()), token ?? H.AdminToken);

    private static Guid IdOf(ApiResponse response) => System.Text.Json.JsonDocument.Parse(response.Ok().Body).RootElement.GetProperty("id").GetGuid();

    [Fact]
    public async Task Serah_terima_material_pada_WO_Draft_tercatat_dengan_satuan_snapshot_dan_tidak_memengaruhi_stok()
    {
        var (material, w, _, wo) = await MaterialSetupAsync();
        var second = await H.NewMaterialAsync();

        var id = IdOf(await PostMaterialAsync(wo.Id, [new MaterialHandoverLine(material.Id, 12.345m, "roll 1"), new MaterialHandoverLine(second.Id, 3m, null)], docNo: "SJ-MAT-1"));

        var detail = (await H.Api.GetAsync($"/api/v1/handovers/material/{id}", H.AdminToken)).Ok().As<MaterialHandoverDto>();
        Assert.Equal((2, "SJ-MAT-1", DocStatus.Active, true), (detail.Lines.Count, detail.Item.DocNo, detail.Item.Status, detail.CanVoid));
        var line = detail.Lines.Single(l => l.MaterialId == material.Id);
        Assert.Equal((12.345m, "roll 1", material.Code), (line.Qty, line.Notes, line.MaterialCode));
        Assert.False(string.IsNullOrEmpty(line.UomCode));
        Assert.Equal((0, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));

        await using var db = H.CreateDbContext();
        Assert.Contains("MaterialHandoverCreated", await db.AuditLogs.Where(a => a.TargetId == id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Validasi_serah_terima_material()
    {
        var (material, _, _, wo) = await MaterialSetupAsync();
        var inactive = await H.NewMaterialAsync();
        await using (var db = H.CreateDbContext())
        {
            await db.Materials.Where(m => m.Id == inactive.Id).ExecuteUpdateAsync(m => m.SetProperty(x => x.IsActive, false));
        }

        MaterialHandoverLine Line(decimal qty, Guid? id = null) => new(id ?? material.Id, qty, null);
        string[] Errors(ApiResponse r) => ProblemAssert.Is(r, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys.ToArray();

        Assert.Contains("lines", Errors(await PostMaterialAsync(wo.Id, [])));
        Assert.Contains("lines[0].qty", Errors(await PostMaterialAsync(wo.Id, [Line(0)])));
        Assert.Contains("lines[0].qty", Errors(await PostMaterialAsync(wo.Id, [Line(-1)])));
        Assert.Contains("lines[0].qty", Errors(await PostMaterialAsync(wo.Id, [Line(1.2345m)]))); // 4 digit desimal ditolak, tidak dibulatkan
        Assert.Contains("lines[0].materialId", Errors(await PostMaterialAsync(wo.Id, [Line(1, inactive.Id)])));
        Assert.Contains("lines[0].materialId", Errors(await PostMaterialAsync(wo.Id, [Line(1, Guid.NewGuid())])));
        Assert.Contains("date", Errors(await PostMaterialAsync(wo.Id, [Line(1)], date: Today(1))));
        ProblemAssert.Is(await PostMaterialAsync(Guid.NewGuid(), [Line(1)]), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        Assert.True((await PostMaterialAsync(wo.Id, [Line(0.001m)])).IsSuccess);
    }

    [Fact]
    public async Task Serah_terima_material_ditolak_pada_WO_Closed_atau_Cancelled_dan_void_mengikuti_aturan_yang_sama()
    {
        var (material, _, _, wo) = await MaterialSetupAsync();
        var line = new MaterialHandoverLine(material.Id, 5, null);
        var id = IdOf(await PostMaterialAsync(wo.Id, [line]));

        Assert.Contains("reason", ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/material/{id}/void", new ReasonRequest(" ", 0), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        await H.SetWoStatusAsync(wo.Id, WoStatus.Closed);
        ProblemAssert.Is(await PostMaterialAsync(wo.Id, [line]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/material/{id}/void", new ReasonRequest("x", 0), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.False((await H.Api.GetAsync($"/api/v1/handovers/material/{id}", H.AdminToken)).Ok().As<MaterialHandoverDto>().CanVoid);

        await H.SetWoStatusAsync(wo.Id, WoStatus.Completed);
        Assert.True((await PostMaterialAsync(wo.Id, [line])).IsSuccess); // Completed diizinkan
        Assert.Equal(HttpStatusCode.NoContent, (await H.Api.PostAsync($"/api/v1/handovers/material/{id}/void", new ReasonRequest("salah catat", 0), H.AdminToken)).Status);
        var voided = (await H.Api.GetAsync($"/api/v1/handovers/material/{id}", H.AdminToken)).Ok().As<MaterialHandoverDto>();
        Assert.Equal((DocStatus.Voided, "salah catat", false), (voided.Item.Status, voided.VoidReason, voided.CanVoid));
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/material/{id}/void", new ReasonRequest("lagi", 0), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/material/{Guid.NewGuid()}/void", new ReasonRequest("x", 0), H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);

        await H.SetWoStatusAsync(wo.Id, WoStatus.Cancelled);
        ProblemAssert.Is(await PostMaterialAsync(wo.Id, [line]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Daftar_serah_terima_material_menyaring_dan_permission_dipisah()
    {
        var (material, _, _, wo) = await MaterialSetupAsync();
        var line = new MaterialHandoverLine(material.Id, 5, null);
        var a = IdOf(await PostMaterialAsync(wo.Id, [line, line], docNo: "MAT-A"));
        var b = IdOf(await PostMaterialAsync(wo.Id, [line], docNo: "MAT-B"));
        await H.Api.PostAsync($"/api/v1/handovers/material/{b}/void", new ReasonRequest("batal", 0), H.AdminToken);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.SerahTerimaMaterial.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.SerahTerimaMaterial.View, Permissions.SerahTerimaMaterial.Create)).Id);

        async Task<PagedResult<MaterialHandoverListItem>> List(string q) =>
            (await H.Api.GetAsync($"/api/v1/handovers/material?pageSize=50&woId={wo.Id}{q}", H.AdminToken)).Ok().As<PagedResult<MaterialHandoverListItem>>();

        Assert.Equal(2, (await List("")).TotalCount);
        Assert.Equal([b], (await List("&status=Voided")).Items.Select(i => i.Id));
        Assert.Equal(2, (await List("")).Items.Single(i => i.Id == a).ItemCount);
        Assert.Equal([a], (await List("&q=MAT-A")).Items.Select(i => i.Id));
        Assert.Equal(0, (await List($"&from={Today(1)}")).TotalCount);

        ProblemAssert.Is(await PostMaterialAsync(wo.Id, [line], token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await PostMaterialAsync(wo.Id, [line], token: creator.Token)).IsSuccess);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/material/{a}/void", new ReasonRequest("x", 0), creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.False((await H.Api.GetAsync($"/api/v1/handovers/material/{a}", viewer.Token)).Ok().As<MaterialHandoverDto>().CanVoid);
    }
}
