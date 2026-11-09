using Gms.Shared.Problems;
using Gms.Web.Components.Layout;
using Gms.Web.Components.Pages;
using Gms.Web.Components.Shared;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Format;
using Gms.Web.Infrastructure.Http;
using Gms.Shared.Security;
using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace Gms.Web.Tests;

public sealed class FormatAndLogicTests
{
    // ===== IdFormat =====

    [Fact]
    public void Tanggal_dan_waktu_memakai_nama_bulan_Indonesia_dan_zona_WIB()
    {
        Assert.Equal("08 Nov 2026", IdFormat.Date(new DateOnly(2026, 11, 8)));
        Assert.Equal("01 Mei 2026", IdFormat.Date(new DateOnly(2026, 5, 1)));
        Assert.Equal("17 Agu 2026", IdFormat.Date(new DateOnly(2026, 8, 17)));
        Assert.Equal("—", IdFormat.Date((DateOnly?)null));
        Assert.Equal("08/11/2026", IdFormat.DateInput(new DateOnly(2026, 11, 8)));
        Assert.Equal("08 Nov 2026 14:30", IdFormat.DateTime(new DateTimeOffset(2026, 11, 8, 7, 30, 0, TimeSpan.Zero))); // 07:30 UTC = 14:30 WIB
        Assert.Equal("00:05", IdFormat.Time(new DateTimeOffset(2026, 11, 7, 17, 5, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(12500, "12.500")]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000000, "1.000.000")]
    [InlineData(-20, "−20")]
    public void Qty_memakai_pemisah_ribuan_titik_dan_minus_tipografis(int value, string expected) => Assert.Equal(expected, IdFormat.Qty(value));

    [Theory]
    [InlineData("1250000", "Rp 1.250.000")]
    [InlineData("1250000.5", "Rp 1.250.001")]   // setengah ke atas
    [InlineData("0", "Rp 0")]
    [InlineData("-20000", "−Rp 20.000")]
    [InlineData("-0.5", "−Rp 1")]                 // menjauhi nol
    public void Rupiah_tanpa_desimal_dibulatkan_setengah_ke_atas(string value, string expected) =>
        Assert.Equal(expected, IdFormat.Rupiah(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("1250.5", "Rp 1.250,5")]
    [InlineData("1000", "Rp 1.000")]            // tanpa nol ekor
    [InlineData("0.12345", "Rp 0,12345")]
    [InlineData("1001.10000", "Rp 1.001,1")]
    [InlineData("-5", "−Rp 5")]
    public void Tarif_hingga_lima_desimal_tanpa_nol_ekor(string value, string expected) =>
        Assert.Equal(expected, IdFormat.Rate(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Persentase_dan_angka_desimal()
    {
        Assert.Equal("87%", IdFormat.Percent(87m));
        Assert.Equal("87,5%", IdFormat.Percent(87.5m, 1));
        Assert.Equal("1.234,5", IdFormat.Number(1234.5m, 1));
        Assert.Equal("1.234", IdFormat.Number(1234m, 2));
    }

    // ===== IdNumber (masukan ambigu ditolak) =====

    [Theory]
    [InlineData("12", 12)]
    [InlineData("1.250", 1250)]
    [InlineData("12.500.000", 12500000)]
    [InlineData(" 7 ", 7)]
    [InlineData("-5", -5)]
    [InlineData("1250,0", 1250)]
    public void Bilangan_bulat_valid_diterima(string text, int expected)
    {
        Assert.True(IdNumber.TryParseInt(text, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("1,5", 1.5)]
    [InlineData("1.250,75", 1250.75)]
    [InlineData("Rp 1.000,5", 1000.5)]
    [InlineData("0,00001", 0.00001)]
    [InlineData(",5", 0.5)]
    public void Desimal_format_Indonesia_diterima(string text, double expected)
    {
        Assert.True(IdNumber.TryParseDecimal(text, out var value, out var error), error);
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("1.5")]        // ambigu: titik bukan desimal dan bukan ribuan sah
    [InlineData("12.34")]
    [InlineData("1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1,5.0")]
    public void Masukan_ambigu_atau_tidak_valid_ditolak_dengan_petunjuk(string text)
    {
        Assert.False(IdNumber.TryParseDecimal(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Bilangan_bulat_menolak_desimal_dan_terlalu_besar()
    {
        Assert.False(IdNumber.TryParseInt("1,5", out _, out var error));
        Assert.Contains("bulat", error);
        Assert.False(IdNumber.TryParseInt("99.999.999.999", out _, out _));
    }

    [Fact]
    public void Tanggal_isian_dd_MM_yyyy_dan_iso()
    {
        Assert.True(IdNumber.TryParseDate("08/11/2026", out var a));
        Assert.Equal(new DateOnly(2026, 11, 8), a);
        Assert.True(IdNumber.TryParseDate("2026-11-08", out var b));
        Assert.Equal(a, b);
        Assert.False(IdNumber.TryParseDate("31/02/2026", out _));
        Assert.False(IdNumber.TryParseDate("besok", out _));
    }

    // ===== ApiQuery =====

    [Fact]
    public void Query_string_melewati_nilai_kosong_memformat_tanggal_enum_dan_bool()
    {
        var query = ApiQuery.Build(("page", 2), ("q", "kemeja & celana"), ("status", Gms.Shared.Enums.PoStatus.InProgress),
            ("from", new DateOnly(2026, 11, 8)), ("terlambat", true), ("kosong", null), ("spasi", "  "));

        Assert.Equal("?page=2&q=kemeja%20%26%20celana&status=InProgress&from=2026-11-08&terlambat=true", query);
        Assert.Equal(string.Empty, ApiQuery.Build(("a", null)));
    }

    // ===== Problem Details =====

    [Theory]
    [InlineData(ProblemTypes.ConcurrencyConflict, 409, ProblemKind.Concurrency)]
    [InlineData(ProblemTypes.ValidationFailed, 422, ProblemKind.Validation)]
    [InlineData(ProblemTypes.SessionExpired, 401, ProblemKind.Unauthorized)]
    [InlineData(ProblemTypes.SessionReplaced, 401, ProblemKind.Unauthorized)]
    [InlineData(ProblemTypes.Forbidden, 403, ProblemKind.Forbidden)]
    [InlineData(ProblemTypes.NotFound, 404, ProblemKind.NotFound)]
    [InlineData(ProblemTypes.RuleViolation, 409, ProblemKind.Business)]
    [InlineData(ProblemTypes.StateConflict, 409, ProblemKind.Business)]
    [InlineData(ProblemTypes.PeriodClosed, 409, ProblemKind.Business)]
    [InlineData(ProblemTypes.InternalError, 500, ProblemKind.Server)]
    [InlineData(ProblemTypes.ServiceUnavailable, 503, ProblemKind.Server)]
    public void Problem_dipetakan_ke_jenis_tampilan(string type, int status, ProblemKind expected) =>
        Assert.Equal(expected, ProblemPresenter.Classify(new GmsProblemDetails { Type = type, Status = status }));

    [Fact]
    public void Problem_tak_dikenal_dipetakan_menurut_status_dan_penjelasan_manusiawi()
    {
        Assert.Equal(ProblemKind.Business, ProblemPresenter.Classify(new GmsProblemDetails { Type = "urn:lain", Status = 409 }));
        Assert.Equal(ProblemKind.Server, ProblemPresenter.Classify(new GmsProblemDetails { Type = "urn:lain", Status = 502 }));

        var concurrency = ProblemPresenter.Describe(new GmsProblemDetails { Type = ProblemTypes.ConcurrencyConflict, Status = 409, TraceId = "abc" });
        Assert.Equal("Data telah diubah pengguna lain", concurrency.Title);
        Assert.Contains("Muat ulang", concurrency.Detail);
        Assert.Equal("abc", concurrency.TraceId);

        var server = ProblemPresenter.Describe(new GmsProblemDetails { Type = ProblemTypes.InternalError, Status = 500 });
        Assert.True(server.CanRetry);
        Assert.DoesNotContain("Exception", server.Detail);
    }

    [Theory]
    [InlineData(ProblemTypes.SessionReplaced, "session-replaced")]
    [InlineData(ProblemTypes.AccountDisabled, "account-disabled")]
    [InlineData(ProblemTypes.SessionExpired, "session-expired")]
    public void Alasan_sesi_berakhir_untuk_halaman_login(string type, string reason) =>
        Assert.Equal(reason, ProblemPresenter.SessionEndReason(new GmsProblemDetails { Type = type, Status = 401 }));

    // ===== Autentikasi =====

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/work-orders?x=1", "/work-orders?x=1")]
    [InlineData("//evil.com", "/")]
    [InlineData("https://evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("javascript:alert(1)", "/")]
    [InlineData("/login?x=1", "/")]
    [InlineData("/a\nb", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void ReturnUrl_hanya_jalur_lokal_yang_aman(string? input, string expected) =>
        Assert.Equal(expected, AccountEndpoints.SafeReturnUrl(input));

    [Fact]
    public void Pesan_login_tidak_membocorkan_keberadaan_username_dan_memuat_jam_kunci()
    {
        Assert.Null(LoginPage.Describe(null, null, null));
        Assert.Equal("Username atau password salah.", LoginPage.Describe("invalid-credentials", null, null)!.Text);
        Assert.Contains("Administrator", LoginPage.Describe("account-locked", null, null)!.Text);
        var until = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(); // 15:00 WIB
        Assert.Contains("15:00", LoginPage.Describe("account-locked", null, until)!.Text);
        Assert.Equal("warning", LoginPage.Describe("session-replaced", null, null)!.Kind);
        Assert.Equal("info", LoginPage.Describe(null, "signed-out", null)!.Kind);
    }

    [Fact]
    public void Klaim_pengguna_memuat_permission_dan_peran_dan_menolak_yang_tidak_dimiliki()
    {
        var user = WebTestBase.User([Permissions.CustomerPO.View, Permissions.StokFG.View, Permissions.StokFG.View], "Siti", admin: true);

        Assert.True(user.HasPermission(Permissions.CustomerPO.View));
        Assert.False(user.HasPermission(Permissions.WorkOrder.Release));
        Assert.True(user.HasAnyPermission(Permissions.WorkOrder.Release, Permissions.StokFG.View));
        Assert.Equal("Siti", user.DisplayName());
        Assert.True(user.IsAdmin());
        Assert.False(user.MustChangePasswordNow());
        Assert.Equal(2, user.FindAll(GmsClaims.Permission).Count()); // duplikat dibuang
        Assert.False(new System.Security.Claims.ClaimsPrincipal().HasPermission(Permissions.CustomerPO.View)); // belum login
    }

    [Fact]
    public async Task Kebijakan_perm_dibangun_dinamis_dan_handler_mengikuti_klaim()
    {
        var provider = new PermissionPolicyProvider(Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Authorization.AuthorizationOptions()));
        var policy = await provider.GetPolicyAsync(PermissionPolicyProvider.PolicyFor(Permissions.WorkOrder.View));

        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(Permissions.WorkOrder.View, requirement.Permission);

        async Task<bool> Succeeds(System.Security.Claims.ClaimsPrincipal user)
        {
            var context = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext([requirement], user, null);
            await new PermissionHandler().HandleAsync(context);
            return context.HasSucceeded;
        }

        Assert.True(await Succeeds(WebTestBase.User([Permissions.WorkOrder.View])));
        Assert.False(await Succeeds(WebTestBase.User([Permissions.CustomerPO.View])));
        Assert.NotNull(await provider.GetDefaultPolicyAsync());
    }

    // ===== Navigasi =====

    [Fact]
    public void Navigasi_menyembunyikan_item_tanpa_izin_dan_yang_belum_tersedia_serta_grup_kosong()
    {
        IReadOnlyList<NavGroup> catalog =
        [
            new("Penjualan",
            [
                new("Customer PO", "/purchase-orders", Permissions.CustomerPO.View, new Icons.Regular.Size20.Home(), Enabled: true),
                new("Work Order", "/work-orders", Permissions.WorkOrder.View, new Icons.Regular.Size20.Home(), Enabled: true),
                new("Belum Ada", "/segera", Permissions.CustomerPO.View, new Icons.Regular.Size20.Home(), Enabled: false),
            ]),
            new("Penggajian", [new("Periode", "/payroll", Permissions.Payroll.View, new Icons.Regular.Size20.Home(), Enabled: true)]),
        ];

        var visible = NavCatalog.VisibleFor(p => p == Permissions.CustomerPO.View, catalog);

        var group = Assert.Single(visible);
        Assert.Equal("Penjualan", group.Title);
        Assert.Equal(["Customer PO"], group.Items.Select(i => i.Title));
        Assert.Empty(NavCatalog.VisibleFor(_ => false, catalog));
    }

    [Fact]
    public void Katalog_bawaan_mencakup_semua_grup_rancangan_dan_setiap_item_memakai_permission_yang_ada()
    {
        Assert.Equal(["Penjualan", "Gudang FG", "Produksi", "Penggajian", "Master Data", "Administrasi"], NavCatalog.Groups.Select(g => g.Title));
        var known = PermissionCatalog.All.Select(p => p.Key).ToHashSet();
        Assert.All(NavCatalog.Groups.SelectMany(g => g.Items), item => Assert.Contains(item.Permission, known));
        Assert.True(NavCatalog.Groups.First(g => g.Title == "Master Data").CollapsedByDefault);
    }

    // ===== Status chip =====

    [Fact]
    public void Status_dipetakan_ke_label_nada_dan_ikon_dan_Voided_dicoret()
    {
        Assert.Equal(("In Progress", "brand"), (StatusPresentation.For(Gms.Shared.Enums.PoStatus.InProgress).Label, StatusPresentation.For(Gms.Shared.Enums.PoStatus.InProgress).Tone));
        Assert.Equal("danger-soft", StatusPresentation.For(Gms.Shared.Enums.WoStatus.Cancelled).Tone);
        Assert.Equal("dark", StatusPresentation.For(Gms.Shared.Enums.WoStatus.Closed).Tone);
        var voided = StatusPresentation.For(Gms.Shared.Enums.DocStatus.Voided);
        Assert.True(voided.Strike);
        Assert.Equal("Terlambat", StatusPresentation.For("late").Label);
        Assert.Equal("Belum Final", StatusPresentation.For("provisional").Label);
        Assert.Equal("Siap Produksi", StatusPresentation.For("ready").Label);
        Assert.Equal("Closed", StatusPresentation.For(Gms.Shared.Enums.PayrollStatus.Closed).Label);
        Assert.Equal("tidak dikenal", StatusPresentation.For("tidak dikenal").Label);
        Assert.All(Enum.GetValues<Gms.Shared.Enums.PoStatus>(), s => Assert.NotNull(StatusPresentation.For(s).Icon));
        Assert.All(Enum.GetValues<Gms.Shared.Enums.WoStatus>(), s => Assert.NotNull(StatusPresentation.For(s).Icon));
    }
}
