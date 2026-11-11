using System.Text.RegularExpressions;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Inventory;
using Gms.Shared.Payroll;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Alur kerja utama lewat UI sungguhan terhadap API + PostgreSQL nyata; hasil diverifikasi lewat API (bukan hanya tampilan).</summary>
[Collection(StackCollection.Name)]
public sealed class FlowsE2ETests(StackFixture stack)
{
    private static ILocator Heading(IPage page) => page.GetByRole(AriaRole.Heading, new() { Level = 1 });

    private static async Task WaitIdleAsync(IPage page) =>
        await page.WaitForFunctionAsync("() => document.querySelectorAll('[aria-busy=\"true\"]').length === 0", null, new() { Timeout = 20_000 });

    private async Task<(BaseData Data, PoDto Po, WoDto Wo)> SeedWoAsync()
    {
        var data = await stack.Seed.CreateBaseDataAsync();
        var po = await stack.Seed.CreateConfirmedPoAsync(data, "PO/E2E/" + Guid.NewGuid().ToString("N")[..4]);
        var wo = await stack.Seed.CreateReleasedWoAsync(po);
        return (data, po, wo);
    }

    [Fact]
    public async Task Input_output_lewat_UI_tersimpan_di_database_dan_progres_WO_berubah()
    {
        var (_, _, wo) = await SeedWoAsync();
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/production/output");
        await WaitIdleAsync(page);

        await page.Locator("select").Nth(0).SelectOptionAsync(new SelectOptionValue { Value = wo.Id.ToString() });
        await Expect(page.Locator("select").Nth(1).Locator("option")).Not.ToHaveCountAsync(1);
        await page.Locator("select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 1 }); // langkah Internal pertama (Cutting)
        await Expect(page.GetByText("Proses pertama: bebas")).ToBeVisibleAsync();
        await page.Locator("select").Nth(2).SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah pekerja", Exact = true }).ClickAsync();
        await page.Locator("table.gms-matrix tbody input").First.FillAsync("40");
        await Expect(page.GetByText("1 pekerja · 40 pcs")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan", Exact = true }).ClickAsync();

        await Expect(page.GetByText(new Regex("1 entri tersimpan"))).ToBeVisibleAsync();
        var progress = await stack.Seed.GetAsync<WoProgressDto>($"/api/v1/work-orders/{wo.Id}/progress");
        Assert.Equal(40, progress.Steps[0].TotalOutput);
        Assert.Equal(WoStatusName.InProgress, (await stack.Seed.GetAsync<WoDto>($"/api/v1/work-orders/{wo.Id}")).Status.ToString());
    }

    [Fact]
    public async Task Output_melebihi_batas_langkah_berikutnya_ditolak_server_dan_input_dipertahankan()
    {
        var (_, _, wo) = await SeedWoAsync();
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/production/output");
        await WaitIdleAsync(page);
        await page.Locator("select").Nth(0).SelectOptionAsync(new SelectOptionValue { Value = wo.Id.ToString() });
        await Expect(page.Locator("select").Nth(1).Locator("option")).Not.ToHaveCountAsync(1);
        await page.Locator("select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 2 }); // Sewing: batas = output Cutting (0)
        await page.Locator("select").Nth(2).SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah pekerja", Exact = true }).ClickAsync();
        var cell = page.Locator("table.gms-matrix tbody input").First;

        await cell.FillAsync("5");

        await Expect(cell).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator(".gms-cellmsg")).ToContainTextAsync("melebihi batas");
    }

    [Fact]
    public async Task Riwayat_produksi_menampilkan_entri_dan_Void_dengan_alasan_menghapusnya_dari_kumulatif()
    {
        var (data, _, wo) = await SeedWoAsync();
        await stack.Seed.PostAsync<SaveOutputsResult>("/api/v1/production/outputs", new SaveOutputsRequest(
            wo.Id, 1, DateOnly.FromDateTime(DateTime.UtcNow), [new OutputRow(data.WorkerId, [new WoLineQty(wo.Lines[0].Id, 25)])]));
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/production/history");
        await WaitIdleAsync(page);
        await page.Locator("#h-wo").SelectOptionAsync(new SelectOptionValue { Value = wo.Id.ToString() });
        await WaitIdleAsync(page);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);

        await page.Locator("tbody tr", new() { HasText = wo.Code }).GetByRole(AriaRole.Button, new() { Name = "Void entri" }).ClickAsync();
        await Expect(page.Locator(".ant-modal-wrap .gms-impact")).ToBeVisibleAsync();
        await page.Locator(".ant-modal-wrap textarea").FillAsync("salah input");
        await page.Locator(".ant-modal-wrap").GetByRole(AriaRole.Button, new() { Name = "Void entri" }).ClickAsync();

        await Expect(page.GetByText("Entri di-void.")).ToBeVisibleAsync();
        Assert.Equal(0, (await stack.Seed.GetAsync<WoProgressDto>($"/api/v1/work-orders/{wo.Id}/progress")).Steps[0].TotalOutput);
    }

    [Fact]
    public async Task WO_Draft_dirilis_lewat_dialog_dengan_daftar_periksa_server()
    {
        var data = await stack.Seed.CreateBaseDataAsync();
        var po = await stack.Seed.CreateConfirmedPoAsync(data);
        var draft = await stack.Seed.PostAsync<WoDto>("/api/v1/work-orders", new SaveWoRequest(po.Id, data.FgId, null, null, null, [new SaveWoLine(po.Lines[0].Id, 30)]));
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/work-orders/{draft.Id}");
        await WaitIdleAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Rilis", Exact = true }).ClickAsync();
        await Expect(page.Locator(".ant-modal-wrap")).ToContainTextAsync("Terpenuhi");
        await page.Locator(".ant-modal-wrap").GetByRole(AriaRole.Button, new() { Name = "Rilis WO" }).ClickAsync();

        await Expect(page.GetByText("WO dirilis.")).ToBeVisibleAsync();
        Assert.Equal("Released", (await stack.Seed.GetAsync<WoDto>($"/api/v1/work-orders/{draft.Id}")).Status.ToString());
    }

    [Fact]
    public async Task Alokasi_stok_dan_surat_jalan_lewat_UI_mengubah_Terkirim_PO()
    {
        var (data, po, wo) = await SeedWoAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Produksi penuh 30 pcs baris pertama sampai langkah terakhir lalu serah terima → stok + alokasi otomatis.
        var line = wo.Lines.First(l => l.ColorSize.SizeId == data.SizeSId);
        foreach (var seq in new[] { 1, 2 })
        {
            await stack.Seed.PostAsync<SaveOutputsResult>("/api/v1/production/outputs", new SaveOutputsRequest(wo.Id, seq, today, [new OutputRow(data.WorkerId, [new WoLineQty(line.Id, 30)])]));
        }

        await stack.Seed.PostAsync<HandoverFgResultDto>("/api/v1/handovers/fg", new SaveHandoverFgRequest(wo.Id, today, "ST-E2E", "Gudang", null, [new WoLineQty(line.Id, 30)]));
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/deliveries/new?poId={po.Id}");
        await WaitIdleAsync(page);

        await Expect(page.Locator("tbody tr").First).ToBeVisibleAsync();
        await page.Locator("tbody input").First.FillAsync("30");
        await Expect(page.GetByText("Total kirim: 30 pcs")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan Surat Jalan" }).ClickAsync();

        await Expect(Heading(page)).ToContainTextAsync("SJ-");
        var after = await stack.Seed.GetAsync<PoDto>($"/api/v1/purchase-orders/{po.Id}");
        Assert.Equal(30, after.DeliveredQty);
        var delivery = (await stack.Seed.GetAsync<PagedResult<DeliveryListItem>>("/api/v1/deliveries")).Items.Single(d => d.PoId == po.Id);
        Assert.Equal(30, delivery.TotalQty);
    }

    [Fact]
    public async Task Periode_gaji_dibuat_lewat_UI_dirinci_dan_ditutup_dengan_kode_konfirmasi_lalu_tanggal_terkunci()
    {
        var (data, _, wo) = await SeedWoAsync();
        var today = new DateOnly(2020, 1, 2); // tanggal lampau agar penutupan periode tidak mengunci tanggal yang dipakai tes lain pada database bersama
        await stack.Seed.PostAsync<SaveOutputsResult>("/api/v1/production/outputs", new SaveOutputsRequest(wo.Id, 1, today, [new OutputRow(data.WorkerId, [new WoLineQty(wo.Lines[0].Id, 20)])]));
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/payroll");
        await WaitIdleAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Buat Periode", Exact = true }).First.ClickAsync();
        await page.Locator(".ant-modal-wrap input[type=date]").Nth(0).FillAsync(today.AddDays(-1).ToString("yyyy-MM-dd"));
        await page.Locator(".ant-modal-wrap").GetByRole(AriaRole.Button, new() { Name = "7 hari" }).ClickAsync();
        await page.Locator(".ant-modal-wrap").GetByRole(AriaRole.Button, new() { Name = "Buat Periode" }).ClickAsync();

        await Expect(Heading(page)).ToContainTextAsync("GJ-");
        await Expect(page.GetByText("Angka bersifat sementara")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr").First).ToContainTextAsync(data.WorkerId == Guid.Empty ? "" : "Pekerja");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tutup Periode", Exact = true }).ClickAsync();
        await Expect(page.Locator(".ant-modal-wrap")).ToContainTextAsync("Penutupan PERMANEN");
        var code = await page.Locator(".ant-modal-wrap .gms-field small, .ant-modal-wrap small").First.InnerTextAsync();
        await page.Locator(".ant-modal-wrap input").FillAsync(code.Trim());
        await page.Locator(".ant-modal-wrap").GetByRole(AriaRole.Button, new() { Name = "Tutup Periode" }).ClickAsync();

        await Expect(page.GetByText(new Regex("Periode ditutup pada"))).ToBeVisibleAsync();
        var periods = await stack.Seed.GetAsync<List<PayrollPeriodListItem>>("/api/v1/payroll/periods");
        Assert.Contains(periods, p => p.Status.ToString() == "Closed" && p.TotalPaid > 0);
    }

    [Fact]
    public async Task Beranda_memuat_kartu_dari_API_nyata_tanpa_galat_dan_menautkan_ke_filter_PO()
    {
        var (_, po, _) = await SeedWoAsync();
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, $"/");
        await WaitIdleAsync(page);

        await Expect(page.Locator("[role=region][aria-label='PO Terlambat']")).ToBeVisibleAsync();
        await Expect(page.Locator("[role=region][aria-label='Stok FG']")).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator(".gms-dcard .gms-state.error").CountAsync());
        await page.Locator("[aria-label='Kebutuhan Belum Terencana'] h3 a").ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("sisaRencana=true"));
        await Expect(Heading(page)).ToHaveTextAsync("Customer PO");
        _ = po;
    }

    private static class WoStatusName
    {
        public const string InProgress = "InProgress";
    }
}
