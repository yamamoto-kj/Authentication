using System.Text;
using System.Text.Json;
using Authentication.Api.Contracts.TwoFactor;
using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Entities;
using Authentication.Infrastructure.Identity;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Controllers;

/// <summary>
/// Self-enrollment of the caller's own second factor. Only reachable with
/// an enrollment or challenge token (see AuthorizationController) - never
/// with a fully-scoped tenant token, so this can't be used to add a
/// surprise factor to someone else's account, and never anonymously.
/// Enrollment never issues a login token itself: the client must call
/// /connect/token again afterwards to get a proper 2FA challenge (or a
/// full token, if 2FA wasn't required in the first place).
/// </summary>
[ApiController]
[Route("auth/2fa")]
public class TwoFactorController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _dbContext;
    private readonly IFido2 _fido2;
    private readonly WebAuthnChallengeCache _challengeCache;

    public TwoFactorController(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext dbContext,
        IFido2 fido2,
        WebAuthnChallengeCache challengeCache)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _fido2 = fido2;
        _challengeCache = challengeCache;
    }

    [HttpPost("totp/enroll/start")]
    [Authorize(Policy = "TwoFactorEnrollment")]
    [ProducesResponseType(typeof(TotpEnrollStartResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TotpEnrollStartResponse>> StartTotpEnrollment()
    {
        var user = await GetCurrentUserAsync();

        // A fresh key each time this is called - if the user abandons an
        // old enrollment attempt and starts over, only the latest key can
        // ever be confirmed.
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var secret = await _userManager.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("Authenticator key was not generated.");

        var issuer = Uri.EscapeDataString("Authentication API");
        var label = Uri.EscapeDataString(user.UserName!);
        var otpauthUri = $"otpauth://totp/{issuer}:{label}?secret={secret}&issuer={issuer}&digits=6";

        return Ok(new TotpEnrollStartResponse(secret, otpauthUri));
    }

    [HttpPost("totp/enroll/confirm")]
    [Authorize(Policy = "TwoFactorEnrollment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmTotpEnrollment([FromBody] TotpEnrollConfirmRequest request)
    {
        var user = await GetCurrentUserAsync();

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
        if (!valid)
        {
            return BadRequest(new { error = "Código inválido." });
        }

        // The key already exists from enroll/start; a successful
        // verification here is just proof the user copied it correctly -
        // its mere presence is what AuthorizationController.HasEnrolledFactorAsync checks.
        return NoContent();
    }

    [HttpPost("webauthn/enroll/options")]
    [Authorize(Policy = "TwoFactorEnrollment")]
    [ProducesResponseType(typeof(CredentialCreateOptions), StatusCodes.Status200OK)]
    public async Task<ActionResult<CredentialCreateOptions>> GetWebAuthnEnrollOptions()
    {
        var user = await GetCurrentUserAsync();

        var existingCredentials = await _dbContext.WebAuthnCredentials
            .Where(c => c.UsuarioId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync();

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = user.Id.ToByteArray(),
                Name = user.UserName!,
                DisplayName = $"{user.NomePrimeiro} {user.NomeUltimo}"
            },
            ExcludeCredentials = existingCredentials
                .Select(id => new PublicKeyCredentialDescriptor(id))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection { UserVerification = UserVerificationRequirement.Preferred },
            AttestationPreference = AttestationConveyancePreference.None,
            PubKeyCredParams = new[] { PubKeyCredParam.ES256, PubKeyCredParam.RS256 }
        });

        await _challengeCache.StoreCreationOptionsAsync(user.Id, options);

        return Ok(options);
    }

    [HttpPost("webauthn/enroll/verify")]
    [Authorize(Policy = "TwoFactorEnrollment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyWebAuthnEnrollment([FromBody] WebAuthnEnrollVerifyRequest request)
    {
        var user = await GetCurrentUserAsync();

        var options = await _challengeCache.TakeCreationOptionsAsync(user.Id);
        if (options is null)
        {
            return BadRequest(new { error = "Nenhum cadastro de chave pendente. Solicite novas opções." });
        }

        AuthenticatorAttestationRawResponse attestationResponse;
        try
        {
            attestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(request.AttestationResponseJson)
                ?? throw new JsonException("Empty payload.");
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Resposta de attestation malformada." });
        }

        Fido2NetLib.Objects.RegisteredPublicKeyCredential credential;
        try
        {
            credential = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = attestationResponse,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (args, _) =>
                    !await _dbContext.WebAuthnCredentials.AnyAsync(c => c.CredentialId == args.CredentialId)
            });
        }
        catch (Fido2VerificationException)
        {
            return BadRequest(new { error = "Falha na verificação da chave de segurança." });
        }

        _dbContext.WebAuthnCredentials.Add(new WebAuthnCredential
        {
            UsuarioId = user.Id,
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            AaGuid = credential.AaGuid,
            DeviceName = request.DeviceName,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync(default);

        return NoContent();
    }

    [HttpPost("webauthn/assertion/options")]
    [Authorize(Policy = "TwoFactorChallenge")]
    [ProducesResponseType(typeof(AssertionOptions), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AssertionOptions>> GetWebAuthnAssertionOptions()
    {
        var user = await GetCurrentUserAsync();

        var credentialIds = await _dbContext.WebAuthnCredentials
            .Where(c => c.UsuarioId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync();

        if (credentialIds.Count == 0)
        {
            return BadRequest(new { error = "Usuário não possui nenhuma chave WebAuthn cadastrada." });
        }

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentialIds.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Preferred
        });

        await _challengeCache.StoreAssertionOptionsAsync(user.Id, options);

        return Ok(options);
    }

    private async Task<ApplicationUser> GetCurrentUserAsync()
    {
        var userId = User.FindFirst(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject)!.Value;
        return await _userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("Authenticated user was not found.");
    }
}
