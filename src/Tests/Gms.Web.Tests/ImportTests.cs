using System.Net;
using System.Text;
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

public sealed class MockImportTests
{
    private readonly MockUomApi _uoms = new();
    private readonly MockMaterialCategoryApi _cats = new();
    private readonly MockProcessApi _processes = new();
    private readonly MockSizeApi _sizes = new();
    private readonly MockCustomerApi _customers = new();
    private readonly MockMaterialApi _materials;
    private readonly MockWorkerApi _workers;
    private readonly MockFgApi _fgs;
    private readonly MockImportApi _import;

    public MockImportTests()
    {
        _materials = new MockMaterialApi(_uoms, _cats);
        _workers = new MockWorkerApi(_processes);
        _fgs = new MockFgApi(_sizes, _processes, _customers);
        _import = new MockImportApi(_materials, _cats, _uoms, _workers, _processes, _fgs, _sizes, _customers);
    }

    private static MemoryStream Csv(string text) => new(new UTF8Encoding(false).GetBytes(text));

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    [Fact]
    public async Task Template_berisi_judul_kolom_dan_contoh_dengan_BOM_UTF8()
    {
        var file = await _import.GetTemplateAsync(ImportEntity.Material);

        Assert.Equal(("template-material.csv", "text/csv"), (file.FileName, file.ContentType));
        Assert.Equal([0xEF, 0xBB, 0xBF], file.Bytes.Take(3));
        Assert.StartsWith("code,name,category_code,uom_code,description", Encoding.UTF8.GetString(file.Bytes).TrimStart('﻿'));
        Assert.Contains("S|M|L|XL", Encoding.UTF8.GetString((await _import.GetTemplateAsync(ImportEntity.Fg)).Bytes));
    }

    [Fact]
    public async Task Dry_run_melaporkan_baris_valid_dan_error_tanpa_menulis_data()
    {
        const string csv = "code,name,category_code,uom_code,description\r\nM-1,Kancing,AKS,PCS,\r\nM-2,,AKS,PCS,\r\nM-3,Resleting,XXX,PCS,\r\nKN-001,Dobel Data,KAIN,MTR,\r\nM-1,Dobel File,AKS,PCS,\r\n";

        var result = await _import.ImportAsync(ImportEntity.Material, Csv(csv), "m.csv", dryRun: true);

        Assert.Equal((5, 1, 4, false), (result.TotalRows, result.ValidRows, result.ErrorRows, result.Committed));
        Assert.Equal([2, 3, 4, 5, 6], result.Rows.Select(r => r.RowNumber)); // nomor baris berbasis berkas (judul = baris 1)
        Assert.Equal("Nama wajib diisi.", result.Rows[1].Message);
        Assert.Equal("Kategori \"XXX\" tidak ditemukan.", result.Rows[2].Message);
        Assert.Equal("Kode sudah terdaftar.", result.Rows[3].Message);
        Assert.Equal("Kode ganda di dalam berkas.", result.Rows[4].Message);
        Assert.Equal(2, (await _materials.ListAsync(new MasterListQuery())).TotalCount); // tidak menulis
    }

    [Fact]
    public async Task Commit_menulis_semua_baris_hanya_bila_tanpa_error_semua_atau_tidak_sama_sekali()
    {
        const string bad = "code,name,category_code,uom_code,description\nM-1,Kancing,AKS,PCS,\nM-2,,AKS,PCS,\n";
        const string good = "code,name,category_code,uom_code,description\nM-1,Kancing,AKS,PCS,Hitam\nM-2,Resleting,AKS,PCS,\n";

        var refused = await _import.ImportAsync(ImportEntity.Material, Csv(bad), "m.csv", dryRun: false);
        Assert.False(refused.Committed);
        Assert.Equal(2, (await _materials.ListAsync(new MasterListQuery())).TotalCount);

        var done = await _import.ImportAsync(ImportEntity.Material, Csv(good), "m.csv", dryRun: false);
        Assert.True(done.Committed);
        Assert.Equal(4, (await _materials.ListAsync(new MasterListQuery())).TotalCount);
    }

