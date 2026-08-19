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

    /// <summary>Completes a login gated by 2FA using a TOTP code - see AuthorizationController.HandleTotpVerifyAsync.</summary>
    public const string TwoFactorTotpVerify = "urn:authentication:grant-type:2fa_totp_verify";

    /// <summary>Completes a login gated by 2FA using a WebAuthn assertion - see AuthorizationController.HandleWebAuthnVerifyAsync.</summary>
    public const string TwoFactorWebAuthnVerify = "urn:authentication:grant-type:2fa_webauthn_verify";
}
