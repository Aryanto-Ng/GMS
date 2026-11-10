using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;

namespace Gms.Api.Tests.Master;

/// <summary>Aturan khusus per entitas master (FSD Master Data 4.x dan AC-MD-03/11/13/14/18).</summary>
[Collection(ApiCollection.Name)]
public sealed class MasterRulesTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private const string Base = "/api/v1/master";

    // ===== Satuan & kategori =====

    [Fact]
    public async Task Kode_satuan_terkunci_setelah_dipakai_material_dan_nonaktif_ditolak_selama_masih_dipakai()
    {
        var material = await H.NewMaterialAsync();
        var uom = (await H.Api.GetAsync($"{Base}/uoms/{material.UomId}", H.AdminToken)).Ok().As<SimpleMasterDto>();

        var rename = await H.Api.PutAsync($"{Base}/uoms/{uom.Id}",
            new { code = MasterHelpers.Code("U"), name = uom.Name, rowVersion = uom.RowVersion }, H.AdminToken);
        var problem = ProblemAssert.Is(rename, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["code"], m => m.Contains("tidak dapat diubah"));

        var deactivate = await H.Api.PostAsync($"{Base}/uoms/{uom.Id}/deactivate?rowVersion={uom.RowVersion}", null, H.AdminToken);
        var blocked = ProblemAssert.Is(deactivate, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.MasterInUse, blocked.Code);
        Assert.Contains(blocked.Violations!, v => v.Subject == "Material " + material.Code);

        // Setelah material dinonaktifkan, satuan dapat dinonaktifkan.
        (await H.Api.PostAsync($"{Base}/materials/{material.Id}/deactivate?rowVersion={material.RowVersion}", null, H.AdminToken)).Ok();
        (await H.Api.PostAsync($"{Base}/uoms/{uom.Id}/deactivate?rowVersion={uom.RowVersion}", null, H.AdminToken)).Ok();
    }

    [Fact]
    public async Task Kategori_material_dinonaktifkan_hanya_bila_tidak_dipakai_material_aktif()
    {
        var material = await H.NewMaterialAsync();
        var category = (await H.Api.GetAsync($"{Base}/material-categories/{material.CategoryId}", H.AdminToken)).Ok().As<SimpleMasterDto>();

        var blocked = await H.Api.PostAsync($"{Base}/material-categories/{category.Id}/deactivate?rowVersion={category.RowVersion}", null, H.AdminToken);

        ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
    }

    // ===== Material =====

    [Fact]
    public async Task Material_menolak_kategori_atau_satuan_tidak_ada_atau_nonaktif()
    {
        var category = await H.NewCategoryAsync();
        var uom = await H.NewUomAsync();
        (await H.Api.PostAsync($"{Base}/uoms/{uom.Id}/deactivate?rowVersion={uom.RowVersion}", null, H.AdminToken)).Ok();

        var response = await H.Api.PostAsync($"{Base}/materials",
            new { code = MasterHelpers.Code("M"), name = "Material", categoryId = category.Id, uomId = uom.Id }, H.AdminToken);
        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("uomId", problem.Errors!.Keys);

        var missing = await H.Api.PostAsync($"{Base}/materials",
            new { code = MasterHelpers.Code("M"), name = "Material", categoryId = Guid.NewGuid(), uomId = Guid.NewGuid() }, H.AdminToken);
        var both = ProblemAssert.Is(missing, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("categoryId", both.Errors!.Keys);
        Assert.Contains("uomId", both.Errors.Keys);
    }

    // ===== Size & Color =====

    [Fact]
    public async Task Size_menolak_urutan_negatif_dan_nonaktif_ditahan_selama_menjadi_size_berlaku_FG_aktif()
    {
        var negative = await H.Api.PostAsync($"{Base}/sizes", new { code = MasterHelpers.Code("Z"), name = "Z", sortOrder = -1 }, H.AdminToken);
        Assert.Contains("sortOrder", ProblemAssert.Is(negative, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var setup = await H.NewReadyFgAsync();
        var size = (await H.Api.GetAsync($"{Base}/sizes/{setup.Small.Id}", H.AdminToken)).Ok().As<SizeDto>();

        var blocked = await H.Api.PostAsync($"{Base}/sizes/{size.Id}/deactivate?rowVersion={size.RowVersion}", null, H.AdminToken);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Contains(problem.Violations!, v => v.Subject == "FG " + setup.Fg.Code);

        // Kode size terkunci karena dipakai FG.
        var rename = await H.Api.PutAsync($"{Base}/sizes/{size.Id}",
            new { code = MasterHelpers.Code("Z"), name = size.Name, sortOrder = size.SortOrder, rowVersion = size.RowVersion }, H.AdminToken);
        Assert.Contains("code", ProblemAssert.Is(rename, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Theory]
    [InlineData("merah", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("#a1b2c3", true)]
    [InlineData(null, true)]
    public async Task Color_hex_opsional_dan_wajib_berformat_RRGGBB(string? hex, bool valid)
    {
        var response = await H.Api.PostAsync($"{Base}/colors", new { code = MasterHelpers.Code("C"), name = "Warna", hex }, H.AdminToken);

        if (valid)
        {
            var color = response.Ok().As<ColorDto>();
            Assert.Equal(hex?.ToUpperInvariant(), color.Hex);
        }
        else
        {
            Assert.Contains("hex", ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        }
    }

    [Fact]
    public async Task Color_dapat_dinonaktifkan_walau_sudah_dipakai()
    {
        var setup = await H.NewReadyFgAsync();
        var color = await H.NewColorAsync();
        await H.InsertPoLineAsync(setup.Fg.Id, setup.Small.Id);

        (await H.Api.PostAsync($"{Base}/colors/{color.Id}/deactivate?rowVersion={color.RowVersion}", null, H.AdminToken)).Ok();
    }

    // ===== Customer =====

    [Fact]
    public async Task Nama_customer_unik_tanpa_peka_huruf_besar_kecil()
    {
        var customer = await H.NewCustomerAsync();

        var response = await H.Api.PostAsync($"{Base}/customers", new { name = customer.Name.ToUpperInvariant() }, H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.Duplicate);
        Assert.Contains("name", problem.Errors!.Keys);
    }

    // ===== Process =====

    [Fact]
    public async Task Tipe_proses_terkunci_setelah_dipakai_pada_routing()
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);
        var cut = (await H.Api.GetAsync($"{Base}/processes/{setup.Cut.Id}", H.AdminToken)).Ok().As<ProcessDto>();
        Assert.True(cut.IsUsedInRouting);

        var response = await H.Api.PutAsync($"{Base}/processes/{cut.Id}",
            new { code = cut.Code, name = cut.Name, type = "Vendor", rowVersion = cut.RowVersion }, H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.ProcessTypeLocked, problem.Code);

        // Mengubah nama (bukan tipe) tetap diizinkan.
        (await H.Api.PutAsync($"{Base}/processes/{cut.Id}",
            new { code = cut.Code, name = "Cutting Baru", type = "Internal", rowVersion = cut.RowVersion }, H.AdminToken)).Ok();
    }

    [Fact]
    public async Task Tipe_proses_wajib_valid()
    {
        var response = await H.Api.PostAsync($"{Base}/processes", """{"code":"PX1","name":"X","type":"Hybrid"}""", H.AdminToken);

        ProblemAssert.Is(response, HttpStatusCode.BadRequest, ProblemTypes.BadRequest);
    }

    [Fact]
    public async Task Proses_tidak_dapat_dinonaktifkan_selama_ada_di_routing_FG_aktif_dan_dapat_setelah_dilepas()
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);
        var sew = (await H.Api.GetAsync($"{Base}/processes/{setup.Sew.Id}", H.AdminToken)).Ok().As<ProcessDto>();

        var blocked = await H.Api.PostAsync($"{Base}/processes/{sew.Id}/deactivate?rowVersion={sew.RowVersion}", null, H.AdminToken);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Contains(problem.Violations!, v => v.Subject == "FG " + setup.Fg.Code);

        (await H.PutRoutingAsync(setup.Fg.Id, [setup.Cut.Id, setup.Emb.Id])).Ok();
        (await H.Api.PostAsync($"{Base}/processes/{sew.Id}/deactivate?rowVersion={sew.RowVersion}", null, H.AdminToken)).Ok();
    }

    // ===== Pekerja =====

    [Fact]
    public async Task Proses_utama_pekerja_harus_proses_Internal_aktif()
    {
        var vendorProcess = await H.NewProcessAsync(ProcessType.Vendor);
        var internalProcess = await H.NewProcessAsync(ProcessType.Internal);

        var bad = await H.Api.PostAsync($"{Base}/workers", new { name = "Pekerja X", mainProcessId = vendorProcess.Id }, H.AdminToken);
        Assert.Contains("mainProcessId", ProblemAssert.Is(bad, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var good = (await H.Api.PostAsync($"{Base}/workers", new { name = "Pekerja Y", mainProcessId = internalProcess.Id }, H.AdminToken))
            .Ok().As<WorkerDto>();
        Assert.Equal(internalProcess.Name, good.MainProcessName);
    }

    [Fact]
    public async Task Pekerja_nonaktif_diizinkan_tanpa_syarat_dan_kode_proses_utama_mengunci_kode_proses()
    {
        var process = await H.NewProcessAsync(ProcessType.Internal);
        var worker = await H.NewWorkerAsync(mainProcessId: process.Id);

        // Kode proses terkunci karena dipakai sebagai proses utama pekerja.
        var rename = await H.Api.PutAsync($"{Base}/processes/{process.Id}",
            new { code = MasterHelpers.Code("P"), name = process.Name, type = "Internal", rowVersion = process.RowVersion }, H.AdminToken);
        Assert.Contains("code", ProblemAssert.Is(rename, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        (await H.Api.PostAsync($"{Base}/workers/{worker.Id}/deactivate?rowVersion={worker.RowVersion}", null, H.AdminToken)).Ok();
    }

    // ===== FG =====

    [Fact]
    public async Task FG_menolak_customer_nonaktif_dan_menerima_customer_aktif()
    {
        var customer = await H.NewCustomerAsync();
        (await H.Api.PostAsync($"{Base}/customers/{customer.Id}/deactivate?rowVersion={customer.RowVersion}", null, H.AdminToken)).Ok();

        var bad = await H.Api.PostAsync($"{Base}/fgs", new { code = MasterHelpers.Code("F"), name = "FG", customerId = customer.Id }, H.AdminToken);
        Assert.Contains("customerId", ProblemAssert.Is(bad, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var activeCustomer = await H.NewCustomerAsync();
        var good = (await H.Api.PostAsync($"{Base}/fgs", new { code = MasterHelpers.Code("F"), name = "FG", customerId = activeCustomer.Id }, H.AdminToken))
            .Ok().As<FgDto>();
        Assert.Equal(activeCustomer.Name, good.CustomerName);
    }

    [Fact]
    public async Task FG_dengan_PO_terbuka_tidak_dapat_dinonaktifkan_dan_kodenya_terkunci()
    {
        var setup = await H.NewReadyFgAsync();
        await H.InsertPoLineAsync(setup.Fg.Id, setup.Small.Id, PoStatus.Confirmed);
        var fg = await H.GetFgAsync(setup.Fg.Id);
        Assert.True(fg.IsCodeLocked);

        var blocked = await H.Api.PostAsync($"{Base}/fgs/{fg.Id}/deactivate?rowVersion={fg.RowVersion}", null, H.AdminToken);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.FgHasOpenDocuments, problem.Code);
        Assert.Single(problem.Violations!);

        var rename = await H.Api.PutAsync($"{Base}/fgs/{fg.Id}",
            new { code = MasterHelpers.Code("F"), name = fg.Name, kind = fg.Kind, customerId = fg.CustomerId, notes = fg.Notes, rowVersion = fg.RowVersion },
            H.AdminToken);
        Assert.Contains("code", ProblemAssert.Is(rename, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task FG_dengan_PO_selesai_atau_batal_dapat_dinonaktifkan()
    {
        var setup = await H.NewReadyFgAsync();
        await H.InsertPoLineAsync(setup.Fg.Id, setup.Small.Id, PoStatus.Completed);
        var fg = await H.GetFgAsync(setup.Fg.Id);

        (await H.Api.PostAsync($"{Base}/fgs/{fg.Id}/deactivate?rowVersion={fg.RowVersion}", null, H.AdminToken)).Ok();
        Assert.False((await H.GetFgAsync(fg.Id)).IsActive);
        Assert.False((await H.GetFgAsync(fg.Id)).IsReady, "FG nonaktif tidak Siap Produksi");
    }

    [Fact]
    public async Task Daftar_FG_menampilkan_kesiapan_dan_mendukung_filter_hanya_siap_produksi()
    {
        var ready = await H.NewReadyFgAsync();
        var notReady = await H.NewReadyFgAsync(fillTariffs: false);

        // Database tes dipakai bersama banyak tes: cari per kode agar tidak bergantung pada batas 200 baris/halaman.
        async Task<PagedResult<FgListItem>> ListAsync(string code, bool readyOnly) =>
            (await H.Api.GetAsync($"{Base}/fgs?pageSize=200&q={Uri.EscapeDataString(code)}{(readyOnly ? "&readyOnly=true" : string.Empty)}", H.AdminToken)).Ok().As<PagedResult<FgListItem>>();

        Assert.True((await ListAsync(ready.Fg.Code, false)).Items.Single(f => f.Id == ready.Fg.Id).IsReady);
        Assert.False((await ListAsync(notReady.Fg.Code, false)).Items.Single(f => f.Id == notReady.Fg.Id).IsReady);

        Assert.Contains((await ListAsync(ready.Fg.Code, true)).Items, f => f.Id == ready.Fg.Id);
        var onlyReadyNot = await ListAsync(notReady.Fg.Code, true);
        Assert.DoesNotContain(onlyReadyNot.Items, f => f.Id == notReady.Fg.Id);
        Assert.All(onlyReadyNot.Items, f => Assert.True(f.IsReady));
    }
}
