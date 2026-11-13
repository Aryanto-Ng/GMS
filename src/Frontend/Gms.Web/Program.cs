using System.Threading.RateLimiting;
using Gms.Shared.Access;
using Gms.Shared.CustomerPo;
using Gms.Shared.Dashboard;
using Gms.Shared.Deliveries;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Payroll;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;
using Gms.Web.Components;
using Gms.Web.Infrastructure;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;
using Gms.Web.Services.Tabs;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
var api = builder.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() ?? new ApiOptions();

services.AddRazorComponents().AddInteractiveServerComponents();
services.AddScoped<Gms.Web.Services.IGmsToast, Gms.Web.Services.GmsToast>();
services.AddScoped<TabManager>();
services.Configure<AppIdentityOptions>(builder.Configuration.GetSection(AppIdentityOptions.SectionName));
services.AddLocalization();
services.AddScoped<Gms.Web.Services.Notifications.NotificationText>();
services.AddHttpContextAccessor();

// SEC-004: pembatasan percobaan login per alamat IP (melengkapi lockout per akun yang dapat disalahgunakan untuk password spraying).
var loginPerMinute = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 20);
services.AddRateLimiter(options =>
{
    options.AddPolicy(AccountEndpoints.LoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Redirect($"{AccountEndpoints.LoginPath}?error=too-many-requests");
        return ValueTask.CompletedTask;
    };
});

// Produksi: kunci Data Protection (enkripsi cookie gms.auth) disimpan di volume agar login bertahan setelah restart/deploy.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    services.AddDataProtection().SetApplicationName("gms-web").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// Di belakang reverse proxy (Traefik/Nginx, TLS berakhir di proxy): percayai X-Forwarded-* dari jaringan container.
services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

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

