using Gms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

/// <summary>Helper konfigurasi Fluent API agar pola umum (kode citext, enum string, xmin) konsisten di semua tabel.</summary>
internal static class ConfigurationExtensions
{
    /// <summary>Kode/username: <c>citext</c> sehingga indeks unik tidak peka huruf besar-kecil.</summary>
    public static PropertyBuilder<string> AsCode(this PropertyBuilder<string> property, int maxLength = 50) =>
        property.HasColumnType("citext").HasMaxLength(maxLength).IsRequired();

    public static PropertyBuilder<string> AsText(this PropertyBuilder<string> property, int maxLength) =>
        property.HasMaxLength(maxLength).IsRequired();

    public static PropertyBuilder<string?> AsOptionalText(this PropertyBuilder<string?> property, int maxLength) =>
        property.HasMaxLength(maxLength);

    /// <summary>Enum disimpan sebagai text agar terbaca saat debug/QA.</summary>
    public static PropertyBuilder<TEnum> AsEnum<TEnum>(this PropertyBuilder<TEnum> property, int maxLength = 20)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(maxLength).IsRequired();

    public static PropertyBuilder<TEnum?> AsOptionalEnum<TEnum>(this PropertyBuilder<TEnum?> property, int maxLength = 20)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(maxLength);

    /// <summary>Token konkurensi optimistis memakai kolom sistem PostgreSQL <c>xmin</c> (ADR-003).</summary>
    public static void UseXmin<T>(this EntityTypeBuilder<T> builder)
        where T : VersionedEntity
    {
        // IsRowVersion() pada properti uint dipetakan Npgsql ke kolom sistem xmin (tidak dibuat sebagai kolom oleh migrasi).
        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();
    }

    /// <summary>Kunci GUID v7 pada properti <c>Id</c> yang dibuat aplikasi (bukan oleh database).</summary>
    public static void GuidKey<T>(this EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.HasKey("Id");
        builder.Property<Guid>("Id").ValueGeneratedNever();
    }

    /// <summary>Konfigurasi umum tabel master: kunci GUID, status aktif, xmin.</summary>
    public static void ConfigureMaster<T>(this EntityTypeBuilder<T> builder, string table)
        where T : MasterEntity
    {
        builder.ToTable(table);
        builder.GuidKey();
        builder.UseXmin();
    }
}
