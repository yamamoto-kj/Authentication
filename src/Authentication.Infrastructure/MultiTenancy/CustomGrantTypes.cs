namespace Authentication.Infrastructure.MultiTenancy;

/// <summary>
/// OAuth2 (RFC 6749 §4.5) allows extension grant types identified by a URI.
/// This one exchanges a "selection" token (see SelectionClaimTypes) plus a
/// chosen tenant/empresa for a fully-scoped token - see
/// AuthorizationController.HandleTenantSelectionAsync.
/// </summary>
public static class CustomGrantTypes
{
    public const string TenantSelection = "urn:authentication:grant-type:tenant_selection";
}
