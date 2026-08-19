namespace Authentication.Domain.Common;

/// <summary>
/// Marks an entity as belonging to a single Empresa within a tenant - the
/// finer-grained isolation dimension alongside ITenantOwned. An entity
/// implementing this should also implement ITenantOwned; EmpresaId alone
/// already uniquely determines the tenant (an Empresa belongs to exactly
/// one Tenant), but keeping TenantId too costs nothing and lets the
/// composite index/query filter lead with the coarser, more selective
/// column first.
/// </summary>
public interface IEmpresaOwned
{
    Guid EmpresaId { get; set; }
}
