namespace Authentication.Domain.Common;

/// <summary>
/// Marks an entity as belonging to a single tenant. The EF Core global query
/// filter keyed on TenantId is what actually enforces isolation between tenants;
/// this interface only identifies which entities that filter applies to.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
