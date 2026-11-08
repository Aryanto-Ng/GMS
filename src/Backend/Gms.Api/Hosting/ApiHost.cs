using System.Text.Json;
using System.Text.Json.Serialization;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Features.Access.Audit;
using Gms.Api.Features.Access.Auth;
using Gms.Api.Features.Access.Roles;
using Gms.Api.Features.Access.Seeding;
using Gms.Api.Features.Access.Users;
using Gms.Api.Features.Master;
using Gms.Api.Features.Sales;
using Gms.Api.Security;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Hosting;

/// <summary>
/// Perakitan host API. Dipisah dari Program agar tes integrasi dapat membangun host yang sama
/// (dengan konfigurasi dan layanan yang diganti) tanpa package tambahan.
/// </summary>
public static class ApiHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var services = builder.Services;

        // Konfigurasi bertipe
        services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
        services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));

        // Data: connection string dibaca saat DbContext dibuat (memungkinkan penggantian konfigurasi oleh tes).
        services.AddDbContext<GmsDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AppTimeZone>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // Identity (hanya penyimpanan pengguna + hasher + lockout; sesi memakai token opaque sendiri, ADR-002)
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = false;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<GmsDbContext>()
            .AddPasswordValidator<GmsPasswordValidator>();

        services.AddOptions<IdentityOptions>().Configure<IOptions<AuthOptions>>((identity, auth) =>
        {
            identity.Lockout.MaxFailedAccessAttempts = auth.Value.MaxFailedAttempts;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(auth.Value.LockoutMinutes);
        });

        services.AddAuthentication(GmsSessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, GmsSessionAuthenticationHandler>(GmsSessionAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Handler use case (Vertical Slice): setiap kelas publik berakhiran "Handler" di Features didaftarkan scoped.
        foreach (var type in typeof(ApiHost).Assembly.GetTypes().Where(IsHandler))
        {
            services.AddScoped(type);
        }

        services.AddScoped<PoQueries>();
        services.AddScoped<MasterContext>();
        services.AddScoped<NumberSequences>();

        services.AddScoped<IDataSeeder, AccessSeeder>();
        services.AddScoped<IDataSeeder, MasterSeeder>();
        services.AddScoped<IDataSeeder, PoSeeder>();

        configure?.Invoke(builder);

        var app = builder.Build();
        ConfigurePipeline(app);
        return app;
    }

    private static bool IsHandler(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsPublic: true }
        && type.Name.EndsWith("Handler", StringComparison.Ordinal)
        && type.Namespace?.StartsWith("Gms.Api.Features", StringComparison.Ordinal) == true
        && type != typeof(GmsSessionAuthenticationHandler);

    private static void ConfigurePipeline(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gms.Api");

        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            var problem = ProblemWriter.Map(exception ?? new InvalidOperationException("Unknown"), logger);
            await ProblemWriter.WriteAsync(context, problem);
        }));

        // Respons kosong (404/405 dst.) tetap berbentuk Problem Details.
        app.UseStatusCodePages(async statusContext =>
            await ProblemWriter.WriteAsync(statusContext.HttpContext, ProblemWriter.ForStatus(statusContext.HttpContext.Response.StatusCode)));

        app.UseAuthentication();
        app.UseMiddleware<MustChangePasswordMiddleware>();
        app.UseAuthorization();

        app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        AuthEndpoints.Map(app);
        UsersEndpoints.Map(app);
        RolesEndpoints.Map(app);
        AuditEndpoints.Map(app);
        MasterEndpoints.Map(app);
        PoEndpoints.Map(app);
    }
}
