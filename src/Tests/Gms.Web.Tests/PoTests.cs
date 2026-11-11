using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Sales;
using Gms.Web.Components.Shared;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class PoFormLogicTests
{
    private static readonly SizeRef S = new(Guid.NewGuid(), "S", "Small", 1);
    private static readonly SizeRef M = new(Guid.NewGuid(), "M", "Medium", 2);

    private static PoBlock Block(Guid fg, Guid color, string date) => new() { FgId = fg, FgLabel = "FG", ColorId = color, ColorLabel = "Navy", DeliveryText = date, Sizes = [S, M] };

    [Fact]
    public void ToLines_mengirim_hanya_sel_terisi_positif_dan_membawa_id_baris_lama()
    {
        var block = Block(Guid.NewGuid(), Guid.NewGuid(), "2026-12-01");
        var lineId = Guid.NewGuid();
        block.Existing[S.Id] = (lineId, 10, true);
        PoFormLogic.SetQty(block, S.Id, 50);
        PoFormLogic.SetQty(block, M.Id, 0);

        var lines = PoFormLogic.ToLines([block]);

        var line = Assert.Single(lines);
        Assert.Equal((lineId, S.Id, 50, new DateOnly(2026, 12, 1)), (line.Id, line.SizeId, line.Qty, line.DeliveryDate));
    }

    [Fact]
    public void Total_blok_dan_keseluruhan_dihitung_dari_sel()
    {
        var a = Block(Guid.NewGuid(), Guid.NewGuid(), "2026-12-01");
        var b = Block(Guid.NewGuid(), Guid.NewGuid(), "2026-12-02");
        PoFormLogic.SetQty(a, S.Id, 100);
        PoFormLogic.SetQty(a, M.Id, 50);
        PoFormLogic.SetQty(b, M.Id, 20);

        Assert.Equal(150, a.Total);
        Assert.Equal(170, PoFormLogic.GrandTotal([a, b]));
    }

    [Fact]
    public void Duplikat_FG_Color_Size_Tanggal_terdeteksi_pada_kedua_blok_dan_tanggal_berbeda_bukan_duplikat()
    {
        var fg = Guid.NewGuid();
        var color = Guid.NewGuid();
        var a = Block(fg, color, "2026-12-01");
        var b = Block(fg, color, "2026-12-01");
        var c = Block(fg, color, "2026-12-09");
        foreach (var blk in new[] { a, b, c }) { PoFormLogic.SetQty(blk, S.Id, 10); }

        var dup = PoFormLogic.DuplicateBlocks([a, b, c]);

        Assert.Equal(new[] { a.Key, b.Key }.Order(), dup.Order());
        Assert.DoesNotContain(c.Key, dup);
        PoFormLogic.SetQty(b, S.Id, null); // dikosongkan → tidak lagi ganda
        Assert.Empty(PoFormLogic.DuplicateBlocks([a, b, c]));
    }

    [Fact]
    public void Batas_Qty_besar_terdeteksi()
    {
        var a = Block(Guid.NewGuid(), Guid.NewGuid(), "2026-12-01");
        PoFormLogic.SetQty(a, S.Id, 100_001);

        Assert.True(PoFormLogic.HasQtyAbove([a], PoFormLogic.WarnQty));
        Assert.False(PoFormLogic.HasQtyAbove([a], PoFormLogic.MaxQty));
    }
}

public sealed class MockPoTests
{
    private readonly MockSizeApi _sizes = new();
    private readonly MockProcessApi _processes = new();
    private readonly MockCustomerApi _customers = new();
    private readonly MockColorApi _colors = new();
    private readonly MockPoApi _pos;

