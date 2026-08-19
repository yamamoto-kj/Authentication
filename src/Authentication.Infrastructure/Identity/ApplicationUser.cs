using Microsoft.AspNetCore.Identity;

namespace Authentication.Infrastructure.Identity;

/// <summary>
/// A user always belongs to exactly one tenant. TenantId is stamped into the
/// access token as a claim at login time so every downstream request carries
/// its tenant without a database lookup.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; } = true;
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