    [Fact]
    public async Task Titik_koma_kutip_dan_BOM_dikenali_pada_berkas_Excel()
    {
        var csv = "﻿code;name;category_code;uom_code;description\n\"M,1\";\"Kancing \"\"Besar\"\"\";AKS;PCS;\n";

        var result = await _import.ImportAsync(ImportEntity.Material, Csv(csv), "m.csv", dryRun: false);

        Assert.True(result.Committed);
        var material = (await _materials.ListAsync(new MasterListQuery { Q = "M,1" })).Items.Single();
        Assert.Equal("Kancing \"Besar\"", material.Name);
    }

    [Fact]
    public async Task Judul_kolom_salah_berkas_kosong_dan_terlalu_banyak_baris_ditolak_sebagai_berkas_tidak_valid()
    {
        var header = await Fails(() => _import.ImportAsync(ImportEntity.Material, Csv("code,nama\nx,y\n"), "m.csv", true));
        Assert.Equal((ProblemTypes.ImportInvalid, ProblemCodes.ImportFileInvalid), (header.Problem.Type, header.Problem.Code));
        Assert.Contains("kolom kurang", header.Problem.Detail);
        Assert.Contains("kolom tidak dikenal: nama", header.Problem.Detail);

        Assert.Contains("kosong", (await Fails(() => _import.ImportAsync(ImportEntity.Material, Csv(""), "m.csv", true))).Problem.Detail);

        var many = new StringBuilder("name,join_date,main_process_code,notes\n");
        for (var i = 0; i <= MockImportApi.MaxRows; i++) { many.Append("Orang ").Append(i).Append(",,,\n"); }
        Assert.Contains("Maksimal 2.000 baris", (await Fails(() => _import.ImportAsync(ImportEntity.Worker, Csv(many.ToString()), "w.csv", true))).Problem.Detail);
    }

    [Fact]
    public async Task Impor_pekerja_memvalidasi_tanggal_dan_proses_lalu_membuat_pekerja()
    {
        const string csv = "name,join_date,main_process_code,notes\nRudi,2026-01-15,SEW,\nSari,15/01/2026,SEW,\nTono,,XXX,\nBudi,,,\n";

        var dry = await _import.ImportAsync(ImportEntity.Worker, Csv(csv), "w.csv", true);
        Assert.Equal([true, false, false, true], dry.Rows.Select(r => r.IsValid));
        Assert.Equal("Tanggal masuk harus berformat yyyy-MM-dd.", dry.Rows[1].Message);

        const string good = "name,join_date,main_process_code,notes\nRudi,2026-01-15,SEW,\nBudi,,,Catatan\n";
        Assert.True((await _import.ImportAsync(ImportEntity.Worker, Csv(good), "w.csv", false)).Committed);
        Assert.Equal(["PKJ-001", "PKJ-002", "PKJ-003", "PKJ-004"], (await _workers.ListAsync(new MasterListQuery())).Items.Select(w => w.Code));
    }

    [Fact]
    public async Task Impor_FG_memvalidasi_size_dan_customer_dan_mengisi_Size_Berlaku()
    {
        const string csv = "code,name,kind,customer_code,notes,sizes\nJKT-1,Jaket,Jaket,CUS-001,,S|M\nJKT-2,Jaket 2,,CUS-999,,S\nJKT-3,Jaket 3,,,,S|XXL\n";

        var dry = await _import.ImportAsync(ImportEntity.Fg, Csv(csv), "f.csv", true);
        Assert.Equal([true, false, false], dry.Rows.Select(r => r.IsValid));
        Assert.Contains("CUS-999", dry.Rows[1].Message);
        Assert.Contains("XXL", dry.Rows[2].Message);

        const string good = "code,name,kind,customer_code,notes,sizes\nJKT-1,Jaket,Jaket,CUS-001,,S|M\n";
        Assert.True((await _import.ImportAsync(ImportEntity.Fg, Csv(good), "f.csv", false)).Committed);
        var fg = (await _fgs.ListAsync(new FgListQuery { Q = "JKT-1" })).Items.Single();
        Assert.Equal(2, (await _fgs.GetSizesAsync(fg.Id)).SizeIds.Count);
        Assert.Equal("Jaket", fg.Kind);
    }
}

