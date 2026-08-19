using Authentication.Domain.Common;

namespace Authentication.Domain.Entities;

/// <summary>
/// Global catalog of actions the API knows how to authorize (e.g.
/// "usuarios:criar", "relatorios:exportar"). Not tenant-owned - the catalog
/// is fixed by the system; each Tenant decides which of these its own
/// TenantRoles grant, via TenantRolePermission.
/// </summary>
public class Permission : BaseEntity
{
    public string Chave { get; set; } = default!;

    public string? Descricao { get; set; }
}