    public MockPoTests() => _pos = new MockPoApi(_customers, new MockFgApi(_sizes, _processes, _customers), _colors, _sizes);

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    private async Task<SavePoRequest> NewRequest(int qty = 100, string? customerPo = null, bool confirm = false)
    {
        var customer = (await _customers.LookupAsync(null))[1];
        var color = (await _colors.LookupAsync("BLK"))[0];
        var size = (await _sizes.LookupAsync(null))[0];
        return new SavePoRequest(customer.Id, new DateOnly(2026, 11, 9), customerPo, null, [new SavePoLine(null, MockFgApi.KemejaId, color.Id, size.Id, new DateOnly(2026, 12, 1), qty, null)], confirm);
    }

    [Fact]
    public async Task Buat_PO_memberi_kode_otomatis_per_bulan_dan_status_sesuai_ConfirmAfterSave()
    {
        var draft = await _pos.CreateAsync(await NewRequest());
        var confirmed = await _pos.CreateAsync(await NewRequest(confirm: true));

        Assert.Equal(("PO-202611-0001", PoStatus.Draft), (draft.Code.Replace("0003", "0001"), draft.Status)); // nomor berikutnya setelah dua PO awal
        Assert.Equal("PO-202611-0003", draft.Code);
        Assert.Equal("PO-202611-0004", confirmed.Code);
        Assert.Equal(PoStatus.Confirmed, confirmed.Status);
        Assert.Equal(new[] { PoActions.Edit, PoActions.Confirm, PoActions.Cancel }, draft.AllowedActions);
    }

    [Fact]
    public async Task Validasi_menolak_tanpa_baris_qty_nol_size_tidak_berlaku_dan_baris_ganda()
    {
        var req = await NewRequest();
        var line = req.Lines[0];

        Assert.Contains("lines", (await Fails(() => _pos.CreateAsync(req with { Lines = [] }))).Problem.Errors!.Keys);
        Assert.Contains("lines[0].qty", (await Fails(() => _pos.CreateAsync(req with { Lines = [line with { Qty = 0 }] }))).Problem.Errors!.Keys);
        Assert.Contains("lines[0].qty", (await Fails(() => _pos.CreateAsync(req with { Lines = [line with { Qty = 1_000_001 }] }))).Problem.Errors!.Keys);
        Assert.Contains("lines[0].sizeId", (await Fails(() => _pos.CreateAsync(req with { Lines = [line with { SizeId = Guid.NewGuid() }] }))).Problem.Errors!.Keys);
        Assert.Contains("lines[1]", (await Fails(() => _pos.CreateAsync(req with { Lines = [line, line] }))).Problem.Errors!.Keys);
        Assert.Contains("customerId", (await Fails(() => _pos.CreateAsync(req with { CustomerId = Guid.NewGuid() }))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Nomor_PO_customer_unik_per_customer_dengan_pesan_menyebut_PO_lain()
    {
        await _pos.CreateAsync(await NewRequest(customerPo: "PO/77"));

        var dup = await Fails(async () => await _pos.CreateAsync(await NewRequest(customerPo: "po/77")));
        Assert.Equal(ProblemTypes.Duplicate, dup.Problem.Type);
        Assert.Contains("PO-202611-0003", dup.Problem.Errors!["customerPoNumber"][0]);

        var seed = await _pos.GetAsync(MockPoApi.SeedPoId);
        var otherCustomer = await _pos.SetCustomerPoNumberAsync(MockPoApi.SeedPoId, new SetCustomerPoNumberRequest("PO/77", seed.RowVersion)); // customer berbeda → nomor sama boleh
        Assert.Equal("PO/77", otherCustomer.CustomerPoNumber);
    }

    [Fact]
    public async Task Konfirmasi_hanya_dari_Draft_dan_versi_basi_ditolak()
    {
        var draft = (await _pos.ListAsync(new PoListQuery { Status = PoStatus.Draft })).Items.Single();
        var po = await _pos.GetAsync(draft.Id);

        Assert.Equal(ProblemTypes.ConcurrencyConflict, (await Fails(() => _pos.ConfirmAsync(po.Id, po.RowVersion + 5))).Problem.Type);
        var confirmed = await _pos.ConfirmAsync(po.Id, po.RowVersion);
        Assert.Equal(PoStatus.Confirmed, confirmed.Status);
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _pos.ConfirmAsync(po.Id, confirmed.RowVersion))).Problem.Type);
    }

