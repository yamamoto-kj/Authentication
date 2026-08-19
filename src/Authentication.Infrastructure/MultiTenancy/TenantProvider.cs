using Authentication.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Authentication.Infrastructure.MultiTenancy;

public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";
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
