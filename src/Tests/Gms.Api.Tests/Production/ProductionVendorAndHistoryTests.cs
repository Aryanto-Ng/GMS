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

/// <summary>Kirim/terima vendor, riwayat gabungan, dan perilaku konkurensi (FSD Production Output 4.6, 4.7, AC-PR-35).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductionVendorAndHistoryTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    // ===== Vendor =====

    [Fact]
    public async Task Kirim_dibatasi_output_langkah_sebelumnya_dan_terima_dibatasi_terkirim_per_vendor()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var a = await H.NewVendorAsync();
        var b = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));

        var tooMany = await H.PostVendorAsync(s, VendorMovementKind.Send, a.Id, [(s.LineS, 31)]);
        var problem = ProblemAssert.Is(tooMany, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.VendorSentExceeded, problem.Code);
        Assert.Equal((31m, 30m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));

        await H.SendAsync(s, a.Id, (s.LineS, 20));
        await H.SendAsync(s, b.Id, (s.LineS, 10));
        Assert.Equal(ProblemCodes.VendorSentExceeded, ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, a.Id, [(s.LineS, 1)]), HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        // Vendor A hanya menerima sampai 20 walau total terkirim 30; vendor B tidak boleh menerima milik A.
        var overReceive = await H.PostVendorAsync(s, VendorMovementKind.Receive, a.Id, [(s.LineS, 21)]);
        var received = ProblemAssert.Is(overReceive, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.VendorReceivedExceeded, received.Code);
        Assert.Contains(a.Name, received.Violations![0].Subject);

        await H.ReceiveAsync(s, a.Id, (s.LineS, 12));
        await H.ReceiveAsync(s, a.Id, (s.LineS, 8)); // penerimaan bertahap
        Assert.Equal(ProblemCodes.VendorReceivedExceeded, ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Receive, b.Id, [(s.LineS, 11)]), HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        await H.ReceiveAsync(s, b.Id, (s.LineS, 10));

        Assert.Equal((30, 30), await H.BalanceAsync(s.LineS, s.Emb));
        await H.AssertLedgerInvariantAsync(s);
    }

    [Fact]
    public async Task Vendor_pada_langkah_pertama_dibatasi_Qty_WO_dan_konteks_menampilkan_batas_serta_sisa_di_vendor()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60, woQtyMedium: 40);
        var w = await H.NewWorkerAsync();
        var a = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 50), (s.LineM, 40));
        await H.SendAsync(s, a.Id, (s.LineS, 40), (s.LineM, 10));
        await H.ReceiveAsync(s, a.Id, (s.LineS, 15));

        var ctx = (await H.Api.GetAsync($"/api/v1/production/vendor-context?woId={s.Wo.Id}&seq={s.Emb}&vendorId={a.Id}", H.AdminToken)).Ok().As<VendorContextDto>();

        var cellS = ctx.Columns.Single(c => c.WoLineId == s.LineS);
        Assert.Equal((10, 25, 40, 15), (cellS.MaxSend, cellS.MaxReceive, cellS.SentTotal, cellS.ReceivedTotal)); // 50 output − 40 terkirim
        Assert.Equal(30, ctx.Columns.Single(c => c.WoLineId == s.LineM).MaxSend);
        var summary = Assert.Single(ctx.Vendors);
        Assert.Equal((a.Id, 50, 15, 35), (summary.VendorId, summary.Sent, summary.Received, summary.Outstanding));
        Assert.Null(ctx.BlockedReason);

        var noVendor = (await H.Api.GetAsync($"/api/v1/production/vendor-context?woId={s.Wo.Id}&seq={s.Emb}", H.AdminToken)).Ok().As<VendorContextDto>();
        Assert.All(noVendor.Columns, c => Assert.Equal(0, c.MaxReceive));
        Assert.Contains("Internal", (await H.Api.GetAsync($"/api/v1/production/vendor-context?woId={s.Wo.Id}&seq={s.Cut}", H.AdminToken)).Ok().As<VendorContextDto>().BlockedReason);

        var detail = (await H.Api.GetAsync($"/api/v1/work-orders/{s.Wo.Id}", H.AdminToken)).Ok().As<WoDto>();
        Assert.Equal(35, detail.OutstandingAtVendorQty);
    }

    [Fact]
    public async Task Hanya_Terima_memicu_InProgress_Kirim_tidak()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await using (var db = H.CreateDbContext())
        {
            // Output proses pertama dicatat langsung agar rantai terpenuhi tanpa mengubah status WO.
            var entry = new Gms.Domain.Production.OutputEntry { WoId = s.Wo.Id, Seq = s.Cut, WorkerId = w.Id, WorkDate = Today(), CreatedAt = DateTimeOffset.UtcNow };
            entry.Lines.Add(new Gms.Domain.Production.OutputEntryLine { WoLineId = s.LineS, Qty = 10 });
            db.OutputEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        await H.SendAsync(s, v.Id, (s.LineS, 10));
        Assert.Equal(WoStatus.Released, (await H.GetWoAsync(s.Wo.Id)).Status);

        await H.ReceiveAsync(s, v.Id, (s.LineS, 4));
        Assert.Equal(WoStatus.InProgress, (await H.GetWoAsync(s.Wo.Id)).Status);
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(s.Po.Id)).Status);
    }

    [Fact]
    public async Task Validasi_vendor_nonaktif_langkah_tanggal_dokumen_dan_status_WO()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        var inactive = await H.NewVendorAsync();
        await H.DeactivateVendorAsync(inactive.Id);
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));

        Assert.Contains("vendorId", ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, inactive.Id, [(s.LineS, 1)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("vendorId", ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, Guid.NewGuid(), [(s.LineS, 1)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("date", ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, v.Id, [(s.LineS, 1)], date: Today(1)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("docNo", ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, v.Id, [(s.LineS, 1)], docNo: new string('x', 101)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("cells", ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, v.Id, [(s.LineS, 0)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        var internalStep = await H.Api.PostAsync("/api/v1/production/vendor-movements",
            new SaveVendorMovementRequest(s.Wo.Id, s.Cut, v.Id, VendorMovementKind.Send, Today(), null, null, Cells((s.LineS, 1)).ToList()), H.AdminToken);
        Assert.Contains("seq", ProblemAssert.Is(internalStep, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        (await H.PostVendorAsync(s, VendorMovementKind.Send, v.Id, [(s.LineS, 2)], date: Today(-400))).Ok("tanggal lampau tidak dipengaruhi periode gaji");
        await H.WoDoAsync(s.Wo.Id, "complete");
        ProblemAssert.Is(await H.PostVendorAsync(s, VendorMovementKind.Send, v.Id, [(s.LineS, 1)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Void_kirim_yang_sebagian_sudah_diterima_dan_void_terima_yang_sudah_dipakai_ditolak()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        var send = await H.SendAsync(s, v.Id, (s.LineS, 30));
        var receive = await H.ReceiveAsync(s, v.Id, (s.LineS, 20));
        await H.OutAsync(s, s.Sew, w.Id, (s.LineS, 15));

        var voidSend = await H.VoidAsync(send, vendor: true);
        Assert.Equal(ProblemCodes.VendorReceivedExceeded, ProblemAssert.Is(voidSend, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var preview = await H.PreviewVoidAsync(receive, vendor: true);
        Assert.False(preview.CanProceed);
        Assert.Contains(preview.Violations, x => x.Code == ProblemCodes.OutputBelowDownstream);
        var voidReceive = await H.VoidAsync(receive, vendor: true);
        Assert.Equal(ProblemCodes.OutputBelowDownstream, ProblemAssert.Is(voidReceive, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        await H.AssertLedgerInvariantAsync(s);
        Assert.Equal((20, 30), await H.BalanceAsync(s.LineS, s.Emb));
    }

    [Fact]
    public async Task Void_vendor_yang_aman_berhasil_wajib_alasan_dan_tidak_dapat_diulang()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        var send = await H.SendAsync(s, v.Id, (s.LineS, 10));

        Assert.Contains("reason", ProblemAssert.Is(await H.VoidAsync(send, " ", vendor: true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(HttpStatusCode.NoContent, (await H.VoidAsync(send, "salah vendor", vendor: true)).Status);
        Assert.Equal((0, 0), await H.BalanceAsync(s.LineS, s.Emb));
        ProblemAssert.Is(await H.VoidAsync(send, vendor: true), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.VoidAsync(Guid.NewGuid(), vendor: true), HttpStatusCode.NotFound, ProblemTypes.NotFound);

        await using var db = H.CreateDbContext();
        Assert.Contains("VendorMovementVoided", await db.AuditLogs.Where(a => a.TargetId == send.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Koreksi_vendor_mengganti_vendor_dan_qty_secara_atomik_dan_menolak_kondisi_akhir_yang_melanggar()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var wrong = await H.NewVendorAsync();
        var right = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 30));
        var send = await H.SendAsync(s, wrong.Id, (s.LineS, 30)); // memakai seluruh output
        string Url(string a) => $"/api/v1/production/vendor-movements/{send}/{a}";

        var response = await H.Api.PostAsync(Url("correct"), new CorrectVendorMovementRequest("vendor salah", right.Id, Today(), "DOC-9", null, Cells((s.LineS, 30)).ToList()), H.AdminToken);
        var newId = System.Text.Json.JsonDocument.Parse(response.Ok().Body).RootElement.GetProperty("id").GetGuid();

        var detail = await H.GetEntryAsync(newId);
        Assert.Equal((EntryKind.VendorSend, right.Id, "DOC-9", send), (detail.Item.Kind, detail.Item.VendorId!.Value, detail.DocNo, detail.Item.ReplacesId!.Value));
        Assert.Equal(DocStatus.Voided, (await H.GetEntryAsync(send)).Item.Status);

        var tooMuch = await H.Api.PostAsync($"/api/v1/production/vendor-movements/{newId}/correct",
            new CorrectVendorMovementRequest("kebanyakan", right.Id, Today(), null, null, Cells((s.LineS, 31)).ToList()), H.AdminToken);
        Assert.Equal(ProblemCodes.VendorSentExceeded, ProblemAssert.Is(tooMuch, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        var previewBad = (await H.Api.PostAsync($"/api/v1/production/vendor-movements/{newId}/correct/preview",
            new CorrectVendorMovementRequest("kebanyakan", right.Id, Today(), null, null, Cells((s.LineS, 31)).ToList()), H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.False(previewBad.CanProceed);
        Assert.Equal(DocStatus.Active, (await H.GetEntryAsync(newId)).Item.Status);
        await H.AssertLedgerInvariantAsync(s);
    }

    // ===== Riwayat gabungan =====

    [Fact]
    public async Task Riwayat_menggabungkan_output_penyesuaian_kirim_dan_terima_dengan_filter()
    {
        var host = await fixture.CreateIsolatedAsync();
        var s = await host.NewProdSetupAsync();
        var w1 = await host.NewWorkerAsync("Budi Santoso");
        var w2 = await host.NewWorkerAsync("Siti Aminah");
        var v = await host.NewVendorAsync("CV Bordir Jaya");
        var cut1 = await host.OutAsync(s, s.Cut, w1.Id, (s.LineS, 30));
        await host.OutAsync(s, s.Cut, w2.Id, (s.LineM, 10));
        await host.SendAsync(s, v.Id, (s.LineS, 20));
        await host.ReceiveAsync(s, v.Id, (s.LineS, 10));
        await host.VoidAsync(await host.OutAsync(s, s.Cut, w2.Id, (s.LineM, 5)));
        await host.InsertPeriodAsync(Today(-30), Today(-20));
        await host.Api.PostAsync("/api/v1/production/adjustments",
            new AdjustmentRequest(s.Wo.Id, s.Cut, w1.Id, Today(-25), Today(), "koreksi lama", Cells((s.LineS, 2)).ToList()), host.AdminToken);

        var active = await host.ListEntriesAsync();
        Assert.Equal(5, active.TotalCount); // 2 output + kirim + terima + penyesuaian; void tidak termasuk
        Assert.Equal([EntryKind.Adjustment, EntryKind.VendorReceive, EntryKind.VendorSend], active.Items.Take(3).Select(i => i.Kind));
        Assert.True(active.Items.SequenceEqual(active.Items.OrderByDescending(i => i.CreatedAt)));

        var all = await host.ListEntriesAsync("&includeVoided=true");
        Assert.Equal(6, all.TotalCount);
        Assert.Single(all.Items, i => i.Status == DocStatus.Voided);
        Assert.Equal(1, (await host.ListEntriesAsync("&status=Voided")).TotalCount);

        Assert.Equal(2, (await host.ListEntriesAsync("&kind=Output")).TotalCount);
        Assert.Equal(1, (await host.ListEntriesAsync("&kind=VendorSend")).TotalCount);
        Assert.Equal(2, (await host.ListEntriesAsync($"&workerId={w1.Id}")).TotalCount);
        Assert.Equal(2, (await host.ListEntriesAsync($"&vendorId={v.Id}")).TotalCount);
        Assert.Equal(2, (await host.ListEntriesAsync($"&seq={s.Emb}")).TotalCount);
        Assert.Equal(0, (await host.ListEntriesAsync($"&from={Today(-25)}&to={Today(-1)}")).TotalCount);
        Assert.Equal(5, (await host.ListEntriesAsync($"&from={Today()}&to={Today()}")).TotalCount);
        Assert.Equal(1, (await host.ListEntriesAsync("&q=siti")).TotalCount);
        Assert.Equal(2, (await host.ListEntriesAsync("&q=bordir")).TotalCount);
        Assert.Equal(5, (await host.ListEntriesAsync($"&q={s.Wo.Code}")).TotalCount);
        Assert.Equal(5, (await host.ListEntriesAsync("&hariIni=true")).TotalCount);

        var page = (await host.Api.GetAsync("/api/v1/production/entries?pageSize=2&page=2", host.AdminToken)).Ok().As<PagedResult<EntryListItem>>();
        Assert.Equal((5, 2, 2), (page.TotalCount, page.Page, page.Items.Count));

        var item = active.Items.Single(i => i.Id == cut1);
        Assert.Equal((s.Wo.Code, w1.Name, true, true), (item.WoCode, item.WorkerName, item.CanVoid, item.CanCorrect));
        Assert.False(active.Items.Single(i => i.Kind == EntryKind.Adjustment).CanCorrect);
    }

    [Fact]
    public async Task Riwayat_dipisah_berdasarkan_permission_lihat_output_dan_vendor()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
        var send = await H.SendAsync(s, v.Id, (s.LineS, 5));
        var outputOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.ProductionOutput.View)).Id);
        var vendorOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.VendorJob.View)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);

        Assert.All((await H.ListEntriesAsync($"&woId={s.Wo.Id}", outputOnly.Token)).Items, i => Assert.True(i.Kind is EntryKind.Output or EntryKind.Adjustment));
        Assert.All((await H.ListEntriesAsync($"&woId={s.Wo.Id}", vendorOnly.Token)).Items, i => Assert.True(i.Kind is EntryKind.VendorSend or EntryKind.VendorReceive));
        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/production/entries", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/production/entries/{send}", outputOnly.Token), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        Assert.True((await H.Api.GetAsync($"/api/v1/production/entries/{send}", vendorOnly.Token)).IsSuccess);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/production/entries/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Aksi_riwayat_dinonaktifkan_dengan_alasan_saat_WO_Completed()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
        await H.WoDoAsync(s.Wo.Id, "complete");

        var item = (await H.GetEntryAsync(id)).Item;

        Assert.False(item.CanVoid);
        Assert.False(item.CanCorrect);
        Assert.Contains("selesai", item.DisabledReason);
        ProblemAssert.Is(await H.VoidAsync(id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Respons_modul_tidak_pernah_memuat_tarif_atau_rupiah()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var id = await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));

        foreach (var path in new[] { $"/api/v1/production/entries/{id}", "/api/v1/production/entries?pageSize=5",
                     $"/api/v1/production/input-context?woId={s.Wo.Id}&seq={s.Cut}&workDate={Today()}" })
        {
            var body = (await H.Api.GetAsync(path, H.AdminToken)).Ok().Body;
            Assert.DoesNotContain("rate", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tarif", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ===== KONKURENSI (AC-PR-35) =====

    [Fact]
    public async Task Dua_penyimpanan_bersamaan_yang_bersama_sama_melebihi_batas_tepat_satu_lolos()
    {
        for (var round = 0; round < 5; round++)
        {
            var s = await H.NewProdSetupAsync();
            var w = await H.NewWorkerAsync();
            var v = await H.NewVendorAsync();
            await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
            await H.SendAsync(s, v.Id, (s.LineS, 10));
            await H.ReceiveAsync(s, v.Id, (s.LineS, 10));
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go() => Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostOutputsAsync(s, s.Sew, [Row(w.Id, (s.LineS, 7))]);
            });

            var tasks = new[] { Go(), Go() };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            Assert.Equal(ProblemCodes.OutputCumulativeExceeded, ProblemAssert.Is(results.Single(r => !r.IsSuccess), HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
            Assert.Equal((7, 0), await H.BalanceAsync(s.LineS, s.Sew));
            await H.AssertLedgerInvariantAsync(s);
        }
    }

    [Fact]
    public async Task Tiga_puluh_penyimpanan_paralel_pada_batas_20_tepat_20_lolos_tanpa_5xx()
    {
        var s = await H.NewProdSetupAsync();
        var w = await H.NewWorkerAsync();
        var v = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 20));
        await H.SendAsync(s, v.Id, (s.LineS, 20));
        await H.ReceiveAsync(s, v.Id, (s.LineS, 20));
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await H.PostOutputsAsync(s, s.Sew, [Row(w.Id, (s.LineS, 1))]);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(20, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal((20, 0), await H.BalanceAsync(s.LineS, s.Sew));
        await H.AssertLedgerInvariantAsync(s);
    }

    [Fact]
    public async Task Simpan_sew_bersamaan_dengan_void_terima_tidak_pernah_menghasilkan_rantai_rusak()
    {
        for (var round = 0; round < 5; round++)
        {
            var s = await H.NewProdSetupAsync();
            var w = await H.NewWorkerAsync();
            var v = await H.NewVendorAsync();
            await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 10));
            await H.SendAsync(s, v.Id, (s.LineS, 10));
            var receive = await H.ReceiveAsync(s, v.Id, (s.LineS, 10));
            var gate = new TaskCompletionSource();

            var save = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostOutputsAsync(s, s.Sew, [Row(w.Id, (s.LineS, 4))]);
            });
            var voiding = Task.Run(async () =>
            {
                await gate.Task;
                return await H.VoidAsync(receive, vendor: true);
            });
            gate.SetResult();
            var results = await Task.WhenAll(save, voiding);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            Assert.True(results.Count(r => r.IsSuccess) >= 1);
            await H.AssertLedgerInvariantAsync(s); // sew ≤ diterima, apa pun urutan yang menang
        }
    }

    [Fact]
    public async Task Complete_bersamaan_dengan_simpan_output_salah_satu_menang_dan_status_tetap_konsisten()
    {
        for (var round = 0; round < 4; round++)
        {
            var s = await H.NewProdSetupAsync();
            var w = await H.NewWorkerAsync();
            await H.OutAsync(s, s.Cut, w.Id, (s.LineS, 5));
            var current = await H.GetWoAsync(s.Wo.Id);
            var gate = new TaskCompletionSource();

            var save = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostOutputsAsync(s, s.Cut, [Row(w.Id, (s.LineS, 3))]);
            });
            var complete = Task.Run(async () =>
            {
                await gate.Task;
                return await H.WoActionAsync(s.Wo.Id, "complete", current);
            });
            gate.SetResult();
            var results = await Task.WhenAll(save, complete);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            var after = await H.GetWoAsync(s.Wo.Id);
            var cut = (await H.BalanceAsync(s.LineS, s.Cut)).Qty;
            if (results[1].IsSuccess)
            {
                Assert.Equal(WoStatus.Completed, after.Status);
                // Jika output tersimpan setelah Complete, itu pelanggaran: tidak boleh terjadi.
                Assert.True(cut is 5 or 8);
                if (!results[0].IsSuccess)
                {
                    Assert.Equal(5, cut);
                }
            }

            await H.AssertLedgerInvariantAsync(s);
        }
    }
}
