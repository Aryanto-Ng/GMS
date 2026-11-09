using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IPayrollApi"/> ke API sungguhan (<c>/api/v1/payroll</c>).</summary>
public sealed class HttpPayrollApi(GmsApiClient client) : IPayrollApi
{
    private const string B = "/api/v1/payroll/periods";

    public Task<IReadOnlyList<PayrollPeriodListItem>> ListPeriodsAsync(CancellationToken ct = default) => client.GetAsync<IReadOnlyList<PayrollPeriodListItem>>(B, ct);

    public Task<NextPeriodDefaultsDto> GetNextPeriodDefaultsAsync(CancellationToken ct = default) => client.GetAsync<NextPeriodDefaultsDto>($"{B}/next-defaults", ct);

    public Task<PayrollPeriodDto> CreatePeriodAsync(SavePeriodRequest request, CancellationToken ct = default) => client.PostAsync<PayrollPeriodDto>(B, request, ct);

    public Task<PayrollPeriodDto> GetPeriodAsync(Guid id, CancellationToken ct = default) => client.GetAsync<PayrollPeriodDto>($"{B}/{id}", ct);

    public Task<PayrollPeriodDto> UpdatePeriodAsync(Guid id, SavePeriodRequest request, CancellationToken ct = default) => client.PutAsync<PayrollPeriodDto>($"{B}/{id}", request, ct);

    public Task<ClosePreviewDto> PreviewClosePeriodAsync(Guid id, CancellationToken ct = default) => client.PostAsync<ClosePreviewDto>($"{B}/{id}/close/preview", null, ct);

    public Task<PayrollPeriodDto> ClosePeriodAsync(Guid id, ClosePeriodRequest request, CancellationToken ct = default) => client.PostAsync<PayrollPeriodDto>($"{B}/{id}/close", request, ct);

    public Task<PayrollSummaryDto> GetSummaryAsync(Guid periodId, string? q, CancellationToken ct = default) =>
        client.GetAsync<PayrollSummaryDto>($"{B}/{periodId}/summary" + ApiQuery.Build(("q", q)), ct);

    public Task<PayrollWorkerDetailDto> GetWorkerDetailAsync(Guid periodId, Guid workerId, CancellationToken ct = default) =>
        client.GetAsync<PayrollWorkerDetailDto>($"{B}/{periodId}/workers/{workerId}/detail", ct);

    public Task<PayrollDailyResult> GetDailyAsync(Guid periodId, PayrollDailyQuery q, CancellationToken ct = default) =>
        client.GetAsync<PayrollDailyResult>($"{B}/{periodId}/daily" + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("from", q.From), ("to", q.To), ("workerId", q.WorkerId), ("woId", q.WoId), ("processCode", q.ProcessCode)), ct);

    public Task<PayrollMatrixDto> GetMatrixAsync(Guid periodId, CancellationToken ct = default) => client.GetAsync<PayrollMatrixDto>($"{B}/{periodId}/matrix", ct);

    public Task<IReadOnlyList<PayrollDailyRow>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<PayrollDailyRow>>($"{B}/{periodId}/adjustments", ct);

    public Task<FileContent> ExportAsync(Guid periodId, PayrollExportFormat format, CancellationToken ct = default) =>
        client.DownloadAsync($"{B}/{periodId}/export?format={format switch { PayrollExportFormat.Xlsx => "xlsx", PayrollExportFormat.CsvSummary => "csv-summary", _ => "csv-daily" }}", ct);

    public Task<FileContent> GetSlipsAsync(Guid periodId, Guid? workerId, bool includeDaily, CancellationToken ct = default) =>
        client.DownloadAsync($"{B}/{periodId}/slips" + ApiQuery.Build(("workerId", workerId), ("daily", includeDaily ? true : null)), ct);
}
