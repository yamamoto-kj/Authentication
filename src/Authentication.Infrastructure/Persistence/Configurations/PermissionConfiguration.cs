using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.Property(p => p.Chave).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Descricao).HasMaxLength(500);
        builder.HasIndex(p => p.Chave).IsUnique();

        // Query filter (soft-delete only - global catalog, never tenant-scoped)
        // is set centrally in ApplicationDbContext.OnModelCreating, alongside
        // every other entity's filter, to avoid two competing HasQueryFilter
        // calls silently overriding each other.
    }
}
