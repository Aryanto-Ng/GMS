using Gms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gms.Api.Common;

/// <summary>
/// Penomoran dokumen/master otomatis (ADR-007). Kenaikan nomor dilakukan dengan upsert atomik DI DALAM transaksi bisnis
/// pemanggil: rollback ikut membatalkan kenaikan sehingga tidak ada lubang nomor, dan pembuatan bersamaan diserialkan
/// oleh kunci baris. Pemanggil WAJIB sudah membuka transaksi (<c>db.Database.BeginTransactionAsync</c>).
/// </summary>
public sealed class NumberSequences(GmsDbContext db, TimeProvider time, AppTimeZone zone)
{
    /// <summary>Nomor berikutnya untuk sebuah kunci (mulai dari 1).</summary>
    public async Task<long> NextAsync(string key, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction
                          ?? throw new InvalidOperationException("Penomoran otomatis harus dipanggil di dalam transaksi.");

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            INSERT INTO number_sequences (key, next_value) VALUES (@key, 1)
            ON CONFLICT (key) DO UPDATE SET next_value = number_sequences.next_value + 1
            RETURNING next_value;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    /// <summary>Kode master berurutan tanpa periode, mis. <c>CUS-0001</c>.</summary>
    public async Task<string> NextCodeAsync(string prefix, CancellationToken ct) =>
        $"{prefix}-{await NextAsync(prefix, ct):D4}";

    /// <summary>Kode dokumen bulanan, mis. <c>PO-202611-0001</c> (bulan menurut zona waktu aplikasi).</summary>
    public async Task<string> NextMonthlyCodeAsync(string prefix, CancellationToken ct)
    {
        var today = zone.Today(time);
        var period = $"{today.Year:D4}{today.Month:D2}";
        return $"{prefix}-{period}-{await NextAsync($"{prefix}-{period}", ct):D4}";
    }
}
