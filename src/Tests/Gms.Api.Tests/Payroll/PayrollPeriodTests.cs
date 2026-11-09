using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Payroll;

/// <summary>Pembuatan, urutan, ubah, dan penutupan periode gaji (FSD Payroll 4.1, 4.3). Host terisolasi: periode bersifat global.</summary>
[Collection(ApiCollection.Name)]
public sealed class PayrollPeriodTests(ApiFixture fixture)
{
    private async Task<(TestHost Host, ProdSetup Setup, Gms.Shared.Master.WorkerDto W1, Gms.Shared.Master.WorkerDto W2)> WorldAsync(decimal baseRate = 1000m)
    {
        var host = await fixture.CreateIsolatedAsync();
        var s = await host.NewRatedSetupAsync(baseRate);
        return (host, s, await host.NewWorkerAsync(), await host.NewWorkerAsync());
    }

    // ===== Pembuatan =====

    [Fact]
    public async Task Periode_pertama_bebas_tanggal_mulai_kode_dari_rentang_dan_tidak_boleh_lebih_akhir_dari_output_tertua()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-5), (s.LineS, 10));
        await h.CutAsync(s, w1.Id, Today(-2), (s.LineS, 10));

        var defaults = (await h.Api.GetAsync("/api/v1/payroll/periods/next-defaults", h.AdminToken)).Ok().As<NextPeriodDefaultsDto>();
        Assert.Equal((Today(-5), false, Today(-5), false), (defaults.StartDate, defaults.StartLocked, defaults.OldestOutputDate, defaults.HasOpenPeriod));

        var late = await h.CreatePeriodAsync(Today(-4), Today(-1));
        Assert.Equal(ProblemCodes.PayrollStartAfterOldestOutput, ProblemAssert.Is(late, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Contains("startDate", ProblemAssert.Is(await h.CreatePeriodAsync(null, Today(-1)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("endDate", ProblemAssert.Is(await h.CreatePeriodAsync(Today(-7), Today(-8)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("notes", ProblemAssert.Is(await h.CreatePeriodAsync(Today(-7), Today(-1), new string('x', 501)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var period = await h.NewPeriodAsync(Today(-7), Today(-1));
        Assert.Equal($"GJ-{Today(-7):yyyyMMdd}-{Today(-1):yyyyMMdd}", period.Code);
        Assert.Equal((PayrollStatus.Open, true, true, true), (period.Status, period.IsProvisional, period.CanClose, period.CanEdit));
        Assert.Single((await h.SummaryAsync(period.Id)).Rows); // hanya pekerja w1 yang punya output pada periode
        await using var db = h.CreateDbContext();
        Assert.Contains("PayrollPeriodCreated", await db.AuditLogs.Where(a => a.TargetId == period.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Periode_berikutnya_berurutan_tanpa_celah_hanya_satu_Open_dan_tanggal_mulai_terkunci()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-9), (s.LineS, 1));
        var first = await h.NewPeriodAsync(Today(-10), Today(-6));

        ProblemAssert.Is(await h.CreatePeriodAsync(Today(-5), Today(-1)), HttpStatusCode.Conflict, ProblemTypes.RuleViolation); // masih Open
        Assert.True((await h.Api.GetAsync("/api/v1/payroll/periods/next-defaults", h.AdminToken)).Ok().As<NextPeriodDefaultsDto>().HasOpenPeriod);

        await h.ClosePeriodAsync(first.Id);
        var defaults = (await h.Api.GetAsync("/api/v1/payroll/periods/next-defaults", h.AdminToken)).Ok().As<NextPeriodDefaultsDto>();
        Assert.Equal((Today(-5), true, false), (defaults.StartDate, defaults.StartLocked, defaults.HasOpenPeriod));

        var gap = await h.CreatePeriodAsync(Today(-4), Today(-1));
        Assert.Equal(ProblemCodes.PayrollNotContiguous, ProblemAssert.Is(gap, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var second = (await h.CreatePeriodAsync(null, Today(-1))).Ok().As<PayrollPeriodDto>(); // tanpa tanggal mulai: otomatis
        Assert.Equal(Today(-5), second.StartDate);
        ProblemAssert.Is(await h.CreatePeriodAsync(Today(-5), Today(0)), HttpStatusCode.Conflict, ProblemTypes.RuleViolation); // sudah ada Open lagi

        var list = (await h.Api.GetAsync("/api/v1/payroll/periods", h.AdminToken)).Ok().As<List<PayrollPeriodListItem>>();
        Assert.Equal([second.Id, first.Id], list.Select(p => p.Id));
    }

    [Fact]
    public async Task Dua_pembuat_periode_bersamaan_tepat_satu_berhasil()
    {
        var (h, _, _, _) = await WorldAsync();
        var gate = new TaskCompletionSource();

        Task<ApiResponse> Go() => Task.Run(async () =>
        {
            await gate.Task;
            return await h.CreatePeriodAsync(Today(-10), Today(-4));
        });

        var tasks = new[] { Go(), Go(), Go() };
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
    }

    // ===== Ubah =====

    [Fact]
    public async Task Ubah_periode_Open_tanggal_selesai_catatan_dan_awal_periode_pertama_dengan_aturan_output_tertua()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-6), (s.LineS, 1));
        var period = await h.NewPeriodAsync(Today(-8), Today(-3));
        string Url() => $"/api/v1/payroll/periods/{period.Id}";

        var updated = (await h.Api.PutAsync(Url(), new SavePeriodRequest(null, Today(-1), "catatan baru", period.RowVersion), h.AdminToken)).Ok().As<PayrollPeriodDto>();
        Assert.Equal((Today(-1), "catatan baru", $"GJ-{Today(-8):yyyyMMdd}-{Today(-1):yyyyMMdd}"), (updated.EndDate, updated.Notes, updated.Code));

        var earlier = (await h.Api.PutAsync(Url(), new SavePeriodRequest(Today(-9), Today(-1), null, updated.RowVersion), h.AdminToken)).Ok().As<PayrollPeriodDto>();
        Assert.Equal(Today(-9), earlier.StartDate);

        var afterOldest = await h.Api.PutAsync(Url(), new SavePeriodRequest(Today(-5), Today(-1), null, earlier.RowVersion), h.AdminToken);
        Assert.Equal(ProblemCodes.PayrollStartAfterOldestOutput, ProblemAssert.Is(afterOldest, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Contains("endDate", ProblemAssert.Is(await h.Api.PutAsync(Url(), new SavePeriodRequest(null, Today(-10), null, earlier.RowVersion), h.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        ProblemAssert.Is(await h.Api.PutAsync(Url(), new SavePeriodRequest(null, Today(-2), "basi", updated.RowVersion), h.AdminToken), HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
        ProblemAssert.Is(await h.Api.PutAsync($"/api/v1/payroll/periods/{Guid.NewGuid()}", new SavePeriodRequest(null, Today(-2), null, 0), h.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Awal_periode_bukan_pertama_tidak_dapat_diubah_dan_periode_Closed_tidak_dapat_diubah()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-9), (s.LineS, 1));
        var first = await h.NewPeriodAsync(Today(-10), Today(-6));
        var firstClosed = await h.ClosePeriodAsync(first.Id);
        var second = await h.NewPeriodAsync(null, Today(-1));

        var move = await h.Api.PutAsync($"/api/v1/payroll/periods/{second.Id}", new SavePeriodRequest(Today(-4), Today(-1), null, second.RowVersion), h.AdminToken);
        Assert.Equal(ProblemCodes.PayrollNotContiguous, ProblemAssert.Is(move, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var closedEdit = await h.Api.PutAsync($"/api/v1/payroll/periods/{first.Id}", new SavePeriodRequest(null, Today(-5), null, firstClosed.RowVersion), h.AdminToken);
        ProblemAssert.Is(closedEdit, HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.False(firstClosed.CanEdit);
    }

    // ===== Penutupan =====

    [Fact]
    public async Task Pratinjau_tutup_menampilkan_pekerja_total_negatif_dan_tanggal_selesai_di_masa_depan_serta_kode_konfirmasi()
    {
        var (h, s, w1, w2) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        await h.CutAsync(s, w2.Id, Today(-3), (s.LineM, 4));
        var period = await h.NewPeriodAsync(Today(-4), Today(2)); // selesai di masa depan

        var preview = (await h.Api.PostAsync($"/api/v1/payroll/periods/{period.Id}/close/preview", new { }, h.AdminToken)).Ok().As<ClosePreviewDto>();

        Assert.Equal((2, 10 * 1000m + 4 * 1001m, true, period.Code), (preview.WorkerCount, preview.TotalPaid, preview.EndDateInFuture, preview.ConfirmationCode));
        Assert.Empty(preview.NegativeWorkers);
    }

    [Fact]
    public async Task Tutup_wajib_kode_konfirmasi_membekukan_snapshot_atomik_dan_permanen()
    {
        var (h, s, w1, w2) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10), (s.LineM, 5));
        await h.CutAsync(s, w2.Id, Today(-2), (s.LineS, 20));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        var live = await h.SummaryAsync(period.Id);
        Assert.True(live.IsProvisional);

        var wrongCode = await h.TryClosePeriodAsync(period.Id, "SALAH");
        Assert.Contains("confirmationCode", ProblemAssert.Is(wrongCode, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(PayrollStatus.Open, (await h.GetPeriodAsync(period.Id)).Status);

        var closed = await h.ClosePeriodAsync(period.Id);

        Assert.Equal((PayrollStatus.Closed, false, false, false), (closed.Status, closed.IsProvisional, closed.CanClose, closed.CanEdit));
        Assert.Equal((2, live.TotalPaid), (closed.WorkerCount, closed.TotalPaid));
        Assert.NotNull(closed.ClosedAt);
        Assert.False(string.IsNullOrEmpty(closed.ClosedByName));
        var frozen = await h.SummaryAsync(period.Id);
        Assert.False(frozen.IsProvisional);
        Assert.Equal(live.Rows.Select(r => (r.WorkerCode, r.TotalQty, r.SumBeforeRounding, r.Paid)), frozen.Rows.Select(r => (r.WorkerCode, r.TotalQty, r.SumBeforeRounding, r.Paid)));

        ProblemAssert.Is(await h.TryClosePeriodAsync(period.Id), HttpStatusCode.Conflict, ProblemTypes.StateConflict); // permanen
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/payroll/periods/{period.Id}/close/preview", new { }, h.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        await using var db = h.CreateDbContext();
        Assert.Contains("PayrollPeriodClosed", await db.AuditLogs.Where(a => a.TargetId == period.Id.ToString()).Select(a => a.EventType).ToListAsync());
        Assert.Equal(3, await db.PayrollSnapshotLines.CountAsync(l => l.PeriodId == period.Id));
        Assert.Equal(2, await db.PayrollSnapshotWorkers.CountAsync(w => w.PeriodId == period.Id));
    }

    [Fact]
    public async Task Rekap_Closed_tidak_berubah_walau_data_output_berubah_setelahnya_AC_PY_18()
    {
        var (h, s, w1, _) = await WorldAsync();
        var entry = await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        await h.ClosePeriodAsync(period.Id);
        var before = await h.SummaryAsync(period.Id);

        await using (var db = h.CreateDbContext()) // simulasi perubahan data (void langsung di basis data) setelah penutupan
        {
            await db.OutputEntries.Where(e => e.Id == entry).ExecuteUpdateAsync(e => e.SetProperty(x => x.Status, DocStatus.Voided).SetProperty(x => x.VoidReason, "uji"));
        }

        var after = await h.SummaryAsync(period.Id);
        Assert.Equal(before.TotalPaid, after.TotalPaid);
        Assert.Equal(10000m, after.TotalPaid);
    }

    [Fact]
    public async Task Periode_Closed_mengunci_input_void_dan_koreksi_output_pada_rentangnya_AC_PY_19()
    {
        var (h, s, w1, _) = await WorldAsync();
        var entry = await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        await h.ClosePeriodAsync(period.Id);

        ProblemAssert.Is(await h.PostOutputsAsync(s, s.Cut, [ProdHelpers.Row(w1.Id, (s.LineS, 1))], Today(-3)), HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        ProblemAssert.Is(await h.VoidAsync(entry), HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/production/entries/{entry}/correct",
            new Gms.Shared.Production.CorrectOutputRequest("x", w1.Id, Today(), ProdHelpers.Cells((s.LineS, 5)).ToList()), h.AdminToken), HttpStatusCode.Conflict, ProblemTypes.PeriodClosed);
        // Tanggal sebelum periode pertama juga ditolak (AC-PY-06 / AC-PR-10a); tanggal setelah periode tetap boleh (E4).
        var beforeFirst = await h.PostOutputsAsync(s, s.Cut, [ProdHelpers.Row(w1.Id, (s.LineS, 1))], Today(-6));
        Assert.Equal(ProblemCodes.PayrollDateBeforeFirstPeriod, ProblemAssert.Is(beforeFirst, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        (await h.PostOutputsAsync(s, s.Cut, [ProdHelpers.Row(w1.Id, (s.LineS, 1))], Today(0))).Ok("setelah periode tertutup");
    }

    [Fact]
    public async Task Tarif_snapshot_hilang_menolak_pratinjau_dan_tutup_dengan_daftar_baris_dan_periode_tetap_Open_E14()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        await using (var db = h.CreateDbContext())
        {
            await db.WoTariffSnapshots.Where(t => t.WoId == s.Wo.Id && t.Seq == s.Cut && t.SizeId == s.World.Small.Id).ExecuteDeleteAsync();
        }

        var preview = await h.Api.PostAsync($"/api/v1/payroll/periods/{period.Id}/close/preview", new { }, h.AdminToken);
        var problem = ProblemAssert.Is(preview, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.PayrollTariffMissing, problem.Code);
        Assert.Contains(s.Wo.Code, problem.Violations![0].Subject);

        Assert.Equal(ProblemCodes.PayrollTariffMissing, ProblemAssert.Is(await h.TryClosePeriodAsync(period.Id), HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Equal(PayrollStatus.Open, (await h.GetPeriodAsync(period.Id)).Status);
        await using var verify = h.CreateDbContext();
        Assert.Equal(0, await verify.PayrollSnapshotLines.CountAsync(l => l.PeriodId == period.Id)); // atomik: tidak ada snapshot setengah jadi
    }

    // ===== Konkurensi (AC-PY-22) =====

    [Fact]
    public async Task Dua_penutup_bersamaan_tepat_satu_berhasil_dan_snapshot_tidak_ganda()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        var gate = new TaskCompletionSource();

        Task<ApiResponse> Go() => Task.Run(async () =>
        {
            await gate.Task;
            return await h.Api.PostAsync($"/api/v1/payroll/periods/{period.Id}/close", new ClosePeriodRequest(period.Code, period.RowVersion), h.AdminToken);
        });

        var tasks = new[] { Go(), Go(), Go() };
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        await using var db = h.CreateDbContext();
        Assert.Equal(1, await db.PayrollSnapshotLines.CountAsync(l => l.PeriodId == period.Id));
        Assert.Equal(1, await db.PayrollSnapshotWorkers.CountAsync(w => w.PeriodId == period.Id));
    }

    [Fact]
    public async Task Tutup_bersamaan_dengan_input_output_tidak_pernah_menghasilkan_entri_yang_lolos_dari_snapshot()
    {
        for (var round = 0; round < 4; round++)
        {
            var (h, s, w1, w2) = await WorldAsync();
            await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
            var period = await h.NewPeriodAsync(Today(-4), Today(-1));
            var gate = new TaskCompletionSource();

            var close = Task.Run(async () =>
            {
                await gate.Task;
                return await h.TryClosePeriodAsync(period.Id);
            });
            var inputs = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                await gate.Task;
                return await h.PostOutputsAsync(s, s.Cut, [ProdHelpers.Row(w2.Id, (s.LineM, 7))], Today(-2));
            })).ToList();
            gate.SetResult();
            var closeResult = await close;
            var inputResults = await Task.WhenAll(inputs);

            Assert.True(closeResult.IsSuccess, closeResult.Body);
            Assert.DoesNotContain(inputResults, r => (int)r.Status >= 500);
            Assert.All(inputResults.Where(r => !r.IsSuccess), r => ProblemAssert.Is(r, HttpStatusCode.Conflict, ProblemTypes.PeriodClosed));

            // Setiap output tanggal -2 yang tersimpan HARUS ada di snapshot; tidak boleh ada yang lolos setelah penutupan.
            await using var db = h.CreateDbContext();
            var storedQty = await db.OutputEntryLines.Where(l => db.OutputEntries.Any(e => e.Id == l.EntryId && e.WorkerId == w2.Id && e.WorkDate == Today(-2) && e.Status == DocStatus.Active)).SumAsync(l => (int?)l.Qty) ?? 0;
            var snapshotQty = await db.PayrollSnapshotLines.Where(l => l.PeriodId == period.Id && l.WorkerId == w2.Id).SumAsync(l => (int?)l.Qty) ?? 0;
            Assert.Equal(storedQty, snapshotQty);
            Assert.Equal(7 * inputResults.Count(r => r.IsSuccess), storedQty);
        }
    }

    // ===== Permission =====

    [Fact]
    public async Task Permission_dipisah_untuk_lihat_buat_dan_tutup_periode()
    {
        var (h, s, w1, _) = await WorldAsync();
        await h.CutAsync(s, w1.Id, Today(-3), (s.LineS, 10));
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        var viewer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Payroll.View)).Id);
        var creator = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Payroll.View, Permissions.Payroll.CreatePeriod)).Id);
        var none = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);

        ProblemAssert.Is(await h.Api.GetAsync("/api/v1/payroll/periods", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        foreach (var path in new[] { "summary", "daily", "matrix", "adjustments" })
        {
            ProblemAssert.Is(await h.GetPayrollAsync(period.Id, path, none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        }

        var seen = await h.GetPeriodAsync(period.Id, viewer.Token);
        Assert.False(seen.CanClose);
        Assert.False(seen.CanEdit);
        ProblemAssert.Is(await h.CreatePeriodAsync(null, Today(0), token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await h.Api.GetAsync("/api/v1/payroll/periods/next-defaults", viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await h.Api.PutAsync($"/api/v1/payroll/periods/{period.Id}", new SavePeriodRequest(null, Today(-1), "ok", period.RowVersion), creator.Token)).IsSuccess);
        ProblemAssert.Is(await h.TryClosePeriodAsync(period.Id, token: creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/payroll/periods/{period.Id}/close/preview", new { }, creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }
}
