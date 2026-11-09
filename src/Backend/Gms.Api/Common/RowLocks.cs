using System.Data.Common;
using Gms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Gms.Api.Common;

/// <summary>
/// Penguncian baris eksplisit sesuai URUTAN KUNCI BAKU (ADR-004):
/// (1) payroll_periods → (2) work_orders → (3) po_lines (urut id) → (4) stock_balances (urut fg, color, size).
/// Semua use case yang menyentuh lebih dari satu jenis baris WAJIB mengunci dengan urutan ini agar tidak terjadi deadlock.
/// Pemanggil harus sudah membuka transaksi, dan HARUS mengunci SEBELUM memuat entitas yang akan diubah
/// (agar pembacaan sesudah kunci melihat nilai terbaru, bukan nilai usang dari change tracker).
/// </summary>
public static class RowLocks
{
    private static DbCommand Command(GmsDbContext db, string sql)
    {
        var transaction = db.Database.CurrentTransaction
                          ?? throw new InvalidOperationException("Penguncian baris harus dipanggil di dalam transaksi.");
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static async Task RunAsync(DbCommand command, CancellationToken ct)
    {
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                // hanya untuk memperoleh kunci
            }
        }
    }

    /// <summary>Mengunci satu baris work_orders <c>FOR UPDATE</c> (urutan kunci ke-2, sebelum po_lines).</summary>
    public static async Task LockWorkOrderAsync(GmsDbContext db, Guid woId, CancellationToken ct)
    {
        var command = Command(db, "SELECT id FROM work_orders WHERE id = @id FOR UPDATE");
        command.Parameters.Add(new NpgsqlParameter("id", woId));
        await RunAsync(command, ct);
    }

    /// <summary>Mengunci baris po_lines <c>FOR UPDATE</c> berurutan menurut id.</summary>
    public static async Task LockPoLinesAsync(GmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var command = Command(db, "SELECT id FROM po_lines WHERE id = ANY(@ids) ORDER BY id FOR UPDATE");
        command.Parameters.Add(new NpgsqlParameter("ids", ids.ToArray()));
        await RunAsync(command, ct);
    }

    /// <summary>Mengunci satu baris stock_balances (bila ada) <c>FOR UPDATE</c>.</summary>
    public static async Task LockStockAsync(GmsDbContext db, Guid fgId, Guid colorId, Guid sizeId, CancellationToken ct)
    {
        var command = Command(db,
            "SELECT 1 FROM stock_balances WHERE fg_id = @fg AND color_id = @color AND size_id = @size FOR UPDATE");
        command.Parameters.Add(new NpgsqlParameter("fg", fgId));
        command.Parameters.Add(new NpgsqlParameter("color", colorId));
        command.Parameters.Add(new NpgsqlParameter("size", sizeId));
        await RunAsync(command, ct);
    }

    /// <summary>Mengunci banyak baris stok dalam urutan kunci (fg, color, size) yang konsisten.</summary>
    public static async Task LockStockAsync(
        GmsDbContext db, IEnumerable<(Guid FgId, Guid ColorId, Guid SizeId)> keys, CancellationToken ct)
    {
        foreach (var key in keys.Distinct().OrderBy(k => k.FgId).ThenBy(k => k.ColorId).ThenBy(k => k.SizeId))
        {
            await LockStockAsync(db, key.FgId, key.ColorId, key.SizeId, ct);
        }
    }
}