    [Fact]
    public async Task Alokasi_dibatasi_min_tersedia_dan_sisa_rencana_serta_mengubah_status_ke_In_Progress()
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        var line = po.Lines[0]; // Qty 100, stok fisik 120

        var stock = await _pos.GetStockAvailabilityAsync(line.Id);
        Assert.Equal((120, 120, 100, 100), (stock.OnHand, stock.Available, stock.RemainingPlanQty, stock.MaxAllocatable));

        var tooMany = await Fails(() => _pos.AllocateAsync(line.Id, new AllocateRequest(101, null)));
        Assert.Equal(ProblemCodes.AllocationExceeded, tooMany.Problem.Code);
        var violation = Assert.Single(tooMany.Problem.Violations!);
        Assert.Equal((101m, 100m), (violation.Current, violation.Limit));

        var allocated = await _pos.AllocateAsync(line.Id, new AllocateRequest(40, "uji"));
        Assert.Equal((40, 60), (allocated.AllocatedQty, allocated.RemainingPlanQty));
        Assert.Equal(PoStatus.InProgress, (await _pos.GetAsync(po.Id)).Status);
        Assert.Equal(60, (await _pos.GetStockAvailabilityAsync(line.Id)).MaxAllocatable);
    }

    [Fact]
    public async Task Pelepasan_tidak_boleh_melebihi_alokasi_dan_tercatat_negatif_di_riwayat()
    {
        var line = (await _pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        await _pos.AllocateAsync(line.Id, new AllocateRequest(40, null));

        Assert.Equal(ProblemCodes.AllocationExceeded, (await Fails(() => _pos.ReleaseAllocationAsync(line.Id, new ReleaseAllocationRequest(41, null)))).Problem.Code);
        await _pos.ReleaseAllocationAsync(line.Id, new ReleaseAllocationRequest(15, null));

        var entries = await _pos.GetAllocationsAsync(MockPoApi.SeedPoId);
        Assert.Equal([-15, 40], entries.Select(e => e.Qty));
        Assert.Equal(AllocationKind.Release, entries[0].Kind);
        Assert.Equal(25, (await _pos.GetAsync(MockPoApi.SeedPoId)).AllocatedQty);
    }

    [Fact]
    public async Task Revisi_di_bawah_komitmen_ditolak_dengan_rincian_dan_baris_terpakai_terkunci_dan_tidak_dapat_dihapus()
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        var first = po.Lines[0];
        await _pos.AllocateAsync(first.Id, new AllocateRequest(40, null));
        po = await _pos.GetAsync(po.Id);
        SavePoLine Same(PoLineDto l, int? qty = null) => new(l.Id, l.FgId, l.ColorSize.ColorId, l.ColorSize.SizeId, l.DeliveryDate, qty ?? l.Qty, null);
        SavePoRequest Req(IEnumerable<SavePoLine> lines) => new(po.CustomerId, po.PoDate, po.CustomerPoNumber, null, lines.ToList(), false, po.RowVersion);

        var below = await Fails(() => _pos.UpdateAsync(po.Id, Req(po.Lines.Select(l => Same(l, l.Id == first.Id ? 30 : null)))));
        Assert.Equal(ProblemCodes.PoQtyBelowCommitted, below.Problem.Code);
        Assert.Contains("Qty minimal 40 (Terkirim 0 + Dialokasikan 40 + Dalam Produksi 0)", Assert.Single(below.Problem.Violations!).Message);

        var removed = await Fails(() => _pos.UpdateAsync(po.Id, Req(po.Lines.Skip(1).Select(l => Same(l)))));
        Assert.Equal(ProblemCodes.PoLineInUse, removed.Problem.Code);

        var ok = await _pos.UpdateAsync(po.Id, Req(po.Lines.Select(l => Same(l, l.Id == first.Id ? 60 : null))));
        Assert.Equal(60, ok.Lines[0].Qty);
        Assert.True(ok.Lines[0].IsLocked);
        Assert.True(ok.Lines[0].MinAllowedQty == 40);
    }

    [Fact]
    public async Task Batalkan_butuh_alasan_melepas_alokasi_otomatis_dan_ditolak_bila_ada_produksi()
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        await _pos.AllocateAsync(po.Lines[0].Id, new AllocateRequest(40, null));
        po = await _pos.GetAsync(po.Id);

        Assert.Contains("reason", (await Fails(() => _pos.CancelAsync(po.Id, new ReasonRequest(" ", po.RowVersion)))).Problem.Errors!.Keys);
        _pos.AdjustCommitted(po.Lines[1].Id, deltaInProduction: 10);
        var blocked = await Fails(async () => await _pos.CancelAsync(po.Id, new ReasonRequest("salah", (await _pos.GetAsync(po.Id)).RowVersion)));
        Assert.Equal("po.cancel-blocked", blocked.Problem.Code);

        _pos.AdjustCommitted(po.Lines[1].Id, deltaInProduction: -10);
        var cancelled = await _pos.CancelAsync(po.Id, new ReasonRequest("customer batal", (await _pos.GetAsync(po.Id)).RowVersion));
        Assert.Equal((PoStatus.Cancelled, 0, "customer batal"), (cancelled.Status, cancelled.AllocatedQty, cancelled.CancelReason));
        Assert.Empty(cancelled.AllowedActions);
        Assert.Contains(await _pos.GetAllocationsAsync(po.Id), e => e.Source == AllocationSource.PoCancel);
    }

    [Fact]
    public async Task Filter_cepat_Terlambat_JatuhTempo7_PoCustomerKosong_dan_SisaRencana_dihitung_server()
    {
        _pos.Today = () => new DateOnly(2026, 12, 30); // semua tanggal kirim seed sudah lewat
        Assert.Equal(1, (await _pos.ListAsync(new PoListQuery { Terlambat = true })).TotalCount);
        Assert.True((await _pos.ListAsync(new PoListQuery { Terlambat = true })).Items[0].HasLateLines);
        _pos.Today = () => DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(1, (await _pos.ListAsync(new PoListQuery { SisaRencana = true })).TotalCount);
        Assert.Equal(0, (await _pos.ListAsync(new PoListQuery { PoCustomerKosong = true })).TotalCount); // seed punya nomor
        var counts = await _pos.QuickCountsAsync();
        Assert.Equal(4, counts.Count);
    }

    [Fact]
    public async Task Pengait_modul_lain_menyelesaikan_PO_saat_semua_baris_terkirim()
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        foreach (var l in po.Lines) { _pos.AdjustCommitted(l.Id, deltaDelivered: l.Qty); }

        Assert.Equal(PoStatus.Completed, (await _pos.GetAsync(po.Id)).Status);
    }
}

