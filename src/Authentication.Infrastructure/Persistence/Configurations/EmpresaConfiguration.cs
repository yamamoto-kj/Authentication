using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.Property(e => e.RazaoSocial).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Cnpj).IsRequired().HasMaxLength(14);

        // Same CNPJ can legitimately exist under different tenants only in
        // theory (it shouldn't in practice), but must never repeat inside
        // the same tenant.
        builder.HasIndex(e => new { e.TenantId, e.Cnpj }).IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
