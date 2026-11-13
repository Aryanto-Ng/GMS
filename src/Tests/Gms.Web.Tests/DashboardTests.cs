using Bunit;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Dashboard;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Gms.Web.Components.Pages;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockDashboardTests
{
    private readonly ProductionRig _rig = new();

    private async Task Output(int seq, int qty, DateOnly? date = null, int line = 0) =>
        await _rig.Production.SaveOutputsAsync(new SaveOutputsRequest(ProductionRig.WoId, seq, date ?? new DateOnly(2026, 11, 9), [new OutputRow(_rig.Worker(0), [new WoLineQty(_rig.Line(line), qty)])], true));

    [Fact]
    public async Task Widget_PO_selaras_dengan_filter_daftar_PO()
    {
        _rig.Pos.Today = _rig.Dashboard.Today = () => new DateOnly(2026, 12, 30); // semua tanggal kirim sudah lewat

        var late = await _rig.Dashboard.GetPoLateAsync();
        var listed = await _rig.Pos.ListAsync(new PoListQuery { Terlambat = true });

        Assert.Equal(listed.TotalCount, late.PoCount);
        Assert.Equal(500, late.ShortageQty);
        Assert.True(late.Items[0].DaysLate > 0);
        Assert.Equal((await _rig.Pos.ListAsync(new PoListQuery { PoCustomerKosong = true })).TotalCount, (await _rig.Dashboard.GetPoNoCustomerNumberAsync()).PoCount);
        var unplanned = await _rig.Dashboard.GetUnplannedDemandAsync();
        Assert.Equal((1, 350), (unplanned.PoCount, unplanned.Qty)); // 500 − 150 sudah di WO contoh
    }

    [Fact]
    public async Task Jatuh_tempo_7_hari_menghitung_PO_dengan_tanggal_kirim_dalam_jendela()
    {
        _rig.Pos.Today = _rig.Dashboard.Today = () => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)); // seed: kirim +12 hari → dalam jendela 7 hari dari +10

        var due = await _rig.Dashboard.GetDueSoonAsync();

        Assert.Equal((1, 500), (due.PoCount, due.Qty));
    }

    [Fact]
    public async Task Widget_WO_dan_output_hari_ini_serta_sisa_di_vendor()
    {
        var running = await _rig.Dashboard.GetWoRunningAsync();
        Assert.Equal((1, 0, 0), (running.Released, running.InProgress, running.CompletedAwaitingClose));
        Assert.Equal(0, (await _rig.Dashboard.GetWoLateAsync()).Count);

        _rig.Production.Today = _rig.Dashboard.Today = () => new DateOnly(2026, 11, 9);
        await Output(1, 20);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 20)]));
        var today = await _rig.Dashboard.GetOutputTodayAsync();
        Assert.Equal((20, "Cutting"), (today.TotalQty, today.ByProcess[0].ProcessName));
        var vendor = await _rig.Dashboard.GetVendorBalanceAsync();
        Assert.Equal((20, "Bordir Indah"), (vendor.TotalQty, vendor.ByVendor[0].VendorName));
        Assert.Equal(1, (await _rig.Dashboard.GetWoRunningAsync()).InProgress);
    }

    [Fact]
    public async Task Stok_belum_serah_terima_periode_gaji_dan_keamanan()
    {
        var stock = await _rig.Dashboard.GetStockSummaryAsync();
        Assert.Equal((480, 0, 480), (stock.OnHand, stock.Allocated, stock.Available));
        Assert.Equal("KMJ-001", stock.TopFgs[0].FgCode);

        var noPeriod = await _rig.Dashboard.GetPayrollPeriodAsync();
        Assert.False(noPeriod.HasOpenPeriod);
        var period = await _rig.Payroll.CreatePeriodAsync(new SavePeriodRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 14), null));
        var open = await _rig.Dashboard.GetPayrollPeriodAsync();
        Assert.Equal((true, period.Code, 5), (open.HasOpenPeriod, open.Code, open.DaysLeft));

        var security = await _rig.Dashboard.GetSecurityAsync();
        Assert.Equal(1, security.LockedAccounts); // "siti" terkunci pada data awal
        Assert.Equal(0, (await _rig.Dashboard.GetUnhandedOverAsync()).TotalQty);
    }
}

