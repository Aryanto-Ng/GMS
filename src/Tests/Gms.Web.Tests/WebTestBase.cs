using System.Security.Claims;
using Bunit;
using Gms.Shared.Access;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>Dasar tes komponen: layanan AntDesign, JS longgar (modul isolasi dipalsukan), pengguna uji, dan penanganan galat API.</summary>
public abstract class WebTestBase : BunitContext
{
    private readonly System.Globalization.CultureInfo _uiBefore = System.Globalization.CultureInfo.CurrentUICulture;

    protected WebTestBase()
    {
        // Bahasa UI aplikasi bawaan = Indonesia; mesin uji dapat berbahasa lain (teks tes ditulis dalam Indonesia).
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("id-ID");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLocalization();
        Services.AddOptions<Gms.Web.Infrastructure.AppIdentityOptions>();
        Services.AddSingleton<Gms.Shared.Notifications.INotificationsApi>(new Gms.Web.Services.Api.MockNotificationsApi());
        Services.AddScoped<Gms.Web.Services.Notifications.NotificationText>();
        Services.AddSingleton(TimeProvider.System);
        Services.AddScoped<Gms.Web.Services.Tabs.TabManager>();
        Services.AddScoped<Gms.Web.Services.IGmsToast, Gms.Web.Services.GmsToast>();
        Services.AddScoped<ApiErrorHandler>();
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuth(User(Array.Empty<string>())));
        Services.AddSingleton<Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider, FakeAntiforgery>();
    }

    public static ClaimsPrincipal User(IEnumerable<string> permissions, string name = "Budi", bool admin = false, bool mustChange = false) =>
        GmsClaims.Create(
            new CurrentUserDto(Guid.NewGuid(), "budi", name, Guid.NewGuid(), "Staf Comfi", admin), "token-uji", mustChange, permissions);

    protected void SignIn(params string[] permissions)
    {
        var existing = Services.FirstOrDefault(d => d.ServiceType == typeof(AuthenticationStateProvider));
        if (existing is not null)
        {
            Services.Remove(existing);
        }

        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuth(User(permissions)));
    }

    /// <summary>Menunggu tugas selesai di metode bantu (analyzer xUnit melarang pemblokiran langsung di metode tes).</summary>
    protected static T Sync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    protected NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private sealed class FakeAntiforgery : Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider
    {
        public override Microsoft.AspNetCore.Components.Forms.AntiforgeryRequestToken? GetAntiforgeryToken() => new("token-uji", "__RequestVerificationToken");
    }

    private sealed class FakeAuth(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
