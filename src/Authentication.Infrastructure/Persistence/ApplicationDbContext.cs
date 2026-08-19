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

    public DbSet<Empresa> Empresas => Set<Empresa>();

    public DbSet<Vinculo> Vinculos => Set<Vinculo>();

    public DbSet<VinculoEmpresa> VinculoEmpresas => Set<VinculoEmpresa>();

    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<TenantRolePermission> TenantRolePermissions => Set<TenantRolePermission>();

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
        builder.Entity<Empresa>().HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
        builder.Entity<TenantRole>().HasQueryFilter(r => !r.IsDeleted && r.TenantId == CurrentTenantId);
        builder.Entity<Permission>().HasQueryFilter(p => !p.IsDeleted);

        // Vinculo intentionally has NO tenant query filter: the login flow
        // must look up a user's memberships *across every tenant* before any
        // tenant is resolved (see AuthorizationController). Every other read
        // of Vinculo happens already scoped by an explicit TenantId/UsuarioId
        // predicate at the call site, so the absence of a filter here is a
        // deliberate exception, not an oversight - do not "fix" it by adding
        // one back without re-checking the login flow.
        builder.Entity<Vinculo>().HasQueryFilter(v => !v.IsDeleted);
    }

    public new EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class
        => base.Entry(entity);
}
