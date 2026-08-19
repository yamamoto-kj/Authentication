namespace Authentication.Domain.Entities;

/// <summary>
/// Join entity: which specific Empresas (within the Vinculo's Tenant) this
/// membership can access. Composite key, no surrogate Id - a row here means
/// explicit, granted access; there is no implicit "all empresas" default,
/// so provisioning a Vinculo must also create at least one of these.
/// </summary>
public class VinculoEmpresa
{
    public Guid VinculoId { get; set; }

    public Guid EmpresaId { get; set; }
}