// Master Data: Mock = singleton (state bertahan selama proses), Http = scoped.
services.AddSingleton<MockUomApi>();
services.AddSingleton<MockMaterialCategoryApi>();
services.AddSingleton<MockMaterialApi>();
services.AddSingleton<MockSizeApi>();
services.AddSingleton<MockColorApi>();
services.AddSingleton<MockCustomerApi>();
services.AddSingleton<MockVendorApi>();
services.AddSingleton<MockProcessApi>();
services.AddSingleton<MockWorkerApi>();
services.AddSingleton<MockFgApi>();
services.AddSingleton<MockImportApi>();
services.AddSingleton<MockPoApi>();
services.AddSingleton<MockWoApi>();
services.AddSingleton<MockProductionApi>();
services.AddSingleton<MockInventoryApi>();
services.AddSingleton<MockDeliveriesApi>();
services.AddSingleton<MockPayrollApi>();
services.AddSingleton<MockDashboardApi>();
services.AddScoped<IDashboardApi>(sp => api.UseMock ? sp.GetRequiredService<MockDashboardApi>() : ActivatorUtilities.CreateInstance<HttpDashboardApi>(sp));
services.AddScoped<IPayrollApi>(sp => api.UseMock ? sp.GetRequiredService<MockPayrollApi>() : ActivatorUtilities.CreateInstance<HttpPayrollApi>(sp));
services.AddScoped<IDeliveriesApi>(sp => api.UseMock ? sp.GetRequiredService<MockDeliveriesApi>() : ActivatorUtilities.CreateInstance<HttpDeliveriesApi>(sp));
services.AddScoped<IInventoryApi>(sp => api.UseMock ? sp.GetRequiredService<MockInventoryApi>() : ActivatorUtilities.CreateInstance<HttpInventoryApi>(sp));
services.AddScoped<IProductionApi>(sp => api.UseMock ? sp.GetRequiredService<MockProductionApi>() : ActivatorUtilities.CreateInstance<HttpProductionApi>(sp));
services.AddScoped<IWorkOrdersApi>(sp => api.UseMock ? sp.GetRequiredService<MockWoApi>() : ActivatorUtilities.CreateInstance<HttpWoApi>(sp));
services.AddScoped<IPurchaseOrdersApi>(sp => api.UseMock ? sp.GetRequiredService<MockPoApi>() : ActivatorUtilities.CreateInstance<HttpPoApi>(sp));
services.AddScoped<IImportApi>(sp => api.UseMock ? sp.GetRequiredService<MockImportApi>() : ActivatorUtilities.CreateInstance<HttpImportApi>(sp));
services.AddScoped<FileDownloader>();
services.AddScoped<IFgApi>(sp => api.UseMock ? sp.GetRequiredService<MockFgApi>() : ActivatorUtilities.CreateInstance<HttpFgApi>(sp));
services.AddScoped<IUomApi>(sp => api.UseMock ? sp.GetRequiredService<MockUomApi>() : ActivatorUtilities.CreateInstance<HttpUomApi>(sp));
services.AddScoped<IMaterialCategoryApi>(sp => api.UseMock ? sp.GetRequiredService<MockMaterialCategoryApi>() : ActivatorUtilities.CreateInstance<HttpMaterialCategoryApi>(sp));
services.AddScoped<IMaterialApi>(sp => api.UseMock ? sp.GetRequiredService<MockMaterialApi>() : ActivatorUtilities.CreateInstance<HttpMaterialApi>(sp));
services.AddScoped<ISizeApi>(sp => api.UseMock ? sp.GetRequiredService<MockSizeApi>() : ActivatorUtilities.CreateInstance<HttpSizeApi>(sp));
services.AddScoped<IColorApi>(sp => api.UseMock ? sp.GetRequiredService<MockColorApi>() : ActivatorUtilities.CreateInstance<HttpColorApi>(sp));
services.AddScoped<ICustomerApi>(sp => api.UseMock ? sp.GetRequiredService<MockCustomerApi>() : ActivatorUtilities.CreateInstance<HttpCustomerApi>(sp));
services.AddScoped<IVendorApi>(sp => api.UseMock ? sp.GetRequiredService<MockVendorApi>() : ActivatorUtilities.CreateInstance<HttpVendorApi>(sp));
services.AddScoped<IProcessApi>(sp => api.UseMock ? sp.GetRequiredService<MockProcessApi>() : ActivatorUtilities.CreateInstance<HttpProcessApi>(sp));
services.AddScoped<IWorkerApi>(sp => api.UseMock ? sp.GetRequiredService<MockWorkerApi>() : ActivatorUtilities.CreateInstance<HttpWorkerApi>(sp));
services.AddSingleton<MockNotificationsApi>();
services.AddSingleton<MockNotificationSettingsApi>();
services.AddScoped<Gms.Shared.Notifications.INotificationsApi>(sp => api.UseMock ? sp.GetRequiredService<MockNotificationsApi>() : ActivatorUtilities.CreateInstance<HttpNotificationsApi>(sp));
services.AddScoped<Gms.Shared.Notifications.INotificationSettingsApi>(sp => api.UseMock ? sp.GetRequiredService<MockNotificationSettingsApi>() : ActivatorUtilities.CreateInstance<HttpNotificationSettingsApi>(sp));
services.AddSingleton<MockAccessStore>();
services.AddScoped<IUsersApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockUsersApi>(sp) : ActivatorUtilities.CreateInstance<HttpUsersApi>(sp));
services.AddScoped<IRolesApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockRolesApi>(sp) : ActivatorUtilities.CreateInstance<HttpRolesApi>(sp));
services.AddScoped<IAuditApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockAuditApi>(sp) : ActivatorUtilities.CreateInstance<HttpAuditApi>(sp));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Bahasa UI: Indonesia (bawaan) dan English; dipilih lewat cookie (menu bahasa di header dan form masuk). Culture ditetapkan EKSPLISIT, bukan culture server.
var localization = new RequestLocalizationOptions()
    .SetDefaultCulture(AccountEndpoints.DefaultCulture)
    .AddSupportedCultures(AccountEndpoints.SupportedCultures)
    .AddSupportedUICultures(AccountEndpoints.SupportedCultures);
localization.RequestCultureProviders = [new CookieRequestCultureProvider()]; // pilihan pengguna saja; header Accept-Language browser diabaikan (bawaan Indonesia)
app.UseRequestLocalization(localization);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
AccountEndpoints.Map(app);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Titik masuk untuk tes.</summary>
public partial class Program;
