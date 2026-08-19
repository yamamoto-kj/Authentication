using Authentication.Domain.Common;

namespace Authentication.Domain.Entities;

/// <summary>
/// A role defined by and scoped to a single Tenant (Grupo Econômico) - e.g.
/// "admin", "operador", "leitura", or a name the tenant made up itself.
/// Named TenantRole (not Role) to avoid clashing with
/// Infrastructure.Identity.ApplicationRole, which is ASP.NET Identity's
/// unrelated, global role concept and is not used for business
/// authorization in this system.
/// </summary>
public class TenantRole : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string Nome { get; set; } = default!;
}
