using System.Net;
using Gms.Api.Documents;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Deliveries;

/// <summary>Cetak Surat Jalan PDF (FSD Pengiriman 4.5, AC-PG-17..23).</summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryPdfTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private async Task<(HttpStatusCode Status, byte[] Bytes, string? ContentType, string? FileName)> PrintAsync(Guid id, string? token = null) =>
        await H.Api.DownloadAsync($"/api/v1/deliveries/{id}/pdf", token ?? H.AdminToken);

    private static PdfSharp.Pdf.PdfDocument Open(byte[] bytes) =>
        PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    private async Task<int> PrintCountAsync(Guid id)
    {
        await using var db = H.CreateDbContext();
        return await db.Deliveries.AsNoTracking().Where(d => d.Id == id).Select(d => d.PrintCount).SingleAsync();
    }

    [Fact]
    public async Task Cetak_menghasilkan_PDF_A4_bernama_kode_SJ_menaikkan_print_count_dan_tercatat_di_audit()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10), (po.PoLineId(w.Medium), 5));

        var result = await PrintAsync(delivery.Item.Id);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal($"{delivery.Item.Code}.pdf", result.FileName?.Trim('"'));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(result.Bytes, 0, 5));
        using var pdf = Open(result.Bytes);
        Assert.Equal(1, pdf.PageCount);
        Assert.Equal((595, 842), ((int)Math.Round(pdf.Pages[0].Width.Point), (int)Math.Round(pdf.Pages[0].Height.Point))); // A4 portrait
        Assert.Equal($"Surat Jalan {delivery.Item.Code}", pdf.Info.Title);
        Assert.Equal(string.Empty, pdf.Info.Subject); // cetakan pertama: tanpa penanda
        Assert.Equal(1, await PrintCountAsync(delivery.Item.Id));
        Assert.Equal(1, (await H.GetDeliveryAsync(delivery.Item.Id)).Item.PrintCount);

        await using var db = H.CreateDbContext();
        Assert.Contains("DeliveryPrinted", await db.AuditLogs.Where(a => a.TargetId == delivery.Item.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Cetakan_kedua_dan_seterusnya_surat_jalan_aktif_diberi_penanda_CETAK_ULANG()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));

        await PrintAsync(delivery.Item.Id);
        var second = await PrintAsync(delivery.Item.Id);
        var third = await PrintAsync(delivery.Item.Id);

        using (var pdf = Open(second.Bytes)) { Assert.Contains("CETAK ULANG", pdf.Info.Subject); }
        using (var pdf = Open(third.Bytes)) { Assert.Contains("CETAK ULANG", pdf.Info.Subject); }
        Assert.Equal(3, await PrintCountAsync(delivery.Item.Id));
    }

    [Fact]
    public async Task Surat_jalan_Voided_dicetak_dengan_watermark_DIBATALKAN_tanpa_penanda_cetak_ulang()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        await H.VoidDeliveryAsync(delivery.Item.Id, "salah kirim");

        await PrintAsync(delivery.Item.Id);
        var again = await PrintAsync(delivery.Item.Id);

        Assert.Equal(HttpStatusCode.OK, again.Status);
        using var pdf = Open(again.Bytes);
        Assert.Contains("DIBATALKAN", pdf.Info.Subject);
        Assert.DoesNotContain("CETAK ULANG", pdf.Info.Subject);
    }

    [Fact]
    public async Task Perubahan_info_non_qty_terlihat_pada_cetakan_berikutnya_dan_karakter_Indonesia_tidak_menggagalkan_cetak()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        var updated = (await H.Api.PutAsync($"/api/v1/deliveries/{delivery.Item.Id}/info",
            new Gms.Shared.Deliveries.UpdateDeliveryInfoRequest(Today(), "PT Sinar Jaya – Bandung (Ångström, é, ü, “kutip”)", "D 1 ÅÉ", "Ahmad Ñandú", "Fragile — jangan ditumpuk", delivery.RowVersion), H.AdminToken))
            .Ok().As<Gms.Shared.Deliveries.DeliveryDto>();

        var result = await PrintAsync(updated.Item.Id);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        using var pdf = Open(result.Bytes);
        Assert.Equal(1, pdf.PageCount);
    }

    [Fact]
    public async Task Cetak_dilindungi_permission_Print_dan_surat_jalan_tidak_ada_404()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.Pengiriman.View)).Id);
        var printer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.Pengiriman.View, Permissions.Pengiriman.Print)).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await PrintAsync(delivery.Item.Id, viewer.Token)).Status);
        Assert.Equal(0, await PrintCountAsync(delivery.Item.Id));
        Assert.Equal(HttpStatusCode.OK, (await PrintAsync(delivery.Item.Id, printer.Token)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PrintAsync(Guid.NewGuid())).Status);
        Assert.Contains("print", (await H.GetDeliveryAsync(delivery.Item.Id, printer.Token)).AllowedActions);
        Assert.DoesNotContain("print", (await H.GetDeliveryAsync(delivery.Item.Id, viewer.Token)).AllowedActions);
    }

    [Fact]
    public async Task Pencetakan_paralel_menghitung_setiap_cetakan_tepat_sekali()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await PrintAsync(delivery.Item.Id);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        Assert.Equal(8, await PrintCountAsync(delivery.Item.Id));
        // Tepat satu cetakan yang boleh tanpa penanda (print_count = 1).
        var originals = results.Count(r => { using var pdf = Open(r.Bytes); return !pdf.Info.Subject.Contains("CETAK ULANG"); });
        Assert.Equal(1, originals);
    }

    // ===== Pembuat PDF langsung =====

    private static DeliveryPrintModel Model(IReadOnlyList<PrintLine> lines, bool voided = false, bool reprint = false) =>
        new("SJ-202611-0001", new DateOnly(2026, 11, 9), "PO-202611-0001", "PO/CUST/1", "PT Customer", "Jl. Contoh No. 1, Bandung",
            "Penerima", "B 1 CD", "Sopir", "catatan", "", "", lines, voided, voided ? "salah kirim" : null, reprint, "Admin", new DateTime(2026, 11, 9, 10, 30, 0));

    [Fact]
    public void Kop_Comfi_kosong_tetap_menghasilkan_PDF_dan_matriks_lebar_dipecah_per_blok_Size()
    {
        var lines = Enumerable.Range(1, 20)
            .Select(i => new PrintLine("FG-1", "Kemeja", "NAVY", "Navy", i, $"S{i:00}", i))
            .ToList();

        var bytes = DeliveryPdfBuilder.Build(Model(lines), "Fonts");

        using var pdf = Open(bytes);
        Assert.True(pdf.PageCount >= 1);
        Assert.Equal("Surat Jalan SJ-202611-0001", pdf.Info.Title);
        // 20 size / 8 per blok = 3 blok; lebih besar dari satu tabel tunggal.
        Assert.True(bytes.Length > DeliveryPdfBuilder.Build(Model(lines.Take(3).ToList()), "Fonts").Length);
    }

    [Fact]
    public void Watermark_DIBATALKAN_benar_benar_digambar_pada_setiap_halaman()
    {
        var lines = Enumerable.Range(1, 120)
            .Select(i => new PrintLine($"FG-{i:000}", "Kemeja", $"C{i:000}", "Warna", 1, "M", i))
            .ToList();

        var normal = DeliveryPdfBuilder.Build(Model(lines), "Fonts");
        var voided = DeliveryPdfBuilder.Build(Model(lines, voided: true), "Fonts");

        using var normalPdf = Open(normal);
        using var voidedPdf = Open(voided);
        Assert.Equal(normalPdf.PageCount, voidedPdf.PageCount);
        for (var i = 0; i < voidedPdf.PageCount; i++)
        {
            // Watermark ditambahkan sebagai aliran konten tambahan pada tiap halaman.
            Assert.True(voidedPdf.Pages[i].Contents.Elements.Count > normalPdf.Pages[i].Contents.Elements.Count,
                $"halaman {i + 1} tidak memiliki konten watermark");
        }
    }

    [Fact]
    public void Banyak_baris_barang_berlanjut_ke_halaman_berikutnya()
    {
        var lines = Enumerable.Range(1, 120)
            .Select(i => new PrintLine($"FG-{i:000}", "Kemeja Panjang", $"C{i:000}", "Warna", 1, "M", 10 + i))
            .ToList();

        var bytes = DeliveryPdfBuilder.Build(Model(lines, voided: true, reprint: false), "Fonts");

        using var pdf = Open(bytes);
        Assert.True(pdf.PageCount >= 2, $"halaman {pdf.PageCount}");
        Assert.Contains("DIBATALKAN", pdf.Info.Subject);
    }
}
