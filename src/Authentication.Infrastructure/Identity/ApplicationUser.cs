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
/// TwoFactorRequired is this system's own flag, deliberately NOT the
/// Identity-native TwoFactorEnabled: SignInManager's automatic
/// RequiresTwoFactor detection only recognizes Identity's built-in
/// "token provider" 2FA methods (TOTP, email, SMS) via
/// GetValidTwoFactorProvidersAsync - a WebAuthn-only user would never
/// trip it, since WebAuthn is a challenge/response credential, not a
/// token provider. AuthorizationController.HandlePasswordAsync computes
/// the 2FA gate itself instead of relying on that built-in mechanism.
/// Only an admin endpoint may set this (see AdminUsuariosController) -
/// there is no self-service toggle for the *requirement*, though a user
/// with the requirement on still self-enrolls their own factor (nobody
/// else can hold their TOTP secret or WebAuthn private key for them).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string NomePrimeiro { get; set; } = default!;

    public string? NomeMeio { get; set; }

    public string NomeUltimo { get; set; } = default!;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; } = true;

    public bool TwoFactorRequired { get; set; }
}

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
