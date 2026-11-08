using System.Diagnostics;
using Gms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Kredensial TIDAK boleh ada di repository: gunakan user-secrets (dev) atau environment variable
// (ConnectionStrings__Default) pada lingkungan lain.
builder.Services.AddDbContext<GmsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// RFC 7807 Problem Details untuk SEMUA galat. Tipe dan ekstensi: lihat docs/Architecture/ApiConventions.md.
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Gms.Api.UnhandledException");
    logger.LogError(exception, "Unhandled exception");

    // Tidak membocorkan detail internal ke klien (hanya traceId untuk dilaporkan).
    var problem = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await problem.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails =
        {
            Type = "urn:gms:problem:internal-error",
            Title = "Terjadi kesalahan pada server.",
            Status = StatusCodes.Status500InternalServerError,
        },
    });
}));
app.UseStatusCodePages();

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();
