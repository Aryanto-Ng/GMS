using Gms.Domain.Access;
using Gms.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.Property(x => x.FullName).AsText(200);
        builder.Property(x => x.RowVersion).HasColumnName("xmin").IsRowVersion();

        builder.HasOne<AppRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.RoleId);
    }
}

internal sealed class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> builder)
    {
        builder.ToTable("app_roles");
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Name).AsCode(100);
        builder.Property(x => x.Description).AsOptionalText(500);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).AsText(150);
        builder.Property(x => x.Resource).AsText(120);
        builder.Property(x => x.Action).AsText(50);
        builder.Property(x => x.Module).AsText(60);
        builder.Property(x => x.Description).AsText(250);
        builder.HasIndex(x => x.SortOrder);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionKey });
        builder.Property(x => x.PermissionKey).AsText(150);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionKey).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.GuidKey();
        builder.Property(x => x.TokenHash).IsRequired();
        builder.Property(x => x.RevokedReason).AsOptionalEnum();
        builder.Property(x => x.IpAddress).AsOptionalText(64);
        builder.Property(x => x.UserAgent).AsOptionalText(500);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TokenHash).IsUnique();

        // Satu sesi aktif per pengguna ditegakkan di database (ADR-002, AC-AM-06a).
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("revoked_at IS NULL").HasDatabaseName("ux_user_sessions_active_per_user");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.ActorUsername).AsOptionalText(100);
        builder.Property(x => x.EventType).AsText(60);
        builder.Property(x => x.TargetType).AsOptionalText(60);
        builder.Property(x => x.TargetId).AsOptionalText(100);
        builder.Property(x => x.OldValue).HasColumnType("jsonb");
        builder.Property(x => x.NewValue).HasColumnType("jsonb");
        builder.Property(x => x.IpAddress).AsOptionalText(64);

        builder.HasIndex(x => x.OccurredAt).IsDescending();
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.EventType, x.OccurredAt }).IsDescending(false, true);
    }
}

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<Domain.Common.NumberSequence>
{
    public void Configure(EntityTypeBuilder<Domain.Common.NumberSequence> builder)
    {
        builder.ToTable("number_sequences");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).AsText(40);
    }
}
