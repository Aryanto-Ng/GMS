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
using Gms.Api.Features.Notifications;
using Gms.Api.Features.Dashboard;
using Gms.Api.Features.Deliveries;
using Gms.Api.Features.Inventory;
using Gms.Api.Features.Payroll;
using Gms.Api.Features.Production;
using Gms.Api.Features.Sales;
using Gms.Api.Features.WorkOrders;
using Gms.Api.Security;
using Gms.Domain.Notifications;
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
    public const int DefaultMaxPoolSize = 20;

    public static string? WithPoolLimit(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
        {
            return connection;
        }

        var explicitLimit = connection.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase)
            || connection.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase);
        if (explicitLimit)
        {
            return connection;
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection)
        {
            MaxPoolSize = Math.Max(1, configuration.GetValue("Database:MaxPoolSize", DefaultMaxPoolSize)),
        };
        return builder.ConnectionString;
    }

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var services = builder.Services;

        // Konfigurasi bertipe
        services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
        services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
        services.Configure<NotificationConfig>(builder.Configuration.GetSection(NotificationConfig.SectionName));

        // Data: connection string dibaca saat DbContext dibuat (memungkinkan penggantian konfigurasi oleh tes).
        // BUG-002: pool Npgsql bawaan (100) melebihi slot server (max_connections 100 - 3 cadangan), sehingga lonjakan permintaan memicu SqlState 53300.
        // Batas pool diterapkan dari konfigurasi (Database:MaxPoolSize, bawaan 20) kecuali connection string sudah menetapkan "Maximum Pool Size".
        services.AddDbContext<GmsDbContext>((sp, options) =>
            options.UseNpgsql(WithPoolLimit(sp.GetRequiredService<IConfiguration>()))
                .AddInterceptors(new NotificationSideEffectsInterceptor(sp)));

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
        services.AddScoped<WoQueries>();
        services.AddScoped<StepLedger>();
        services.AddScoped<EntryQueries>();
        services.AddScoped<HandoverEngine>();
        services.AddScoped<HandoverFgQueries>();
        services.AddScoped<MaterialHandoverQueries>();
        services.AddScoped<StockQueries>();
        services.AddScoped<DeliveryEngine>();
        services.AddScoped<DeliveryQueries>();
        services.AddScoped<PayrollCalculator>();
        services.AddScoped<PayrollQueries>();
        services.AddScoped<PayrollPeriodQueries>();
        services.AddScoped<DashboardQueries>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<NotificationQueries>();
        services.AddScoped<NotificationEmitter>();
        services.AddScoped<NotificationJob>();
        services.AddScoped<MasterContext>();
        services.AddScoped<NumberSequences>();

        services.AddScoped<IDataSeeder, AccessSeeder>();
        services.AddScoped<IDataSeeder, MasterSeeder>();
        services.AddScoped<IDataSeeder, PoSeeder>();
        services.AddScoped<IDataSeeder, NotificationSeeder>();

        // Pekerja latar notifikasi (dapat dimatikan lewat Notifications:SchedulerEnabled=false, mis. pada tes).
        if (builder.Configuration.GetValue("Notifications:SchedulerEnabled", true))
        {
            services.AddHostedService<NotificationScheduler>();
        }

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
        WoEndpoints.Map(app);
        ProductionEndpoints.Map(app);
        InventoryEndpoints.Map(app);
        DeliveryEndpoints.Map(app);
        PayrollEndpoints.Map(app);
        DashboardEndpoints.Map(app);
        NotificationEndpoints.Map(app);
    }
}
