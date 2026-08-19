namespace Authentication.Application.Common.Interfaces;

/// <summary>
/// Resolves the current Empresa (CNPJ) for the request/operation, from the
/// JWT "empresa_id" claim - the finer-grained sibling of ITenantProvider.
/// Every final (post tenant/empresa-selection) token carries both claims
/// together; see AuthorizationController.
/// </summary>
public interface IEmpresaProvider
{
    /// <summary>
    /// Returns Guid.Empty when no empresa is resolved (e.g. design-time/
    /// migrations, or a request that never went through empresa selection -
    /// a platform-admin or selection-scoped token, for instance). Consumers
    /// that require an empresa must check <see cref="IsResolved"/>
    /// explicitly rather than relying on this returning a valid id.
    /// </summary>
    Guid EmpresaId { get; }

    bool IsResolved { get; }
}