public sealed class PoPageTests : WebTestBase
{
    private static readonly string[] AllPo = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("CustomerPO.", StringComparison.Ordinal) || p.Key.StartsWith("WorkOrder.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly MockPoApi _pos;
    private readonly MockFgApi _fgs;
    private readonly MockColorApi _colors = new();
    private readonly MockSizeApi _sizes = new();
    private readonly MockCustomerApi _customers = new();

    public PoPageTests()
    {
        var processes = new MockProcessApi();
        _fgs = new MockFgApi(_sizes, processes, _customers);
        _pos = new MockPoApi(_customers, _fgs, _colors, _sizes);
        Services.AddSingleton<IPurchaseOrdersApi>(_pos);
        Services.AddSingleton<ICustomerApi>(_customers);
        Services.AddSingleton<IFgApi>(_fgs);
        Services.AddSingleton<IColorApi>(_colors);
        Services.AddSingleton<ISizeApi>(_sizes);
        SignIn(AllPo);
    }

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelectorAll("fluent-button, button.ant-btn").Last();

    // ===== Daftar =====

    [Fact]
    public void Daftar_menampilkan_kode_status_tag_belum_ada_dan_bar_progres_dengan_angka_teks()
    {
        var cut = Render<PoListPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        var draft = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("PO-202611-0002"));
        Assert.Contains("Belum ada", draft.TextContent);
        var seed = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("PO-202611-0001"));
        Assert.Contains("PO/25/118", seed.TextContent);
        Assert.Contains("Confirmed", seed.TextContent);
        Assert.Equal("Kirim 0, Alok 0, Prod 0 dari 500", seed.QuerySelector("[role=img][aria-label]")!.GetAttribute("aria-label"));
    }

    [Fact]
    public void Chip_cepat_adalah_tombol_toggle_dengan_aria_pressed_dan_jumlah_dari_server()
    {
        var cut = Render<PoListPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));

        var chip = cut.FindAll(".gms-chipbtn").Single(c => c.TextContent.Contains("PO Customer belum ada"));
        Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        Assert.Contains("0", chip.QuerySelector(".gms-chipcount")!.TextContent);
        cut.FindAll(".gms-chipbtn").Single(c => c.TextContent.Contains("Sisa rencana")).Click();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Equal("true", cut.FindAll(".gms-chipbtn").Single(c => c.TextContent.Contains("Sisa rencana")).GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Filter_status_dan_pencarian_kosong_menampilkan_state_dengan_Reset_filter()
    {
        var cut = Render<PoListPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));

        cut.Find("#po-status").Change("Cancelled");

        cut.WaitForAssertion(() => Assert.Contains("Tidak ada PO untuk filter ini", cut.Markup));
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Reset filter")).Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
    }

    // ===== Form =====

    private static IRenderedComponent<PoFormPage> Form(IRenderedComponent<PoFormPage> cut) => cut;

    [Fact]
    public void Form_buat_PO_menambah_blok_mengisi_Qty_dan_menyimpan_sebagai_Draft()
    {
        var cut = Render<PoFormPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));
        var selects = cut.FindAll("form select");
        selects[0].Change(Sync(_customers.LookupAsync(null))[0].Id.ToString()); // Customer
        selects[1].Change(MockFgApi.KemejaId.ToString());                          // FG
        selects[2].Change(Sync(_colors.LookupAsync(null))[1].Id.ToString());      // Color
        cut.Find(".gms-toolbar input[type=date]").Input("2026-12-20");           // tanggal kirim blok
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Tambah blok FG")).Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-block")));
        Assert.Equal(["S", "M", "L", "XL"], cut.FindAll(".gms-block thead th").Skip(1).Take(4).Select(t => t.TextContent));
        cut.Find(".gms-block input[aria-label='Qty, Size M']").Input("200");
        cut.Find(".gms-block input[aria-label='Qty, Size S']").Input("100");
        Assert.Contains("Total keseluruhan: 300 pcs", cut.Markup);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/purchase-orders/[0-9a-f-]{36}$", Nav.Uri));
        Assert.Equal(3, Sync(_pos.ListAsync(new PoListQuery())).TotalCount);
    }

    [Fact]
    public void Form_tanpa_qty_atau_customer_menampilkan_ringkasan_galat_tanpa_menyimpan()
    {
        var cut = Render<PoFormPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));

        cut.Find("form").Submit();

        Assert.Contains("Customer dan Tanggal PO wajib diisi", cut.Find(".gms-formsummary").TextContent);
        Assert.Equal(2, Sync(_pos.ListAsync(new PoListQuery())).TotalCount);
    }

    [Fact]
    public void Form_ubah_menampilkan_blok_dikelompokkan_dan_batas_minimum_untuk_baris_terpakai()
    {
        var line = Sync(_pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        Sync(_pos.AllocateAsync(line.Id, new AllocateRequest(40, null)));
        var cut = Render<PoFormPage>(p => p.Add(x => x.Id, (Guid?)MockPoApi.SeedPoId));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gms-block")));
        Assert.Equal("100", cut.Find(".gms-block input[aria-label='Qty, Size S']").GetAttribute("value"));
        Assert.Contains("Size S: minimal 40", cut.Find(".gms-block").TextContent);
        Assert.Contains("terkunci", cut.Find(".gms-block").TextContent);
        Assert.Contains("Total keseluruhan: 500 pcs", cut.Markup);
    }

    [Fact]
    public void Form_ubah_qty_di_bawah_komitmen_menampilkan_pesan_server_dan_tidak_menavigasi()
    {
        var line = Sync(_pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        Sync(_pos.AllocateAsync(line.Id, new AllocateRequest(40, null)));
        var cut = Render<PoFormPage>(p => p.Add(x => x.Id, (Guid?)MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gms-block")));

        cut.Find(".gms-block input[aria-label='Qty, Size S']").Input("30");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Qty minimal 40", cut.Markup));
        Assert.DoesNotContain("/purchase-orders/" + MockPoApi.SeedPoId, Nav.Uri.Replace("/edit", ""));
    }

    [Fact]
    public void Form_tanpa_izin_menampilkan_state_tanpa_akses()
    {
        SignIn(Permissions.CustomerPO.View);

        var cut = Render<PoFormPage>();

        cut.WaitForAssertion(() => Assert.Contains("Anda tidak memiliki akses", cut.Markup));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Form_blok_ganda_ditandai_dan_peringatan_tanggal_kirim_lebih_awal_dari_tanggal_PO()
    {
        var cut = Render<PoFormPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));
        void AddBlock()
        {
            var selects = cut.FindAll("form select");
            selects[1].Change(MockFgApi.KemejaId.ToString());
            selects[2].Change(Sync(_colors.LookupAsync(null))[0].Id.ToString());
            cut.Find(".gms-toolbar input[type=date]").Input("2020-01-01");
            cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Tambah blok FG")).Click();
        }

        AddBlock();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".gms-block")));
        AddBlock();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".gms-block").Count));
        for (var i = 0; i < 2; i++) { cut.FindAll(".gms-block")[i].QuerySelector("input[aria-label='Qty, Size S']")!.Input("10"); }

        Assert.Equal(2, cut.FindAll(".gms-block.dup").Count);
        Assert.Contains("ganda", cut.Find(".gms-block").TextContent);
        Assert.Contains("lebih awal dari Tanggal PO", cut.Find(".gms-block").TextContent);
    }

    // ===== Detail =====

    [Fact]
    public void Detail_menampilkan_ringkasan_baris_aksi_kontekstual_dan_nomor_PO_customer()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));

        cut.WaitForAssertion(() => Assert.Contains("PO-202611-0001", cut.Find("h1").TextContent));
        Assert.Equal(4, cut.FindAll("tbody tr").Count);
        Assert.Contains("PO/25/118", cut.Find(".gms-customerpo").TextContent);
        Assert.Contains("Ubah", cut.Find(".gms-pageheader-actions").TextContent);
        Assert.Contains("Buat WO", cut.Find(".gms-pageheader-actions").TextContent);
        Assert.DoesNotContain("Konfirmasi", cut.Find(".gms-pageheader-actions").TextContent); // bukan Draft
        Assert.Equal("Confirmed", cut.Find(".gms-pageheader .gms-tag").TextContent.Trim());
    }

    [Fact]
    public void Detail_Draft_menawarkan_Konfirmasi_dan_dialog_menyebut_jumlah_baris_serta_total()
    {
        var draftId = Sync(_pos.ListAsync(new PoListQuery { Status = PoStatus.Draft })).Items[0].Id;
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, draftId));
        cut.WaitForAssertion(() => Assert.Contains("Draft", cut.Markup));

        cut.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Contains("Konfirmasi")).Click();
        Assert.Contains("1 baris, total 80 pcs", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent);
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal("Confirmed", cut.Find(".gms-pageheader .gms-tag").TextContent.Trim()));
    }

    [Fact]
    public void Ubah_nomor_PO_customer_inline_dan_duplikat_ditampilkan_sebagai_pesan()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.Contains("PO/25/118", cut.Markup));
        var other = Sync(_pos.CreateAsync(new SavePoRequest(Sync(_pos.GetAsync(MockPoApi.SeedPoId)).CustomerId, new DateOnly(2026, 11, 9), "PO/99", null,
            [new SavePoLine(null, MockFgApi.KemejaId, Sync(_colors.LookupAsync(null))[1].Id, Sync(_sizes.LookupAsync(null))[0].Id, new DateOnly(2026, 12, 1), 5, null)])));

        cut.Find("[aria-label='Ubah No. PO Customer']").Click();
        cut.Find("#po-number").Input("PO/99");
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent == "Simpan").Click();
        cut.WaitForAssertion(() => Assert.Contains($"sudah dipakai {other.Code}", cut.Markup));

        cut.Find("#po-number").Input("PO/25/120");
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent == "Simpan").Click();
        cut.WaitForAssertion(() => Assert.Contains("PO/25/120", cut.Find(".gms-customerpo").TextContent));
    }

    [Fact]
    public void Dialog_alokasi_memuat_stok_menjelaskan_batas_menahan_qty_berlebih_dan_menyimpan()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));

        cut.FindAll("tbody fluent-button, tbody button.ant-btn").First(b => b.GetAttribute("aria-label")!.StartsWith("Alokasi KMJ-001")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Tersedia 120", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent));
        var dialog = () => cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null);
        Assert.Contains("Maks 100", dialog().TextContent);
        dialog().QuerySelector("input")!.Input("101");
        Assert.Contains("melebihi batas", dialog().TextContent);
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        dialog().QuerySelector("input")!.Input("40");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal("40", cut.FindAll("tbody tr")[0].QuerySelectorAll("td")[5].TextContent));
        Assert.Equal("In Progress", cut.Find(".gms-pageheader .gms-tag").TextContent.Trim());
    }

    [Fact]
    public void Konflik_alokasi_menampilkan_pesan_stok_berubah_dan_memperbarui_batas()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        cut.FindAll("tbody fluent-button, tbody button.ant-btn").First(b => b.GetAttribute("aria-label")!.StartsWith("Alokasi KMJ-001")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Maks 100", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent));
        var line = Sync(_pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelector("input")!.Input("90");
        _pos.AdjustStock(line.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, -80); // stok berkurang oleh pengguna lain

        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Stok tersedia berubah menjadi 40", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent));
        Assert.Contains("Maks 40", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent);
    }

    [Fact]
    public void Batalkan_PO_meminta_alasan_wajib_dan_menyembunyikan_aksi_setelahnya()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.Contains("Batalkan", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Contains("Batalkan")).Click();
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelector("textarea")!.Input("customer batal");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Dibatalkan: customer batal", cut.Markup));
        Assert.Empty(cut.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn"));
    }

    [Fact]
    public void Tab_Alokasi_menampilkan_riwayat_dan_state_kosong()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=tab]")));

        cut.FindAll("[role=tab]").Single(t => t.TextContent == "Alokasi").Click();
        cut.WaitForAssertion(() => Assert.Contains("Belum ada alokasi stok", cut.Markup));
    }

    [Fact]
    public void PO_tidak_ditemukan_menampilkan_state_galat()
    {
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => Assert.Contains("Data tidak ditemukan", cut.Markup));
    }

    [Fact]
    public void Tanpa_izin_Edit_dan_Alokasi_tombolnya_tidak_dirender()
    {
        SignIn(Permissions.CustomerPO.View);
        var cut = Render<PoDetailPage>(p => p.Add(x => x.Id, MockPoApi.SeedPoId));

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        Assert.Empty(cut.FindAll(".gms-pageheader-actions *"));
        Assert.Empty(cut.FindAll("tbody fluent-button, tbody button.ant-btn"));
        Assert.Empty(cut.FindAll("[aria-label='Ubah No. PO Customer']"));
    }
}
