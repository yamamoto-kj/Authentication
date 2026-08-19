using Authentication.Domain.Common;
using Authentication.Domain.Enums;

namespace Authentication.Domain.Entities;

/// <summary>
/// Grants one global Usuario (identified by CPF) access to one Tenant
/// (Grupo Econômico), with a role scoped to that tenant. UsuarioId is a
/// plain Guid, not a navigation property to ApplicationUser - Domain must
/// not depend on Infrastructure.Identity; the relational FK to AspNetUsers
/// is wired up in Infrastructure's VinculoConfiguration instead.
/// A user can be active in one Tenant and suspended in another
/// simultaneously - status lives here, not on the global Usuario.
/// </summary>
public class Vinculo : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid TenantRoleId { get; set; }

    public VinculoStatus Status { get; set; } = VinculoStatus.Ativo;
}
