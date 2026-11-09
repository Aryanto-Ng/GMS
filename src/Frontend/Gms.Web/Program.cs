using Gms.Shared.Access;
using Gms.Web.Components;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();

services.AddRazorComponents().AddInteractiveServerComponents();
services.AddFluentUIComponents();
services.AddHttpContextAccessor();

// Autentikasi: cookie terenkripsi berisi token sesi opaque + klaim permission (pola BFF, ADR-002).
services.AddAuthentication(GmsClaims.Scheme)
    .AddCookie(GmsClaims.Scheme, options =>
    {
        options.Cookie.Name = "gms.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.LoginPath = AccountEndpoints.LoginPath;
        options.ReturnUrlParameter = "returnUrl";
        options.AccessDeniedPath = "/forbidden";
    });
services.AddAuthorization(options =>
{
    // Semua halaman wajib login kecuali yang ditandai [AllowAnonymous] (login, 403).
    options.FallbackPolicy = new AuthorizationPolicyBuilder(GmsClaims.Scheme).RequireAuthenticatedUser().Build();
});
services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
services.AddCascadingAuthenticationState();

// Klien API: HttpClient bernama + GmsApiClient (token dari pengguna saat ini) + antarmuka bertipe (Http atau Mock, ADR-014).
services.AddHttpClient(GmsApiClient.HttpClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
services.AddScoped<ISessionTokenProvider, ClaimsSessionTokenProvider>();
services.AddScoped<GmsApiClient>();
services.AddScoped<ApiErrorHandler>();
services.AddScoped<IAuthApi>(sp => api.UseMock
    ? ActivatorUtilities.CreateInstance<MockAuthApi>(sp)
    : ActivatorUtilities.CreateInstance<HttpAuthApi>(sp));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
AccountEndpoints.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Titik masuk untuk tes.</summary>
public partial class Program;
