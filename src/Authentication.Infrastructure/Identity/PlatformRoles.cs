namespace Authentication.Infrastructure.Identity;

/// <summary>
/// Platform-level (cross-tenant) authorization, distinct from the
/// per-tenant TenantRole/Permission system. A platform admin manages
/// global concerns - provisioning brand-new Usuario records - which have
/// no tenant context to authorize against. Implemented as an ASP.NET
/// Identity Role (AspNetRoles/AspNetUserRoles), the one place in this
/// system that infrastructure is used for.
/// </summary>
public static class PlatformRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
}

public static class PlatformClaimTypes
{
    /// <summary>
    /// Present only for users in the PlatformAdmin role; unrelated to (and
    /// never a substitute for) the per-tenant "role" claim.
    /// </summary>
    public const string PlatformRole = "platform_role";
}