public sealed class DashboardPageTests : WebTestBase
{
    private sealed class FailingWoLate(IDashboardApi inner) : IDashboardApi
    {
        public Task<PoLateWidget> GetPoLateAsync(CancellationToken ct = default) => inner.GetPoLateAsync(ct);
        public Task<DueSoonWidget> GetDueSoonAsync(CancellationToken ct = default) => inner.GetDueSoonAsync(ct);
        public Task<PoNoCustomerNumberWidget> GetPoNoCustomerNumberAsync(CancellationToken ct = default) => inner.GetPoNoCustomerNumberAsync(ct);
        public Task<UnplannedDemandWidget> GetUnplannedDemandAsync(CancellationToken ct = default) => inner.GetUnplannedDemandAsync(ct);
        public Task<WoRunningWidget> GetWoRunningAsync(CancellationToken ct = default) => inner.GetWoRunningAsync(ct);
        public Task<WoLateWidget> GetWoLateAsync(CancellationToken ct = default) => throw new ApiException(new GmsProblemDetails { Type = ProblemTypes.InternalError, Status = 500, Title = "x", TraceId = "00-gagal" });
        public Task<OutputTodayWidget> GetOutputTodayAsync(CancellationToken ct = default) => inner.GetOutputTodayAsync(ct);
        public Task<VendorBalanceWidget> GetVendorBalanceAsync(CancellationToken ct = default) => inner.GetVendorBalanceAsync(ct);
        public Task<UnhandedOverWidget> GetUnhandedOverAsync(CancellationToken ct = default) => inner.GetUnhandedOverAsync(ct);
        public Task<StockSummaryWidget> GetStockSummaryAsync(CancellationToken ct = default) => inner.GetStockSummaryAsync(ct);
        public Task<PayrollPeriodWidget> GetPayrollPeriodAsync(CancellationToken ct = default) => inner.GetPayrollPeriodAsync(ct);
        public Task<SecurityWidget> GetSecurityAsync(CancellationToken ct = default) => inner.GetSecurityAsync(ct);
    }

    private readonly ProductionRig _rig = new();

    public DashboardPageTests()
    {
        _rig.Pos.Today = _rig.Dashboard.Today = () => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Services.AddSingleton<IDashboardApi>(_rig.Dashboard);
    }

    private static readonly string[] Everything = [.. PermissionCatalog.All.Select(p => p.Key)];

