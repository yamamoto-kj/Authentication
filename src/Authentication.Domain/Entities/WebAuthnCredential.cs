using Authentication.Domain.Common;

namespace Authentication.Domain.Entities;

/// <summary>
/// A registered WebAuthn/FIDO2 authenticator (passkey/security key) for a
/// Usuario's second factor. UsuarioId is a plain Guid, not a navigation
/// property - same reasoning as Vinculo.UsuarioId: Domain must not
/// reference Infrastructure.Identity.ApplicationUser.
/// </summary>
public class WebAuthnCredential : BaseEntity
{
    public Guid UsuarioId { get; set; }

    /// <summary>The authenticator-issued credential id (opaque, provider-assigned).</summary>
    public byte[] CredentialId { get; set; } = default!;

    public byte[] PublicKey { get; set; } = default!;

    /// <summary>
    /// Cloned-authenticator detection: must strictly increase on every use.
    /// A non-increasing value on verification means the credential was
    /// cloned and must be rejected.
    /// </summary>
    public uint SignCount { get; set; }

    public string? DeviceName { get; set; }

    public Guid AaGuid { get; set; }
}
