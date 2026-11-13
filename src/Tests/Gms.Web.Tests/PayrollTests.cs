using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Payroll;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockPayrollTests
{
    private readonly ProductionRig _rig = new();

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    private static readonly DateOnly D1 = new(2026, 11, 1);
    private static readonly DateOnly D7 = new(2026, 11, 7);

    private Task<SaveOutputsResult> Output(DateOnly date, int qty, int worker = 0, int line = 0) =>
        _rig.Production.SaveOutputsAsync(new SaveOutputsRequest(ProductionRig.WoId, 1, date, [new OutputRow(_rig.Worker(worker), [new WoLineQty(_rig.Line(line), qty)])], ConfirmExceedWoQty: true));

    [Fact]
    public async Task Periode_pertama_bebas_berikutnya_berurutan_dan_hanya_satu_Open()
    {
        var defaults = await _rig.Payroll.GetNextPeriodDefaultsAsync();
        Assert.Equal((null, false, false), (defaults.StartDate, defaults.StartLocked, defaults.HasOpenPeriod));

        Assert.Contains("startDate", (await Fails(() => _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(null, D7, null)))).Problem.Errors!.Keys);
        var first = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));
        Assert.Equal("GJ-20261101-20261107", first.Code);
        Assert.Equal(ProblemCodes.PayrollOpenPeriodExists, (await Fails(() => _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(null, D7.AddDays(7), null)))).Problem.Code);

        await Output(D1, 1); // butuh output untuk menutup tanpa galat tarif
        var preview = await _rig.Payroll.PreviewClosePeriodAsync(first.Id);
        await _rig.Payroll.ClosePeriodAsync(first.Id, new ClosePeriodRequest(preview.ConfirmationCode, first.RowVersion));
        var next = await _rig.Payroll.GetNextPeriodDefaultsAsync();
        Assert.Equal((D7.AddDays(1), true), (next.StartDate, next.StartLocked));
        Assert.Equal(ProblemCodes.PayrollNotContiguous, (await Fails(() => _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D7.AddDays(3), D7.AddDays(9), null)))).Problem.Code);
        Assert.Contains("endDate", (await Fails(() => _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(null, D7, null)))).Problem.Errors!.Keys);
        Assert.Equal("GJ-20261108-20261114", (await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(null, D7.AddDays(7), null))).Code);
    }

    [Fact]
    public async Task Periode_pertama_tidak_boleh_dimulai_setelah_output_tertua()
    {
        await Output(new DateOnly(2026, 11, 3), 5);

        var late = await Fails(() => _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(new DateOnly(2026, 11, 5), D7, null)));

        Assert.Equal(ProblemCodes.PayrollStartAfterOldestOutput, late.Problem.Code);
        Assert.Equal(new DateOnly(2026, 11, 3), (await _rig.Payroll.GetNextPeriodDefaultsAsync()).OldestOutputDate);
    }

    [Fact]
    public async Task Rekap_menghitung_upah_dari_tarif_snapshot_dan_total_per_pekerja_dibulatkan()
    {
        await Output(new DateOnly(2026, 11, 2), 20, worker: 0);          // Cutting S: 20 × 350
        await Output(new DateOnly(2026, 11, 3), 30, worker: 1, line: 1); // Cutting M: 30 × 350
        var p = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));

        var summary = await _rig.Payroll.GetSummaryAsync(p.Id, null);

        Assert.True(summary.IsProvisional);
        Assert.Equal([(7000m, 20), (10500m, 30)], summary.Rows.Select(r => (r.Paid, r.TotalQty)));
        Assert.Equal((17500m, 2), (summary.TotalPaid, summary.WorkerCount));
        Assert.Single((await _rig.Payroll.GetSummaryAsync(p.Id, "dedi")).Rows);
        Assert.Equal(17500m, (await _rig.Payroll.GetPeriodAsync(p.Id)).TotalPaid);
    }

    [Fact]
    public async Task Detail_pekerja_matriks_harian_dan_rincian_per_hari()
    {
        await Output(new DateOnly(2026, 11, 2), 10);
        await Output(new DateOnly(2026, 11, 4), 15);
        var p = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));

        var detail = await _rig.Payroll.GetWorkerDetailAsync(p.Id, _rig.Worker(0));
        var group = Assert.Single(detail.ByWo);
        Assert.Equal(("Cutting", 25, 8750m), (group.ProcessName, group.Lines[0].Qty, group.Subtotal));
        Assert.Equal([3500m, 5250m], detail.ByDay.Select(d => d.Subtotal));
        var matrix = await _rig.Payroll.GetMatrixAsync(p.Id);
        Assert.Equal(7, matrix.Dates.Count);
        Assert.Equal([(new DateOnly(2026, 11, 2), 10), (new DateOnly(2026, 11, 4), 15)], matrix.Rows[0].Cells.Select(c => (c.Date, c.Qty)));
        Assert.Equal(2, matrix.DayTotals.Count);
        var daily = await _rig.Payroll.GetDailyAsync(p.Id, new PayrollDailyQuery { From = new DateOnly(2026, 11, 3) });
        Assert.Equal((1, 5250m), (daily.Rows.TotalCount, daily.Subtotal));
        Assert.Equal(404, (await Fails(() => _rig.Payroll.GetWorkerDetailAsync(p.Id, _rig.Worker(1)))).StatusCode);
    }

    [Fact]
    public async Task Tutup_butuh_kode_konfirmasi_membekukan_angka_dan_mengunci_tanggal_produksi()
    {
        await Output(new DateOnly(2026, 11, 2), 10);
        var p = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));
        var preview = await _rig.Payroll.PreviewClosePeriodAsync(p.Id);
        Assert.Equal(("GJ-20261101-20261107", 1, 3500m, true), (preview.ConfirmationCode, preview.WorkerCount, preview.TotalPaid, preview.EndDateInFuture == false));

        Assert.Contains("confirmationCode", (await Fails(() => _rig.Payroll.ClosePeriodAsync(p.Id, new ClosePeriodRequest("salah", p.RowVersion)))).Problem.Errors!.Keys);
        Assert.Equal(409, (await Fails(() => _rig.Payroll.ClosePeriodAsync(p.Id, new ClosePeriodRequest(preview.ConfirmationCode, p.RowVersion + 3)))).StatusCode);
        var closed = await _rig.Payroll.ClosePeriodAsync(p.Id, new ClosePeriodRequest(preview.ConfirmationCode, p.RowVersion));

        Assert.Equal((PayrollStatus.Closed, false, false), (closed.Status, closed.IsProvisional, closed.CanEdit));
        Assert.Equal(ProblemTypes.PeriodClosed, (await Fails(() => Output(new DateOnly(2026, 11, 5), 1))).Problem.Type); // tanggal terkunci
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _rig.Payroll.PreviewClosePeriodAsync(p.Id))).Problem.Type);
        Assert.Equal(3500m, (await _rig.Payroll.GetSummaryAsync(p.Id, null)).TotalPaid); // angka beku
    }

    [Fact]
    public async Task Penyesuaian_di_periode_berikutnya_menghasilkan_total_negatif_yang_ditandai_dan_masuk_tab_penyesuaian()
    {
        await Output(new DateOnly(2026, 11, 5), 10);
        var p1 = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));
        var preview = await _rig.Payroll.PreviewClosePeriodAsync(p1.Id);
        await _rig.Payroll.ClosePeriodAsync(p1.Id, new ClosePeriodRequest(preview.ConfirmationCode, p1.RowVersion));
        var p2 = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(null, D7.AddDays(7), null));

        await _rig.Production.CreateAdjustmentAsync(new AdjustmentRequest(ProductionRig.WoId, 1, _rig.Worker(0), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 9), "salah input", [new WoLineQty(_rig.Line(0), -5)]));

        var summary = await _rig.Payroll.GetSummaryAsync(p2.Id, null);
        var row = Assert.Single(summary.Rows);
        Assert.Equal((-1750m, true, -5), (row.Paid, row.IsNegative, row.TotalQty));
        Assert.Equal(-1750m, (await _rig.Payroll.GetPeriodAsync(p2.Id)).TotalPaid);
        var adjustment = Assert.Single(await _rig.Payroll.GetAdjustmentsAsync(p2.Id));
        Assert.Equal((OutputKind.Adjustment, new DateOnly(2026, 11, 5), "salah input"), (adjustment.Kind, adjustment.OriginalDate, adjustment.Reason));
        Assert.Single((await _rig.Payroll.PreviewClosePeriodAsync(p2.Id)).NegativeWorkers);
    }

    [Fact]
    public async Task Ubah_periode_Open_hanya_akhir_dan_catatan_dengan_rowVersion()
    {
        var p = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));

        var updated = await _rig.Payroll.UpdatePeriodAsync(p.Id, new SavePeriodRequest(null, D7.AddDays(2), "catatan", p.RowVersion));
        Assert.Equal(("GJ-20261101-20261109", "catatan"), (updated.Code, updated.Notes));
        Assert.Equal(409, (await Fails(() => _rig.Payroll.UpdatePeriodAsync(p.Id, new SavePeriodRequest(null, D7, null, p.RowVersion)))).StatusCode);
        Assert.Contains("endDate", (await Fails(() => _rig.Payroll.UpdatePeriodAsync(p.Id, new SavePeriodRequest(null, D1.AddDays(-1), null, updated.RowVersion)))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Ekspor_CSV_berstatus_BELUM_FINAL_untuk_Open_dan_nama_berkas_bertanda_dan_slip_404_tanpa_output()
    {
        await Output(new DateOnly(2026, 11, 2), 10);
        var p = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(D1, D7, null));

        var csv = await _rig.Payroll.ExportAsync(p.Id, PayrollExportFormat.CsvSummary);
        Assert.EndsWith("-ringkasan-BELUM-FINAL.csv", csv.FileName);
        var text = System.Text.Encoding.UTF8.GetString(csv.Bytes);
        Assert.StartsWith("﻿status,kode_pekerja", text);
        Assert.Contains("BELUM FINAL,PKJ-001,Wati,10,3500,3500", text);
        Assert.Contains("harian-BELUM-FINAL.csv", (await _rig.Payroll.ExportAsync(p.Id, PayrollExportFormat.CsvDaily)).FileName);
        Assert.EndsWith(".xlsx", (await _rig.Payroll.ExportAsync(p.Id, PayrollExportFormat.Xlsx)).FileName.Replace("-BELUM-FINAL", string.Empty));
        Assert.Equal(404, (await Fails(() => _rig.Payroll.GetSlipsAsync(p.Id, _rig.Worker(1), false))).StatusCode);
        Assert.Contains("BELUM FINAL", System.Text.Encoding.ASCII.GetString((await _rig.Payroll.GetSlipsAsync(p.Id, null, true)).Bytes));
    }
}

