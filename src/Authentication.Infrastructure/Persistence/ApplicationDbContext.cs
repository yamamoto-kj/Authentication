using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Entities;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Authentication.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for Identity, OpenIddict (via UseOpenIddict() in
/// OnModelCreating) and business entities. Every ITenantOwned entity gets a
/// global query filter comparing TenantId against the resolved tenant for
/// the current request, so a leaked id or crafted route can never surface
/// another tenant's row - the filter runs at the SQL level, not in
/// application code that a handler could forget to apply.
/// </summary>
public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    private readonly ITenantProvider _tenantProvider;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Read by the query filters below via an implicit "this." instance
    /// access, which EF Core specifically recognizes and re-evaluates for
    /// every query execution against every DbContext instance. A filter
    /// that instead closed over the injected ITenantProvider directly (a
    /// captured service, not an instance member) would get baked into the
    /// cached model the first time it runs and never change again - the
    /// bug this shape avoids.
    /// </summary>
    private Guid CurrentTenantId => _tenantProvider.IsResolved ? _tenantProvider.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.UseOpenIddict<Guid>();

        builder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted && p.TenantId == CurrentTenantId);
        builder.Entity<Tenant>().HasQueryFilter(t => !t.IsDeleted);
    }

    public new EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class
        => base.Entry(entity);
}
