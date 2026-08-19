using Authentication.Domain.Common;

namespace Authentication.Domain.Entities;

/// <summary>
/// Sample tenant- and empresa-owned resource demonstrating the isolation
/// pattern that applies to every business entity in the system: a product
/// belongs to one specific Empresa (CNPJ), not to the Grupo Econômico at
/// large.
/// </summary>
public class Product : BaseEntity, ITenantOwned, IEmpresaOwned
{
    public Guid TenantId { get; set; }

    public Guid EmpresaId { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }
}
