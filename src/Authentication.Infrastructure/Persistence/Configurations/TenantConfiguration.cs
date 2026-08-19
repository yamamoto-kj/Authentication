using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Slug).IsRequired().HasMaxLength(100);
        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
