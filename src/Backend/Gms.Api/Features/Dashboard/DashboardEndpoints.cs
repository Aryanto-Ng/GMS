using Gms.Api.Security;

namespace Gms.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Satu endpoint ringan per widget (dimuat independen). Izin dicek per widget di dalam DashboardQueries; login saja yang wajib di sini
        // karena Beranda dapat diakses semua pengguna (FSD Beranda 5).
        app.MapGet("/api/v1/dashboard/{widgetId}", async (string widgetId, DashboardQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(widgetId, ct))).WithTags("Beranda").RequireLogin();
    }
}
