using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Production;

/// <summary>Input output pekerja, aturan kumulatif, void, koreksi, penyesuaian, dan aturan periode (FSD Production Output 4.1–4.5).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductionOutputTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    // ===== Simpan output =====

    [Fact]
    public async Task Satu_penyimpanan_banyak_pekerja_membuat_satu_entri_per_pekerja_dan_memicu_InProgress()
    {
        var s = await H.NewProdSetupAsync();
        var w1 = await H.NewWorkerAsync();
        var w2 = await H.NewWorkerAsync();

        var result = (await H.PostOutputsAsync(s, s.Cut, [Row(w1.Id, (s.LineS, 20), (s.LineM, 5)), Row(w2.Id, (s.LineS, 10))])).Ok().As<SaveOutputsResult>();

        Assert.Equal((2, 35, 2), (result.EntryIds.Count, result.TotalQty, result.WorkerCount));
        Assert.Equal((30, 0), await H.BalanceAsync(s.LineS, s.Cut));
        Assert.Equal((5, 0), await H.BalanceAsync(s.LineM, s.Cut));

        var wo = await H.GetWoAsync(s.Wo.Id);
        Assert.Equal(WoStatus.InProgress, wo.Status);
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(s.Po.Id)).Status);
        var history = (await H.Api.GetAsync($"/api/v1/work-orders/{s.Wo.Id}/status-history", H.AdminToken)).Ok().As<List<WoStatusHistoryItem>>();
        Assert.Equal(WoStatus.InProgress, history[^1].ToStatus);

        var detail = await H.GetEntryAsync(result.EntryIds[0]);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Equal((EntryKind.Output, w1.Name, 25), (detail.Item.Kind, detail.Item.WorkerName, detail.Item.TotalQty));

        await using var db = H.CreateDbContext();
        Assert.Contains("OutputCreated", await db.AuditLogs.Where(a => a.TargetId == s.Wo.Id.ToString()).Select(a => a.EventType).ToListAsync());
        await H.AssertLedgerInvariantAsync(s);
    }

    [Fact]
    public async Task Pekerja_yang_sama_boleh_punya_beberapa_entri_pada_penyimpanan_berbeda_tetapi_tidak_dalam_satu_penyimpanan()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();

        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 5));
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 5));
        Assert.Equal((10, 0), await H.BalanceAsync(s.LineS, s.Cut));

        var dup = await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1)), Row(w.Id, (s.LineM, 1))]);
        Assert.Contains("rows[1].workerId", ProblemAssert.Is(dup, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task Validasi_baris_pekerja_sel_langkah_dan_tanggal()
    {
        var s = await H.NewProdSetupAsync();
        var worker = await H.NewWorkerAsync();
        var inactive = await H.NewWorkerAsync();
        await H.DeactivateWorkerAsync(inactive.Id);
        var otherWo = await H.NewProdSetupAsync();

        Assert.Contains("rows", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(worker.Id, (s.LineS, 0))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("rows", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, []), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("rows[0].cells[0].qty", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(worker.Id, (s.LineS, -1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("rows[0].cells[0].woLineId", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(worker.Id, (otherWo.LineS, 1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("rows[0].workerId", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(inactive.Id, (s.LineS, 1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("rows[0].workerId", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(Guid.NewGuid(), (s.LineS, 1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("seq", ProblemAssert.Is(await H.PostOutputsAsync(s, 99, [Row(worker.Id, (s.LineS, 1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("seq", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Emb, [Row(worker.Id, (s.LineS, 1))]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("workDate", ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(worker.Id, (s.LineS, 1))], date: Today(1)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        (await H.PostOutputsAsync(s, s.Cut, [Row(worker.Id, (s.LineS, 1))])).Ok("input yang valid tetap lolos");
    }

    [Fact]
    public async Task Sel_nol_dilewati_dan_baris_semua_nol_tidak_disimpan_selama_ada_baris_bernilai()
    {
        var s = await H.NewProdSetupAsync();
        var a = await H.NewWorkerAsync();
        var b = await H.NewWorkerAsync();

        var result = (await H.PostOutputsAsync(s, s.Cut, [Row(a.Id, (s.LineS, 0), (s.LineM, 0)), Row(b.Id, (s.LineS, 7), (s.LineM, 0))])).Ok().As<SaveOutputsResult>();

        Assert.Equal((1, 7), (result.EntryIds.Count, result.TotalQty));
    }

    // ===== Aturan kumulatif =====

    [Fact]
    public async Task Langkah_pertama_tanpa_batas_tetapi_melebihi_Qty_WO_meminta_konfirmasi()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        var w = await H.NewWorkerAsync();

        var unconfirmed = await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 61))]);
        var problem = ProblemAssert.Is(unconfirmed, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.OutputExceedsWoQtyNeedsConfirmation, problem.Code);
        Assert.Equal((61m, 60m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Equal((0, 0), await H.BalanceAsync(s.LineS, s.Cut));

        var confirmed = (await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 61))], confirm: true)).Ok().As<SaveOutputsResult>();
        Assert.Single(confirmed.Warnings);
        Assert.Equal((61, 0), await H.BalanceAsync(s.LineS, s.Cut));
        var progress = (await H.Api.GetAsync($"/api/v1/work-orders/{s.Wo.Id}/progress", H.AdminToken)).Ok().As<WoProgressDto>();
        Assert.Equal(61, progress.Steps[0].Cells.Single(c => c.WoLineId == s.LineS).Output);
    }

    [Fact]
    public async Task Langkah_berikutnya_dibatasi_output_langkah_sebelumnya_dengan_batas_dan_sel_pada_pesan()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        await H.SendAsync(s, v.Id, (s.LineS, 30));
        await H.ReceiveAsync(s, v.Id, (s.LineS, 20));

        var over = await H.PostOutputsAsync(s, s.Sew, [Row(w.Id, (s.LineS, 21))]);

        var problem = ProblemAssert.Is(over, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.OutputCumulativeExceeded, problem.Code);
        Assert.Equal((21m, 20m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Contains("langkah " + s.Sew, problem.Violations[0].Subject);
        Assert.Equal((0, 0), await H.BalanceAsync(s.LineS, s.Sew));

        await H.OutAsync(s, s.Sew, w.Id, (s.LineS, 20));
        await H.AssertLedgerInvariantAsync(s);
    }

    [Fact]
    public async Task Penyimpanan_semua_atau_tidak_sama_sekali_satu_baris_melanggar_menggagalkan_semuanya()
    {
        var s = await H.NewProdSetupAsync();
        var a = await H.NewWorkerAsync();
        var b = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, a.Id, (s.LineS, 10));
        await H.SendAsync(s, v.Id, (s.LineS, 10));
        await H.ReceiveAsync(s, v.Id, (s.LineS, 10));

        var response = await H.PostOutputsAsync(s, s.Sew, [Row(a.Id, (s.LineS, 6)), Row(b.Id, (s.LineS, 6))]); // total 12 > 10

        ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal((0, 0), await H.BalanceAsync(s.LineS, s.Sew));
        await using var db = H.CreateDbContext();
        Assert.Equal(0, await db.OutputEntries.CountAsync(e => e.WoId == s.Wo.Id && e.Seq == s.Sew));
    }

    [Fact]
    public async Task Output_ditolak_bila_WO_Completed_Closed_atau_Draft_dengan_pesan_jelas()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 5));

        await H.WoDoAsync(s.Wo.Id, "complete");
        var problem = ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.Contains("selesai", problem.Detail);
        Assert.Equal(ProblemCodes.WoStatusInvalid, problem.Code);

        await H.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);
        ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        var (w2, po2, draft) = await H.NewWoSetupAsync();
        var s2 = new ProdSetup(w2, po2, draft, 1, 2, 3, draft.Lines[0].Id, draft.Lines[1].Id);
        ProblemAssert.Is(await H.PostOutputsAsync(s2, 1, [Row(w.Id, (s2.LineS, 1))]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Konteks input =====

    [Fact]
    public async Task Konteks_input_menampilkan_batas_per_sel_dan_total_dan_langkah_pertama_tanpa_batas()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60, woQtyMedium: 40);
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30), (s.LineM, 10));
        await H.SendAsync(s, v.Id, (s.LineS, 30), (s.LineM, 10));
        await H.ReceiveAsync(s, v.Id, (s.LineS, 25), (s.LineM, 10));
        await H.OutAsync(s, s.Sew, w.Id, (s.LineS, 5));

        async Task<InputContextDto> Ctx(int seq, DateOnly? date = null) =>
            (await H.Api.GetAsync($"/api/v1/production/input-context?woId={s.Wo.Id}&seq={seq}&workDate={date ?? Today()}", H.AdminToken)).Ok().As<InputContextDto>();

        var first = await Ctx(s.Cut);
        Assert.True(first.IsFirstStep);
        Assert.All(first.Columns, c => Assert.Null(c.MaxAllowed));
        Assert.Equal((100, 40, 60), (first.PreviousStepTotal, first.CurrentTotal, first.AvailableTotal));
        Assert.Null(first.BlockedReason);

        var sew = await Ctx(s.Sew);
        Assert.False(sew.IsFirstStep);
        Assert.Equal(20, sew.Columns.Single(c => c.WoLineId == s.LineS).MaxAllowed); // 25 diterima − 5 dijahit
        Assert.Equal(10, sew.Columns.Single(c => c.WoLineId == s.LineM).MaxAllowed);
        Assert.Equal((35, 5, 30), (sew.PreviousStepTotal, sew.CurrentTotal, sew.AvailableTotal));

        Assert.Contains("Vendor", (await Ctx(s.Emb)).BlockedReason);
        Assert.Contains("masa depan", (await Ctx(s.Cut, Today(1))).BlockedReason);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/production/input-context?woId={s.Wo.Id}&seq=99&workDate={Today()}", H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);

        await H.WoDoAsync(s.Wo.Id, "complete");
        Assert.Contains("selesai", (await Ctx(s.Cut)).BlockedReason);
    }

    // ===== Void =====

    [Fact]
    public async Task Void_wajib_beralasan_tidak_dihitung_lagi_dan_tidak_dapat_diaktifkan_kembali()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 25));

        Assert.Contains("reason", ProblemAssert.Is(await H.VoidAsync(id, " "), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(HttpStatusCode.NoContent, (await H.VoidAsync(id, "salah orang")).Status);

        Assert.Equal((0, 0), await H.BalanceAsync(s.LineS, s.Cut));
        var detail = await H.GetEntryAsync(id);
        Assert.Equal((DocStatus.Voided, "salah orang"), (detail.Item.Status, detail.Item.VoidReason));
        Assert.False(detail.Item.CanVoid);
        ProblemAssert.Is(await H.VoidAsync(id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        await H.AssertLedgerInvariantAsync(s);

        await using var db = H.CreateDbContext();
        Assert.Contains("OutputVoided", await db.AuditLogs.Where(a => a.TargetId == id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Void_ditolak_bila_langkah_hilir_sudah_memakai_dan_pratinjau_menyebut_sel_penahan()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        var cut1 = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
        await H.SendAsync(s, v.Id, (s.LineS, 35));

        var preview = await H.PreviewVoidAsync(cut1);
        Assert.False(preview.CanProceed);
        Assert.Contains(preview.Violations, x => x.Code == ProblemCodes.OutputBelowDownstream && x.Message.Contains("10"));
        Assert.Contains(preview.Rows, r => !r.Ok);
        var cutRow = preview.Rows.First(r => r.Subject.Contains("langkah " + s.Cut));
        Assert.Equal((40m, 10m), (cutRow.Before!.Value, cutRow.After!.Value)); // kumulatif 40 → 10 bila entri 30 di-void

        var response = await H.VoidAsync(cut1);
        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.OutputBelowDownstream, problem.Code);
        Assert.Equal(DocStatus.Active, (await H.GetEntryAsync(cut1)).Item.Status);
        Assert.Equal((40, 0), await H.BalanceAsync(s.LineS, s.Cut)); // pratinjau tidak mengubah apa pun
    }

    [Fact]
    public async Task Pratinjau_void_yang_aman_tidak_menyimpan_apa_pun()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 12));

        var preview = await H.PreviewVoidAsync(id);

        Assert.True(preview.CanProceed);
        Assert.Contains(preview.Rows, r => r is { Ok: true, Before: 12, After: 0 });
        Assert.Equal((12, 0), await H.BalanceAsync(s.LineS, s.Cut));
        Assert.Equal(DocStatus.Active, (await H.GetEntryAsync(id)).Item.Status);
    }

    // ===== Koreksi =====

    [Fact]
    public async Task Koreksi_mengganti_pekerja_secara_atomik_walau_langkah_hilir_sudah_memakai_seluruh_qty()
    {
        var s = await H.NewProdSetupAsync();
        var wrong = await H.NewWorkerAsync();
        var right = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        var id = await H.OutAsync(s, s.Cut, wrong.Id, (s.LineS, 20));
        await H.SendAsync(s, v.Id, (s.LineS, 20)); // memakai seluruh output; void biasa akan ditolak

        ProblemAssert.Is(await H.VoidAsync(id), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);

        var response = await H.Api.PostAsync($"/api/v1/production/entries/{id}/correct",
            new CorrectOutputRequest("pekerja salah", right.Id, Today(), Cells((s.LineS, 20)).ToList()), H.AdminToken);
        var newId = System.Text.Json.JsonDocument.Parse(response.Ok().Body).RootElement.GetProperty("id").GetGuid();

        var old = await H.GetEntryAsync(id);
        var replacement = await H.GetEntryAsync(newId);
        Assert.Equal(DocStatus.Voided, old.Item.Status);
        Assert.Equal(newId, old.Item.ReplacedById);
        Assert.Equal((id, right.Id, "pekerja salah"), (replacement.Item.ReplacesId!.Value, replacement.Item.WorkerId!.Value, replacement.Reason));
        Assert.Equal((20, 0), await H.BalanceAsync(s.LineS, s.Cut));
        await H.AssertLedgerInvariantAsync(s);

        await using var db = H.CreateDbContext();
        Assert.Contains("OutputCorrected", await db.AuditLogs.Where(a => a.TargetId == id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Koreksi_ditolak_bila_kondisi_akhir_melanggar_dan_validasi_alasan_pekerja_serta_status()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var inactive = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 20));
        await H.SendAsync(s, v.Id, (s.LineS, 20));
        string Url(Guid x, string a) => $"/api/v1/production/entries/{x}/{a}";

        var lower = await H.Api.PostAsync(Url(id, "correct"), new CorrectOutputRequest("turun", w.Id, Today(), Cells((s.LineS, 19)).ToList()), H.AdminToken);
        Assert.Equal(ProblemCodes.OutputBelowDownstream, ProblemAssert.Is(lower, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        var preview = (await H.Api.PostAsync(Url(id, "correct/preview"), new CorrectOutputRequest("turun", w.Id, Today(), Cells((s.LineS, 19)).ToList()), H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.False(preview.CanProceed);
        Assert.Equal(DocStatus.Active, (await H.GetEntryAsync(id)).Item.Status);

        Assert.Contains("reason", ProblemAssert.Is(await H.Api.PostAsync(Url(id, "correct"), new CorrectOutputRequest(" ", w.Id, Today(), Cells((s.LineS, 20)).ToList()), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        await H.DeactivateWorkerAsync(inactive.Id);
        Assert.Contains("workerId", ProblemAssert.Is(await H.Api.PostAsync(Url(id, "correct"), new CorrectOutputRequest("x", inactive.Id, Today(), Cells((s.LineS, 20)).ToList()), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("cells", ProblemAssert.Is(await H.Api.PostAsync(Url(id, "correct"), new CorrectOutputRequest("x", w.Id, Today(), Cells((s.LineS, 0)).ToList()), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        await H.VoidAsync(await H.OutAsync(s, s.Cut, w.Id, (s.LineM, 5)));
        var voided = (await H.ListEntriesAsync($"&includeVoided=true&woId={s.Wo.Id}")).Items.First(i => i.Status == DocStatus.Voided).Id;
        ProblemAssert.Is(await H.Api.PostAsync(Url(voided, "correct"), new CorrectOutputRequest("x", w.Id, Today(), Cells((s.LineM, 5)).ToList()), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Periode gaji dan penyesuaian =====

    [Fact]
    public async Task Tanggal_di_periode_tertutup_atau_sebelum_periode_pertama_ditolak_dan_void_koreksi_juga()
    {
        var host = await fixture.CreateIsolatedAsync();
        var s = await host.NewProdSetupAsync();
        var w = await host.NewWorkerAsync();
        var oldEntry = await host.OutAsync(s, s.Cut, w.Id, (s.LineS, 10)); // hari ini, periode belum ada
        await host.InsertPeriodAsync(Today(-30), Today(-20), PayrollStatus.Closed);
        await host.InsertPeriodAsync(Today(-19), Today(-10), PayrollStatus.Closed);

        var inClosed = await host.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))], date: Today(-25));
        ProblemAssert.Is(inClosed, HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        Assert.Contains("Penyesuaian", inClosed.Problem.Detail);

        var beforeFirst = await host.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))], date: Today(-40));
        Assert.Equal(ProblemCodes.PayrollDateBeforeFirstPeriod, ProblemAssert.Is(beforeFirst, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        (await host.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))], date: Today(-5))).Ok("tanggal mundur di luar periode tertutup");

        // Entri lama dipindah ke tanggal di periode tertutup (simulasi data historis) → void/koreksi ditolak.
        await using (var db = host.CreateDbContext())
        {
            await db.OutputEntries.Where(e => e.Id == oldEntry).ExecuteUpdateAsync(e => e.SetProperty(x => x.WorkDate, Today(-25)));
        }

        ProblemAssert.Is(await host.VoidAsync(oldEntry), HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        var correct = await host.Api.PostAsync($"/api/v1/production/entries/{oldEntry}/correct",
            new CorrectOutputRequest("x", w.Id, Today(), Cells((s.LineS, 10)).ToList()), host.AdminToken);
        ProblemAssert.Is(correct, HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        var listed = (await host.ListEntriesAsync($"&woId={s.Wo.Id}")).Items.Single(i => i.Id == oldEntry);
        Assert.False(listed.CanVoid);
        Assert.Contains("Penyesuaian", listed.DisabledReason);
    }

    [Fact]
    public async Task Penyesuaian_bertanggal_asal_di_periode_tertutup_boleh_negatif_dan_tunduk_pada_rantai()
    {
        var host = await fixture.CreateIsolatedAsync();
        var s = await host.NewProdSetupAsync();
        var w = await host.NewWorkerAsync();
        var inactive = await host.NewWorkerAsync();
        var v = await host.NewVendorAsync();
        await host.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        await host.InsertPeriodAsync(Today(-30), Today(-20), PayrollStatus.Closed);

        async Task<ApiResponse> Adjust(DateOnly original, DateOnly work, int qty, string reason = "salah hitung", Guid? worker = null) =>
            await host.Api.PostAsync("/api/v1/production/adjustments",
                new AdjustmentRequest(s.Wo.Id, s.Cut, worker ?? w.Id, original, work, reason, Cells((s.LineS, qty)).ToList()), host.AdminToken);

        Assert.Contains("originalDate", ProblemAssert.Is(await Adjust(Today(-5), Today(), -5), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("reason", ProblemAssert.Is(await Adjust(Today(-25), Today(), -5, " "), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        ProblemAssert.Is(await Adjust(Today(-25), Today(-22), -5), HttpStatusCode.Conflict, ProblemTypes.PeriodClosed); // tanggal kerja harus di periode terbuka
        Assert.Contains("cells", ProblemAssert.Is(await host.Api.PostAsync("/api/v1/production/adjustments",
            new AdjustmentRequest(s.Wo.Id, s.Cut, w.Id, Today(-25), Today(), "x", Cells((s.LineS, 0)).ToList()), host.AdminToken),
            HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var negative = System.Text.Json.JsonDocument.Parse((await Adjust(Today(-25), Today(), -10)).Ok().Body).RootElement.GetProperty("id").GetGuid();
        Assert.Equal((20, 0), await host.BalanceAsync(s.LineS, s.Cut));
        var detail = await host.GetEntryAsync(negative);
        Assert.Equal((EntryKind.Adjustment, -10, Today(-25), "salah hitung"), (detail.Item.Kind, detail.Item.TotalQty, detail.OriginalDate!.Value, detail.Reason));

        var plus5 = System.Text.Json.JsonDocument.Parse((await Adjust(Today(-25), Today(), 5, worker: inactive.Id)).Ok("positif").Body).RootElement.GetProperty("id").GetGuid();
        await host.DeactivateWorkerAsync(inactive.Id);
        (await Adjust(Today(-25), Today(), 1, worker: inactive.Id)).Ok("pekerja nonaktif diizinkan");
        Assert.Equal((26, 0), await host.BalanceAsync(s.LineS, s.Cut));

        // Melanggar rantai: kirim 26 ke vendor, lalu penyesuaian negatif -1 membuat Kum < terkirim.
        await host.SendAsync(s, v.Id, (s.LineS, 26));
        var violating = await Adjust(Today(-25), Today(), -1);
        Assert.Equal(ProblemCodes.OutputBelowDownstream, ProblemAssert.Is(violating, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        // Void penyesuaian +5 membuat Kum 21 < terkirim 26 → ditolak; void penyesuaian −10 menaikkan Kum → boleh.
        ProblemAssert.Is(await host.VoidAsync(plus5), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(HttpStatusCode.NoContent, (await host.VoidAsync(negative)).Status);
        Assert.Equal((36, 0), await host.BalanceAsync(s.LineS, s.Cut));
        await host.AssertLedgerInvariantAsync(s);
    }

    [Fact]
    public async Task Penyesuaian_tidak_dapat_dikoreksi_tetapi_dapat_di_void_bila_rantai_tetap_utuh()
    {
        var host = await fixture.CreateIsolatedAsync();
        var s = await host.NewProdSetupAsync();
        var w = await host.NewWorkerAsync();
        await host.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        await host.InsertPeriodAsync(Today(-30), Today(-20), PayrollStatus.Closed);
        var adj = System.Text.Json.JsonDocument.Parse((await host.Api.PostAsync("/api/v1/production/adjustments",
            new AdjustmentRequest(s.Wo.Id, s.Cut, w.Id, Today(-25), Today(), "tambah", Cells((s.LineS, 4)).ToList()), host.AdminToken)).Ok().Body)
            .RootElement.GetProperty("id").GetGuid();

        ProblemAssert.Is(await host.Api.PostAsync($"/api/v1/production/entries/{adj}/correct",
            new CorrectOutputRequest("x", w.Id, Today(), Cells((s.LineS, 4)).ToList()), host.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        Assert.Equal(HttpStatusCode.NoContent, (await host.VoidAsync(adj, "batal")).Status);
        Assert.Equal((30, 0), await host.BalanceAsync(s.LineS, s.Cut));
    }

    // ===== Permission =====

    [Fact]
    public async Task Create_void_correct_dan_adjust_dilindungi_permission_masing_masing()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.ProductionOutput.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.ProductionOutput.View, Permissions.ProductionOutput.Create)).Id);
        var voider = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.ProductionOutput.View, Permissions.ProductionOutput.Void)).Id);

        ProblemAssert.Is(await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))], token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 1))], token: creator.Token)).IsSuccess);
        ProblemAssert.Is(await H.VoidAsync(id, token: creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/production/entries/{id}/correct", new CorrectOutputRequest("x", w.Id, Today(), Cells((s.LineS, 10)).ToList()), voider.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync("/api/v1/production/adjustments", new AdjustmentRequest(s.Wo.Id, s.Cut, w.Id, Today(-90), Today(), "x", Cells((s.LineS, 1)).ToList()), voider.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/production/input-context?woId={s.Wo.Id}&seq={s.Cut}&workDate={Today()}", viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await H.VoidAsync(id, token: voider.Token)).Status);

        var item = (await H.ListEntriesAsync($"&woId={s.Wo.Id}", viewer.Token)).Items.First();
        Assert.False(item.CanVoid);
        Assert.False(item.CanCorrect);
    }
}
