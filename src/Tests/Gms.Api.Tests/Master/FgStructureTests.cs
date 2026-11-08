using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Master;

/// <summary>Size Berlaku, Routing, Tarif, kesiapan, dan salin dari FG lain (FSD Master Data AC-MD-15 s.d. AC-MD-27).</summary>
[Collection(ApiCollection.Name)]
public sealed class FgStructureTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private const string Base = "/api/v1/master/fgs";

    // ===== Size Berlaku =====

    [Fact]
    public async Task Size_berlaku_wajib_minimal_satu_dan_hanya_size_aktif_dapat_ditambahkan()
    {
        var fg = await H.NewFgAsync();
        var active = await H.NewSizeAsync();
        var inactive = await H.NewSizeAsync();
        (await H.Api.PostAsync($"/api/v1/master/sizes/{inactive.Id}/deactivate?rowVersion={inactive.RowVersion}", null, H.AdminToken)).Ok();

        var empty = await H.PutSizesAsync(fg.Id, []);
        Assert.Contains("sizeIds", ProblemAssert.Is(empty, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var withInactive = await H.PutSizesAsync(fg.Id, [active.Id, inactive.Id]);
        var problem = ProblemAssert.Is(withInactive, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["sizeIds"], m => m.Contains("nonaktif"));

        var unknown = await H.PutSizesAsync(fg.Id, [Guid.NewGuid()]);
        ProblemAssert.Is(unknown, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);

        var ok = (await H.PutSizesAsync(fg.Id, [active.Id])).Ok().As<FgSizesDto>();
        Assert.Equal([active.Id], ok.SizeIds);
    }

    [Fact]
    public async Task Size_berlaku_terurut_menurut_urutan_tampil()
    {
        var fg = await H.NewFgAsync();
        var third = await H.NewSizeAsync(sortOrder: 300);
        var first = await H.NewSizeAsync(sortOrder: 100);
        var second = await H.NewSizeAsync(sortOrder: 200);

        var result = (await H.PutSizesAsync(fg.Id, [third.Id, first.Id, second.Id])).Ok().As<FgSizesDto>();

        Assert.Equal([first.Id, second.Id, third.Id], result.SizeIds);
    }

    [Fact]
    public async Task Menghapus_size_bertarif_butuh_konfirmasi_lalu_menghapus_tarif_size_itu_saja()
    {
        var setup = await H.NewReadyFgAsync();
        var before = await H.GetGridAsync(setup.Fg.Id);
        Assert.Equal(4, before.Cells.Count(c => c.Rate is not null));

        var blocked = await H.PutSizesAsync(setup.Fg.Id, [setup.Small.Id]);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.FgSizeHasTariffs, problem.Code);
        Assert.Equal(2, problem.Violations![0].Current);

        // Belum ada yang berubah.
        Assert.Equal(4, (await H.GetGridAsync(setup.Fg.Id)).Cells.Count(c => c.Rate is not null));

        (await H.PutSizesAsync(setup.Fg.Id, [setup.Small.Id], confirm: true)).Ok();
        var after = await H.GetGridAsync(setup.Fg.Id);
        Assert.Equal([setup.Small.Id], after.Sizes.Select(s => s.Id));
        Assert.Equal(2, after.Cells.Count(c => c.Rate is not null));
        Assert.All(after.Cells, c => Assert.Equal(setup.Small.Id, c.SizeId));
    }

    [Fact]
    public async Task Menambah_size_membuat_FG_belum_siap_sampai_tarifnya_diisi()
    {
        var setup = await H.NewReadyFgAsync();
        Assert.True(setup.Fg.IsReady);
        var extra = await H.NewSizeAsync(sortOrder: 30);

        (await H.PutSizesAsync(setup.Fg.Id, [setup.Small.Id, setup.Medium.Id, extra.Id])).Ok();

        var fg = await H.GetFgAsync(setup.Fg.Id);
        Assert.False(fg.IsReady);
        var readiness = await H.GetReadinessAsync(setup.Fg.Id);
        Assert.Contains(readiness.Items, i => !i.Ok && i.Code == "fg.tariffs" && i.Message.Contains("Cutting") && i.Message.Contains(extra.Code));
        Assert.Contains(readiness.Items, i => !i.Ok && i.Message.Contains("Sewing"));
    }

    // ===== Routing =====

    [Fact]
    public async Task Routing_menolak_kosong_ganda_tidak_dikenal_dan_proses_nonaktif()
    {
        var fg = await H.NewFgAsync();
        var a = await H.NewProcessAsync();
        var inactive = await H.NewProcessAsync();
        (await H.Api.PostAsync($"/api/v1/master/processes/{inactive.Id}/deactivate?rowVersion={inactive.RowVersion}", null, H.AdminToken)).Ok();

        Guid[][] invalids = [[], [a.Id, a.Id], [Guid.NewGuid()], [a.Id, inactive.Id]];
        foreach (var invalid in invalids)
        {
            var response = await H.PutRoutingAsync(fg.Id, invalid);
            var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
            Assert.Contains("processIds", problem.Errors!.Keys);
        }

        var ok = (await H.PutRoutingAsync(fg.Id, [a.Id])).Ok().As<FgRoutingDto>();
        Assert.Single(ok.Steps);
        Assert.Equal(1, ok.Steps[0].Seq);
    }

    [Fact]
    public async Task Mengubah_urutan_routing_memakai_unik_deferrable_dan_tidak_menghapus_tarif()
    {
        var setup = await H.NewReadyFgAsync();
        var tariffsBefore = (await H.GetGridAsync(setup.Fg.Id)).Cells.Where(c => c.Rate is not null).ToList();

        // Urutan semula CUT, EMB, SEW -> SEW, EMB, CUT (menukar langkah 1 dan 3).
        var result = (await H.PutRoutingAsync(setup.Fg.Id, [setup.Sew.Id, setup.Emb.Id, setup.Cut.Id])).Ok().As<FgRoutingDto>();

        Assert.Equal([setup.Sew.Id, setup.Emb.Id, setup.Cut.Id], result.Steps.Select(s => s.ProcessId));
        Assert.Equal([1, 2, 3], result.Steps.Select(s => s.Seq));

        var grid = await H.GetGridAsync(setup.Fg.Id);
        Assert.Equal([setup.Sew.Id, setup.Cut.Id], grid.InternalSteps.Select(s => s.ProcessId));
        var tariffsAfter = grid.Cells.Where(c => c.Rate is not null).ToList();
        Assert.Equal(tariffsBefore.Count, tariffsAfter.Count);
        foreach (var cell in tariffsBefore)
        {
            Assert.Equal(cell.Rate, tariffsAfter.Single(c => c.ProcessId == cell.ProcessId && c.SizeId == cell.SizeId).Rate);
        }
    }

    [Fact]
    public async Task Menghapus_langkah_bertarif_butuh_konfirmasi_dan_hanya_menghapus_tarif_langkah_itu()
    {
        var setup = await H.NewReadyFgAsync();

        var blocked = await H.PutRoutingAsync(setup.Fg.Id, [setup.Cut.Id, setup.Emb.Id]);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.FgStepHasTariffs, problem.Code);

        (await H.PutRoutingAsync(setup.Fg.Id, [setup.Cut.Id, setup.Emb.Id], confirm: true)).Ok();
        var grid = await H.GetGridAsync(setup.Fg.Id);
        Assert.Equal([setup.Cut.Id], grid.InternalSteps.Select(s => s.ProcessId));
        Assert.Equal(2, grid.Cells.Count(c => c.Rate is not null));
    }

    [Fact]
    public async Task Menghapus_langkah_vendor_tidak_butuh_konfirmasi_karena_tidak_punya_tarif()
    {
        var setup = await H.NewReadyFgAsync();

        (await H.PutRoutingAsync(setup.Fg.Id, [setup.Cut.Id, setup.Sew.Id])).Ok();

        Assert.Empty((await H.GetGridAsync(setup.Fg.Id)).VendorSteps);
    }

    [Fact]
    public async Task Menambah_langkah_Internal_baru_membuat_FG_belum_siap()
    {
        var setup = await H.NewReadyFgAsync();
        var qc = await H.NewProcessAsync(ProcessType.Internal, name: "QC");

        (await H.PutRoutingAsync(setup.Fg.Id, [setup.Cut.Id, setup.Emb.Id, setup.Sew.Id, qc.Id])).Ok();

        Assert.False((await H.GetFgAsync(setup.Fg.Id)).IsReady);
        var grid = await H.GetGridAsync(setup.Fg.Id);
        Assert.All(grid.Cells.Where(c => c.ProcessId == qc.Id), c => Assert.Null(c.Rate));
    }

    // ===== Tarif =====

    [Fact]
    public async Task Grid_tarif_memuat_langkah_Internal_x_size_dan_langkah_vendor_terpisah()
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);

        var grid = await H.GetGridAsync(setup.Fg.Id);

        Assert.Equal([setup.Cut.Id, setup.Sew.Id], grid.InternalSteps.Select(s => s.ProcessId));
        Assert.Equal([setup.Emb.Id], grid.VendorSteps.Select(s => s.ProcessId));
        Assert.Equal([setup.Small.Id, setup.Medium.Id], grid.Sizes.Select(s => s.Id));
        Assert.Equal(4, grid.Cells.Count);
        Assert.All(grid.Cells, c => Assert.Null(c.Rate));
    }

    [Fact]
    public async Task Tarif_5_digit_desimal_tersimpan_dan_kembali_persis()
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);

        var saved = (await H.PutTariffsAsync(setup.Fg.Id,
            [new TariffCell(setup.Cut.Id, setup.Small.Id, 1250.12345m), new TariffCell(setup.Sew.Id, setup.Small.Id, 0.00001m)]))
            .Ok().As<FgTariffGridDto>();

        Assert.Equal(1250.12345m, saved.Cells.Single(c => c.ProcessId == setup.Cut.Id && c.SizeId == setup.Small.Id).Rate);
        Assert.Equal(0.00001m, saved.Cells.Single(c => c.ProcessId == setup.Sew.Id && c.SizeId == setup.Small.Id).Rate);
    }

    [Theory]
    [InlineData("0", "lebih dari 0")]
    [InlineData("-5", "lebih dari 0")]
    [InlineData("1.123456", "5 digit")]
    [InlineData("100000000", "terlalu besar")]
    public async Task Tarif_tidak_valid_ditolak_dengan_pesan_per_sel(string rate, string messagePart)
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);
        var rowVersion = await H.FgRowVersionAsync(setup.Fg.Id);
        var json = $$"""{"cells":[{"processId":"{{setup.Cut.Id}}","sizeId":"{{setup.Small.Id}}","rate":{{rate}}}],"rowVersion":{{rowVersion}}}""";

        var response = await H.Api.PutAsync($"{Base}/{setup.Fg.Id}/tariffs", json, H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["cells[0].rate"], m => m.Contains(messagePart, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tarif_hanya_untuk_langkah_Internal_dan_size_berlaku_serta_tanpa_sel_ganda()
    {
        var setup = await H.NewReadyFgAsync(fillTariffs: false);
        var foreignSize = await H.NewSizeAsync();

        var vendorCell = await H.PutTariffsAsync(setup.Fg.Id, [new TariffCell(setup.Emb.Id, setup.Small.Id, 100m)]);
        Assert.Contains("cells[0].processId", ProblemAssert.Is(vendorCell, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var badSize = await H.PutTariffsAsync(setup.Fg.Id, [new TariffCell(setup.Cut.Id, foreignSize.Id, 100m)]);
        Assert.Contains("cells[0].sizeId", ProblemAssert.Is(badSize, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var duplicate = await H.PutTariffsAsync(setup.Fg.Id,
            [new TariffCell(setup.Cut.Id, setup.Small.Id, 100m), new TariffCell(setup.Cut.Id, setup.Small.Id, 200m)]);
        Assert.Contains("cells[1].rate", ProblemAssert.Is(duplicate, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        // Tidak ada yang tersimpan.
        Assert.All((await H.GetGridAsync(setup.Fg.Id)).Cells, c => Assert.Null(c.Rate));
    }

    [Fact]
    public async Task Tarif_sebagian_tidak_mengubah_sel_lain_dan_nilai_kosong_menghapus_tarif()
    {
        var setup = await H.NewReadyFgAsync();
        var before = await H.GetGridAsync(setup.Fg.Id);
        var untouched = before.Cells.Single(c => c.ProcessId == setup.Sew.Id && c.SizeId == setup.Medium.Id).Rate;

        var result = (await H.PutTariffsAsync(setup.Fg.Id,
            [new TariffCell(setup.Cut.Id, setup.Small.Id, 777m), new TariffCell(setup.Cut.Id, setup.Medium.Id, null)])).Ok().As<FgTariffGridDto>();

        Assert.Equal(777m, result.Cells.Single(c => c.ProcessId == setup.Cut.Id && c.SizeId == setup.Small.Id).Rate);
        Assert.Null(result.Cells.Single(c => c.ProcessId == setup.Cut.Id && c.SizeId == setup.Medium.Id).Rate);
        Assert.Equal(untouched, result.Cells.Single(c => c.ProcessId == setup.Sew.Id && c.SizeId == setup.Medium.Id).Rate);
        Assert.False((await H.GetFgAsync(setup.Fg.Id)).IsReady, "menghapus tarif membuat FG belum siap");
    }

    [Fact]
    public async Task Dua_penyunting_tarif_bersamaan_penyimpan_kedua_mendapat_409()
    {
        var setup = await H.NewReadyFgAsync();
        var rowVersion = await H.FgRowVersionAsync(setup.Fg.Id);

        (await H.PutTariffsAsync(setup.Fg.Id, [new TariffCell(setup.Cut.Id, setup.Small.Id, 11m)], rowVersion)).Ok();
        var stale = await H.PutTariffsAsync(setup.Fg.Id, [new TariffCell(setup.Cut.Id, setup.Small.Id, 22m)], rowVersion);

        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Perubahan_tarif_tercatat_di_audit_dengan_nilai_lama_dan_baru_dan_tarif_terhapus_terekam()
    {
        var setup = await H.NewReadyFgAsync();
        var oldRate = (await H.GetGridAsync(setup.Fg.Id)).Cells.Single(c => c.ProcessId == setup.Cut.Id && c.SizeId == setup.Small.Id).Rate;
        (await H.PutTariffsAsync(setup.Fg.Id, [new TariffCell(setup.Cut.Id, setup.Small.Id, 4321.5m)])).Ok();
        (await H.PutSizesAsync(setup.Fg.Id, [setup.Small.Id], confirm: true)).Ok();

        await using var db = H.CreateDbContext();
        var tariffAudit = await db.Database
            .SqlQuery<string>($"SELECT new_value::text AS \"Value\" FROM audit_logs WHERE event_type = 'FgTariffsChanged' AND target_id = {setup.Fg.Id.ToString()} ORDER BY id DESC LIMIT 1")
            .SingleAsync();
        Assert.Contains("4321.5", tariffAudit);
        Assert.Contains(oldRate!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture).Split('.')[0], tariffAudit);

        var sizeAudit = await db.Database
            .SqlQuery<string>($"SELECT new_value::text AS \"Value\" FROM audit_logs WHERE event_type = 'FgSizesChanged' AND target_id = {setup.Fg.Id.ToString()} ORDER BY id DESC LIMIT 1")
            .SingleAsync();
        Assert.Contains("removedTariffs", sizeAudit);
    }

    // ===== Kesiapan =====

    [Fact]
    public async Task Kesiapan_menjelaskan_alasan_spesifik_dan_berubah_siap_setelah_dilengkapi()
    {
        var fg = await H.NewFgAsync();

        var empty = await H.GetReadinessAsync(fg.Id);
        Assert.False(empty.IsReady);
        Assert.Contains(empty.Items, i => i.Code == "fg.sizes" && !i.Ok && i.FixTarget == "sizes");
        Assert.Contains(empty.Items, i => i.Code == "fg.routing" && !i.Ok && i.FixTarget == "routing");

        var setup = await H.NewReadyFgAsync(fillTariffs: false);
        var missing = await H.GetReadinessAsync(setup.Fg.Id);
        Assert.False(missing.IsReady);
        Assert.Equal(4, missing.Items.Count(i => i.Code == "fg.tariffs" && !i.Ok));
        Assert.Contains(missing.Items, i => i.Message == $"Tarif {setup.Cut.Name} untuk size {setup.Small.Code} belum diisi.");

        await H.FillAllTariffsAsync(setup.Fg.Id);
        var ready = await H.GetReadinessAsync(setup.Fg.Id);
        Assert.True(ready.IsReady);
        Assert.All(ready.Items, i => Assert.True(i.Ok));
        Assert.True((await H.GetFgAsync(setup.Fg.Id)).IsReady);
    }

    [Fact]
    public async Task FG_dengan_routing_hanya_vendor_tidak_butuh_tarif_dan_langsung_siap()
    {
        var size = await H.NewSizeAsync();
        var vendorProcess = await H.NewProcessAsync(ProcessType.Vendor);
        var fg = await H.NewFgAsync();
        (await H.PutSizesAsync(fg.Id, [size.Id])).Ok();
        (await H.PutRoutingAsync(fg.Id, [vendorProcess.Id])).Ok();

        Assert.True((await H.GetFgAsync(fg.Id)).IsReady);
    }

    // ===== Salin dari FG lain =====

    [Fact]
    public async Task Salin_pratinjau_menampilkan_routing_tarif_dan_size_yang_dilewati()
    {
        var source = await H.NewReadyFgAsync();
        var onlySmall = await H.NewFgAsync();
        (await H.PutSizesAsync(onlySmall.Id, [source.Small.Id])).Ok();

        var preview = (await H.Api.PostAsync($"{Base}/{onlySmall.Id}/copy-from/preview",
            new CopyFgRequest(source.Fg.Id), H.AdminToken)).Ok().As<CopyFgPreviewDto>();

        Assert.Equal(3, preview.Steps.Count);
        Assert.Equal(2, preview.Cells.Count);
        Assert.All(preview.Cells, c => Assert.Equal(source.Small.Id, c.SizeId));
        Assert.Equal([source.Medium.Code], preview.SkippedSizeCodes);
        Assert.False(preview.WillOverwriteExisting);
    }

    [Fact]
    public async Task Salin_ke_FG_kosong_tanpa_konfirmasi_menghasilkan_routing_dan_tarif_yang_cocok()
    {
        var source = await H.NewReadyFgAsync();
        var target = await H.NewFgAsync();
        (await H.PutSizesAsync(target.Id, [source.Small.Id, source.Medium.Id])).Ok();

        var copied = (await H.Api.PostAsync($"{Base}/{target.Id}/copy-from", new CopyFgRequest(source.Fg.Id), H.AdminToken)).Ok().As<FgDto>();

        Assert.True(copied.IsReady);
        var sourceGrid = await H.GetGridAsync(source.Fg.Id);
        var targetGrid = await H.GetGridAsync(target.Id);
        Assert.Equal(sourceGrid.InternalSteps.Select(s => s.ProcessId), targetGrid.InternalSteps.Select(s => s.ProcessId));
        Assert.Equal(sourceGrid.Cells.Select(c => (c.ProcessId, c.SizeId, c.Rate)).OrderBy(c => c.ProcessId).ThenBy(c => c.SizeId),
            targetGrid.Cells.Select(c => (c.ProcessId, c.SizeId, c.Rate)).OrderBy(c => c.ProcessId).ThenBy(c => c.SizeId));
    }

    [Fact]
    public async Task Salin_ke_FG_yang_sudah_punya_routing_butuh_konfirmasi_menimpa()
    {
        var source = await H.NewReadyFgAsync();
        var target = await H.NewReadyFgAsync();
        (await H.PutSizesAsync(target.Fg.Id, [source.Small.Id, source.Medium.Id], confirm: true)).Ok();

        var withoutConfirm = await H.Api.PostAsync($"{Base}/{target.Fg.Id}/copy-from", new CopyFgRequest(source.Fg.Id), H.AdminToken);
        Assert.Equal(ProblemCodes.FgCopyNeedsConfirm, ProblemAssert.Is(withoutConfirm, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Equal(target.Cut.Id, (await H.GetGridAsync(target.Fg.Id)).InternalSteps[0].ProcessId);

        (await H.Api.PostAsync($"{Base}/{target.Fg.Id}/copy-from", new CopyFgRequest(source.Fg.Id, true), H.AdminToken)).Ok();
        Assert.Equal(source.Cut.Id, (await H.GetGridAsync(target.Fg.Id)).InternalSteps[0].ProcessId);
    }

    [Fact]
    public async Task Salin_menolak_sumber_yang_sama_tidak_ada_atau_belum_punya_routing()
    {
        var fg = await H.NewFgAsync();
        var noRouting = await H.NewFgAsync();

        foreach (var sourceId in new[] { fg.Id, Guid.NewGuid(), noRouting.Id })
        {
            var response = await H.Api.PostAsync($"{Base}/{fg.Id}/copy-from/preview", new CopyFgRequest(sourceId), H.AdminToken);
            Assert.Contains("sourceFgId", ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        }
    }

    [Fact]
    public async Task Struktur_FG_dilindungi_permission_terpisah_untuk_routing_dan_tarif()
    {
        var setup = await H.NewReadyFgAsync();
        var fgEditOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Gms.Shared.Security.Permissions.MasterData.FG.View, Gms.Shared.Security.Permissions.MasterData.FG.Edit)).Id);
        var tariffView = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Gms.Shared.Security.Permissions.MasterData.FG.View, Gms.Shared.Security.Permissions.MasterData.Tarif.View)).Id);

        // Hanya FG.Edit: tidak boleh melihat/ubah tarif maupun routing.
        ProblemAssert.Is(await H.Api.GetAsync($"{Base}/{setup.Fg.Id}/tariffs", fgEditOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"{Base}/{setup.Fg.Id}/routing", fgEditOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.Api.GetAsync($"{Base}/{setup.Fg.Id}/sizes", fgEditOnly.Token)).IsSuccess);

        // Tarif.View boleh melihat tetapi tidak mengubah; tanpa Tarif.Edit penyimpanan ditolak.
        Assert.True((await H.Api.GetAsync($"{Base}/{setup.Fg.Id}/tariffs", tariffView.Token)).IsSuccess);
        var save = await H.Api.PutAsync($"{Base}/{setup.Fg.Id}/tariffs",
            new SaveTariffsRequest([new TariffCell(setup.Cut.Id, setup.Small.Id, 1m)], await H.FgRowVersionAsync(setup.Fg.Id)), tariffView.Token);
        ProblemAssert.Is(save, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        // Salin butuh Routing.Edit DAN Tarif.Edit.
        var copy = await H.Api.PostAsync($"{Base}/{setup.Fg.Id}/copy-from/preview", new CopyFgRequest(setup.Fg.Id), fgEditOnly.Token);
        ProblemAssert.Is(copy, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }
}