public sealed class HttpImportApiTests
{
    private sealed class Token : ISessionTokenProvider
    {
        public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult<string?>("t");
        public void Override(string? value) { }
    }

    private sealed class Recorder : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, string Url, string? ContentType, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content?.Headers.ContentType?.MediaType, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (request.RequestUri.AbsolutePath.EndsWith("import-template", StringComparison.Ordinal))
            {
                var file = new ByteArrayContent("a,b"u8.ToArray());
                file.Headers.ContentType = new("text/csv");
                file.Headers.ContentDisposition = new("attachment") { FileName = "template-material.csv" };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = file };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"totalRows\":1,\"validRows\":1,\"errorRows\":0,\"committed\":false,\"rows\":[]}", Encoding.UTF8, "application/json"),
            };
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    [Fact]
    public async Task Unggah_multipart_dengan_field_file_dryRun_eksplisit_dan_template_diunduh_dengan_nama_berkas()
    {
        var http = new Recorder();
        var api = new HttpImportApi(new GmsApiClient(http, new Token()));

        var template = await api.GetTemplateAsync(ImportEntity.Worker);
        var dry = await api.ImportAsync(ImportEntity.Material, new MemoryStream("code,name\n"u8.ToArray()), "bahan.csv", dryRun: true);
        await api.ImportAsync(ImportEntity.Fg, new MemoryStream("x"u8.ToArray()), "fg.csv", dryRun: false);

        Assert.Equal(("template-material.csv", "/api/v1/master/workers/import-template"), (template.FileName, http.Calls[0].Url));
        Assert.Equal((HttpMethod.Post, "/api/v1/master/materials/import?dryRun=true", "multipart/form-data"), (http.Calls[1].Method, http.Calls[1].Url, http.Calls[1].ContentType));
        Assert.Contains("name=file; filename=bahan.csv", http.Calls[1].Body.Replace("\"", string.Empty));
        Assert.Contains("code,name", http.Calls[1].Body);
        Assert.Equal("/api/v1/master/fgs/import?dryRun=false", http.Calls[2].Url);
        Assert.Equal(1, dry.TotalRows);
    }

    [Fact]
    public async Task Entitas_tanpa_dukungan_impor_master_ditolak()
    {
        var api = new HttpImportApi(new GmsApiClient(new Recorder(), new Token()));

        await Assert.ThrowsAsync<NotSupportedException>(() => api.GetTemplateAsync((ImportEntity)99));
    }
}

public sealed class ImportWizardTests : WebTestBase
{
    private readonly MockUomApi _uoms = new();
    private readonly MockMaterialCategoryApi _cats = new();
    private readonly MockMaterialApi _materials;

    public ImportWizardTests()
    {
        _materials = new MockMaterialApi(_uoms, _cats);
        var processes = new MockProcessApi();
        var sizes = new MockSizeApi();
        var customers = new MockCustomerApi();
        Services.AddSingleton<IImportApi>(new MockImportApi(_materials, _cats, _uoms, new MockWorkerApi(processes), processes, new MockFgApi(sizes, processes, customers), sizes, customers));
        Services.AddSingleton<IMaterialApi>(_materials);
        Services.AddSingleton<FileDownloader>();
        SignIn(Permissions.MasterData.Material.View, Permissions.MasterData.Material.Import);
    }

    private IRenderedComponent<MaterialsImportPage> Wizard() => Render<MaterialsImportPage>();

