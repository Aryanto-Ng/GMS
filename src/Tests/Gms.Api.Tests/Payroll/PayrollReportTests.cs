using System.Net;
using System.Text;
using ClosedXML.Excel;
using Gms.Api.Documents;
using Gms.Api.Features.Payroll;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Payroll;

/// <summary>Perhitungan upah, rekap, rincian, ekspor, dan slip gaji (FSD Payroll 2, 4.2, 4.5). Host terisolasi.</summary>
[Collection(ApiCollection.Name)]
public sealed class PayrollReportTests(ApiFixture fixture)
{
    // ===== Perhitungan murni =====

    [Theory]
    [InlineData("0.5", "1")]
    [InlineData("1.5", "2")]
    [InlineData("2.5", "3")]      // setengah ke atas, bukan pembulatan bankir
    [InlineData("-0.5", "-1")]    // menjauhi nol
    [InlineData("-2.5", "-3")]
    [InlineData("1000.49999", "1000")]
    [InlineData("1000.50001", "1001")]
    public void Pembulatan_setengah_ke_atas_menjauhi_nol(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected), PayrollCalculator.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Total_dibulatkan_sekali_per_pekerja_bukan_per_baris()
    {
        PayrollLine Line(Guid w, int qty, decimal rate) =>
            new(Guid.NewGuid(), new DateOnly(2026, 11, 1), w, "PKJ-1", "A", Guid.NewGuid(), "WO", "CUT", "Cutting", "S", 1, OutputKind.Output, null, null, qty, rate, false);
        var worker = Guid.NewGuid();
        // Tiga baris 0,4 → 1,2 → dibulatkan sekali = 1 (bukan 3 × round(0,4) = 0).
        var lines = Enumerable.Range(0, 3).Select(_ => Line(worker, 1, 0.4m)).ToList();

        var total = Assert.Single(PayrollCalculator.Totals(lines));

        Assert.Equal((3, 1.2m, 1m), (total.TotalQty, total.SumBeforeRounding, total.Paid));
    }

    // ===== Rekap Open =====

