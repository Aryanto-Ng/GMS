using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Problems;
using Gms.Web.Components.Pages;
using Gms.Web.Components.Shared;
using Gms.Web.Infrastructure.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>ReasonDialog (alasan wajib + pratinjau dampak), AsyncContent (state UI), penjaga perubahan, dan halaman akun.</summary>
public sealed class DialogAndAsyncTests : WebTestBase
{
    private static GmsProblemDetails Problem(string type, int status, string? detail = null) =>
        new() { Type = type, Status = status, Title = "Judul", Detail = detail, TraceId = "00-t" };

    private IRenderedComponent<ReasonDialog> Dialog(
        Func<string, Task>? onConfirm = null, Func<CancellationToken, Task<ImpactPreview>>? preview = null, bool open = true, bool canConfirm = true,
        EventCallback<bool>? openChanged = null) =>
        Render<ReasonDialog>(p =>
        {
            p.Add(x => x.Open, open).Add(x => x.Title, "Void entri output").Add(x => x.Description, "Entri akan dibatalkan.")
             .Add(x => x.OnConfirm, onConfirm ?? (_ => Task.CompletedTask)).Add(x => x.CanConfirm, canConfirm);
            if (preview is not null)
            {
                p.Add(x => x.LoadPreview, preview);
            }

            if (openChanged is { } callback)
            {
                p.Add(x => x.OpenChanged, callback);
            }
        });

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<ReasonDialog> cut) =>
        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Ya, lanjutkan"));

    // ===== ReasonDialog =====

    [Fact]
    public void Dialog_memakai_dialog_native_dengan_judul_deskripsi_dan_label_alasan_terhubung()
    {
        var cut = Dialog();

        var dialog = cut.Find("dialog");
        var title = cut.Find("h2");
        Assert.Equal(title.Id, dialog.GetAttribute("aria-labelledby"));
        Assert.Contains("Void entri output", title.TextContent);
        Assert.Equal(cut.Find("p").Id, dialog.GetAttribute("aria-describedby"));
        var textarea = cut.Find("textarea");
        Assert.Equal(textarea.Id, cut.Find("label").GetAttribute("for"));
        Assert.Contains("wajib", cut.Find("label").TextContent); // penanda wajib berupa teks
        Assert.Equal("500", textarea.GetAttribute("maxlength"));
        Assert.Contains("0 / 500", cut.Find("small").TextContent);
    }

    [Fact]
    public void Dialog_tertutup_tidak_merender_isi_sama_sekali()
    {
        var cut = Dialog(open: false);

        Assert.Empty(cut.FindAll("h2, textarea, fluent-button"));
    }

    [Fact]
    public void Tombol_utama_nonaktif_sampai_alasan_terisi_dan_spasi_saja_tidak_cukup()
    {
        var cut = Dialog();
        Assert.True(Confirm(cut).HasAttribute("disabled"));

        cut.Find("textarea").Input("   ");
        Assert.True(Confirm(cut).HasAttribute("disabled"));

        cut.Find("textarea").Input("salah orang");
        Assert.False(Confirm(cut).HasAttribute("disabled"));
        Assert.Contains("11 / 500", cut.Find("small").TextContent);
    }

    [Fact]
    public void Tombol_utama_berwarna_bahaya_untuk_aksi_destruktif_dan_biasa_bila_tidak()
    {
        var danger = Dialog();
        var plain = Render<ReasonDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Lepas").Add(x => x.Danger, false).Add(x => x.OnConfirm, _ => Task.CompletedTask));

        Assert.Contains("gms-danger", Confirm(danger).ClassName);
        Assert.DoesNotContain("gms-danger", plain.FindAll("fluent-button").Last().ClassName ?? string.Empty);
    }

    [Fact]
    public void Alasan_opsional_mengizinkan_konfirmasi_tanpa_alasan()
    {
        var cut = Render<ReasonDialog>(p => p.Add(x => x.Open, true).Add(x => x.Title, "Tutup").Add(x => x.ReasonRequired, false).Add(x => x.OnConfirm, _ => Task.CompletedTask));

        Assert.Contains("opsional", cut.Find("label").TextContent);
        Assert.False(cut.FindAll("fluent-button").Last().HasAttribute("disabled"));
    }

    [Fact]
    public async Task Konfirmasi_mengirim_alasan_yang_dipangkas_menutup_dialog_dan_mengabari_OpenChanged()
    {
        string? received = null;
        bool? opened = null;
        var cut = Dialog(onConfirm: reason => { received = reason; return Task.CompletedTask; },
            openChanged: EventCallback.Factory.Create<bool>(this, v => opened = v));
        cut.Find("textarea").Input("  kesalahan input  ");

        await cut.InvokeAsync(() => Confirm(cut).Click());

        Assert.Equal("kesalahan input", received);
        Assert.False(opened);
    }

    [Fact]
    public async Task Galat_bisnis_ditampilkan_di_dalam_dialog_tanpa_menutupnya_dan_dapat_dicoba_lagi()
    {
        var attempts = 0;
        bool? opened = null;
        var problem = Problem(ProblemTypes.RuleViolation, 409, "Void ditolak.");
        var withViolations = problem with { Violations = [new RuleViolation("output.below-downstream", "Navy / L", 5, 25, "Output langkah berikutnya sudah 25.")] };
        var cut = Dialog(onConfirm: _ => ++attempts == 1 ? throw new ApiException(withViolations) : Task.CompletedTask,
            openChanged: EventCallback.Factory.Create<bool>(this, v => opened = v));
        cut.Find("textarea").Input("alasan");

        await cut.InvokeAsync(() => Confirm(cut).Click());

        Assert.Null(opened); // tidak menutup
        Assert.Contains("Output langkah berikutnya sudah 25.", cut.Find(".gms-violations").TextContent);
        Assert.False(Confirm(cut).HasAttribute("disabled")); // dapat dicoba lagi
        await cut.InvokeAsync(() => Confirm(cut).Click());
        Assert.False(opened);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Galat_401_mengalihkan_ke_login_dengan_alasan_dan_returnUrl()
    {
        var cut = Dialog(onConfirm: _ => throw new ApiException(Problem(ProblemTypes.SessionReplaced, 401)));
        Nav.NavigateTo("/work-orders/abc");
        cut.Find("textarea").Input("alasan");

        await cut.InvokeAsync(() => Confirm(cut).Click());

        Assert.Contains("/account/session-ended?reason=session-replaced", Nav.Uri);
        Assert.Contains("returnUrl=%2Fwork-orders%2Fabc", Nav.Uri);
    }

    [Fact]
    public async Task Pratinjau_dimuat_saat_dibuka_dan_aman_mengizinkan_konfirmasi()
    {
        var calls = 0;
        var safe = new ImpactPreview([new ImpactRow("Navy / M", "Kumulatif", 40m, 10m, null, true, null)], true, [], []);
        var cut = Dialog(preview: _ => { calls++; return Task.FromResult(safe); });
        cut.Find("textarea").Input("alasan");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-impact")));
        Assert.Equal(1, calls);
        Assert.False(Confirm(cut).HasAttribute("disabled"));
        await Task.CompletedTask;
    }

    [Fact]
    public void Pratinjau_yang_melanggar_menonaktifkan_tombol_utama_walau_alasan_terisi_dan_menampilkan_penahan()
    {
        var unsafePreview = new ImpactPreview(
            [new ImpactRow("Navy / L", "Kumulatif", 30m, 5m, 20m, false, "Sudah dipakai langkah berikutnya.")], false,
            [new RuleViolation("output.below-downstream", "Navy / L", 5, 25, "Output langkah berikutnya sudah 25.")], []);
        var cut = Dialog(preview: _ => Task.FromResult(unsafePreview));
        cut.Find("textarea").Input("alasan");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-impact")));

        Assert.True(Confirm(cut).HasAttribute("disabled"));
        Assert.Contains("Aksi tidak dapat dilanjutkan", cut.Markup);
        Assert.Contains("Output langkah berikutnya sudah 25.", cut.Markup);
    }

    [Fact]
    public void Pratinjau_gagal_dimuat_menampilkan_galat_dan_tombol_utama_tetap_nonaktif()
    {
        var cut = Dialog(preview: _ => throw new ApiException(Problem(ProblemTypes.InternalError, 500)));
        cut.Find("textarea").Input("alasan");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-banner")));

        Assert.True(Confirm(cut).HasAttribute("disabled")); // tanpa pratinjau valid, tidak boleh dikonfirmasi
    }

    [Fact]
    public void Syarat_tambahan_CanConfirm_false_menahan_tombol_walau_alasan_terisi()
    {
        var cut = Dialog(canConfirm: false);
        cut.Find("textarea").Input("alasan");

        Assert.True(Confirm(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task Modul_JS_dimuat_saat_dibuka_dan_Esc_diabaikan_saat_memproses()
    {
        var gate = new TaskCompletionSource();
        bool? opened = null;
        var cut = Dialog(onConfirm: _ => gate.Task, openChanged: EventCallback.Factory.Create<bool>(this, v => opened = v));
        cut.Find("textarea").Input("alasan");
        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, i => i.Identifier == "show"));

        var running = cut.InvokeAsync(() => Confirm(cut).Click());
        await cut.InvokeAsync(() => cut.Instance.RequestClose()); // Esc saat memproses
        Assert.Null(opened);
        Assert.Equal("true", cut.Find("dialog").GetAttribute("aria-busy"));

        gate.SetResult();
        await running;
        cut.WaitForAssertion(() => Assert.False(opened));
    }

    [Fact]
    public async Task Esc_saat_tidak_memproses_menutup_dan_Batal_menutup()
    {
        var states = new List<bool>();
        var cut = Dialog(openChanged: EventCallback.Factory.Create<bool>(this, states.Add));

        await cut.InvokeAsync(() => cut.Instance.RequestClose());
        Assert.Equal([false], states);
    }

    // ===== AsyncContent =====

    private IRenderedComponent<AsyncContent<string>> Async(
        Func<CancellationToken, Task<string>> loader, Func<string, bool>? isEmpty = null, object? key = null) =>
        Render<AsyncContent<string>>(p => p
            .Add(x => x.Loader, loader)
            .Add(x => x.Key, key)
            .Add(x => x.IsEmpty, isEmpty)
            .Add(x => x.ChildContent, (string data) => (RenderFragment)(b => b.AddMarkupContent(0, $"<p id='isi'>{data}</p>")))
            .Add(x => x.EmptyContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p id='kosong'>Tidak ada data</p>"))));

    [Fact]
    public void AsyncContent_menampilkan_skeleton_saat_memuat_awal_lalu_konten()
    {
        var gate = new TaskCompletionSource<string>();
        var cut = Async(_ => gate.Task);

        Assert.NotNull(cut.Find(".gms-skeleton"));
        Assert.Equal("true", cut.Find(".gms-async").GetAttribute("aria-busy"));

        gate.SetResult("halo");
        cut.WaitForAssertion(() => Assert.Equal("halo", cut.Find("#isi").TextContent));
        Assert.Empty(cut.FindAll(".gms-skeleton"));
        Assert.Equal("false", cut.Find(".gms-async").GetAttribute("aria-busy"));
    }

    [Fact]
    public void AsyncContent_menampilkan_state_kosong_bila_data_kosong()
    {
        var cut = Async(_ => Task.FromResult(string.Empty), isEmpty: s => s.Length == 0);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#kosong")));
        Assert.Empty(cut.FindAll("#isi"));
    }

    [Fact]
    public void AsyncContent_galat_awal_menampilkan_state_galat_dengan_traceId_dan_Coba_lagi_memuat_ulang()
    {
        var calls = 0;
        var cut = Async(_ => ++calls == 1 ? throw new ApiException(Problem(ProblemTypes.InternalError, 500)) : Task.FromResult("pulih"));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".gms-state.error")));
        Assert.Contains("00-t", cut.Find("code").TextContent);

        cut.Find("fluent-button").Click();
        cut.WaitForAssertion(() => Assert.Equal("pulih", cut.Find("#isi").TextContent));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void AsyncContent_404_dan_403_tidak_menawarkan_Coba_lagi()
    {
        var notFound = Async(_ => throw new ApiException(Problem(ProblemTypes.NotFound, 404)));
        var forbidden = Async(_ => throw new ApiException(Problem(ProblemTypes.Forbidden, 403)));

        notFound.WaitForAssertion(() => Assert.NotNull(notFound.Find(".gms-state.error")));
        forbidden.WaitForAssertion(() => Assert.NotNull(forbidden.Find(".gms-state.error")));
        Assert.Empty(notFound.FindAll("fluent-button"));
        Assert.Empty(forbidden.FindAll("fluent-button"));
        Assert.Contains("Data tidak ditemukan", notFound.Markup);
    }

    [Fact]
    public void AsyncContent_sesi_berakhir_mengalihkan_ke_login_bukan_menampilkan_galat()
    {
        var cut = Async(_ => throw new ApiException(Problem(ProblemTypes.SessionExpired, 401)));

        cut.WaitForAssertion(() => Assert.Contains("/account/session-ended?reason=session-expired", Nav.Uri));
        Assert.Empty(cut.FindAll(".gms-state.error"));
    }

    [Fact]
    public void AsyncContent_muat_ulang_mempertahankan_konten_lama_dan_menampilkan_indikator_tipis()
    {
        var calls = 0;
        var second = new TaskCompletionSource<string>();
        var cut = Async(_ => ++calls == 1 ? Task.FromResult("lama") : second.Task, key: 1);
        cut.WaitForAssertion(() => Assert.Equal("lama", cut.Find("#isi").TextContent));

        cut.Render(p => p.Add(x => x.Key, 2)); // filter berubah → muat ulang
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-progress")));
        Assert.Equal("lama", cut.Find("#isi").TextContent); // konten lama tetap terlihat, bukan skeleton
        Assert.Empty(cut.FindAll(".gms-skeleton"));

        second.SetResult("baru");
        cut.WaitForAssertion(() => Assert.Equal("baru", cut.Find("#isi").TextContent));
        Assert.Empty(cut.FindAll(".gms-progress"));
    }

    [Fact]
    public void AsyncContent_galat_saat_muat_ulang_menampilkan_banner_dan_tetap_memegang_data_lama()
    {
        var calls = 0;
        var cut = Async(_ => ++calls == 1 ? Task.FromResult("lama") : throw new ApiException(Problem(ProblemTypes.ConcurrencyConflict, 409)), key: 1);
        cut.WaitForAssertion(() => Assert.Equal("lama", cut.Find("#isi").TextContent));

        cut.Render(p => p.Add(x => x.Key, 2));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-banner")));
        Assert.Equal("lama", cut.Find("#isi").TextContent);
    }

    [Fact]
    public void AsyncContent_hasil_lama_yang_terlambat_tidak_menimpa_hasil_terbaru()
    {
        var first = new TaskCompletionSource<string>();
        var second = new TaskCompletionSource<string>();
        var calls = 0;
        var cut = Async(_ => ++calls == 1 ? first.Task : second.Task, key: 1);

        cut.Render(p => p.Add(x => x.Key, 2)); // memicu muat kedua sebelum yang pertama selesai
        second.SetResult("baru");
        cut.WaitForAssertion(() => Assert.Equal("baru", cut.Find("#isi").TextContent));
        first.SetResult("basi");

        cut.WaitForAssertion(() => Assert.Equal("baru", cut.Find("#isi").TextContent));
    }

    // ===== UnsavedChangesGuard =====

    [Fact]
    public void Navigasi_internal_dengan_perubahan_dikonfirmasi_dan_dibatalkan_bila_ditolak()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        Render<UnsavedChangesGuard>(p => p.Add(x => x.HasChanges, true));
        var start = Nav.Uri;

        Nav.NavigateTo("/lain");

        Assert.Equal(start, Nav.Uri);
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "confirm");
    }

    [Fact]
    public void Navigasi_dilanjutkan_bila_pengguna_menyetujui_dan_tanpa_perubahan_tidak_bertanya()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        Render<UnsavedChangesGuard>(p => p.Add(x => x.HasChanges, true));
        Nav.NavigateTo("/lanjut");
        Assert.EndsWith("/lanjut", Nav.Uri);

        var clean = new Bunit.BunitContext();
        clean.JSInterop.Mode = JSRuntimeMode.Strict; // bertanya akan gagal keras
        clean.Render<UnsavedChangesGuard>(p => p.Add(x => x.HasChanges, false));
        clean.Services.GetRequiredService<NavigationManager>().NavigateTo("/bebas");
        Assert.EndsWith("/bebas", clean.Services.GetRequiredService<NavigationManager>().Uri);
    }

    // ===== Halaman akun (Static SSR) =====

    [Fact]
    public void Halaman_login_memuat_form_post_ke_endpoint_dengan_label_terhubung_dan_ReturnUrl_aman()
    {
        Nav.NavigateTo("/login?returnUrl=%2Fstock&error=invalid-credentials");

        var cut = Render<LoginPage>();

        var form = cut.Find("form");
        Assert.Equal(("post", "/account/login"), (form.GetAttribute("method"), form.GetAttribute("action")));
        Assert.Equal("Username", cut.Find("label[for='username']").TextContent.Replace("wajib", "").Trim());
        Assert.Equal("username", cut.Find("#username").GetAttribute("autocomplete"));
        Assert.Equal("current-password", cut.Find("#password").GetAttribute("autocomplete"));
        Assert.Equal("password", cut.Find("#password").GetAttribute("type"));
        Assert.Equal("/stock", cut.Find("input[name='ReturnUrl']").GetAttribute("value"));
        Assert.Contains("Username atau password salah.", cut.Find("[role='alert']").TextContent);
        Assert.Null(cut.Find("#password").GetAttribute("value")); // password tidak pernah dipantulkan
    }

    [Fact]
    public void Halaman_login_menolak_ReturnUrl_berbahaya_dan_tanpa_galat_tidak_menampilkan_alert()
    {
        Nav.NavigateTo("/login?returnUrl=https%3A%2F%2Fevil.com");

        var cut = Render<LoginPage>();

        Assert.Equal("/", cut.Find("input[name='ReturnUrl']").GetAttribute("value"));
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public void Halaman_ganti_password_wajib_menampilkan_banner_dan_tanpa_tombol_Batal_serta_pesan_galat()
    {
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new object()));
        var must = User(["X.View"], mustChange: true);
        var existing = Services.First(d => d.ServiceType == typeof(Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider));
        Services.Remove(existing);
        Services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(new FixedAuth(must));
        Nav.NavigateTo("/account/change-password?error=mismatch");

        var cut = Render<ChangePasswordPage>();

        Assert.Contains("harus mengganti password", cut.Find(".gms-banner.info").TextContent);
        Assert.Empty(cut.FindAll("a.gms-secondary")); // wajib: tidak boleh dibatalkan
        Assert.Contains("Konfirmasi password tidak sama", cut.Find(".gms-banner.error").TextContent);
        Assert.Equal("new-password", cut.Find("#new").GetAttribute("autocomplete"));
        Assert.Equal("8", cut.Find("#new").GetAttribute("minlength"));
        Assert.Equal("/account/change-password", cut.Find("form").GetAttribute("action"));
    }

    [Fact]
    public void Halaman_ganti_password_sukarela_menampilkan_Batal_dan_pesan_server_apa_adanya()
    {
        SignIn("X.View");
        Nav.NavigateTo("/account/change-password?error=rejected&message=Password%20terlalu%20pendek&returnUrl=%2Fstock");

        var cut = Render<ChangePasswordPage>();

        Assert.Equal("/stock", cut.Find("a.gms-secondary").GetAttribute("href"));
        Assert.Contains("Password terlalu pendek", cut.Find(".gms-banner.error").TextContent);
        Assert.Empty(cut.FindAll(".gms-banner.info"));
    }

    private sealed class FixedAuth(System.Security.Claims.ClaimsPrincipal user) : Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider
    {
        public override Task<Microsoft.AspNetCore.Components.Authorization.AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new Microsoft.AspNetCore.Components.Authorization.AuthenticationState(user));
    }
}
