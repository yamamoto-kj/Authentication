using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Entities;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Authentication.Api.Extensions;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

// OpenIddict.Server.AspNetCore's HttpContext extension helpers (GetOpenIddictServerRequest)
// live in namespace Microsoft.AspNetCore, not OpenIddict.Server.AspNetCore.
using Microsoft.AspNetCore;

namespace Authentication.Api.Controllers;

/// <summary>
/// OAuth2 token endpoint (RFC 6749), listening on two registered URIs:
///  - /connect/token: client_credentials, password, refresh_token.
///  - /auth/select-context: the custom "tenant_selection" extension grant
///    (RFC 6749 §4.5) that completes the CPF-Global login flow.
///
/// password grant behavior: CPF/senha alone never yields a fully-scoped
/// token unless the user has zero Vinculo and is a PlatformAdmin. Anyone
/// with one or more active Vinculo instead gets a short-lived *selection*
/// token (see SelectionClaimTypes) good only for GET /auth/contexts and
/// this controller's tenant_selection grant - even a single-tenant user
/// goes through this so the client only ever needs one code path (it can
/// auto-submit the sole option without prompting).
///
/// The tenant_id/empresa_id claims are stamped into a token in exactly one
/// place (BuildUserIdentity, called only from the tenant_selection branch
/// and the refresh-token branch) - never by the initial password grant -
/// which is what makes them trustworthy for TenantProvider to read
/// downstream.
/// </summary>
[ApiController]
[Route("connect")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.TokenEndpointPolicy)]
public class AuthorizationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IApplicationDbContext _dbContext;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IFido2 _fido2;
    private readonly WebAuthnChallengeCache _webAuthnChallengeCache;

    public AuthorizationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IApplicationDbContext dbContext,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IFido2 fido2,
        WebAuthnChallengeCache webAuthnChallengeCache)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
        _fido2 = fido2;
        _webAuthnChallengeCache = webAuthnChallengeCache;
    }

    [HttpPost("token")]
    [HttpPost("/auth/select-context")]
    [HttpPost("/auth/2fa/totp/verify")]
    [HttpPost("/auth/2fa/webauthn/assertion/verify")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            return await HandleClientCredentialsAsync(request);
        }

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordAsync(request);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenAsync();
        }

        if (request.GrantType == CustomGrantTypes.TenantSelection)
        {
            return await HandleTenantSelectionAsync(request);
        }

        if (request.GrantType == CustomGrantTypes.TwoFactorTotpVerify)
        {
            return await HandleTotpVerifyAsync(request);
        }

        if (request.GrantType == CustomGrantTypes.TwoFactorWebAuthnVerify)
        {
            return await HandleWebAuthnVerifyAsync(request);
        }

        return Forbid(
            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.UnsupportedGrantType,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                    "The specified grant type is not supported by this authorization server."
            }));
    }

    private async Task<IActionResult> HandleClientCredentialsAsync(OpenIddictRequest request)
    {
        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("The application details cannot be found.");

        var properties = await _applicationManager.GetPropertiesAsync(application);
        if (!properties.TryGetValue("tenant_id", out var tenantIdElement) ||
            tenantIdElement.GetString() is not { Length: > 0 } tenantId)
        {
            return Forbidden(Errors.InvalidClient, "The client application is not associated with a tenant.");
        }

        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        var clientId = await _applicationManager.GetClientIdAsync(application) ?? request.ClientId!;

        identity.SetClaim(Claims.Subject, clientId);
        identity.SetClaim(Claims.Name, await _applicationManager.GetDisplayNameAsync(application));
        identity.SetClaim(TenantClaimTypes.TenantId, tenantId);

        identity.SetScopes(request.GetScopes());
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandlePasswordAsync(OpenIddictRequest request)
    {
        // request.Username carries the CPF (digits only) - it IS the
        // Identity UserName, see ApplicationUser's doc comment.
        var user = await _userManager.FindByNameAsync(request.Username!);
        if (user is null || !user.IsActive)
        {
            return Forbidden(Errors.InvalidGrant, "CPF/senha inválidos.");
        }

        // Uses Identity's lockout-aware password check so brute-force
        // attempts trip the same account lockout as the normal login path.
        // Not CheckPasswordSignInAsync's own RequiresTwoFactor branching -
        // see ApplicationUser's doc comment on why the 2FA gate below is
        // computed by hand instead (WebAuthn-only users would never trip
        // Identity's built-in detection).
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Forbidden(Errors.InvalidGrant, "CPF/senha inválidos.");
        }

        if (user.TwoFactorRequired)
        {
            var purpose = await HasEnrolledFactorAsync(user)
                ? SelectionClaimTypes.TwoFactorChallengePurpose
                : SelectionClaimTypes.TwoFactorEnrollPurpose;

            return await IssuePurposeTokenAsync(user, purpose);
        }

        return await IssuePostCredentialTokenAsync(user, request.GetScopes());
    }

    /// <summary>
    /// Shared tail of every grant that ends in "the caller is definitely
    /// this Usuario, mint whatever comes next": the plain password grant
    /// when 2FA isn't required, and both 2FA verify grants on success.
    /// Decides between a platform-admin token, a selection token, or
    /// rejection, exactly as the password grant always has.
    /// </summary>
    private async Task<IActionResult> IssuePostCredentialTokenAsync(ApplicationUser user, ImmutableArray<string> requestedScopes)
    {
        var isPlatformAdmin = await _userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);

        // Cross-tenant by design: no tenant is resolved yet at this point in
        // the flow, so the standard Vinculo query filter is bypassed
        // explicitly and deliberately (see ApplicationDbContext's comment on
        // why Vinculo carries no tenant filter at all).
        var vinculosAtivos = await _dbContext.Vinculos
            .Where(v => v.UsuarioId == user.Id && v.Status == VinculoStatus.Ativo)
            .ToListAsync();

        ClaimsIdentity identity;

        if (vinculosAtivos.Count == 0)
        {
            // A platform admin manages global concerns (e.g. provisioning
            // other users) that have no tenant context, so they are allowed
            // to hold zero Vinculo and still log in - everyone else with no
            // membership anywhere has nothing this API can do for them.
            if (!isPlatformAdmin)
            {
                return Forbidden(Errors.InvalidGrant, "Usuário sem acesso a nenhum grupo econômico.");
            }

            identity = BuildUserIdentity(user, tenantId: null, empresaId: null, roleName: null, permissions: ImmutableArray<string>.Empty, isPlatformAdmin: true);
            identity.SetScopes(requestedScopes);
        }
        else
        {
            // One or many Vinculo: always a selection token, never a full
            // one, directly from here - see the class-level doc comment for
            // why even the single-tenant case goes through this same path.
            identity = BuildPurposeIdentity(user, SelectionClaimTypes.TenantSelectionPurpose);
            // No scopes granted here (and no refresh token as a result for
            // most clients) - this token is only ever meant to be traded
            // immediately for a full one via the tenant_selection grant.
        }

        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Issues a purpose-only token (2FA enrollment/challenge) and signs in with it directly.</summary>
    private async Task<IActionResult> IssuePurposeTokenAsync(ApplicationUser user, string purpose)
    {
        var identity = BuildPurposeIdentity(user, purpose);
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>TOTP counts once a key exists (Identity has no separate "confirmed" flag); WebAuthn counts once any credential is registered.</summary>
    private async Task<bool> HasEnrolledFactorAsync(ApplicationUser user)
    {
        var totpKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (totpKey is not null)
        {
            return true;
        }

        return await _dbContext.WebAuthnCredentials.AnyAsync(c => c.UsuarioId == user.Id);
    }

    private async Task<IActionResult> HandleTenantSelectionAsync(OpenIddictRequest request)
    {
        // The selection token was presented as a normal Bearer credential
        // on this very request - the validation handler (the API's default
        // authentication scheme) already authenticated it into HttpContext.User
        // before this action ran, exactly as it would for any other
        // endpoint. AllowAnonymous on the controller only skips the
        // *authorization* check, not authentication itself.
        if (User.FindFirst(SelectionClaimTypes.Purpose)?.Value != SelectionClaimTypes.TenantSelectionPurpose)
        {
            return Forbidden(Errors.InvalidGrant, "Token de seleção ausente ou inválido.");
        }

        var userId = User.FindFirst(Claims.Subject)?.Value;
        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
        if (user is null || !user.IsActive)
        {
            return Forbidden(Errors.InvalidGrant, "Token de seleção ausente ou inválido.");
        }

        var tenantIdParam = request.GetParameter("tenant_id")?.ToString();
        if (!Guid.TryParse(tenantIdParam, out var tenantId))
        {
            return Forbidden(Errors.InvalidRequest, "Parâmetro 'tenant_id' ausente ou inválido.");
        }

        // Cross-tenant by design, same reasoning as in HandlePasswordAsync:
        // no tenant is resolved on this DbContext yet.
        var vinculo = await _dbContext.Vinculos.FirstOrDefaultAsync(
            v => v.UsuarioId == user.Id && v.TenantId == tenantId && v.Status == VinculoStatus.Ativo);
        if (vinculo is null)
        {
            return Forbidden(Errors.InvalidGrant, "Usuário não possui vínculo ativo com o grupo econômico informado.");
        }

        // IgnoreQueryFilters: see the identical comment in HandlePasswordAsync.
        var empresaIds = await _dbContext.VinculoEmpresas.IgnoreQueryFilters()
            .Where(ve => ve.VinculoId == vinculo.Id)
            .Select(ve => ve.EmpresaId)
            .ToListAsync();

        var empresaIdParam = request.GetParameter("empresa_id")?.ToString();
        Guid empresaId;

        if (!string.IsNullOrEmpty(empresaIdParam))
        {
            if (!Guid.TryParse(empresaIdParam, out empresaId) || !empresaIds.Contains(empresaId))
            {
                return Forbidden(Errors.InvalidRequest, "Empresa informada não pertence a este vínculo.");
            }
        }
        else if (empresaIds.Count == 1)
        {
            empresaId = empresaIds[0];
        }
        else
        {
            // Zero empresas would mean the Vinculo was provisioned wrong
            // (VinculoEmpresa requires at least one row) - and more than
            // one requires the client to ask the user which one.
            return Forbidden(Errors.InvalidRequest, "Parâmetro 'empresa_id' é obrigatório para este grupo econômico.");
        }

        var role = await _dbContext.TenantRoles.IgnoreQueryFilters().FirstAsync(r => r.Id == vinculo.TenantRoleId);
        var permissions = await GetPermissionKeysAsync(role.Id);
        var isPlatformAdmin = await _userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);

        var identity = BuildUserIdentity(user, tenantId, empresaId, role.Nome, permissions, isPlatformAdmin);
        identity.SetScopes(ImmutableArray.Create(Scopes.OfflineAccess, "api"));
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleTotpVerifyAsync(OpenIddictRequest request)
    {
        // Same Bearer-authenticated-via-default-scheme trick as
        // HandleTenantSelectionAsync: the 2FA challenge token was sent as a
        // normal Authorization header, already validated before this action ran.
        if (User.FindFirst(SelectionClaimTypes.Purpose)?.Value != SelectionClaimTypes.TwoFactorChallengePurpose)
        {
            return Forbidden(Errors.InvalidGrant, "Token de desafio de dois fatores ausente ou inválido.");
        }

        var userId = User.FindFirst(Claims.Subject)?.Value;
        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
        if (user is null || !user.IsActive)
        {
            return Forbidden(Errors.InvalidGrant, "Token de desafio de dois fatores ausente ou inválido.");
        }

        var code = request.GetParameter("code")?.ToString();
        if (string.IsNullOrEmpty(code))
        {
            return Forbidden(Errors.InvalidRequest, "Parâmetro 'code' ausente.");
        }

        var validCode = await _userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
        if (!validCode)
        {
            return Forbidden(Errors.InvalidGrant, "Código inválido.");
        }

        return await IssuePostCredentialTokenAsync(user, request.GetScopes());
    }

    private async Task<IActionResult> HandleWebAuthnVerifyAsync(OpenIddictRequest request)
    {
        if (User.FindFirst(SelectionClaimTypes.Purpose)?.Value != SelectionClaimTypes.TwoFactorChallengePurpose)
        {
            return Forbidden(Errors.InvalidGrant, "Token de desafio de dois fatores ausente ou inválido.");
        }

        var userId = User.FindFirst(Claims.Subject)?.Value;
        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
        if (user is null || !user.IsActive)
        {
            return Forbidden(Errors.InvalidGrant, "Token de desafio de dois fatores ausente ou inválido.");
        }

        var responseJson = request.GetParameter("assertion_response")?.ToString();
        if (string.IsNullOrEmpty(responseJson))
        {
            return Forbidden(Errors.InvalidRequest, "Parâmetro 'assertion_response' ausente.");
        }

        // One-shot: TakeAssertionOptionsAsync removes the entry, so a
        // replayed verify call (same or different response) always fails
        // here instead of re-validating against a stale challenge.
        var options = await _webAuthnChallengeCache.TakeAssertionOptionsAsync(user.Id);
        if (options is null)
        {
            return Forbidden(Errors.InvalidGrant, "Nenhum desafio pendente para este usuário. Solicite um novo.");
        }

        AuthenticatorAssertionRawResponse assertionResponse;
        try
        {
            assertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(responseJson)
                ?? throw new JsonException("Empty payload.");
        }
        catch (JsonException)
        {
            return Forbidden(Errors.InvalidRequest, "Parâmetro 'assertion_response' malformado.");
        }

        var credential = await _dbContext.WebAuthnCredentials
            .FirstOrDefaultAsync(c => c.UsuarioId == user.Id && c.CredentialId == assertionResponse.RawId);
        if (credential is null)
        {
            return Forbidden(Errors.InvalidGrant, "Credencial não reconhecida.");
        }

        VerifyAssertionResult assertionResult;
        try
        {
            assertionResult = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertionResponse,
                OriginalOptions = options,
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = credential.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.SequenceEqual(user.Id.ToByteArray()))
            });
        }
        catch (Fido2VerificationException)
        {
            return Forbidden(Errors.InvalidGrant, "Falha na verificação da chave de segurança.");
        }

        // Strictly-increasing counter check: a non-increasing value means a
        // cloned authenticator. Fido2NetLib validates this internally
        // against StoredSignatureCounter and throws above if it fails, so
        // reaching here means it held - just persist the new value.
        credential.SignCount = assertionResult.SignCount;
        await _dbContext.SaveChangesAsync(default);

        return await IssuePostCredentialTokenAsync(user, request.GetScopes());
    }

    private async Task<IActionResult> HandleRefreshTokenAsync()
    {
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal
            ?? throw new InvalidOperationException("The refresh token principal cannot be retrieved.");

        var userId = principal.GetClaim(Claims.Subject);
        var tenantIdClaim = principal.GetClaim(TenantClaimTypes.TenantId);
        var empresaIdClaim = principal.GetClaim(TenantClaimTypes.EmpresaId);

        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;

        if (user is null || !user.IsActive)
        {
            // Revoke: the user was deleted/deactivated since the refresh
            // token was issued - do not let a stale token keep working.
            return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
        }

        var isPlatformAdmin = await _userManager.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);

        ClaimsIdentity identity;

        if (tenantIdClaim is null)
        {
            // The original token had no tenant (platform-admin login) -
            // re-verify the role wasn't revoked since then before reissuing.
            if (!isPlatformAdmin)
            {
                return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
            }

            identity = BuildUserIdentity(user, tenantId: null, empresaId: null, roleName: null, permissions: ImmutableArray<string>.Empty, isPlatformAdmin: true);
        }
        else
        {
            if (!Guid.TryParse(tenantIdClaim, out var tenantId) || !Guid.TryParse(empresaIdClaim, out var empresaId))
            {
                return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
            }

            // Re-verify the membership is still active on every refresh (not
            // just at original login) - a suspended Vinculo must invalidate
            // in-flight refresh tokens for that tenant, not just new logins.
            var vinculo = await _dbContext.Vinculos.FirstOrDefaultAsync(
                v => v.UsuarioId == user.Id && v.TenantId == tenantId && v.Status == VinculoStatus.Ativo);

            if (vinculo is null)
            {
                return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
            }

            // Re-verify the chosen Empresa is still granted to this Vinculo -
            // an admin may have narrowed access since the original login.
            var empresaAllowed = await _dbContext.VinculoEmpresas.IgnoreQueryFilters()
                .AnyAsync(ve => ve.VinculoId == vinculo.Id && ve.EmpresaId == empresaId);
            if (!empresaAllowed)
            {
                return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
            }

            // IgnoreQueryFilters: no tenant is resolved on this DbContext yet
            // (we are still building the very first token for this
            // request), so the standard TenantRole filter would exclude
            // every row. Safe here because vinculo.TenantRoleId was already
            // read from a Vinculo whose TenantId we trust explicitly.
            var role = await _dbContext.TenantRoles.IgnoreQueryFilters().FirstAsync(r => r.Id == vinculo.TenantRoleId);
            var permissions = await GetPermissionKeysAsync(role.Id);

            identity = BuildUserIdentity(user, tenantId, empresaId, role.Nome, permissions, isPlatformAdmin);
        }

        identity.SetScopes(principal.GetScopes());
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>IgnoreQueryFilters: same reasoning as every other lookup keyed off an already-trusted TenantRoleId in this file.</summary>
    private async Task<ImmutableArray<string>> GetPermissionKeysAsync(Guid tenantRoleId)
    {
        var keys = await _dbContext.TenantRolePermissions.IgnoreQueryFilters()
            .Where(rp => rp.TenantRoleId == tenantRoleId)
            .Join(_dbContext.Permissions.IgnoreQueryFilters(), rp => rp.PermissionId, p => p.Id, (rp, p) => p.Chave)
            .ToListAsync();

        return keys.ToImmutableArray();
    }

    private static ClaimsIdentity BuildUserIdentity(
        ApplicationUser user, Guid? tenantId, Guid? empresaId, string? roleName,
        ImmutableArray<string> permissions, bool isPlatformAdmin)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Name, $"{user.NomePrimeiro} {user.NomeUltimo}");
        identity.SetClaim(Claims.Email, user.Email);

        if (tenantId is not null)
        {
            identity.SetClaim(TenantClaimTypes.TenantId, tenantId.Value.ToString());
            identity.SetClaim(TenantClaimTypes.EmpresaId, empresaId!.Value.ToString());
            identity.SetClaims(Claims.Role, ImmutableArray.Create(roleName!));
            identity.SetClaims(PermissionClaimTypes.Permission, permissions);
        }

        if (isPlatformAdmin)
        {
            identity.SetClaim(PlatformClaimTypes.PlatformRole, PlatformRoles.PlatformAdmin);
        }

        return identity;
    }

    /// <summary>
    /// Intermediate credential: identifies the user (Subject) and a single
    /// "purpose" claim - no tenant, no role, no empresa. Only ever meant to
    /// be handed straight back to the one or two endpoints that check for
    /// that specific purpose (tenant selection, or the matching half of the
    /// 2FA enroll/challenge pair).
    /// </summary>
    private static ClaimsIdentity BuildPurposeIdentity(ApplicationUser user, string purpose)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(SelectionClaimTypes.Purpose, purpose);
        identity.SetScopes(ImmutableArray<string>.Empty);

        return identity;
    }

    private IActionResult Forbidden(string error, string description) =>
        Forbid(
            authenticationSchemes: new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }));

    /// <summary>
    /// Keeps sensitive claims (roles, email) out of the id_token and only
    /// in the access_token, and out of tokens entirely if never requested.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name or Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case TenantClaimTypes.TenantId or Claims.Role or Claims.Email or PlatformClaimTypes.PlatformRole:
                yield return Destinations.AccessToken;
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