    [Fact]
    public void Admin_melihat_semua_area_dan_kartu_dengan_region_berlabel_dan_tautan_filter_sama_dengan_modul()
    {
        SignIn(Everything);

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("WO Berjalan", cut.Markup));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=region][aria-label='PO Terlambat'] .gms-ok, [role=region][aria-label='PO Terlambat'] .gms-big")));
        var regions = cut.FindAll("[role=region]").Select(r => r.GetAttribute("aria-label")).ToArray();
        foreach (var expected in new[] { "Aksi Cepat", "Output Hari Ini", "WO Berjalan", "Sisa di Vendor", "Belum Diserahterimakan", "WO Terlambat", "PO Terlambat", "Jatuh Tempo 7 Hari", "PO Belum Ada No. Customer", "Kebutuhan Belum Terencana", "Stok FG", "Periode Gaji", "Keamanan Akun" })
        {
            Assert.Contains(expected, regions);
        }

        Assert.Equal("/purchase-orders?terlambat=true", cut.Find("[aria-label='PO Terlambat'] h3 a").GetAttribute("href"));
        Assert.Equal("/purchase-orders?jatuhTempo7=true", cut.Find("[aria-label='Jatuh Tempo 7 Hari'] h3 a").GetAttribute("href"));
        Assert.Equal("/purchase-orders?poCustomerKosong=true", cut.Find("[aria-label='PO Belum Ada No. Customer'] h3 a").GetAttribute("href"));
        Assert.Equal("/purchase-orders?sisaRencana=true", cut.Find("[aria-label='Kebutuhan Belum Terencana'] h3 a").GetAttribute("href"));
        Assert.Equal("/work-orders?terlambat=true", cut.Find("[aria-label='WO Terlambat'] h3 a").GetAttribute("href"));
    }

    [Fact]
    public void Kartu_nol_menampilkan_nilai_netral_dengan_ikon_centang_bukan_state_kosong()
    {
        SignIn(Everything);

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("0 WO terlambat", cut.Find("[aria-label='WO Terlambat']").TextContent));
        Assert.Contains("Belum ada output hari ini", cut.Find("[aria-label='Output Hari Ini']").TextContent);
        Assert.Contains("Semua output sudah diserahterimakan", cut.Find("[aria-label='Belum Diserahterimakan']").TextContent);
    }

    [Fact]
    public void Kartu_hanya_untuk_izin_pengguna_dan_area_diurutkan_menurut_relevansi()
    {
        SignIn(Permissions.CustomerPO.View, Permissions.StokFG.View, Permissions.Payroll.View);

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("PO Terlambat", cut.Markup));
        var regions = cut.FindAll("[role=region]").Select(r => r.GetAttribute("aria-label")).ToArray();
        Assert.DoesNotContain("WO Berjalan", regions);
        Assert.DoesNotContain("Keamanan Akun", regions);
        Assert.Equal(["Penjualan", "Gudang", "Penggajian"], cut.FindAll("h2.gms-area").Select(h => h.TextContent)); // area dengan skor sama mengikuti urutan baku
        Assert.Contains("Stok FG", regions);
        Assert.Contains("Periode Gaji", regions);
    }

    [Fact]
    public void Satu_kartu_gagal_menampilkan_galat_di_dalam_kartu_dengan_traceId_dan_kartu_lain_tetap_normal()
    {
        SignIn(Permissions.WorkOrder.View, Permissions.CustomerPO.View);
        Services.AddSingleton<IDashboardApi>(new FailingWoLate(_rig.Dashboard)); // menimpa registrasi dashboard

        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.Contains("00-gagal", cut.Find("[aria-label='WO Terlambat']").TextContent));
        Assert.NotEmpty(cut.Find("[aria-label='WO Terlambat']").QuerySelectorAll("button.gms-btn")); // Coba lagi
        cut.WaitForAssertion(() => Assert.DoesNotContain("Memuat", cut.Find("[aria-label='WO Berjalan']").TextContent));
        Assert.Contains("Released", cut.Find("[aria-label='WO Berjalan']").TextContent);
        Assert.Contains("PO", cut.Find("[aria-label='PO Terlambat']").TextContent);
    }

    [Fact]
    public void Pengguna_tanpa_izin_widget_melihat_sapaan_dan_arahan_tanpa_kartu()
    {
        SignIn(Permissions.MasterData.Size.View);

        var cut = Render<Home>();

        Assert.Contains("Belum ada ringkasan untuk peran Anda", cut.Markup);
        Assert.Empty(cut.FindAll("[role=region]"));
        Assert.Contains("Selamat datang, Budi", cut.Find("h1").TextContent);
    }

    [Fact]
    public void Aksi_Cepat_hanya_menampilkan_aksi_yang_diizinkan_maksimal_enam()
    {
        SignIn(Permissions.ProductionOutput.Create, Permissions.CustomerPO.Create);

        var cut = Render<Home>();

        var links = cut.FindAll("[aria-label='Aksi Cepat'] a.gms-btn").Select(a => a.TextContent).ToArray();
        Assert.Equal(["Input Output", "Buat PO"], links);
    }

    [Fact]
    public void Aksi_Cepat_dibatasi_enam_tombol_walau_semua_izin_dimiliki()
    {
        SignIn(Everything);

        var cut = Render<Home>();

        Assert.Equal(6, cut.FindAll("[aria-label='Aksi Cepat'] a.gms-btn").Count);
    }

    [Fact]
    public void Segarkan_memuat_ulang_kartu_memperbarui_waktu_dan_mengumumkan_sekali()
    {
        SignIn(Permissions.CustomerPO.View);
        var cut = Render<Home>();
        cut.WaitForAssertion(() => Assert.Contains("PO Terlambat", cut.Markup));

        cut.Find("button[aria-label='Segarkan Beranda']").Click(); // tombol ikon saja (Design.md 7)

        Assert.Equal("Beranda diperbarui", cut.Find("[role=status][aria-live=polite].gms-sr-only").TextContent);
        cut.WaitForAssertion(() => Assert.Contains("PO", cut.Find("[aria-label='PO Terlambat']").TextContent));
    }

    [Fact]
    public void Progres_WO_di_kartu_memakai_progressbar_dengan_teks_nilai()
    {
        SignIn(Permissions.WorkOrder.View);
        var cut = Render<Home>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=progressbar]")));

        var bar = cut.Find("[role=progressbar]");
        Assert.Equal("0", bar.GetAttribute("aria-valuenow"));
        Assert.Contains("persen", bar.GetAttribute("aria-valuetext"));
    }
}
