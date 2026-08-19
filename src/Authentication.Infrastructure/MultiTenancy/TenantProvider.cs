using Authentication.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Authentication.Infrastructure.MultiTenancy;

public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";

    /// <summary>Present on a final token alongside TenantId once phase 5 wires Empresa-level isolation.</summary>
    public const string EmpresaId = "empresa_id";
}

/// <summary>
/// Marks a token as an intermediate "selection" credential (issued by the
/// password grant when the user has one or more active Vinculo) rather
/// than a fully-scoped one - it carries no tenant_id/role/empresa_id and is
/// only good for GET /auth/contexts and POST /auth/select-context.
/// </summary>
public static class SelectionClaimTypes
{
    public const string Purpose = "purpose";
    public const string TenantSelectionPurpose = "tenant_selection";
}

/// <summary>
/// Resolves the tenant strictly from the validated JWT's "tenant_id" claim.
/// Never trusts a client-supplied header/route value for authenticated
/// requests - that would let a caller impersonate another tenant simply by
/// changing a header. The X-Tenant-Id header is only meaningful before
/// authentication, to pick which tenant a login attempt targets.
/// </summary>
public class TenantProvider : ITenantProvider
{
    private readonly Guid _tenantId;
    private readonly bool _isResolved;

    public TenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        var claimValue = httpContextAccessor.HttpContext?.User
            .FindFirst(TenantClaimTypes.TenantId)?.Value;

        if (Guid.TryParse(claimValue, out var tenantId))
        {
            _tenantId = tenantId;
            _isResolved = true;
        }
    }

    public Guid TenantId => _tenantId;

    public bool IsResolved => _isResolved;
}
