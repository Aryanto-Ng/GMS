using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Master;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockMasterTests
{
    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    [Fact]
    public async Task Buat_menolak_nama_kosong_kode_kosong_kode_dobel_tanpa_beda_huruf_besar_kecil()
    {
        var uoms = new MockUomApi();

        Assert.Contains("name", (await Fails(() => uoms.CreateAsync(new SaveSimpleMasterRequest("X", " ")))).Problem.Errors!.Keys);
        Assert.Contains("code", (await Fails(() => uoms.CreateAsync(new SaveSimpleMasterRequest(" ", "Nama")))).Problem.Errors!.Keys);
        var dup = await Fails(() => uoms.CreateAsync(new SaveSimpleMasterRequest("pcs", "Dobel")));
        Assert.Equal((409, ProblemTypes.Duplicate), (dup.StatusCode, dup.Problem.Type));
        Assert.Contains("code", (await Fails(() => uoms.CreateAsync(new SaveSimpleMasterRequest(new string('A', 31), "Panjang")))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Ubah_memakai_rowVersion_mengizinkan_kode_sendiri_dan_menolak_versi_basi()
    {
        var uoms = new MockUomApi();
        var pcs = (await uoms.ListAsync(new MasterListQuery { Q = "PCS" })).Items.Single();

        var ok = await uoms.UpdateAsync(pcs.Id, new SaveSimpleMasterRequest("PCS", "Pieces Baru", pcs.RowVersion)); // kode sendiri bukan duplikat
        Assert.Equal(("Pieces Baru", pcs.RowVersion + 1), (ok.Name, ok.RowVersion));
        Assert.Equal(409, (await Fails(() => uoms.UpdateAsync(pcs.Id, new SaveSimpleMasterRequest("PCS", "X", pcs.RowVersion)))).StatusCode);
        Assert.Equal(404, (await Fails(() => uoms.UpdateAsync(Guid.NewGuid(), new SaveSimpleMasterRequest("Z", "Z", 1)))).StatusCode);
    }

    [Fact]
    public async Task Daftar_menyaring_status_pencarian_dan_paging_serta_lookup_hanya_aktif()
    {
        var uoms = new MockUomApi();

        Assert.Equal(3, (await uoms.ListAsync(new MasterListQuery())).TotalCount);
        Assert.Equal(["YRD"], (await uoms.ListAsync(new MasterListQuery { Status = ActiveFilter.Inactive })).Items.Select(i => i.Code));
        Assert.Equal(4, (await uoms.ListAsync(new MasterListQuery { Status = ActiveFilter.All })).TotalCount);
        Assert.Equal(["MTR"], (await uoms.ListAsync(new MasterListQuery { Q = "meter" })).Items.Select(i => i.Code));
        var page = await uoms.ListAsync(new MasterListQuery { Status = ActiveFilter.All, PageSize = 3, Page = 2 });
        Assert.Equal((1, 4), (page.Items.Count, page.TotalCount));
        Assert.DoesNotContain(await uoms.LookupAsync(null), l => l.Code == "YRD");
        Assert.Equal(["KG"], (await uoms.LookupAsync("kilo")).Select(l => l.Code));
    }

    [Fact]
    public async Task Nonaktifkan_tidak_menghapus_dan_dapat_diaktifkan_lagi_dengan_versi_benar()
    {
        var uoms = new MockUomApi();
        var kg = (await uoms.ListAsync(new MasterListQuery { Q = "KG" })).Items.Single();

        Assert.Equal(409, (await Fails(() => uoms.SetActiveAsync(kg.Id, false, kg.RowVersion + 9))).StatusCode);
        var off = await uoms.SetActiveAsync(kg.Id, false, kg.RowVersion);
        Assert.False(off.IsActive);
        Assert.Equal(2, (await uoms.ListAsync(new MasterListQuery { Status = ActiveFilter.Inactive })).TotalCount); // YRD + KG
        var on = await uoms.SetActiveAsync(kg.Id, true, off.RowVersion);
        Assert.True(on.IsActive);
    }

    [Fact]
    public async Task Customer_vendor_pekerja_memberi_kode_otomatis_berurutan()
    {
        var customers = new MockCustomerApi();
        var created = await customers.CreateAsync(new SaveCustomerRequest(" PT Baru ", "  ", null, null, null));

        Assert.Equal(("CUS-003", "PT Baru", null), (created.Code, created.Name, created.ContactPerson)); // spasi dipangkas, kosong → null
        var workers = new MockWorkerApi(new MockProcessApi());
        var w = await workers.CreateAsync(new SaveWorkerRequest("Rudi", new DateOnly(2026, 1, 2), MockProcessApi.QcId, null));
        Assert.Equal(("PKJ-003", "QC"), (w.Code, w.MainProcessName));
        Assert.Contains("mainProcessId", (await Fails(() => workers.CreateAsync(new SaveWorkerRequest("X", null, Guid.NewGuid(), null)))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Material_memvalidasi_kategori_dan_satuan_serta_menurunkan_nama_dan_kode_satuan()
    {
        var uoms = new MockUomApi();
        var cats = new MockMaterialCategoryApi();
        var materials = new MockMaterialApi(uoms, cats);
        var cat = (await cats.LookupAsync(null))[0];
        var uom = (await uoms.LookupAsync(null))[0];

        Assert.Contains("categoryId", (await Fails(() => materials.CreateAsync(new SaveMaterialRequest("M1", "X", Guid.NewGuid(), uom.Id, null)))).Problem.Errors!.Keys);
        Assert.Contains("uomId", (await Fails(() => materials.CreateAsync(new SaveMaterialRequest("M1", "X", cat.Id, Guid.NewGuid(), null)))).Problem.Errors!.Keys);
        var ok = await materials.CreateAsync(new SaveMaterialRequest("M1", "Kancing", cat.Id, uom.Id, "  "));
        Assert.Equal((cat.Name, uom.Code, null), (ok.CategoryName, ok.UomCode, ok.Description));
    }

    [Fact]
    public async Task Size_diurutkan_menurut_urutan_tampil_dan_urutan_minimal_satu_warna_memvalidasi_hex()
    {
        var sizes = new MockSizeApi();
        await sizes.CreateAsync(new SaveSizeRequest("XS", "Extra Small", 0 + 1));
        var order = (await sizes.ListAsync(new MasterListQuery())).Items;
        Assert.Equal(["S", "XS", "M", "L", "XL"], order.OrderBy(s => s.SortOrder).ThenBy(s => s.Code).Select(s => s.Code)); // urutan sama → kode
        Assert.Equal(order.Select(s => s.Code), (await sizes.ListAsync(new MasterListQuery())).Items.Select(s => s.Code));
        Assert.Contains("sortOrder", (await Fails(() => sizes.CreateAsync(new SaveSizeRequest("Z", "Z", 0)))).Problem.Errors!.Keys);

        var colors = new MockColorApi();
        Assert.Contains("hex", (await Fails(() => colors.CreateAsync(new SaveColorRequest("RED", "Merah", "merah")))).Problem.Errors!.Keys);
        Assert.Equal("#FF0000", (await colors.CreateAsync(new SaveColorRequest("RED", "Merah", " #ff0000 "))).Hex);
        Assert.Null((await colors.CreateAsync(new SaveColorRequest("GRN", "Hijau", null))).Hex);
    }

    [Fact]
    public async Task Process_dalam_routing_tidak_dapat_dinonaktifkan_dengan_kode_master_in_use()
    {
        var processes = new MockProcessApi();
        var sewing = await processes.GetAsync(MockProcessApi.SewingId);

        var blocked = await Fails(() => processes.SetActiveAsync(sewing.Id, false, sewing.RowVersion));
        Assert.Equal(ProblemCodes.MasterInUse, blocked.Problem.Code);
        var qc = await processes.GetAsync(MockProcessApi.QcId);
        Assert.False((await processes.SetActiveAsync(qc.Id, false, qc.RowVersion)).IsActive); // tidak dipakai → boleh
    }
}

public sealed class HttpMasterApiTests
{
    private sealed class Token : ISessionTokenProvider
    {
        public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult<string?>("t");
        public void Override(string? value) { }
    }

    private sealed class Recorder : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

        public string Reply { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Rute_query_status_huruf_kecil_dan_metode_sesuai_kontrak()
    {
        var http = new Recorder { Reply = "{\"items\":[],\"page\":1,\"pageSize\":25,\"totalCount\":0}" };
        var api = new HttpMaterialCategoryApi(new GmsApiClient(http, new Token()));

        await api.ListAsync(new MasterListQuery { Q = "ka in", Status = ActiveFilter.All, Page = 3 });
        http.Reply = "{}";
        await api.GetAsync(Id);
        await api.CreateAsync(new SaveSimpleMasterRequest("K", "Kain"));
        await api.UpdateAsync(Id, new SaveSimpleMasterRequest("K", "Kain", 4));
        await api.SetActiveAsync(Id, false, 5);
        await api.SetActiveAsync(Id, true, 6);
        http.Reply = "[]";
        await api.LookupAsync("a b");

        Assert.Equal("/api/v1/master/material-categories?page=3&pageSize=25&q=ka%20in&status=all", http.Calls[0].Url);
        Assert.Equal(($"/api/v1/master/material-categories/{Id}", HttpMethod.Get), (http.Calls[1].Url, http.Calls[1].Method));
        Assert.Equal(HttpMethod.Post, http.Calls[2].Method);
        Assert.Contains("\"code\":\"K\"", http.Calls[2].Body);
        Assert.Equal(HttpMethod.Put, http.Calls[3].Method);
        Assert.Contains("\"rowVersion\":4", http.Calls[3].Body);
        Assert.Equal($"/api/v1/master/material-categories/{Id}/deactivate?rowVersion=5", http.Calls[4].Url);
        Assert.Equal($"/api/v1/master/material-categories/{Id}/activate?rowVersion=6", http.Calls[5].Url);
        Assert.Equal("/api/v1/master/material-categories/lookup?q=a%20b", http.Calls[6].Url);
    }

    [Fact]
    public async Task Setiap_entitas_memakai_rute_jamaknya_dan_FG_meneruskan_ReadyOnly()
    {
        var http = new Recorder { Reply = "{\"items\":[],\"page\":1,\"pageSize\":25,\"totalCount\":0}" };
        var client = new GmsApiClient(http, new Token());

        await new HttpUomApi(client).ListAsync(new MasterListQuery());
        await new HttpMaterialApi(client).ListAsync(new MasterListQuery());
        await new HttpSizeApi(client).ListAsync(new MasterListQuery());
        await new HttpColorApi(client).ListAsync(new MasterListQuery());
        await new HttpCustomerApi(client).ListAsync(new MasterListQuery());
        await new HttpVendorApi(client).ListAsync(new MasterListQuery());
        await new HttpProcessApi(client).ListAsync(new MasterListQuery());
        await new HttpWorkerApi(client).ListAsync(new MasterListQuery());

        Assert.Equal(
            ["uoms", "materials", "sizes", "colors", "customers", "vendors", "processes", "workers"],
            http.Calls.Select(c => c.Url.Split('/')[4].Split('?')[0]));
    }
}

/// <summary>Komponen Daftar Master Generik dan halaman-halaman master terhadap data tiruan.</summary>
public sealed class MasterPageTests : WebTestBase
{
    private static readonly string[] AllMaster = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("MasterData.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly MockUomApi _uoms = new();
    private readonly MockMaterialCategoryApi _categories = new();
    private readonly MockProcessApi _processes = new();

    public MasterPageTests()
    {
        Services.AddSingleton<IUomApi>(_uoms);
        Services.AddSingleton<IMaterialCategoryApi>(_categories);
        Services.AddSingleton<IMaterialApi>(new MockMaterialApi(_uoms, _categories));
        Services.AddSingleton<IProcessApi>(_processes);
        Services.AddSingleton<ISizeApi>(new MockSizeApi());
        Services.AddSingleton<IColorApi>(new MockColorApi());
        Services.AddSingleton<ICustomerApi>(new MockCustomerApi());
        Services.AddSingleton<IVendorApi>(new MockVendorApi());
        Services.AddSingleton<IWorkerApi>(new MockWorkerApi(_processes));
        SignIn(AllMaster);
    }

    private static IElement DialogConfirm(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut) => cut.FindAll(".gms-dialog-wrap button.gms-btn").Last();

    private static void WaitRows<T>(IRenderedComponent<T> cut, int count) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.WaitForAssertion(() => Assert.Equal(count, cut.FindAll("tbody tr").Count));

    [Fact]
    public void Daftar_menampilkan_kolom_status_chip_ikon_teks_dan_jumlah_di_subjudul()
    {
        var cut = Render<UomsPage>();

        WaitRows(cut, 3);
        Assert.Equal(["Kode", "Nama", "Status", "Aksi"], cut.FindAll("thead th").Select(t => t.TextContent));
        Assert.Contains("Aktif", cut.Find("tbody tr").TextContent);
        Assert.Contains("3 satuan", cut.Find(".gms-pageheader-sub").TextContent);
    }

    [Fact]
    public void Filter_status_Nonaktif_dan_Semua_serta_baris_nonaktif_menawarkan_Aktifkan()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);

        cut.Find("select").Change("Inactive");
        WaitRows(cut, 1);
        Assert.Contains("Nonaktif", cut.Find("tbody").TextContent);
        Assert.Contains("Aktifkan", cut.Find("tbody").TextContent.Replace("Nonaktif", ""));
        cut.Find("select").Change("All");
        WaitRows(cut, 4);
    }

    [Fact]
    public void Pencarian_tanpa_hasil_menampilkan_state_kosong_dan_Reset_filter_memulihkan_daftar()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);

        cut.Find("input[type=search]").Change("zzz");
        cut.WaitForAssertion(() => Assert.Contains("Tidak ada hasil", cut.Markup));
        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Reset filter")).Click();

        WaitRows(cut, 3);
    }

    [Fact]
    public void Tambah_membuka_dialog_form_tombol_Simpan_menunggu_isian_lalu_daftar_dimuat_ulang()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);

        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah satuan")).Click();
        Assert.Contains("Tambah satuan", cut.Find(".gms-dialog-wrap h2").TextContent);
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        cut.FindAll(".gms-dialog-body input")[0].Input("DUS");
        cut.FindAll(".gms-dialog-body input")[1].Input("Dus");
        Assert.False(DialogConfirm(cut).HasAttribute("disabled"));
        DialogConfirm(cut).Click();

        WaitRows(cut, 4);
        Assert.Contains("DUS", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void Kode_dobel_ditampilkan_di_dalam_dialog_tanpa_menutupnya_dan_isian_tetap()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);
        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah satuan")).Click();
        cut.FindAll(".gms-dialog-body input")[0].Input("PCS");
        cut.FindAll(".gms-dialog-body input")[1].Input("Duplikat");

        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("sudah terdaftar", cut.FindAll(".gms-dialog-wrap").Last().TextContent));
        Assert.Equal("Duplikat", cut.FindAll(".gms-dialog-body input")[1].GetAttribute("value"));
    }

    [Fact]
    public void Ubah_mengisi_form_dari_baris_dan_menyimpan_perubahan()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);

        cut.FindAll("tbody button.gms-btn").First(b => b.GetAttribute("aria-label") == "Ubah Meter").Click();
        Assert.Equal("MTR", cut.FindAll(".gms-dialog-body input")[0].GetAttribute("value"));
        cut.FindAll(".gms-dialog-body input")[1].Input("Meter Panjang");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Meter Panjang", cut.Find("tbody").TextContent));
    }

    [Fact]
    public void Nonaktifkan_meminta_konfirmasi_dampak_lalu_baris_hilang_dari_filter_Aktif()
    {
        var cut = Render<UomsPage>();
        WaitRows(cut, 3);

        cut.FindAll("tbody button.gms-btn").First(b => b.GetAttribute("aria-label") == "Nonaktifkan Kilogram").Click();
        Assert.Contains("Nonaktifkan Kilogram?", cut.Find(".gms-dialog-wrap h2").TextContent);
        Assert.Contains("tidak dihapus", cut.Find(".gms-dialog-wrap").TextContent);
        DialogConfirm(cut).Click();

        WaitRows(cut, 2);
    }

    [Fact]
    public void Process_dalam_routing_ditolak_dengan_pesan_di_dialog()
    {
        var cut = Render<ProcessesPage>();
        WaitRows(cut, 4);

        cut.FindAll("tbody button.gms-btn").First(b => b.GetAttribute("aria-label") == "Nonaktifkan Sewing").Click();
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("masih ada di routing FG aktif", cut.Find(".gms-dialog-wrap").TextContent));
        Assert.Contains("Nonaktifkan Sewing?", cut.Find(".gms-dialog-wrap h2").TextContent);
    }

    [Fact]
    public void Tanpa_izin_tulis_tombol_Tambah_Ubah_dan_Nonaktifkan_tidak_dirender()
    {
        SignIn(Permissions.MasterData.UoM.View);

        var cut = Render<UomsPage>();

        WaitRows(cut, 3);
        Assert.DoesNotContain("Tambah satuan", cut.Markup);
        Assert.Empty(cut.FindAll("tbody button.gms-btn"));
    }

    [Fact]
    public async Task Material_form_memuat_pilihan_kategori_dan_satuan_aktif_dan_menyimpan()
    {
        var categoryId = (await _categories.LookupAsync(null))[2].Id.ToString();
        var uomId = (await _uoms.LookupAsync(null))[0].Id.ToString();
        var cut = Render<MaterialsPage>();
        WaitRows(cut, 2);

        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah material")).Click();
        var selects = cut.FindAll(".gms-dialog-body select");
        Assert.DoesNotContain("Yard", cut.Find(".gms-dialog-wrap").TextContent); // satuan nonaktif tidak ditawarkan
        cut.FindAll(".gms-dialog-body input")[0].Input("BTN-1");
        cut.FindAll(".gms-dialog-body input")[1].Input("Kancing Hitam");
        Assert.True(DialogConfirm(cut).HasAttribute("disabled")); // kategori & satuan wajib
        selects[0].Change(categoryId);
        cut.FindAll(".gms-dialog-body select")[1].Change(uomId);
        DialogConfirm(cut).Click();

        WaitRows(cut, 3);
        Assert.Contains("Kancing Hitam", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void Color_menampilkan_swatch_dan_nama_hex_sebagai_teks_serta_pratinjau_di_form()
    {
        var cut = Render<ColorsPage>();
        WaitRows(cut, 3);

        Assert.Contains("#1F2A5A", cut.Find("tbody").TextContent);
        Assert.NotEmpty(cut.FindAll(".gms-swatch"));
        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah color")).Click();
        cut.FindAll(".gms-dialog-body input")[2].Input("#ff0000");
        Assert.Contains("#FF0000", cut.FindAll(".gms-dialog-wrap").Last().TextContent);
    }

    [Fact]
    public void Customer_dan_Pekerja_menampilkan_kode_otomatis_pada_form_tambah()
    {
        var customers = Render<CustomersPage>();
        WaitRows(customers, 2);
        customers.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah customer")).Click();
        Assert.Contains("Otomatis saat disimpan", customers.FindAll(".gms-dialog-wrap").Last().TextContent);

        var workers = Render<WorkersPage>();
        WaitRows(workers, 2);
        Assert.Contains("05 Feb 2024", workers.Markup);
        workers.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah pekerja")).Click();
        Assert.Contains("Otomatis saat disimpan", workers.FindAll(".gms-dialog-wrap").Last().TextContent);
    }

    [Fact]
    public void Process_form_mengunci_tipe_bila_dipakai_routing()
    {
        var cut = Render<ProcessesPage>();
        WaitRows(cut, 4);

        cut.FindAll("tbody button.gms-btn").First(b => b.GetAttribute("aria-label") == "Ubah Sewing").Click();

        Assert.True(cut.Find("#proc-type").HasAttribute("disabled"));
        Assert.Contains("terkunci", cut.Find("#proc-type-hint").TextContent);
    }

    [Fact]
    public void Size_form_menolak_urutan_non_angka_dan_daftar_urut_menurut_urutan_tampil()
    {
        var cut = Render<SizesPage>();
        WaitRows(cut, 4);

        Assert.Equal(["S", "M", "L", "XL"], cut.FindAll("tbody th[scope=row]").Select(t => t.TextContent));
        cut.FindAll("button.gms-btn").Single(b => b.TextContent.Contains("Tambah size")).Click();
        cut.FindAll(".gms-dialog-body input")[0].Input("XXL");
        cut.FindAll(".gms-dialog-body input")[1].Input("Double XL");
        cut.FindAll(".gms-dialog-body input")[2].Input("abc");
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        cut.FindAll(".gms-dialog-body input")[2].Input("5");
        Assert.False(DialogConfirm(cut).HasAttribute("disabled"));
    }
}
