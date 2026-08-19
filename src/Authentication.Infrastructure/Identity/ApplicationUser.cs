using Microsoft.AspNetCore.Identity;

namespace Authentication.Infrastructure.Identity;

/// <summary>
/// The single global identity (one per CPF). No longer tied to a single
/// tenant - access to Tenants/Empresas is granted via Vinculo rows, so one
/// person with multiple jobs only ever has one login.
///
/// UserName IS the CPF (digits only, no formatting) - there is no separate
/// Cpf column. Identity already normalizes/indexes UserName uniquely, so a
/// second column would just be a redundant, driftable copy of the same
/// value.
///
/// TwoFactorEnabled is ASP.NET Identity's own built-in flag, reused as-is:
/// SignInManager.CheckPasswordSignInAsync already returns
/// SignInResult.RequiresTwoFactor when it's set, which is exactly the gate
/// this system needs. Only an admin endpoint may set it (see
/// AdminUsersController, phase 2) - there is no self-service toggle.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string NomePrimeiro { get; set; } = default!;

    public string? NomeMeio { get; set; }

    public string NomeUltimo { get; set; } = default!;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; } = true;
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
