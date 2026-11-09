using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Security;

namespace Gms.Api.Features.Payroll;

public static class PayrollEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/payroll/periods").WithTags("Payroll");

        g.MapGet("", async (PayrollPeriodQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/next-defaults", async (PayrollPeriodQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.NextDefaultsAsync(ct))).RequirePermission(Permissions.Payroll.CreatePeriod);

        g.MapPost("", async (SavePeriodRequest request, CreatePayrollPeriodHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/payroll/periods/{created.Id}", created);
        }).RequirePermission(Permissions.Payroll.CreatePeriod);

        g.MapGet("/{id:guid}", async (Guid id, PayrollPeriodQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(id, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapPut("/{id:guid}", async (Guid id, SavePeriodRequest request, UpdatePayrollPeriodHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.Payroll.CreatePeriod);

        g.MapPost("/{id:guid}/close/preview", async (Guid id, PreviewClosePayrollPeriodHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct))).RequirePermission(Permissions.Payroll.ClosePeriod);

        g.MapPost("/{id:guid}/close", async (Guid id, ClosePeriodRequest request, ClosePayrollPeriodHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.Payroll.ClosePeriod);

        g.MapGet("/{id:guid}/summary", async (Guid id, string? q, PayrollQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetSummaryAsync(id, q, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/{id:guid}/workers/{workerId:guid}/detail", async (Guid id, Guid workerId, PayrollQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetWorkerDetailAsync(id, workerId, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/{id:guid}/daily", async (
            Guid id, int? page, int? pageSize, string? q, DateOnly? from, DateOnly? to, Guid? workerId, Guid? woId, string? processCode,
            PayrollQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetDailyAsync(id, new PayrollDailyQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 50, Q = q, From = from, To = to, WorkerId = workerId, WoId = woId, ProcessCode = processCode,
            }, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/{id:guid}/matrix", async (Guid id, PayrollQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetMatrixAsync(id, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/{id:guid}/adjustments", async (Guid id, PayrollQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAdjustmentsAsync(id, ct))).RequirePermission(Permissions.Payroll.View);

        g.MapGet("/{id:guid}/export", async (Guid id, string? format, PayrollExportHandler handler, CancellationToken ct) =>
        {
            var parsed = (format ?? "xlsx").ToLowerInvariant() switch
            {
                "xlsx" => PayrollExportFormat.Xlsx,
                "csv-summary" => PayrollExportFormat.CsvSummary,
                "csv-daily" => PayrollExportFormat.CsvDaily,
                _ => throw ProblemException.Validation("format", "Format harus xlsx, csv-summary, atau csv-daily."),
            };
            var file = await handler.HandleAsync(id, parsed, ct);
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        }).RequirePermission(Permissions.Payroll.Export);

        g.MapGet("/{id:guid}/slips", async (Guid id, Guid? workerId, bool? daily, PayrollSlipsHandler handler, CancellationToken ct) =>
        {
            var file = await handler.HandleAsync(id, workerId, daily ?? false, ct);
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        }).RequirePermission(Permissions.Payroll.PrintSlip);
    }
}
