using System.Net;
using System.Text;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Master;

/// <summary>Impor CSV Material/FG/Pekerja (FSD Master Data AC-MD-30 s.d. AC-MD-35).</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private const string Base = "/api/v1/master";

    private static byte[] Csv(bool bom, params string[] lines) =>
        (bom ? new UTF8Encoding(true).GetPreamble() : [])
        .Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();

    private static byte[] Csv(params string[] lines) => Csv(false, lines);

    private Task<ApiResponse> Upload(string route, byte[] content, bool dryRun, string? token = null, string fileName = "impor.csv") =>
        H.Api.PostFileAsync($"{Base}/{route}/import?dryRun={dryRun.ToString().ToLowerInvariant()}", fileName, content, token ?? H.AdminToken);

    private async Task<(string CategoryCode, string UomCode)> NewMaterialRefsAsync()
    {
        var category = await H.NewCategoryAsync();
        var uom = await H.NewUomAsync();
        return (category.Code, uom.Code);
    }

    private async Task<bool> MaterialExistsAsync(string code)
    {
        await using var db = H.CreateDbContext();
        return await db.Materials.AnyAsync(m => m.Code == code);
    }

    // ===== Template =====

    [Theory]
    [InlineData("materials", "code,name,category_code,uom_code,description")]
    [InlineData("fgs", "code,name,kind,customer_code,notes,sizes")]
    [InlineData("workers", "name,join_date,main_process_code,notes")]
    public async Task Template_berupa_CSV_UTF8_dengan_judul_kolom_yang_benar(string route, string header)
    {
        var (status, bytes, contentType, fileName) = await H.Api.DownloadAsync($"{Base}/{route}/import-template", H.AdminToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("text/csv", contentType);
        Assert.StartsWith("template-", fileName?.Trim('"'));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.StartsWith(header, Encoding.UTF8.GetString(bytes).TrimStart('﻿'));
    }

    [Fact]
    public async Task Template_dan_unggah_dilindungi_permission_Import_per_entitas()
    {
        var viewOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.MasterData.Material.View)).Id);
        var canImport = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Permissions.MasterData.Material.View, Permissions.MasterData.Material.Import)).Id);
        var content = Csv("code,name,category_code,uom_code,description");

        ProblemAssert.Is(await H.Api.GetAsync($"{Base}/materials/import-template", viewOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await Upload("materials", content, true, viewOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await Upload("fgs", content, true, canImport.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.Api.GetAsync($"{Base}/materials/import-template", canImport.Token)).IsSuccess);
    }

    // ===== Material =====

    [Fact]
    public async Task Dry_run_memvalidasi_tanpa_menulis_dan_impor_sungguhan_menambah_semua_baris()
    {
        var (category, uom) = await NewMaterialRefsAsync();
        var c1 = MasterHelpers.Code("IMP");
        var c2 = MasterHelpers.Code("IMP");
        var file = Csv("code,name,category_code,uom_code,description",
            $"{c1},Kain Uji 1,{category},{uom},spesifikasi", $"{c2},Kain Uji 2,{category.ToLowerInvariant()},{uom},");

        var dry = (await Upload("materials", file, dryRun: true)).Ok().As<ImportResultDto>();
        Assert.Equal((2, 2, 0, false), (dry.TotalRows, dry.ValidRows, dry.ErrorRows, dry.Committed));
        Assert.False(await MaterialExistsAsync(c1));

        var real = (await Upload("materials", file, dryRun: false)).Ok().As<ImportResultDto>();
        Assert.True(real.Committed);
        Assert.True(await MaterialExistsAsync(c1));
        Assert.True(await MaterialExistsAsync(c2));

        await using var db = H.CreateDbContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EventType == "ImportCompleted" && a.TargetType == "Material"));
    }

    [Fact]
    public async Task Semua_atau_tidak_sama_sekali_satu_baris_salah_membatalkan_seluruh_impor()
    {
        var (category, uom) = await NewMaterialRefsAsync();
        var good = MasterHelpers.Code("IMP");
        var file = Csv("code,name,category_code,uom_code,description",
            $"{good},Baris Baik,{category},{uom},", $"{MasterHelpers.Code("IMP")},Baris Buruk,TIDAK-ADA,{uom},");

        var result = (await Upload("materials", file, dryRun: false)).Ok().As<ImportResultDto>();

        Assert.False(result.Committed);
        Assert.Equal((2, 1, 1), (result.TotalRows, result.ValidRows, result.ErrorRows));
        Assert.False(await MaterialExistsAsync(good), "baris yang valid pun tidak boleh tersimpan");
        Assert.Contains("Kategori 'TIDAK-ADA' tidak ditemukan", result.Rows.Single(r => !r.IsValid).Message);
    }

    [Fact]
    public async Task Galat_per_baris_menyebut_nomor_baris_berkas_dan_alasan()
    {
        var (category, uom) = await NewMaterialRefsAsync();
        var existing = (await H.NewMaterialAsync()).Code;
        var dup = MasterHelpers.Code("IMP");
        var file = Csv("code,name,category_code,uom_code,description",
            $"{dup},Pertama,{category},{uom},",
            $"{dup},Kedua,{category},{uom},",
            $"{existing.ToLowerInvariant()},Sudah ada,{category},{uom},",
            $"kode salah,Nama,{category},{uom},",
            $"{MasterHelpers.Code("IMP")},,{category},{uom},",
            $"{MasterHelpers.Code("IMP")},Satuan salah,{category},XYZ,");

        var rows = (await Upload("materials", file, dryRun: true)).Ok().As<ImportResultDto>().Rows;

        Assert.Equal([2, 3, 4, 5, 6, 7], rows.Select(r => r.RowNumber));
        Assert.True(rows[0].IsValid);
        Assert.Contains("ganda di dalam berkas", rows[1].Message);
        Assert.Contains("Kode sudah ada", rows[2].Message);
        Assert.Contains("Kode hanya boleh", rows[3].Message);
        Assert.Contains("Nama wajib diisi", rows[4].Message);
        Assert.Contains("Satuan 'XYZ' tidak ditemukan", rows[5].Message);
    }

    [Fact]
    public async Task Titik_koma_BOM_kutipan_dan_baris_kosong_dibaca_dengan_benar()
    {
        var (category, uom) = await NewMaterialRefsAsync();
        var code = MasterHelpers.Code("IMP");
        var file = Csv(true, "code;name;category_code;uom_code;description",
            $"{code};\"Kain; dengan titik koma\";{category};{uom};\"Baris 1", "Baris 2\"", "", ";;;;");

        var result = (await Upload("materials", file, dryRun: false)).Ok().As<ImportResultDto>();

        Assert.True(result.Committed);
        Assert.Equal(1, result.TotalRows);
        await using var db = H.CreateDbContext();
        var material = await db.Materials.SingleAsync(m => m.Code == code);
        Assert.Equal("Kain; dengan titik koma", material.Name);
        Assert.Equal("Baris 1\nBaris 2", material.Description!.ReplaceLineEndings("\n"));
    }

    // ===== Galat tingkat berkas =====

    [Fact]
    public async Task Judul_kolom_tidak_sesuai_template_ditolak_menyebut_kolom_kurang_dan_berlebih()
    {
        var response = await Upload("materials", Csv("code,name,kategori,uom_code,description,ekstra", "A,B,C,D,E,F"), dryRun: true);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);
        Assert.Equal(ProblemCodes.ImportFileInvalid, problem.Code);
        Assert.Contains("category_code", problem.Detail);
        Assert.Contains("ekstra", problem.Detail);
    }

    [Fact]
    public async Task Berkas_kosong_bukan_UTF8_atau_kutip_tidak_tertutup_ditolak()
    {
        ProblemAssert.Is(await Upload("materials", [], true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);

        var latin1 = Encoding.Latin1.GetBytes("code,name,category_code,uom_code,description\r\nA,Café,K,U,\r\n");
        var notUtf8 = ProblemAssert.Is(await Upload("materials", latin1, true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);
        Assert.Contains("UTF-8", notUtf8.Detail);

        var unclosed = Csv("code,name,category_code,uom_code,description", "A,\"tidak ditutup,K,U,");
        ProblemAssert.Is(await Upload("materials", unclosed, true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);
    }

    [Fact]
    public async Task Melebihi_2000_baris_atau_2_MB_ditolak()
    {
        var many = new List<string> { "code,name,category_code,uom_code,description" };
        many.AddRange(Enumerable.Range(0, 2001).Select(i => $"K{i},N{i},C,U,"));
        var tooMany = ProblemAssert.Is(await Upload("materials", Csv(many.ToArray()), true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);
        Assert.Contains("2000", tooMany.Detail);

        var huge = Csv("code,name,category_code,uom_code,description", "A," + new string('x', 2 * 1024 * 1024 + 10) + ",C,U,");
        var tooBig = ProblemAssert.Is(await Upload("materials", huge, true), HttpStatusCode.UnprocessableEntity, ProblemTypes.ImportInvalid);
        Assert.Contains("2 MB", tooBig.Detail);
    }

    [Fact]
    public async Task Permintaan_tanpa_berkas_memberi_400_dan_isi_non_multipart_memberi_415_tetap_Problem_Details()
    {
        var wrongField = await H.Api.PostFileAsync($"{Base}/materials/import?dryRun=true", "x.csv", Csv("a"), H.AdminToken, fieldName: "bukan-file");
        ProblemAssert.Is(wrongField, HttpStatusCode.BadRequest, ProblemTypes.BadRequest);

        var json = await H.Api.PostAsync($"{Base}/materials/import?dryRun=true", "{}", H.AdminToken);
        ProblemAssert.Is(json, HttpStatusCode.UnsupportedMediaType, ProblemTypes.BadRequest);
    }

    // ===== FG =====

    [Fact]
    public async Task Impor_FG_membuat_data_dasar_dan_size_berlaku_tetapi_belum_siap_produksi()
    {
        var s1 = await H.NewSizeAsync();
        var s2 = await H.NewSizeAsync();
        var customer = await H.NewCustomerAsync();
        var code = MasterHelpers.Code("IMF");
        var file = Csv("code,name,kind,customer_code,notes,sizes", $"{code},FG Impor,Kemeja,{customer.Code},catatan,{s1.Code} | {s2.Code.ToLowerInvariant()}");

        var result = (await Upload("fgs", file, dryRun: false)).Ok().As<ImportResultDto>();

        Assert.True(result.Committed);
        var created = (await H.Api.GetAsync($"{Base}/fgs?q={code}", H.AdminToken)).Ok().As<Gms.Shared.Common.PagedResult<FgListItem>>().Items.Single();
        Assert.False(created.IsReady);
        Assert.Equal(customer.Name, created.CustomerName);
        var sizes = (await H.Api.GetAsync($"{Base}/fgs/{created.Id}/sizes", H.AdminToken)).Ok().As<FgSizesDto>();
        Assert.Equal(2, sizes.SizeIds.Count);
        Assert.Empty((await H.Api.GetAsync($"{Base}/fgs/{created.Id}/routing", H.AdminToken)).Ok().As<FgRoutingDto>().Steps);
    }

    [Fact]
    public async Task Impor_FG_menolak_size_atau_customer_tidak_dikenal_dan_FG_tanpa_size()
    {
        var size = await H.NewSizeAsync();
        var file = Csv("code,name,kind,customer_code,notes,sizes",
            $"{MasterHelpers.Code("IMF")},A,,CUS-9999,,{size.Code}",
            $"{MasterHelpers.Code("IMF")},B,,,,TIDAKADA",
            $"{MasterHelpers.Code("IMF")},C,,,,");

        var rows = (await Upload("fgs", file, dryRun: true)).Ok().As<ImportResultDto>().Rows;

        Assert.Contains("Customer 'CUS-9999' tidak ditemukan", rows[0].Message);
        Assert.Contains("Size 'TIDAKADA' tidak ditemukan", rows[1].Message);
        Assert.Contains("Minimal 1 size", rows[2].Message);
    }

    // ===== Pekerja =====

    [Fact]
    public async Task Impor_pekerja_memberi_kode_otomatis_berurutan_dan_menerima_dua_format_tanggal()
    {
        var process = await H.NewProcessAsync(ProcessType.Internal);
        var file = Csv("name,join_date,main_process_code,notes",
            $"Pekerja Impor A,2026-02-03,{process.Code},", "Pekerja Impor B,15/03/2026,,catatan");

        var result = (await Upload("workers", file, dryRun: false)).Ok().As<ImportResultDto>();

        Assert.True(result.Committed);
        await using var db = H.CreateDbContext();
        var workers = await db.Workers.Where(w => w.Name.StartsWith("Pekerja Impor")).OrderBy(w => w.Name).ToListAsync();
        Assert.Equal(new DateOnly(2026, 2, 3), workers[0].JoinDate);
        Assert.Equal(new DateOnly(2026, 3, 15), workers[1].JoinDate);
        Assert.Equal(process.Id, workers[0].MainProcessId);
        var n1 = int.Parse(workers[0].Code[4..]);
        var n2 = int.Parse(workers[1].Code[4..]);
        Assert.Equal(1, n2 - n1);
    }

    [Fact]
    public async Task Impor_pekerja_menolak_tanggal_salah_dan_proses_utama_non_Internal()
    {
        var vendorProcess = await H.NewProcessAsync(ProcessType.Vendor);
        var file = Csv("name,join_date,main_process_code,notes",
            "Salah Tanggal,31-12-2026,,", $"Salah Proses,,{vendorProcess.Code},", ",2026-01-01,,");

        var rows = (await Upload("workers", file, dryRun: true)).Ok().As<ImportResultDto>().Rows;

        Assert.Contains("Tanggal masuk", rows[0].Message);
        Assert.Contains("bukan proses Internal aktif", rows[1].Message);
        Assert.Contains("Nama wajib diisi", rows[2].Message);
    }
}
