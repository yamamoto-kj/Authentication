using Authentication.Domain.Entities;
using Authentication.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class WebAuthnCredentialConfiguration : IEntityTypeConfiguration<WebAuthnCredential>
{
    public void Configure(EntityTypeBuilder<WebAuthnCredential> builder)
    {
        builder.Property(c => c.CredentialId).IsRequired();
        builder.Property(c => c.PublicKey).IsRequired();
        builder.Property(c => c.DeviceName).HasMaxLength(200);

        builder.HasIndex(c => c.CredentialId).IsUnique();

        // No CLR navigation to ApplicationUser - same reasoning as
        // VinculoConfiguration: Domain must not reference Infrastructure.Identity.
        builder.HasOne(typeof(ApplicationUser))
            .WithMany()
            .HasForeignKey(nameof(WebAuthnCredential.UsuarioId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