    private async Task<(TestHost H, ProdSetup S, Gms.Shared.Master.WorkerDto A, Gms.Shared.Master.WorkerDto B, Gms.Shared.Master.WorkerDto C, PayrollPeriodDto Period)> OpenWorldAsync()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(1000m);
        var a = await h.NewWorkerAsync("Ani Wulandari");
        var b = await h.NewWorkerAsync("Budi Santoso");
        var c = await h.NewWorkerAsync("Citra Tanpa Output");
        await h.CutAsync(s, a.Id, Today(-3), (s.LineS, 10), (s.LineM, 5));   // 10×1000 + 5×1001 = 15005
        await h.CutAsync(s, a.Id, Today(-2), (s.LineS, 6));                  //  6×1000 = 6000
        await h.CutAsync(s, b.Id, Today(-3), (s.LineS, 20));                 // 20000
        await h.CutAsync(s, b.Id, Today(-2), (s.LineM, 4));                  //  4×1001 = 4004
        await h.DeactivateWorkerAsync(b.Id);                                 // E8: tetap tampil
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        return (h, s, a, b, c, period);
    }

    [Fact]
    public async Task Rekap_Open_dihitung_langsung_dari_output_dan_tarif_snapshot_WO_dengan_pekerja_nonaktif_tetap_tampil()
    {
        var (h, _, a, b, c, period) = await OpenWorldAsync();

        var summary = await h.SummaryAsync(period.Id);

        Assert.True(summary.IsProvisional);
        Assert.Equal(2, summary.WorkerCount);
        Assert.DoesNotContain(summary.Rows, r => r.WorkerId == c.Id); // tanpa output tidak tampil
        var ani = summary.Rows.Single(r => r.WorkerId == a.Id);
        var budi = summary.Rows.Single(r => r.WorkerId == b.Id); // nonaktif tetap tampil
        Assert.Equal((21, 21005m, 21005m, false), (ani.TotalQty, ani.SumBeforeRounding, ani.Paid, ani.IsNegative));
        Assert.Equal((24, 24004m, 24004m), (budi.TotalQty, budi.SumBeforeRounding, budi.Paid));
        Assert.Equal(45009m, summary.TotalPaid);
        Assert.Equal([a.Code, b.Code], summary.Rows.Select(r => r.WorkerCode)); // urut kode pekerja

        Assert.Equal([a.Id], (await h.SummaryAsync(period.Id, "?q=wulan")).Rows.Select(r => r.WorkerId));
        Assert.Equal([b.Id], (await h.SummaryAsync(period.Id, $"?q={b.Code}")).Rows.Select(r => r.WorkerId));
        Assert.Empty((await h.SummaryAsync(period.Id, "?q=tidak-ada")).Rows);
    }

    [Fact]
    public async Task Rekap_hanya_memuat_output_aktif_dalam_rentang_tanggal_kerja_periode_dan_void_langsung_terlihat()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(1000m);
        var w = await h.NewWorkerAsync();
        var kept = await h.CutAsync(s, w.Id, Today(-3), (s.LineS, 10));
        var voided = await h.CutAsync(s, w.Id, Today(-3), (s.LineS, 4));
        await h.CutAsync(s, w.Id, Today(0), (s.LineS, 100)); // setelah akhir periode (output lebih awal dari periode pertama memang ditolak)
        var period = await h.NewPeriodAsync(Today(-4), Today(-1));
        Assert.Equal(14000m, (await h.SummaryAsync(period.Id)).TotalPaid);

        Assert.Equal(HttpStatusCode.NoContent, (await h.VoidAsync(voided)).Status);

        var after = await h.SummaryAsync(period.Id);
        Assert.Equal((10000m, 10), (after.TotalPaid, after.Rows.Single().TotalQty));
        Assert.NotEqual(Guid.Empty, kept);
    }

    [Fact]
    public async Task Pembulatan_sekali_per_pekerja_dengan_tarif_lima_desimal_dan_selisih_terlihat()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(0.5m); // CUT-S 0,5 ; CUT-M 1,5
        var w = await h.NewWorkerAsync();
        await h.CutAsync(s, w.Id, Today(-2), (s.LineS, 1));
        var period = await h.NewPeriodAsync(Today(-3), Today(-1));

        var row = (await h.SummaryAsync(period.Id)).Rows.Single();

        Assert.Equal((0.5m, 1m), (row.SumBeforeRounding, row.Paid)); // 0,5 → 1 (menjauhi nol)
    }

    // ===== Rincian =====

    [Fact]
    public async Task Rincian_pekerja_per_WO_proses_size_per_hari_dan_subtotal_harian()
    {
        var (h, s, a, _, _, period) = await OpenWorldAsync();

        var detail = (await h.GetPayrollAsync(period.Id, $"workers/{a.Id}/detail")).Ok().As<PayrollWorkerDetailDto>();

        Assert.Equal((a.Code, 21, 21005m, 21005m), (detail.WorkerCode, detail.TotalQty, detail.SumBeforeRounding, detail.Paid));
        var group = Assert.Single(detail.ByWo);
        Assert.Equal((s.Wo.Code, 21005m), (group.WoCode, group.Subtotal));
        Assert.Equal([(16, 1000m, 16000m), (5, 1001m, 5005m)], group.Lines.Select(l => (l.Qty, l.Rate, l.Subtotal))); // urut Size: S lalu M
        Assert.Equal([Today(-3), Today(-2)], detail.ByDay.Select(d => d.WorkDate));
        Assert.Equal([15005m, 6000m], detail.ByDay.Select(d => d.Subtotal));
        Assert.Equal(21005m, detail.ByDay.Sum(d => d.Subtotal));
        Assert.Empty(detail.Adjustments);
        ProblemAssert.Is(await h.GetPayrollAsync(period.Id, $"workers/{Guid.NewGuid()}/detail"), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Rincian_harian_periode_dengan_filter_paging_subtotal_dan_penelusuran_ke_entri_asal()
    {
        var (h, s, a, b, _, period) = await OpenWorldAsync();

        var all = (await h.GetPayrollAsync(period.Id, "daily?pageSize=2")).Ok().As<PayrollDailyResult>();
        Assert.Equal((5, 2, 45009m), (all.Rows.TotalCount, all.Rows.Items.Count, all.Subtotal));
        Assert.True(all.Rows.Items.SequenceEqual(all.Rows.Items.OrderBy(r => r.WorkDate)));

        var perWorker = (await h.GetPayrollAsync(period.Id, $"daily?workerId={b.Id}")).Ok().As<PayrollDailyResult>();
        Assert.Equal((2, 24004m), (perWorker.Rows.TotalCount, perWorker.Subtotal));
        var byDate = (await h.GetPayrollAsync(period.Id, $"daily?from={Today(-2)}&to={Today(-2)}")).Ok().As<PayrollDailyResult>();
        Assert.Equal((2, 10004m), (byDate.Rows.TotalCount, byDate.Subtotal));
        Assert.Equal(5, (await h.GetPayrollAsync(period.Id, $"daily?woId={s.Wo.Id}")).Ok().As<PayrollDailyResult>().Rows.TotalCount);
        Assert.Equal(0, (await h.GetPayrollAsync(period.Id, $"daily?woId={Guid.NewGuid()}")).Ok().As<PayrollDailyResult>().Rows.TotalCount);
        Assert.Equal(5, (await h.GetPayrollAsync(period.Id, $"daily?processCode={s.World.Fg.Cut.Code}")).Ok().As<PayrollDailyResult>().Rows.TotalCount);
        Assert.Equal(0, (await h.GetPayrollAsync(period.Id, "daily?processCode=TIDAK-ADA")).Ok().As<PayrollDailyResult>().Rows.TotalCount);
        Assert.Equal(2, (await h.GetPayrollAsync(period.Id, $"daily?q={a.Code}&from={Today(-3)}&to={Today(-3)}")).Ok().As<PayrollDailyResult>().Rows.TotalCount);

        var row = all.Rows.Items[0];
        var entry = (await h.Api.GetAsync($"/api/v1/production/entries/{row.SourceEntryId}", h.AdminToken)).Ok().As<EntryDetailDto>();
        Assert.Equal((row.WorkDate, row.WorkerId), (entry.Item.WorkDate, entry.Item.WorkerId!.Value)); // rujukan entri output asal
    }

    [Fact]
    public async Task Matriks_harian_pekerja_kali_tanggal_dengan_total_per_pekerja_dan_per_hari()
    {
        var (h, _, a, b, _, period) = await OpenWorldAsync();

        var matrix = (await h.GetPayrollAsync(period.Id, "matrix")).Ok().As<PayrollMatrixDto>();

        Assert.Equal(4, matrix.Dates.Count); // setiap tanggal pada periode, termasuk yang kosong
        Assert.Equal(Today(-4), matrix.Dates[0]);
        var ani = matrix.Rows.Single(r => r.WorkerId == a.Id);
        Assert.Equal((21, 21005m), (ani.TotalQty, ani.TotalAmount));
        Assert.Equal([Today(-3), Today(-2)], ani.Cells.Select(c => c.Date)); // hari tanpa output: tanpa sel
        Assert.Equal((15, 15005m), (ani.Cells[0].Qty, ani.Cells[0].Amount));
        Assert.Equal([Today(-3), Today(-2)], matrix.DayTotals.Select(d => d.Date));
        Assert.Equal((35, 35005m), (matrix.DayTotals[0].Qty, matrix.DayTotals[0].Amount)); // 15+20 ; 15005+20000
        Assert.Equal(45009m, matrix.Rows.Sum(r => r.TotalAmount));
        Assert.Equal(b.Id, matrix.Rows[1].WorkerId);
    }

    // ===== Penyesuaian =====

    [Fact]
    public async Task Penyesuaian_masuk_ke_periode_yang_memuat_tanggal_kerjanya_dengan_tanggal_asal_alasan_dan_total_negatif_apa_adanya()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(1000m);
        var w = await h.NewWorkerAsync();
        await h.CutAsync(s, w.Id, Today(-8), (s.LineS, 10));
        var p1 = await h.NewPeriodAsync(Today(-9), Today(-5));
        await h.ClosePeriodAsync(p1.Id);
        var p2 = await h.NewPeriodAsync(null, Today(0));

        // Kesalahan ditemukan di periode tertutup: koreksi −15 (melebihi 10 yang tercatat → total negatif pekerja pada periode ini).
        await h.CutAsync(s, w.Id, Today(-1), (s.LineS, 5));
        var adjust = await h.Api.PostAsync("/api/v1/production/adjustments",
            new AdjustmentRequest(s.Wo.Id, s.Cut, w.Id, Today(-8), Today(0), "kelebihan input", Cells((s.LineS, -9)).ToList()), h.AdminToken);
        adjust.Ok("penyesuaian");

        var summary = await h.SummaryAsync(p2.Id);
        var row = summary.Rows.Single();
        Assert.Equal((-4, -4000m, -4000m, true), (row.TotalQty, row.SumBeforeRounding, row.Paid, row.IsNegative)); // 5 − 9 = −4 pcs

        var adjustments = (await h.GetPayrollAsync(p2.Id, "adjustments")).Ok().As<List<PayrollDailyRow>>();
        var line = Assert.Single(adjustments);
        Assert.Equal((OutputKind.Adjustment, -9, -9000m, Today(-8), "kelebihan input"), (line.Kind, line.Qty, line.Subtotal, line.OriginalDate, line.Reason));
        Assert.Empty((await h.GetPayrollAsync(p1.Id, "adjustments")).Ok().As<List<PayrollDailyRow>>()); // periode asal tidak berubah
        Assert.Equal(10000m, (await h.SummaryAsync(p1.Id)).TotalPaid);

        var preview = (await h.Api.PostAsync($"/api/v1/payroll/periods/{p2.Id}/close/preview", new { }, h.AdminToken)).Ok().As<ClosePreviewDto>();
        Assert.Equal(w.Id, Assert.Single(preview.NegativeWorkers).WorkerId);
        var closed = await h.ClosePeriodAsync(p2.Id);
        Assert.Equal(-4000m, closed.TotalPaid); // ditampilkan apa adanya, tidak dibatasi nol
    }

    // ===== Ekspor =====

    [Fact]
    public async Task Ekspor_xlsx_memiliki_empat_sheet_angka_sebagai_angka_format_dan_penanda_BELUM_FINAL_untuk_Open()
    {
        var (h, _, a, _, _, period) = await OpenWorldAsync();

        var file = await h.DownloadPayrollAsync(period.Id, "export?format=xlsx");

        Assert.Equal(HttpStatusCode.OK, file.Status);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Contains("BELUM-FINAL", file.FileName);
        using var book = new XLWorkbook(new MemoryStream(file.Bytes));
        Assert.Equal(["Ringkasan", "Rincian", "Harian", "Penyesuaian"], book.Worksheets.Select(w => w.Name));
        var summary = book.Worksheet("Ringkasan");
        Assert.Contains("BELUM FINAL", summary.Cell("A1").GetString());
        Assert.Equal(a.Code, summary.Cell("A4").GetString());
        var qty = summary.Cell("C4");
        var sum = summary.Cell("D4");
        var paid = summary.Cell("E4");
        Assert.All(new[] { qty, sum, paid }, c => Assert.Equal(XLDataType.Number, c.DataType)); // angka, bukan teks
        Assert.Equal((21d, 21005d, 21005d), (qty.GetDouble(), sum.GetDouble(), paid.GetDouble()));
        Assert.Equal("#,##0", paid.Style.NumberFormat.Format);
        Assert.Equal(45009d, summary.Cell("E6").GetDouble()); // baris total

        var daily = book.Worksheet("Harian");
        Assert.Equal(XLDataType.DateTime, daily.Cell("A4").DataType);
        Assert.Equal("#,##0.#####", daily.Cell("I4").Style.NumberFormat.Format); // tarif hingga 5 desimal
        Assert.Equal(XLDataType.Number, daily.Cell("J4").DataType);
        Assert.Equal(5, daily.LastRowUsed()!.RowNumber() - 3);
        var detail = book.Worksheet("Rincian");
        Assert.Equal(XLDataType.Number, detail.Cell("G4").DataType);
    }

    [Fact]
    public async Task Ekspor_CSV_berisi_BOM_UTF8_titik_desimal_kolom_status_dan_mencegah_injeksi_rumus()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(0.5m);
        var evil = await h.NewWorkerAsync("=HYPERLINK(\"http://x\")");
        var comma = await h.NewWorkerAsync("Ahmad, S.Kom");
        await h.CutAsync(s, evil.Id, Today(-2), (s.LineS, 1));
        await h.CutAsync(s, comma.Id, Today(-2), (s.LineM, 3));
        var period = await h.NewPeriodAsync(Today(-3), Today(-1));

        var summary = await h.DownloadPayrollAsync(period.Id, "export?format=csv-summary");
        var daily = await h.DownloadPayrollAsync(period.Id, "export?format=csv-daily");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, summary.Bytes.Take(3).ToArray());
        Assert.Contains("BELUM-FINAL", summary.FileName);
        var text = Encoding.UTF8.GetString(summary.Bytes, 3, summary.Bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("kode_pekerja,nama,total_qty,jumlah_sebelum_pembulatan,dibayarkan,status", lines[0]);
        Assert.Contains(lines, l => l.Contains(",0.5,1,BELUM FINAL")); // titik desimal; 0,5 dibulatkan 1 hanya pada kolom dibayarkan
        Assert.Contains(lines, l => l.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"") || l.Contains("'=HYPERLINK")); // diawali apostrof
        Assert.Contains(lines, l => l.Contains("\"Ahmad, S.Kom\"")); // koma dikutip
        Assert.DoesNotContain(lines.Skip(1), l => l.StartsWith('='));

        var dailyText = Encoding.UTF8.GetString(daily.Bytes, 3, daily.Bytes.Length - 3);
        Assert.StartsWith("tanggal_kerja,kode_pekerja,nama,wo,proses,size,jenis,qty,tarif,subtotal,tanggal_asal,alasan,status", dailyText);
        Assert.Contains($"{Today(-2):yyyy-MM-dd}", dailyText);
        Assert.Contains("Output,3,1.5,4.5", dailyText);
    }

    [Fact]
    public async Task Ekspor_periode_Closed_identik_dengan_snapshot_tanpa_penanda_BELUM_FINAL_dan_tercatat_di_audit()
    {
        var (h, _, _, _, _, period) = await OpenWorldAsync();
        await h.ClosePeriodAsync(period.Id);

        var xlsx = await h.DownloadPayrollAsync(period.Id, "export?format=xlsx");
        var csv = await h.DownloadPayrollAsync(period.Id, "export?format=csv-summary");

        Assert.DoesNotContain("BELUM", xlsx.FileName);
        Assert.DoesNotContain("BELUM", csv.FileName);
        using var book = new XLWorkbook(new MemoryStream(xlsx.Bytes));
        Assert.Contains("FINAL", book.Worksheet("Ringkasan").Cell("A1").GetString());
        Assert.DoesNotContain("BELUM", book.Worksheet("Ringkasan").Cell("A1").GetString());
        Assert.Equal(45009d, book.Worksheet("Ringkasan").Cell("E6").GetDouble());

        await using var db = h.CreateDbContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EventType == "PayrollExported" && a.TargetId == period.Id.ToString()));
        ProblemAssert.Is(await h.GetPayrollAsync(period.Id, "export?format=pdf"), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
    }

    // ===== Slip gaji =====

    private static PdfSharp.Pdf.PdfDocument Open(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    [Fact]
    public async Task Slip_satu_pekerja_dan_semua_pekerja_masing_masing_di_halaman_baru_watermark_BELUM_FINAL_untuk_Open()
    {
        var (h, _, a, b, _, period) = await OpenWorldAsync();

        var one = await h.DownloadPayrollAsync(period.Id, $"slips?workerId={a.Id}");
        var all = await h.DownloadPayrollAsync(period.Id, "slips");

        Assert.Equal(HttpStatusCode.OK, one.Status);
        Assert.Equal("application/pdf", one.ContentType);
        Assert.Contains(a.Code, one.FileName);
        Assert.Contains("semua", all.FileName);
        using (var pdf = Open(one.Bytes))
        {
            Assert.Equal(1, pdf.PageCount);
            Assert.Equal("BELUM FINAL", pdf.Info.Subject);
            Assert.Contains(period.Code, pdf.Info.Title);
        }

        using (var pdf = Open(all.Bytes))
        {
            Assert.True(pdf.PageCount >= 2, $"halaman {pdf.PageCount}"); // satu pekerja per halaman baru
        }
    }

    [Fact]
    public async Task Slip_pekerja_tanpa_output_404_slip_Closed_final_dan_rincian_harian_menambah_isi()
    {
        var (h, _, a, _, c, period) = await OpenWorldAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await h.DownloadPayrollAsync(period.Id, $"slips?workerId={c.Id}")).Status);
        await h.ClosePeriodAsync(period.Id);

        var plain = await h.DownloadPayrollAsync(period.Id, $"slips?workerId={a.Id}");
        var withDaily = await h.DownloadPayrollAsync(period.Id, $"slips?workerId={a.Id}&daily=true");

        using var pdf = Open(plain.Bytes);
        Assert.Equal("FINAL", pdf.Info.Subject);
        Assert.DoesNotContain("BELUM", plain.FileName);
        Assert.True(withDaily.Bytes.Length > plain.Bytes.Length, "rincian harian harus menambah isi");
        await using var db = h.CreateDbContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.EventType == "PayrollSlipPrinted" && x.TargetId == period.Id.ToString()));
    }

    [Fact]
    public void Pembuat_slip_menandai_total_negatif_menampilkan_penyesuaian_dan_banyak_pekerja_tanpa_error()
    {
        var adjustment = new PayrollDailyRow(new DateOnly(2026, 11, 8), Guid.NewGuid(), "PKJ-1", "Ñandú Åsa", Guid.NewGuid(), "WO-1", "Cutting", "M",
            OutputKind.Adjustment, -5, 1001m, -5005m, new DateOnly(2026, 10, 20), "salah catat — “kutip”", Guid.NewGuid());
        var worker = new SlipWorker("PKJ-1", "Ñandú Åsa (é ü)", -5, -5005m, -5005m, [], [new PayrollDayGroup(new DateOnly(2026, 11, 8), [adjustment], -5005m)], [adjustment]);
        var many = Enumerable.Range(1, 30).Select(i => worker with { WorkerCode = $"PKJ-{i:000}" }).ToList();
        var model = new SlipModel("GJ-20261101-20261108", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 8), false, "", "", "Admin", new DateTime(2026, 11, 9, 9, 0, 0), true, many);

        var bytes = PayrollSlipPdfBuilder.Build(model, "Fonts");

        using var pdf = Open(bytes);
        Assert.True(pdf.PageCount >= 30);
        Assert.Equal("FINAL", pdf.Info.Subject);
        Assert.Equal("1.234.567", PayrollSlipPdfBuilder.Money(1234567m));
        Assert.Equal("1.234,5", PayrollSlipPdfBuilder.Amount(1234.5m));
        Assert.Equal("-5.005", PayrollSlipPdfBuilder.Amount(-5005m));
        Assert.Equal("0,5", PayrollSlipPdfBuilder.Rate(0.5m));
        Assert.Equal("1.001,12345", PayrollSlipPdfBuilder.Rate(1001.12345m));
    }

    // ===== Permission =====

    [Fact]
    public async Task Ekspor_dan_slip_dilindungi_permission_masing_masing_dan_daftar_periode_menghitung_Open_langsung_dan_Closed_dari_snapshot()
    {
        var (h, _, a, _, _, period) = await OpenWorldAsync();
        var viewer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Payroll.View)).Id);
        var exporter = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Payroll.View, Permissions.Payroll.Export)).Id);
        var printer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Payroll.View, Permissions.Payroll.PrintSlip)).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.DownloadPayrollAsync(period.Id, "export?format=xlsx", viewer.Token)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.DownloadPayrollAsync(period.Id, "slips", viewer.Token)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.DownloadPayrollAsync(period.Id, "slips", exporter.Token)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.DownloadPayrollAsync(period.Id, "export?format=xlsx", printer.Token)).Status);
        Assert.Equal(HttpStatusCode.OK, (await h.DownloadPayrollAsync(period.Id, "export?format=csv-summary", exporter.Token)).Status);
        Assert.Equal(HttpStatusCode.OK, (await h.DownloadPayrollAsync(period.Id, $"slips?workerId={a.Id}", printer.Token)).Status);

        var openList = (await h.Api.GetAsync("/api/v1/payroll/periods", h.AdminToken)).Ok().As<List<PayrollPeriodListItem>>().Single();
        Assert.Equal((2, 45009m, true, null), (openList.WorkerCount, openList.TotalPaid, openList.IsProvisional, openList.ClosedByName));
        await h.ClosePeriodAsync(period.Id);
        var closedList = (await h.Api.GetAsync("/api/v1/payroll/periods", h.AdminToken)).Ok().As<List<PayrollPeriodListItem>>().Single();
        Assert.Equal((2, 45009m, false), (closedList.WorkerCount, closedList.TotalPaid, closedList.IsProvisional));
        Assert.False(string.IsNullOrEmpty(closedList.ClosedByName));
    }
}
