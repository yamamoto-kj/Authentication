using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Common;
using Authentication.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Authentication.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit fields on every save and, defense-in-depth on top of the
/// global query filter, rejects any attempt to insert or modify a
/// tenant-owned row for a tenant other than the current one - guards against
/// a handler bug that constructs an entity with the wrong TenantId.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantProvider _tenantProvider;
    private readonly IEmpresaProvider _empresaProvider;
    private readonly ICurrentUserService _currentUser;

    public AuditableEntitySaveChangesInterceptor(
        ITenantProvider tenantProvider, IEmpresaProvider empresaProvider, ICurrentUserService currentUser)
    {
        _tenantProvider = tenantProvider;
        _empresaProvider = empresaProvider;
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = DateTimeOffset.UtcNow;
                entry.Entity.CreatedBy ??= _currentUser.UserId?.ToString();
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
                entry.Entity.UpdatedBy = _currentUser.UserId?.ToString();
            }

            if (entry.Entity is ITenantOwned tenantOwned && _tenantProvider.IsResolved)
            {
                if (entry.State == EntityState.Added && tenantOwned.TenantId == Guid.Empty)
                {
                    tenantOwned.TenantId = _tenantProvider.TenantId;
                }

                if (tenantOwned.TenantId != _tenantProvider.TenantId)
                {
                    throw new TenantMismatchException();
                }
            }

            if (entry.Entity is IEmpresaOwned empresaOwned && _empresaProvider.IsResolved)
            {
                if (entry.State == EntityState.Added && empresaOwned.EmpresaId == Guid.Empty)
                {
                    empresaOwned.EmpresaId = _empresaProvider.EmpresaId;
                }

                if (empresaOwned.EmpresaId != _empresaProvider.EmpresaId)
                {
                    throw new EmpresaMismatchException();
                }
            }
        }
    }
}
