using Gms.Shared.Access;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;
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
services.AddSingleton<MockAccessStore>();
services.AddScoped<IUsersApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockUsersApi>(sp) : ActivatorUtilities.CreateInstance<HttpUsersApi>(sp));
services.AddScoped<IRolesApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockRolesApi>(sp) : ActivatorUtilities.CreateInstance<HttpRolesApi>(sp));
services.AddScoped<IAuditApi>(sp => api.UseMock ? ActivatorUtilities.CreateInstance<MockAuditApi>(sp) : ActivatorUtilities.CreateInstance<HttpAuditApi>(sp));

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
