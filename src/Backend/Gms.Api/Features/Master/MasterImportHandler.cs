using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Domain.Master;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

/// <summary>
/// Impor CSV untuk Material, FG, dan Pekerja (FSD AC-MD-30 s.d. AC-MD-35): seluruh baris divalidasi lebih dulu, impor hanya
/// dilanjutkan bila SEMUA valid (semua-atau-tidak-sama-sekali), hanya menambah data baru (tanpa update massal), batas
/// 2.000 baris / 2 MB. FG hanya membuat data dasar + Size Berlaku (routing/tarif tidak diimpor).
/// </summary>
public sealed partial class MasterImportHandler(MasterContext ctx)
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex CodePattern();

    private static readonly string[] MaterialColumns = ["code", "name", "category_code", "uom_code", "description"];
    private static readonly string[] FgColumns = ["code", "name", "kind", "customer_code", "notes", "sizes"];
    private static readonly string[] WorkerColumns = ["name", "join_date", "main_process_code", "notes"];

    private GmsDbContext Db => ctx.Db;

    // ===== Template =====

    public FileContent GetTemplate(ImportEntity entity)
    {
        var (name, header, sample) = entity switch
        {
            ImportEntity.Material => ("material", MaterialColumns, "KN-001,Kain Drill Navy,KAIN,MTR,Kain drill 280gsm"),
            ImportEntity.Fg => ("fg", FgColumns, "KMJ-001,Kemeja Wearpak,Kemeja,CUS-0001,,S|M|L|XL"),
            ImportEntity.Worker => ("pekerja", WorkerColumns, "Budi Santoso,2026-01-15,SEW,"),
            _ => throw ProblemException.Validation("entity", "Entitas tidak mendukung impor di modul ini."),
        };

        var csv = string.Join(',', header) + "\r\n" + sample + "\r\n";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return new FileContent($"template-{name}.csv", "text/csv", bytes);
    }

    // ===== Impor =====

    public async Task<ImportResultDto> ImportAsync(ImportEntity entity, Stream csv, string fileName, bool dryRun, CancellationToken ct)
    {
        var table = await CsvParser.ParseAsync(csv, ct);
        var expected = entity switch
        {
            ImportEntity.Material => MaterialColumns,
            ImportEntity.Fg => FgColumns,
            ImportEntity.Worker => WorkerColumns,
            _ => throw ProblemException.Validation("entity", "Entitas tidak mendukung impor di modul ini."),
        };
        ValidateHeader(table.Header, expected);

        return entity switch
        {
            ImportEntity.Material => await ImportMaterialsAsync(table, fileName, dryRun, ct),
            ImportEntity.Fg => await ImportFgsAsync(table, fileName, dryRun, ct),
            _ => await ImportWorkersAsync(table, fileName, dryRun, ct),
        };
    }

    private static void ValidateHeader(IReadOnlyList<string> header, string[] expected)
    {
        var normalized = header.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = expected.Except(normalized).ToList();
        var extra = normalized.Except(expected).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            var parts = new List<string>();
            if (missing.Count > 0)
            {
                parts.Add("kolom kurang: " + string.Join(", ", missing));
            }

            if (extra.Count > 0)
            {
                parts.Add("kolom tidak dikenal: " + string.Join(", ", extra));
            }

            throw CsvParser.Invalid("Judul kolom tidak sesuai template (" + string.Join("; ", parts) + "). Unduh template terbaru.");
        }

        if (normalized.Distinct().Count() != normalized.Count)
        {
            throw CsvParser.Invalid("Judul kolom ganda pada berkas.");
        }
    }

    private static Dictionary<string, string> Map(CsvTable table, CsvRow row)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Header.Count; i++)
        {
            dict[table.Header[i].Trim()] = i < row.Cells.Count ? row.Cells[i].Trim() : string.Empty;
        }

        return dict;
    }

    private static ImportResultDto Summarize(List<ImportRowResult> results, bool committed) =>
        new(results.Count, results.Count(r => r.IsValid), results.Count(r => !r.IsValid), committed, results);

    private static string? ValidateCode(string code, int max)
    {
        if (code.Length == 0)
        {
            return "Kode wajib diisi.";
        }

        if (code.Length > max)
        {
            return $"Kode maksimal {max} karakter.";
        }

        return CodePattern().IsMatch(code) ? null : "Kode hanya boleh berisi huruf, angka, titik, strip, garis miring, atau garis bawah.";
    }

    private static string? Length(string value, string label, int max, bool required)
    {
        if (required && value.Length == 0)
        {
            return $"{label} wajib diisi.";
        }

        return value.Length > max ? $"{label} maksimal {max} karakter." : null;
    }

    // ===== Material =====

    private async Task<ImportResultDto> ImportMaterialsAsync(CsvTable table, string fileName, bool dryRun, CancellationToken ct)
    {
        var categories = await Db.MaterialCategories.AsNoTracking().Where(c => c.IsActive).ToDictionaryAsync(c => c.Code.ToLower(), c => c.Id, ct);
        var uoms = await Db.Uoms.AsNoTracking().Where(u => u.IsActive).ToDictionaryAsync(u => u.Code.ToLower(), u => u, ct);
        var existing = (await Db.Materials.AsNoTracking().Select(m => m.Code).ToListAsync(ct)).Select(c => c.ToLowerInvariant()).ToHashSet();
        var seen = new HashSet<string>();
        var results = new List<ImportRowResult>();
        var toCreate = new List<Material>();

        foreach (var row in table.Rows)
        {
            var v = Map(table, row);
            var code = v["code"];
            var problems = new List<string>();
            AddIfNotNull(problems, ValidateCode(code, 50));
            AddIfNotNull(problems, Length(v["name"], "Nama", 200, true));
            AddIfNotNull(problems, Length(v["description"], "Spesifikasi", 1000, false));

            if (code.Length > 0 && !seen.Add(code.ToLowerInvariant()))
            {
                problems.Add("Kode ganda di dalam berkas.");
            }
            else if (existing.Contains(code.ToLowerInvariant()))
            {
                problems.Add("Kode sudah ada.");
            }

            if (!categories.TryGetValue(v["category_code"].ToLowerInvariant(), out var categoryId))
            {
                problems.Add($"Kategori '{v["category_code"]}' tidak ditemukan.");
            }

            if (!uoms.TryGetValue(v["uom_code"].ToLowerInvariant(), out var uom))
            {
                problems.Add($"Satuan '{v["uom_code"]}' tidak ditemukan.");
            }

            results.Add(new ImportRowResult(row.LineNumber, code, problems.Count == 0, problems.Count == 0 ? null : string.Join(" ", problems)));
            if (problems.Count == 0)
            {
                toCreate.Add(new Material
                {
                    Code = code, Name = v["name"], CategoryId = categoryId, UomId = uom!.Id,
                    Description = v["description"].Length == 0 ? null : v["description"],
                    CreatedAt = ctx.Time.GetUtcNow(), CreatedBy = ctx.User.Id,
                });
            }
        }

        var allValid = results.All(r => r.IsValid);
        if (dryRun || !allValid)
        {
            return Summarize(results, false);
        }

        Db.Materials.AddRange(toCreate);
        ctx.Audit.Add(AuditEvents.ImportCompleted, "Material", null, null, new { fileName, rows = toCreate.Count });
        await Db.SaveChangesAsync(ct);
        return Summarize(results, true);
    }

    // ===== FG =====

    private async Task<ImportResultDto> ImportFgsAsync(CsvTable table, string fileName, bool dryRun, CancellationToken ct)
    {
        var customers = await Db.Customers.AsNoTracking().Where(c => c.IsActive).ToDictionaryAsync(c => c.Code.ToLower(), c => c.Id, ct);
        var sizes = await Db.Sizes.AsNoTracking().Where(s => s.IsActive).ToDictionaryAsync(s => s.Code.ToLower(), s => s.Id, ct);
        var existing = (await Db.Fgs.AsNoTracking().Select(f => f.Code).ToListAsync(ct)).Select(c => c.ToLowerInvariant()).ToHashSet();
        var seen = new HashSet<string>();
        var results = new List<ImportRowResult>();
        var toCreate = new List<Fg>();

        foreach (var row in table.Rows)
        {
            var v = Map(table, row);
            var code = v["code"];
            var problems = new List<string>();
            AddIfNotNull(problems, ValidateCode(code, 50));
            AddIfNotNull(problems, Length(v["name"], "Nama", 200, true));
            AddIfNotNull(problems, Length(v["kind"], "Jenis", 100, false));
            AddIfNotNull(problems, Length(v["notes"], "Keterangan", 1000, false));

            if (code.Length > 0 && !seen.Add(code.ToLowerInvariant()))
            {
                problems.Add("Kode ganda di dalam berkas.");
            }
            else if (existing.Contains(code.ToLowerInvariant()))
            {
                problems.Add("Kode sudah ada.");
            }

            Guid? customerId = null;
            if (v["customer_code"].Length > 0)
            {
                if (customers.TryGetValue(v["customer_code"].ToLowerInvariant(), out var cid))
                {
                    customerId = cid;
                }
                else
                {
                    problems.Add($"Customer '{v["customer_code"]}' tidak ditemukan.");
                }
            }

            var sizeIds = new List<Guid>();
            var sizeCodes = v["sizes"].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sizeCodes.Length == 0)
            {
                problems.Add("Minimal 1 size berlaku (pisahkan dengan |).");
            }

            foreach (var sizeCode in sizeCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (sizes.TryGetValue(sizeCode.ToLowerInvariant(), out var sid))
                {
                    sizeIds.Add(sid);
                }
                else
                {
                    problems.Add($"Size '{sizeCode}' tidak ditemukan.");
                }
            }

            results.Add(new ImportRowResult(row.LineNumber, code, problems.Count == 0, problems.Count == 0 ? null : string.Join(" ", problems)));
            if (problems.Count == 0)
            {
                var fg = new Fg
                {
                    Code = code, Name = v["name"], Kind = v["kind"].Length == 0 ? null : v["kind"], CustomerId = customerId,
                    Notes = v["notes"].Length == 0 ? null : v["notes"],
                    CreatedAt = ctx.Time.GetUtcNow(), CreatedBy = ctx.User.Id,
                };
                foreach (var sid in sizeIds)
                {
                    fg.Sizes.Add(new FgSize { FgId = fg.Id, SizeId = sid });
                }

                toCreate.Add(fg);
            }
        }

        var allValid = results.All(r => r.IsValid);
        if (dryRun || !allValid)
        {
            return Summarize(results, false);
        }

        Db.Fgs.AddRange(toCreate);
        ctx.Audit.Add(AuditEvents.ImportCompleted, "Fg", null, null, new { fileName, rows = toCreate.Count });
        await Db.SaveChangesAsync(ct);
        return Summarize(results, true);
    }

    // ===== Pekerja =====

    private async Task<ImportResultDto> ImportWorkersAsync(CsvTable table, string fileName, bool dryRun, CancellationToken ct)
    {
        var processes = await Db.Processes.AsNoTracking().Where(p => p.IsActive && p.Type == ProcessType.Internal)
            .ToDictionaryAsync(p => p.Code.ToLower(), p => p.Id, ct);
        var results = new List<ImportRowResult>();
        var toCreate = new List<Worker>();

        foreach (var row in table.Rows)
        {
            var v = Map(table, row);
            var problems = new List<string>();
            AddIfNotNull(problems, Length(v["name"], "Nama", 150, true));
            AddIfNotNull(problems, Length(v["notes"], "Keterangan", 1000, false));

            DateOnly? joinDate = null;
            if (v["join_date"].Length > 0)
            {
                if (TryParseDate(v["join_date"], out var date))
                {
                    joinDate = date;
                }
                else
                {
                    problems.Add("Tanggal masuk harus berformat yyyy-MM-dd atau dd/MM/yyyy.");
                }
            }

            Guid? processId = null;
            if (v["main_process_code"].Length > 0)
            {
                if (processes.TryGetValue(v["main_process_code"].ToLowerInvariant(), out var pid))
                {
                    processId = pid;
                }
                else
                {
                    problems.Add($"Proses utama '{v["main_process_code"]}' bukan proses Internal aktif.");
                }
            }

            results.Add(new ImportRowResult(row.LineNumber, v["name"], problems.Count == 0, problems.Count == 0 ? null : string.Join(" ", problems)));
            if (problems.Count == 0)
            {
                toCreate.Add(new Worker
                {
                    Name = v["name"], JoinDate = joinDate, MainProcessId = processId,
                    Notes = v["notes"].Length == 0 ? null : v["notes"],
                    CreatedAt = ctx.Time.GetUtcNow(), CreatedBy = ctx.User.Id,
                });
            }
        }

        var allValid = results.All(r => r.IsValid);
        if (dryRun || !allValid)
        {
            return Summarize(results, false);
        }

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        foreach (var worker in toCreate)
        {
            worker.Code = await ctx.Sequences.NextCodeAsync("PKJ", ct);
        }

        Db.Workers.AddRange(toCreate);
        ctx.Audit.Add(AuditEvents.ImportCompleted, "Worker", null, null, new { fileName, rows = toCreate.Count });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Summarize(results, true);
    }

    private static bool TryParseDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void AddIfNotNull(List<string> list, string? message)
    {
        if (message is not null)
        {
            list.Add(message);
        }
    }
}
