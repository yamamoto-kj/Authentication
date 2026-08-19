using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)");

        // Composite index: every tenant-scoped list query filters on TenantId
        // first, so it should always lead the index.
        builder.HasIndex(p => new { p.TenantId, p.Name });

        builder.HasOne<Domain.Entities.Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
