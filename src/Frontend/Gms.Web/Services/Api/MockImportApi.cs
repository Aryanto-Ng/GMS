using System.Text;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Web.Infrastructure.Format;

namespace Gms.Web.Services.Api;

/// <summary>
/// Impor CSV tiruan (ADR-014). Meniru kontrak API: judul kolom harus sama dengan template, validasi per baris, semua-atau-tidak-sama-sekali,
/// dry-run tidak menulis, batas 2.000 baris. Saat commit, baris dibuat lewat API master tiruan.
/// </summary>
public sealed class MockImportApi(
    MockMaterialApi materials, MockMaterialCategoryApi categories, MockUomApi uoms, MockWorkerApi workers, MockProcessApi processes,
    MockFgApi fgs, MockSizeApi sizes, MockCustomerApi customers, MockColorApi? colors = null, MockInventoryApi? inventory = null) : IImportApi
{
    public const int MaxRows = 2000;

    private static readonly string[] MaterialColumns = ["code", "name", "category_code", "uom_code", "description"];
    private static readonly string[] FgColumns = ["code", "name", "kind", "customer_code", "notes", "sizes"];
    private static readonly string[] WorkerColumns = ["name", "join_date", "main_process_code", "notes"];
    private static readonly string[] StockColumns = ["fg_code", "color_code", "size_code", "qty", "date"];

    private static ApiException FileInvalid(string detail) =>
        MockAccessStore.Problem(ProblemTypes.ImportInvalid, 422, "Berkas impor ditolak.", detail, ProblemCodes.ImportFileInvalid);

    public Task<FileContent> GetTemplateAsync(ImportEntity entity, CancellationToken ct = default)
    {
        var (name, header, sample) = entity switch
        {
            ImportEntity.Material => ("material", MaterialColumns, "KN-001,Kain Drill Navy,KAIN,MTR,Kain drill 280gsm"),
            ImportEntity.Fg => ("fg", FgColumns, "KMJ-001,Kemeja Wearpak,Kemeja,CUS-001,,S|M|L|XL"),
            ImportEntity.Worker => ("pekerja", WorkerColumns, "Budi Santoso,2026-01-15,SEW,"),
            ImportEntity.StockOpening => ("saldo-awal", StockColumns, "KMJ-001,NVY,M,120,2026-11-01"),
            _ => throw new NotSupportedException($"Impor {entity} tidak tersedia di modul Master Data."),
        };
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join(',', header) + "\r\n" + sample + "\r\n")).ToArray();
        return Task.FromResult(new FileContent($"template-{name}.csv", "text/csv", bytes));
    }

    public async Task<ImportResultDto> ImportAsync(ImportEntity entity, Stream csv, string fileName, bool dryRun, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await csv.CopyToAsync(ms, ct);
        if (ms.Length > 2 * 1024 * 1024) { throw FileInvalid("Ukuran berkas melebihi 2 MB."); }
        var text = new UTF8Encoding(false, true).GetString(ms.ToArray()).TrimStart('﻿');
        var table = ParseCsv(text);
        var expected = entity switch
        {
            ImportEntity.Material => MaterialColumns,
            ImportEntity.Fg => FgColumns,
            ImportEntity.Worker => WorkerColumns,
            ImportEntity.StockOpening => StockColumns,
            _ => throw new NotSupportedException(),
        };
        var header = table[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = expected.Except(header).ToList();
        var extra = header.Except(expected).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0) { parts.Add("kolom kurang: " + string.Join(", ", missing)); }
            if (extra.Count > 0) { parts.Add("kolom tidak dikenal: " + string.Join(", ", extra)); }
            throw FileInvalid("Judul kolom tidak sesuai template (" + string.Join("; ", parts) + "). Unduh template terbaru.");
        }

        var rows = table.Skip(1).ToList();
        if (rows.Count > MaxRows) { throw FileInvalid($"Maksimal {Gms.Web.Infrastructure.Format.IdFormat.Qty(MaxRows)} baris per berkas."); }
        var maps = rows.Select(r => header.Select((h, i) => (h, v: i < r.Count ? r[i].Trim() : string.Empty)).ToDictionary(x => x.h, x => x.v)).ToList();

        var results = entity switch
        {
            ImportEntity.Material => await ValidateMaterialsAsync(maps),
            ImportEntity.Fg => await ValidateFgsAsync(maps),
            ImportEntity.StockOpening => await ValidateStockAsync(maps),
            _ => await ValidateWorkersAsync(maps),
        };
        var errors = results.Count(r => !r.Result.IsValid);
        var commit = !dryRun && errors == 0 && results.Count > 0;
        if (commit)
        {
            foreach (var r in results) { await r.Apply!(); }
        }

        var list = results.Select(r => r.Result).ToList();
        return new ImportResultDto(list.Count, list.Count - errors, errors, commit, list);
    }

    private sealed record Checked(ImportRowResult Result, Func<Task>? Apply);

    private static Checked Bad(int row, string? key, string message) => new(new ImportRowResult(row, key, false, message), null);

    private static Checked Good(int row, string? key, Func<Task> apply) => new(new ImportRowResult(row, key, true, null), apply);

    private async Task<List<Checked>> ValidateMaterialsAsync(List<Dictionary<string, string>> maps)
    {
        var cats = (await categories.LookupAsync(null)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var uomMap = (await uoms.LookupAsync(null)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await materials.ListAsync(new MasterListQuery { Status = ActiveFilter.All, PageSize = 100_000 })).Items.Select(m => m.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Checked>();
        for (var i = 0; i < maps.Count; i++)
        {
            var (row, m) = (i + 2, maps[i]);
            var code = m["code"];
            if (code.Length == 0) { list.Add(Bad(row, null, "Kode wajib diisi.")); continue; }
            if (code.Length > 30) { list.Add(Bad(row, code, "Kode maksimal 30 karakter.")); continue; }
            if (existing.Contains(code)) { list.Add(Bad(row, code, "Kode sudah terdaftar.")); continue; }
            if (!seen.Add(code)) { list.Add(Bad(row, code, "Kode ganda di dalam berkas.")); continue; }
            if (m["name"].Length == 0) { list.Add(Bad(row, code, "Nama wajib diisi.")); continue; }
            if (!cats.TryGetValue(m["category_code"], out var cat)) { list.Add(Bad(row, code, $"Kategori \"{m["category_code"]}\" tidak ditemukan.")); continue; }
            if (!uomMap.TryGetValue(m["uom_code"], out var uom)) { list.Add(Bad(row, code, $"Satuan \"{m["uom_code"]}\" tidak ditemukan.")); continue; }
            list.Add(Good(row, code, () => materials.CreateAsync(new SaveMaterialRequest(code, m["name"], cat.Id, uom.Id, m["description"].Length == 0 ? null : m["description"]))));
        }

        return list;
    }

    private async Task<List<Checked>> ValidateWorkersAsync(List<Dictionary<string, string>> maps)
    {
        var procs = (await processes.LookupAsync(null)).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var list = new List<Checked>();
        for (var i = 0; i < maps.Count; i++)
        {
            var (row, m) = (i + 2, maps[i]);
            var name = m["name"];
            if (name.Length == 0) { list.Add(Bad(row, null, "Nama wajib diisi.")); continue; }
            DateOnly? join = null;
            if (m["join_date"].Length > 0)
            {
                if (!DateOnly.TryParseExact(m["join_date"], "yyyy-MM-dd", out var parsed)) { list.Add(Bad(row, name, "Tanggal masuk harus berformat yyyy-MM-dd.")); continue; }
                join = parsed;
            }

            Guid? process = null;
            if (m["main_process_code"].Length > 0)
            {
                if (!procs.TryGetValue(m["main_process_code"], out var p)) { list.Add(Bad(row, name, $"Proses \"{m["main_process_code"]}\" tidak ditemukan.")); continue; }
                process = p.Id;
            }

            list.Add(Good(row, name, () => workers.CreateAsync(new SaveWorkerRequest(name, join, process, m["notes"].Length == 0 ? null : m["notes"]))));
        }

        return list;
    }

    private async Task<List<Checked>> ValidateStockAsync(List<Dictionary<string, string>> maps)
    {
        var fgMap = (await fgs.LookupAsync(null)).ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
        var colorMap = (await (colors ?? throw new InvalidOperationException("MockColorApi diperlukan")).LookupAsync(null)).ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
        var sizeMap = (await sizes.LookupAsync(null)).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var list = new List<Checked>();
        for (var i = 0; i < maps.Count; i++)
        {
            var (row, m) = (i + 2, maps[i]);
            var key = $"{m["fg_code"]}/{m["color_code"]}/{m["size_code"]}";
            if (!fgMap.TryGetValue(m["fg_code"], out var fg)) { list.Add(Bad(row, key, $"FG \"{m["fg_code"]}\" tidak ditemukan.")); continue; }
            if (!colorMap.TryGetValue(m["color_code"], out var color)) { list.Add(Bad(row, key, $"Color \"{m["color_code"]}\" tidak ditemukan.")); continue; }
            if (!sizeMap.TryGetValue(m["size_code"], out var size)) { list.Add(Bad(row, key, $"Size \"{m["size_code"]}\" tidak ditemukan.")); continue; }
            if (!(await fgs.GetSizesAsync(fg.Id)).SizeIds.Contains(size.Id)) { list.Add(Bad(row, key, "Size bukan Size Berlaku FG ini.")); continue; }
            if (!IdNumber.TryParseInt(m["qty"], out var qty, out _) || qty <= 0) { list.Add(Bad(row, key, "Qty harus bilangan bulat > 0.")); continue; }
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            if (m["date"].Length > 0 && !DateOnly.TryParseExact(m["date"], "yyyy-MM-dd", out date)) { list.Add(Bad(row, key, "Tanggal harus berformat yyyy-MM-dd.")); continue; }
            var when = date;
            list.Add(Good(row, key, () => { inventory?.AddOpeningBalance(fg.Id, color.Id, size.Id, qty, when); return Task.CompletedTask; }));
        }

        return list;
    }

    private async Task<List<Checked>> ValidateFgsAsync(List<Dictionary<string, string>> maps)
    {
        var sizeMap = (await sizes.LookupAsync(null)).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var customerMap = (await customers.LookupAsync(null)).ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var existing = (await fgs.ListAsync(new FgListQuery { Status = ActiveFilter.All, PageSize = 100_000 })).Items.Select(f => f.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Checked>();
        for (var i = 0; i < maps.Count; i++)
        {
            var (row, m) = (i + 2, maps[i]);
            var code = m["code"];
            if (code.Length == 0) { list.Add(Bad(row, null, "Kode wajib diisi.")); continue; }
            if (existing.Contains(code)) { list.Add(Bad(row, code, "Kode sudah terdaftar.")); continue; }
            if (!seen.Add(code)) { list.Add(Bad(row, code, "Kode ganda di dalam berkas.")); continue; }
            if (m["name"].Length == 0) { list.Add(Bad(row, code, "Nama wajib diisi.")); continue; }
            Guid? customer = null;
            if (m["customer_code"].Length > 0)
            {
                if (!customerMap.TryGetValue(m["customer_code"], out var c)) { list.Add(Bad(row, code, $"Customer \"{m["customer_code"]}\" tidak ditemukan.")); continue; }
                customer = c.Id;
            }

            var codes = m["sizes"].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = codes.Where(c => !sizeMap.ContainsKey(c)).ToList();
            if (unknown.Count > 0) { list.Add(Bad(row, code, $"Size tidak ditemukan: {string.Join(", ", unknown)}.")); continue; }
            var sizeIds = codes.Select(c => sizeMap[c].Id).Distinct().ToList();
            list.Add(Good(row, code, async () =>
            {
                var fg = await fgs.CreateAsync(new SaveFgRequest(code, m["name"], m["kind"].Length == 0 ? null : m["kind"], customer, m["notes"].Length == 0 ? null : m["notes"]));
                if (sizeIds.Count > 0) { await fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest(sizeIds, false, (await fgs.GetAsync(fg.Id)).RowVersion)); }
            }));
        }

        return list;
    }

    /// <summary>Penguraian CSV sederhana: pemisah koma atau titik koma (dideteksi dari baris judul), kutip ganda, baris kosong dilewati.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) { throw FileInvalid("Berkas kosong atau tanpa baris judul."); }
        var delimiter = lines[0].Contains(';') && !lines[0].Contains(',') ? ';' : ',';
        var table = new List<List<string>>();
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') { quoted = false; }
                    else { cell.Append(ch); }
                }
                else if (ch == '"') { quoted = true; }
                else if (ch == delimiter) { cells.Add(cell.ToString()); cell.Clear(); }
                else { cell.Append(ch); }
            }

            cells.Add(cell.ToString());
            table.Add(cells);
        }

        return table;
    }
}
