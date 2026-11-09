using Gms.Shared.Access;
using Gms.Shared.Security;

namespace Gms.Web.Components.Pages.Admin;

/// <summary>Satu resource pada matriks: izin per aksi (kunci = aksi), apakah dapat diberikan, dan modul induknya.</summary>
public sealed record PermissionRow(string Module, string Resource, IReadOnlyDictionary<string, PermissionItem> ByAction, bool Assignable);

/// <summary>Logika murni matriks izin Role (AC-AM-18): View otomatis, lepas View melepas aksi lain pada baris itu, ringkasan perubahan.</summary>
public static class PermissionMatrixLogic
{
    /// <summary>Mengelompokkan katalog server per Modul lalu Resource, mengikuti SortOrder (katalog tidak di-hardcode di klien).</summary>
    public static IReadOnlyList<PermissionRow> Rows(IEnumerable<PermissionItem> catalog) =>
        catalog.GroupBy(p => (p.Module, p.Resource))
            .Select(g => new { g.Key.Module, g.Key.Resource, Min = g.Min(p => p.SortOrder), Items = g.ToList() })
            .OrderBy(g => g.Min)
            .Select(g => new PermissionRow(g.Module, g.Resource, g.Items.OrderBy(p => p.SortOrder).ToDictionary(p => p.Action), g.Items.All(p => p.IsAssignable)))
            .ToList();

    /// <summary>Kolom = gabungan semua aksi, urut kemunculan menurut SortOrder.</summary>
    public static IReadOnlyList<string> Actions(IEnumerable<PermissionItem> catalog) =>
        catalog.OrderBy(p => p.SortOrder).Select(p => p.Action).Distinct().ToList();

    /// <summary>
    /// Mengubah satu izin. Mencentang aksi lain otomatis mencentang View; melepas View melepas semua izin resource itu.
    /// Mengembalikan jumlah izin LAIN yang ikut terlepas (untuk toast).
    /// </summary>
    public static int Toggle(ISet<string> selected, string key, bool on)
    {
        if (on)
        {
            selected.Add(key);
            if (PermissionCatalog.RequiredViewFor(key) is { } view)
            {
                selected.Add(view);
            }

            return 0;
        }

        selected.Remove(key);
        if (key.EndsWith(".View", StringComparison.Ordinal))
        {
            var prefix = key[..^"View".Length];
            var others = selected.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var other in others)
            {
                selected.Remove(other);
            }

            return others.Count;
        }

        return 0;
    }

    /// <summary>Semua aksi satu baris (dilewati yang tidak dapat diberikan) dicentang atau dilepas.</summary>
    public static void SetRow(ISet<string> selected, PermissionRow row, bool on)
    {
        foreach (var item in row.ByAction.Values.Where(p => p.IsAssignable))
        {
            if (on) { selected.Add(item.Key); } else { selected.Remove(item.Key); }
        }
    }

    /// <summary>Pintasan "Hanya baca": mempertahankan View dan melepas aksi lain pada baris-baris yang diberikan.</summary>
    public static void SetReadOnly(ISet<string> selected, IEnumerable<PermissionRow> rows)
    {
        foreach (var row in rows.Where(r => r.Assignable))
        {
            foreach (var item in row.ByAction.Values)
            {
                if (item.Action == "View") { selected.Add(item.Key); } else { selected.Remove(item.Key); }
            }
        }
    }

    public static bool IsRowFull(ISet<string> selected, PermissionRow row) =>
        row.ByAction.Values.Where(p => p.IsAssignable).All(p => selected.Contains(p.Key)) && row.ByAction.Values.Any(p => p.IsAssignable);

    public static (int Added, int Removed) Diff(IEnumerable<string> before, IEnumerable<string> after)
    {
        var b = before.ToHashSet();
        var a = after.ToHashSet();
        return (a.Count(k => !b.Contains(k)), b.Count(k => !a.Contains(k)));
    }
}
