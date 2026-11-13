using System.Globalization;
using System.Text.Json;
using Gms.Api.Security;
using Gms.Domain.Inventory;
using Gms.Domain.Notifications;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Menerbitkan NE-11 (stok tersedia habis) dan NE-12 (alokasi dilepas otomatis) pada kondisi AKHIR transaksi:
/// membaca entitas yang akan disimpan (<c>StockBalance</c> berubah dan <c>PoAllocation</c> bertipe Release) sehingga tercakup semua jalur
/// penulisan (pengiriman, serah terima, void, alokasi manual, penyesuaian) tanpa mengubah masing-masing handler.
/// Notifikasi ditambahkan ke DbContext yang sama (atomik). Penerima/pengaturan/label dibaca lewat DbContext TERPISAH
/// (scope baru) agar tidak menjalankan query di tengah SaveChanges pada context yang sama.
/// </summary>
public sealed class NotificationSideEffectsInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is GmsDbContext db)
        {
            await CollectAsync(db, cancellationToken);
        }

        return result;
    }

    private async Task CollectAsync(GmsDbContext db, CancellationToken ct)
    {
        var depleted = db.ChangeTracker.Entries<StockBalance>()
            .Where(e => e.State == EntityState.Modified)
            .Select(e => (Entry: e,
                Before: e.OriginalValues.GetValue<int>(nameof(StockBalance.OnHand)) - e.OriginalValues.GetValue<int>(nameof(StockBalance.Allocated)),
                After: e.Entity.OnHand - e.Entity.Allocated))
            .Where(x => x.Before > 0 && x.After == 0)
            .Select(x => x.Entry.Entity)
            .ToList();

        var released = db.ChangeTracker.Entries<PoAllocation>()
            .Where(e => e.State == EntityState.Added && e.Entity is { Kind: AllocationKind.Release, Source: AllocationSource.HandoverVoid })
            .Select(e => e.Entity)
            .ToList();

        if (depleted.Count == 0 && released.Count == 0)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<GmsDbContext>();
        var actor = services.GetService<ICurrentUser>()?.Id;
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var inv = CultureInfo.InvariantCulture;

        if (depleted.Count > 0)
        {
            var def = NotificationCatalog.Find("NE-11")!;
            if (await IsEnabledAsync(read, def, ct))
            {
                var recipients = await NotificationRecipients.ResolveAsync(read, def, actor, ct);
                if (recipients.Count > 0)
                {
                    foreach (var stock in depleted)
                    {
                        var fg = await read.Fgs.AsNoTracking().Where(f => f.Id == stock.FgId).Select(f => f.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
                        var color = await read.Colors.AsNoTracking().Where(c => c.Id == stock.ColorId).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
                        var size = await read.Sizes.AsNoTracking().Where(s => s.Id == stock.SizeId).Select(s => s.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
                        Add(db, def, recipients, actor, now,
                            $"NE-11:{stock.FgId}:{stock.ColorId}:{stock.SizeId}:{now.UtcTicks}",
                            new Dictionary<string, string> { ["fg"] = fg, ["color"] = color, ["size"] = size },
                            new NotificationTarget(NotificationTargetKind.StockLedger, $"{stock.FgId}:{stock.ColorId}:{stock.SizeId}"));
                    }
                }
            }
        }

        if (released.Count > 0)
        {
            var def = NotificationCatalog.Find("NE-12")!;
            if (await IsEnabledAsync(read, def, ct))
            {
                var recipients = await NotificationRecipients.ResolveAsync(read, def, actor, ct);
                if (recipients.Count > 0)
                {
                    var lineIds = released.Select(r => r.PoLineId).Distinct().ToList();
                    var poOfLine = await read.PoLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.PoId, ct);
                    var codes = await read.PurchaseOrders.AsNoTracking().Where(p => poOfLine.Values.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);

                    // Satu notifikasi per (PO, dokumen sumber).
                    foreach (var group in released.Where(r => poOfLine.ContainsKey(r.PoLineId)).GroupBy(r => (Po: poOfLine[r.PoLineId], r.SourceId)))
                    {
                        Add(db, def, recipients, actor, now,
                            $"NE-12:{group.Key.Po}:{group.Key.SourceId?.ToString() ?? now.UtcTicks.ToString(inv)}",
                            new Dictionary<string, string>
                            {
                                ["po"] = codes.GetValueOrDefault(group.Key.Po, string.Empty),
                                ["qty"] = group.Sum(g => g.Qty).ToString(inv),
                                ["reason"] = "handover-void",
                            },
                            new NotificationTarget(NotificationTargetKind.PurchaseOrder, group.Key.Po.ToString()));
                    }
                }
            }
        }
    }

    private static async Task<bool> IsEnabledAsync(GmsDbContext read, NotificationTypeDefinition def, CancellationToken ct) =>
        await read.NotificationTypeSettings.AsNoTracking().Where(s => s.TypeCode == def.TypeCode).Select(s => (bool?)s.Enabled).FirstOrDefaultAsync(ct)
        ?? def.DefaultEnabled;

    private static void Add(
        GmsDbContext db, NotificationTypeDefinition def, IEnumerable<Guid> recipients, Guid? actor, DateTimeOffset now,
        string sourceKey, Dictionary<string, string> parameters, NotificationTarget target)
    {
        var payload = NotificationPublisher.BuildParams(def, parameters);
        foreach (var recipient in recipients)
        {
            db.Notifications.Add(new Notification
            {
                RecipientUserId = recipient, TypeCode = def.TypeCode, Category = def.Category, Params = payload,
                TargetKind = target.Kind, TargetId = target.Id, ActorUserId = actor, SourceKey = sourceKey, CreatedAt = now,
            });
        }
    }
}
