using Authentication.Domain.Entities;
using Authentication.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class VinculoConfiguration : IEntityTypeConfiguration<Vinculo>
{
    public void Configure(EntityTypeBuilder<Vinculo> builder)
    {
        // One user can only have one membership per tenant.
        builder.HasIndex(v => new { v.UsuarioId, v.TenantId }).IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(v => v.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Vinculo has no CLR navigation to ApplicationUser (Domain must not
        // reference Infrastructure.Identity) - configured here, in
        // Infrastructure, using the type directly instead.
        builder.HasOne(typeof(ApplicationUser))
            .WithMany()
            .HasForeignKey(nameof(Vinculo.UsuarioId))
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TenantRole>()
            .WithMany()
            .HasForeignKey(v => v.TenantRoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