    private static void Upload(IRenderedComponent<MaterialsImportPage> cut, string csv) =>
        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText(csv, "bahan.csv"));

    private const string Header = "code,name,category_code,uom_code,description\n";

    [Fact]
    public void Langkah_satu_menampilkan_ketentuan_template_dan_indikator_langkah_aktif()
    {
        var cut = Wizard();

        Assert.Equal("1. Unggah", cut.Find("[aria-current=step]").TextContent);
        Assert.Contains("Maksimal 2.000 baris dan 2 MB", cut.Markup);
        Assert.Contains("Unduh template", cut.Markup);
        Assert.Equal("Impor Material", cut.Find("h1").TextContent);
        Assert.Equal("Material", cut.Find(".gms-breadcrumb a").TextContent);
    }

    [Fact]
    public void Unduh_template_memanggil_modul_JS_penyimpan_berkas()
    {
        var cut = Wizard();

        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Unduh template")).Click();

        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "save" && (string)i.Arguments[0]! == "template-material.csv"));
    }

    [Fact]
    public void Berkas_dengan_error_menampilkan_ringkasan_filter_Hanya_error_dan_menahan_Lanjut()
    {
        var cut = Wizard();

        Upload(cut, Header + "M-1,Kancing,AKS,PCS,\nM-2,,AKS,PCS,\nM-3,Ok,AKS,PCS,\n");

        cut.WaitForAssertion(() => Assert.Contains("3 baris:", cut.Find("[role=status]").TextContent));
        Assert.Contains("2 valid, 1 error", cut.Find("[role=status]").TextContent);
        Assert.Equal("2. Periksa", cut.Find("[aria-current=step]").TextContent);
        Assert.Single(cut.FindAll("tbody tr")); // default: hanya error saat ada error
        Assert.Contains("Nama wajib diisi.", cut.Find("tbody").TextContent);
        cut.Find("input[type=checkbox]").Change(false);
        Assert.Equal(3, cut.FindAll("tbody tr").Count);
        Assert.Contains("Perbaiki berkas dan unggah ulang", cut.Find("[role=alert]").TextContent);
        Assert.True(cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Lanjut")).HasAttribute("disabled"));
    }

    [Fact]
    public void Unggah_ulang_kembali_ke_langkah_satu()
    {
        var cut = Wizard();
        Upload(cut, Header + "M-1,,AKS,PCS,\n");
        cut.WaitForAssertion(() => Assert.Equal("2. Periksa", cut.Find("[aria-current=step]").TextContent));

        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Unggah ulang")).Click();

        Assert.Equal("1. Unggah", cut.Find("[aria-current=step]").TextContent);
    }

    [Fact]
    public async Task Berkas_valid_lanjut_ke_konfirmasi_dan_Impor_sekarang_menulis_data_serta_menampilkan_tautan_daftar()
    {
        var cut = Wizard();
        Upload(cut, Header + "M-1,Kancing,AKS,PCS,\nM-2,Resleting,AKS,PCS,\n");
        cut.WaitForAssertion(() => Assert.Equal("2. Periksa", cut.Find("[aria-current=step]").TextContent));
        Assert.Empty(cut.FindAll("[role=alert]"));

        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Lanjut")).Click();
        Assert.Contains("2 Material akan ditambahkan", cut.Markup);
        Assert.Equal(2, (await _materials.ListAsync(new MasterListQuery())).TotalCount); // belum menulis
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Impor sekarang")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Impor selesai", cut.Markup));
        Assert.Equal(4, (await _materials.ListAsync(new MasterListQuery())).TotalCount);
        Assert.Equal("/master/materials", cut.Find("fluent-anchor, a.ant-btn").GetAttribute("href"));
    }

    [Fact]
    public void Berkas_ditolak_server_menampilkan_pesan_jelas_dan_tetap_di_langkah_satu()
    {
        var cut = Wizard();

        Upload(cut, "code,nama\nx,y\n");

        cut.WaitForAssertion(() => Assert.Contains("Judul kolom tidak sesuai template", cut.Markup));
        Assert.Equal("1. Unggah", cut.Find("[aria-current=step]").TextContent);
    }

    [Fact]
    public void Berkas_lebih_dari_2MB_ditolak_di_klien_tanpa_memanggil_server()
    {
        var cut = Wizard();
        var big = new string('a', 2 * 1024 * 1024 + 10);

        Upload(cut, big);

        cut.WaitForAssertion(() => Assert.Contains("Ukuran berkas melebihi 2 MB", cut.Markup));
        Assert.Equal("1. Unggah", cut.Find("[aria-current=step]").TextContent);
    }
}
