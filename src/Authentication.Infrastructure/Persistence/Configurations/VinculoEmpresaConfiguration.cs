using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Authentication.Infrastructure.Persistence.Configurations;

public class VinculoEmpresaConfiguration : IEntityTypeConfiguration<VinculoEmpresa>
{
    public void Configure(EntityTypeBuilder<VinculoEmpresa> builder)
    {
        builder.HasKey(ve => new { ve.VinculoId, ve.EmpresaId });

        builder.HasOne<Vinculo>()
            .WithMany()
            .HasForeignKey(ve => ve.VinculoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(ve => ve.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
