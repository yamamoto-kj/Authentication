namespace Authentication.Application.Common.Interfaces;

/// <summary>
/// Resolves the tenant for the current request/operation. Implemented in the
/// API layer from the JWT "tenant_id" claim (falling back to the
/// X-Tenant-Id header only for the unauthenticated token endpoint) so every
/// downstream query and command operates against a single, already-verified
/// tenant - callers never pass a tenant id explicitly.
/// </summary>
public interface ITenantProvider
{
    /// <summary>
    /// Returns Guid.Empty when no tenant is resolved (e.g. design-time/migrations,
    /// or an endpoint executed before authentication). Consumers that require a
    /// tenant must check <see cref="IsResolved"/> explicitly rather than relying
    /// on this returning a valid id.
    /// </summary>
    Guid TenantId { get; }

    bool IsResolved { get; }
}
