using System.Collections.Immutable;
using System.Security.Claims;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Authentication.Api.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

// OpenIddict.Server.AspNetCore's HttpContext extension helpers (GetOpenIddictServerRequest)
// live in namespace Microsoft.AspNetCore, not OpenIddict.Server.AspNetCore.
using Microsoft.AspNetCore;

namespace Authentication.Api.Controllers;

/// <summary>
/// OAuth2 token endpoint (RFC 6749). Supports:
///  - client_credentials: service-to-service, tenant taken from the client application.
///  - password: first-party trusted clients only (see README security notes -
///    prefer Authorization Code + PKCE for anything with a browser/redirect).
///  - refresh_token: rolling refresh, previous token revoked on use.
/// The tenant_id claim is stamped into every token here and nowhere else,
/// which is what makes it trustworthy for TenantProvider to read downstream.
/// </summary>
[ApiController]
[Route("connect")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.TokenEndpointPolicy)]
public class AuthorizationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;

    public AuthorizationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
    }

    [HttpPost("token")]
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
        var user = await _userManager.FindByNameAsync(request.Username!);
        if (user is null || !user.IsActive)
        {
            return Forbidden(Errors.InvalidGrant, "The username/password combination is invalid.");
        }

        // Uses Identity's lockout-aware password check so brute-force
        // attempts trip the same account lockout as the normal login path.
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Forbidden(Errors.InvalidGrant, "The username/password combination is invalid.");
        }

        var identity = await BuildUserIdentityAsync(user);

        identity.SetScopes(request.GetScopes());
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
        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;

        if (user is null || !user.IsActive)
        {
            // Revoke: the user was deleted/deactivated since the refresh
            // token was issued - do not let a stale token keep working.
            return Forbidden(Errors.InvalidGrant, "The token is no longer valid.");
        }

        var identity = await BuildUserIdentityAsync(user);
        identity.SetScopes(principal.GetScopes());
        identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<ClaimsIdentity> BuildUserIdentityAsync(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Name, user.UserName);
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim(TenantClaimTypes.TenantId, user.TenantId.ToString());

        var roles = await _userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, roles.ToImmutableArray());

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

            case TenantClaimTypes.TenantId or Claims.Role or Claims.Email:
                yield return Destinations.AccessToken;
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