public sealed class PayrollPageTests : WebTestBase
{
    private static readonly string[] AllPay = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("Payroll.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly ProductionRig _rig = new();

    public PayrollPageTests()
    {
        Services.AddSingleton<IPayrollApi>(_rig.Payroll);
        Services.AddSingleton<FileDownloader>();
        SignIn(AllPay);
    }

    private static IElement Dialog<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => cut.FindAll(".gms-dialog-wrap").Last(d => d.QuerySelector("h2") is not null);

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => Dialog(cut).QuerySelectorAll("button.gms-btn").Last();

    private async Task<PayrollPeriodDto> SeedOpenPeriodAsync()
    {
        await _rig.Production.SaveOutputsAsync(new SaveOutputsRequest(ProductionRig.WoId, 1, new DateOnly(2026, 11, 2), [new OutputRow(_rig.Worker(0), [new WoLineQty(_rig.Line(0), 20)])], true));
        return await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), null));
    }

    [Fact]
    public void Daftar_kosong_menawarkan_Buat_Periode_dan_dialog_pintasan_mengisi_tanggal_selesai()
    {
        var cut = Render<PayrollPeriodsPage>();
        cut.WaitForAssertion(() => Assert.Contains("Belum ada periode gaji", cut.Markup));

        cut.FindAll(".gms-pageheader-actions button.gms-btn").Single(b => b.TextContent.Trim() == "Buat Periode").Click();
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        Dialog(cut).QuerySelectorAll("input[type=date]")[0].Input("2026-11-01");
        Dialog(cut).QuerySelectorAll("button.gms-btn").Single(b => b.TextContent.Trim() == "7 hari").Click();

        Assert.Equal("2026-11-07", Dialog(cut).QuerySelectorAll("input[type=date]")[1].GetAttribute("value"));
        Assert.False(DialogConfirm(cut).HasAttribute("disabled"));
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Matches("/payroll/[0-9a-f-]{36}$", Nav.Uri));
    }

    [Fact]
    public async Task Daftar_menandai_Open_sementara_dan_menonaktifkan_Buat_bila_ada_periode_Open()
    {
        await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodsPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("Belum final", cut.Find("tbody").TextContent);
        Assert.Contains("7.000 *", cut.Find("tbody").TextContent);
        Assert.Contains("BELUM FINAL", cut.Markup);
        Assert.True(cut.FindAll(".gms-pageheader-actions button.gms-btn").Single(b => b.TextContent.Trim() == "Buat Periode").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Detail_Open_menampilkan_banner_sementara_ringkasan_dan_total_periode()
    {
        var p = await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));
        Assert.Contains("Angka bersifat sementara", cut.Markup);
        Assert.Contains("PKJ-001 Wati", cut.Find("tbody").TextContent);
        Assert.Contains("7.000", cut.Find("tbody").TextContent);
        Assert.Contains("Total periode", cut.Find("tfoot").TextContent);
        Assert.Equal(["Ringkasan", "Rincian", "Rincian Harian", "Matriks Harian", "Penyesuaian"], cut.FindAll("[role=tab]").Select(t => t.TextContent));
    }

    [Fact]
    public async Task Klik_pekerja_membuka_Rincian_per_WO_dan_beralih_ke_per_hari()
    {
        var p = await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.Find("tbody a").Click();
        cut.WaitForAssertion(() => Assert.Contains("Dibayarkan Rp 7.000", cut.Markup));
        Assert.Contains("Cutting", cut.Markup);
        Assert.Contains("Rp 350", cut.Markup);
        cut.FindAll("input[name=detail-view]")[1].Change(true);

        Assert.Contains("20 × 350", cut.Markup);
    }

    [Fact]
    public async Task Tab_Harian_dan_Matriks_menampilkan_baris_sel_dan_toggle_tampilan()
    {
        var p = await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.FindAll("[role=tab]").Single(t => t.TextContent.Trim() == "Rincian Harian").Click();
        cut.WaitForAssertion(() => Assert.Contains("Cutting", cut.Find("tbody").TextContent));
        cut.FindAll("[role=tab]").Single(t => t.TextContent.Trim() == "Matriks Harian").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-daymatrix")));
        Assert.Equal(7, cut.FindAll(".gms-daymatrix thead th").Count - 2);
        Assert.Contains("20", cut.Find(".gms-daymatrix tbody").TextContent);
        cut.FindAll("input[name=matrix-mode]")[1].Change(true);
        Assert.Contains("7.000", cut.Find(".gms-daymatrix tbody").TextContent);
        cut.FindAll("[role=tab]").Single(t => t.TextContent.Trim() == "Penyesuaian").Click();
        cut.WaitForAssertion(() => Assert.Contains("Tidak ada penyesuaian pada periode ini", cut.Markup));
    }

    [Fact]
    public async Task Tutup_Periode_menampilkan_ringkasan_dan_baru_aktif_setelah_kode_diketik_benar()
    {
        var p = await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));
        cut.WaitForAssertion(() => Assert.Contains("Tutup Periode", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions button.gms-btn").Single(b => b.TextContent.Trim() == "Tutup Periode").Click();
        cut.WaitForAssertion(() => Assert.Contains("Penutupan PERMANEN", Dialog(cut).TextContent));
        Assert.Contains("Rp 7.000", Dialog(cut).TextContent);
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        Dialog(cut).QuerySelector("input")!.Input("GJ-salah");
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        Dialog(cut).QuerySelector("input")!.Input("GJ-20261101-20261107");
        Assert.False(DialogConfirm(cut).HasAttribute("disabled"));
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Periode ditutup pada", cut.Markup));
        Assert.DoesNotContain(cut.FindAll(".gms-pageheader-actions button.gms-btn"), b => b.TextContent is "Tutup Periode" or "Ubah");
        Assert.Equal("Closed", cut.Find(".gms-pageheader .gms-tag").TextContent.Trim());
    }

    [Fact]
    public async Task Ekspor_dan_Slip_memicu_unduhan_berkas()
    {
        var p = await SeedOpenPeriodAsync();
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.FindAll(".gms-pageheader-actions button.gms-btn").Single(b => b.TextContent.Trim() == "CSV Ringkasan").Click();
        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "save" && ((string)i.Arguments[0]!).EndsWith("-ringkasan-BELUM-FINAL.csv")));
        cut.FindAll(".gms-pageheader-actions button.gms-btn").Single(b => b.TextContent.Trim() == "Slip Gaji").Click();
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "save" && ((string)i.Arguments[0]!).StartsWith("slip-")));
    }

    [Fact]
    public async Task Tanpa_izin_Export_Slip_dan_Tutup_tombolnya_tidak_dirender()
    {
        var p = await SeedOpenPeriodAsync();
        SignIn(Permissions.Payroll.View);

        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, p.Id));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));
        Assert.Equal(["Segarkan"], cut.FindAll(".gms-pageheader-actions button.gms-btn").Select(b => b.TextContent.Trim()));
    }

    [Fact]
    public void Periode_tidak_ditemukan_menampilkan_state_galat()
    {
        var cut = Render<PayrollPeriodDetailPage>(x => x.Add(y => y.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => Assert.Contains("Data tidak ditemukan", cut.Markup));
    }
}
