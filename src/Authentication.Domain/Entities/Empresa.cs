using Authentication.Domain.Common;
using Authentication.Domain.Enums;

namespace Authentication.Domain.Entities;

/// <summary>
/// A company/CNPJ within a Tenant (Grupo Econômico). A Tenant has one or
/// more Empresas; a user's Vinculo to the Tenant is further scoped to a
/// subset of its Empresas via VinculoEmpresa.
/// </summary>
public class Empresa : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }

    public string RazaoSocial { get; set; } = default!;

    /// <summary>Digits only, unique within the owning tenant (see EmpresaConfiguration).</summary>
    public string Cnpj { get; set; } = default!;

    public EmpresaStatus Status { get; set; } = EmpresaStatus.Ativa;
}
