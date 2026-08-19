using Authentication.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Authentication.Application.Common.Interfaces;

/// <summary>
/// Application-facing view of the persistence context. Keeping this in the
/// Application layer (implemented by the real EF Core DbContext in
/// Infrastructure) means handlers depend on an abstraction, not on EF Core
/// or the concrete database provider.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<Product> Products { get; }

    DbSet<Empresa> Empresas { get; }

    DbSet<Vinculo> Vinculos { get; }

    DbSet<VinculoEmpresa> VinculoEmpresas { get; }

    DbSet<TenantRole> TenantRoles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<TenantRolePermission> TenantRolePermissions { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
