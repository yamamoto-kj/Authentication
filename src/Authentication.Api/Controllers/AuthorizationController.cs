using System.Collections.Immutable;
using System.Security.Claims;
using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Authentication.Api.Extensions;
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

    public AuthorizationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IApplicationDbContext dbContext,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
    }

    [HttpPost("token")]
    [HttpPost("/auth/select-context")]
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
        // TwoFactorRequired users get SignInResult.RequiresTwoFactor here -
        // treated as a plain failure for now; the 2FA gate is phase 4.
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Forbidden(Errors.InvalidGrant, "CPF/senha inválidos.");
        }

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
            identity.SetScopes(request.GetScopes());
        }
        else
        {
            // One or many Vinculo: always a selection token, never a full
            // one, directly from the password grant - see the class-level
            // doc comment for why even the single-tenant case goes through
            // this same path.
            identity = BuildSelectionIdentity(user);
            // No scopes granted here (and no refresh token as a result for
            // most clients) - this token is only ever meant to be traded
            // immediately for a full one via the tenant_selection grant.
        }

        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
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
    /// Intermediate credential: identifies the user (Subject) and nothing
    /// else - no tenant, no role, no empresa. Only ever meant to be handed
    /// straight back to GET /auth/contexts or the tenant_selection grant.
    /// </summary>
    private static ClaimsIdentity BuildSelectionIdentity(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(SelectionClaimTypes.Purpose, SelectionClaimTypes.TenantSelectionPurpose);
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
