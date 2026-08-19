using Authentication.Domain.Common;
using Authentication.Domain.Enums;

namespace Authentication.Domain.Entities;

/// <summary>
/// A tenant (organization/customer) in the multi-tenant system. Identified
/// either by a URL-safe slug (subdomain routing) or by its Id (header/claim
/// routing) - see ITenantResolutionStrategy implementations in the API layer.
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = default!;

    /// <summary>URL-safe unique identifier, e.g. "acme" for acme.api.example.com.</summary>
    public string Slug { get; set; } = default!;

    public TenantStatus Status { get; set; } = TenantStatus.PendingActivation;

    /// <summary>Per-tenant request-per-minute quota override; null uses the global default.</summary>
    public int? RateLimitPermitsPerMinute { get; set; }
}
