using Gms.Api.Hosting;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Sales;

/// <summary>Daftar PO, filter cepat Beranda, dan data demo.</summary>
[Collection(ApiCollection.Name)]
public sealed class PoListAndSeederTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private async Task<PagedResult<PoListItem>> ListAsync(Guid customerId, string extra = "") =>
        (await H.Api.GetAsync($"/api/v1/purchase-orders?customerId={customerId}&pageSize=50{extra}", H.AdminToken)).Ok().As<PagedResult<PoListItem>>();

    [Fact]
    public async Task Daftar_menampilkan_agregat_qty_nama_customer_dan_filter_status()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 50);
        var confirmed = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 50)]);
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        (await H.Api.PostAsync($"/api/v1/po-lines/{confirmed.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id}/allocate", new AllocateRequest(20, null), H.AdminToken)).Ok();

        var all = await ListAsync(w.Customer.Id);
        Assert.Equal(2, all.TotalCount);
        var row = all.Items.Single(i => i.Id == confirmed.Id);
        Assert.Equal((150, 0, 20, 0), (row.TotalQty, row.DeliveredQty, row.AllocatedQty, row.InProductionQty));
        Assert.Equal(w.Customer.Name, row.CustomerName);
        Assert.Equal(PoStatus.InProgress, row.Status);

        var onlyDraft = await ListAsync(w.Customer.Id, "&status=Draft");
        Assert.Equal([draft.Id], onlyDraft.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Pencarian_bebas_mencocokkan_kode_nomor_PO_customer_dan_nama_customer()
    {
        var w = await H.NewPoWorldAsync();
        var number = "ZX" + Guid.NewGuid().ToString("N")[..8];
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: number);
        await H.NewPoAsync(w, [w.Line(w.Small, 10)]);

        Assert.Equal([po.Id], (await ListAsync(w.Customer.Id, $"&q={number.ToLowerInvariant()}")).Items.Select(i => i.Id));
        Assert.Equal([po.Id], (await ListAsync(w.Customer.Id, $"&q={po.Code}")).Items.Select(i => i.Id).Intersect([po.Id]));
        Assert.Equal(2, (await ListAsync(w.Customer.Id, $"&q={w.Customer.Name[..6]}")).TotalCount);
        Assert.Equal(0, (await ListAsync(w.Customer.Id, "&q=tidak-ada-yang-cocok")).TotalCount);
    }

    [Fact]
    public async Task Filter_terlambat_hanya_PO_aktif_dengan_baris_lewat_tanggal_dan_belum_terkirim_penuh()
    {
        var w = await H.NewPoWorldAsync();
        var late = await H.NewPoAsync(w, [w.Line(w.Small, 10, -3)]);
        await H.NewPoAsync(w, [w.Line(w.Small, 10, 5)]);                                   // belum jatuh tempo
        var lateButDraft = await H.NewPoAsync(w, [w.Line(w.Small, 10, -3)], confirm: false); // Draft tidak dihitung
        var lateButDelivered = await H.NewPoAsync(w, [w.Line(w.Small, 10, -3), w.Line(w.Medium, 10, 5)]);
        await H.UpdateLineAsync(lateButDelivered.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id, l => l.DeliveredQty = 10);

        var result = await ListAsync(w.Customer.Id, "&terlambat=true");

        Assert.Equal([late.Id], result.Items.Select(i => i.Id));
        Assert.True(result.Items[0].HasLateLines);
        Assert.DoesNotContain(result.Items, i => i.Id == lateButDraft.Id);
        Assert.False((await ListAsync(w.Customer.Id)).Items.Single(i => i.Id == lateButDelivered.Id).HasLateLines);
    }

    [Fact]
    public async Task Filter_jatuh_tempo_7_hari_mencakup_hari_ini_hingga_hari_ke_7_dan_tidak_termasuk_yang_sudah_lewat()
    {
        var w = await H.NewPoWorldAsync();
        var today = await H.NewPoAsync(w, [w.Line(w.Small, 10, 0)]);
        var day7 = await H.NewPoAsync(w, [w.Line(w.Small, 10, 7)]);
        await H.NewPoAsync(w, [w.Line(w.Small, 10, 8)]);
        await H.NewPoAsync(w, [w.Line(w.Small, 10, -1)]);

        var result = await ListAsync(w.Customer.Id, "&jatuhTempo7=true");

        Assert.Equal(new[] { today.Id, day7.Id }.Order(), result.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public async Task Filter_PO_customer_kosong_mencakup_Draft_Confirmed_dan_InProgress_tetapi_tidak_Cancelled_atau_yang_sudah_terisi()
    {
        var w = await H.NewPoWorldAsync();
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        var confirmed = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        await H.NewPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: "ADA-" + Guid.NewGuid().ToString("N")[..6]);
        var cancelled = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        (await H.Api.PostAsync($"/api/v1/purchase-orders/{cancelled.Id}/cancel", new ReasonRequest("batal", cancelled.RowVersion), H.AdminToken)).Ok();

        var result = await ListAsync(w.Customer.Id, "&poCustomerKosong=true");

        Assert.Equal(new[] { draft.Id, confirmed.Id }.Order(), result.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public async Task Filter_sisa_rencana_hanya_PO_aktif_yang_masih_punya_sisa()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var withRemaining = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        var full = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        (await H.Api.PostAsync($"/api/v1/po-lines/{full.Lines[0].Id}/allocate", new AllocateRequest(10, null), H.AdminToken)).Ok();

        var result = await ListAsync(w.Customer.Id, "&sisaRencana=true");

        Assert.Equal([withRemaining.Id], result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Filter_rentang_tanggal_urutan_dan_paging()
    {
        var w = await H.NewPoWorldAsync();
        var created = new List<PoDto>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await H.NewPoAsync(w, [w.Line(w.Small, 10 + i)]));
        }

        var asc = await ListAsync(w.Customer.Id, "&sort=code:asc");
        Assert.Equal(created.Select(c => c.Code).Order(), asc.Items.Select(i => i.Code));
        var desc = await ListAsync(w.Customer.Id, "&sort=code:desc");
        Assert.Equal(asc.Items.Select(i => i.Id).Reverse(), desc.Items.Select(i => i.Id));

        var page2 = (await H.Api.GetAsync($"/api/v1/purchase-orders?customerId={w.Customer.Id}&pageSize=2&page=2&sort=code:asc", H.AdminToken)).Ok().As<PagedResult<PoListItem>>();
        Assert.Equal((5, 2, 2), (page2.TotalCount, page2.Page, page2.Items.Count));
        Assert.Equal(asc.Items.Skip(2).Take(2).Select(i => i.Id), page2.Items.Select(i => i.Id));

        Assert.Equal(5, (await ListAsync(w.Customer.Id, $"&from={Today()}&to={Today()}")).TotalCount);
        Assert.Equal(0, (await ListAsync(w.Customer.Id, $"&from={Today(1)}")).TotalCount);
        Assert.Equal(0, (await ListAsync(w.Customer.Id, $"&to={Today(-1)}")).TotalCount);
    }

    [Fact]
    public async Task Daftar_dan_detail_menolak_pengguna_tanpa_permission_dan_detail_tidak_ada_404()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        var noPo = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Gms.Shared.Security.Permissions.MasterData.Customer.View)).Id);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/purchase-orders", noPo.Token), System.Net.HttpStatusCode.Forbidden, Gms.Shared.Problems.ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}", noPo.Token), System.Net.HttpStatusCode.Forbidden, Gms.Shared.Problems.ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/purchase-orders/{Guid.NewGuid()}", H.AdminToken), System.Net.HttpStatusCode.NotFound, Gms.Shared.Problems.ProblemTypes.NotFound);
    }

    // ===== Seeder =====

    [Fact]
    public async Task DemoData_membuat_tiga_PO_dan_stok_awal_dengan_invarian_utuh_serta_idempoten()
    {
        var host = await fixture.CreateIsolatedAsync(new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

        var list = (await host.Api.GetAsync("/api/v1/purchase-orders?pageSize=50", host.AdminToken)).Ok().As<PagedResult<PoListItem>>();
        Assert.Equal(3, list.TotalCount);
        Assert.Equal(1, list.Items.Count(i => i.Status == PoStatus.Draft));
        Assert.Equal(2, list.Items.Count(i => i.Status == PoStatus.Confirmed));
        Assert.Single(list.Items, i => i.HasLateLines);
        Assert.Equal(2, list.Items.Count(i => i.CustomerPoNumber is null));
        Assert.Matches(@"^PO-\d{6}-\d{4}$", list.Items[0].Code);

        var lateOnly = (await host.Api.GetAsync("/api/v1/purchase-orders?terlambat=true", host.AdminToken)).Ok().As<PagedResult<PoListItem>>();
        Assert.Single(lateOnly.Items);

        await using (var db = host.CreateDbContext())
        {
            Assert.Equal(220, await db.StockBalances.SumAsync(s => s.OnHand));
            Assert.Equal(0, await db.StockBalances.SumAsync(s => s.Allocated));
            Assert.Equal(3, await db.StockLedger.CountAsync());
        }

        using (var scope = host.Services.CreateScope())
        {
            await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
        }

        await using var again = host.CreateDbContext();
        Assert.Equal(3, await again.PurchaseOrders.CountAsync());
        Assert.Equal(3, await again.StockLedger.CountAsync());
    }

    [Fact]
    public async Task Tanpa_DemoData_tidak_ada_PO_atau_stok()
    {
        var host = await fixture.CreateIsolatedAsync();
        await using var db = host.CreateDbContext();

        Assert.False(await db.PurchaseOrders.AnyAsync());
        Assert.False(await db.StockBalances.AnyAsync());
    }
}
