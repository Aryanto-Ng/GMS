using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gms.Infrastructure.Persistence;

/// <summary>
/// Konvensi penamaan snake_case untuk PostgreSQL, ditulis sendiri agar tidak perlu package tambahan.
/// Diterapkan SETELAH semua konfigurasi Fluent API dimuat. Nama tabel eksplisit (ToTable) dihormati;
/// hanya tabel tanpa nama eksplisit (mis. tabel bawaan Identity) yang diubah.
/// </summary>
internal static class SnakeCaseNaming
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table && entity.FindAnnotation("Relational:TableName") is null)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                if (property.FindAnnotation("Relational:ColumnName") is null)
                {
                    property.SetColumnName(ToSnakeCase(property.Name));
                }
            }
        }
    }

    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 6);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prevLower = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && (prevLower || (char.IsUpper(name[i - 1]) && nextLower)))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
