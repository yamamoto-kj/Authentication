using Authentication.Api.Contracts.Admin;
using Authentication.Api.Extensions;
using Authentication.Domain.Common;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Authentication.Api.Controllers;

/// <summary>
/// Public, unauthenticated account activation. An admin-provisioned Usuario
/// has no usable password until this completes - see
/// AdminUsuariosController's doc comment on why the reset token is issued
/// there instead of a password.
/// </summary>
[ApiController]
[Route("auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.TokenEndpointPolicy)]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpPost("set-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPassword([FromBody] DefinirSenhaRequest request)
    {
        var cpf = CpfValidator.Normalize(request.Cpf);
        if (cpf is null)
        {
            return BadRequest(new { error = "CPF inválido." });
        }

        var user = await _userManager.FindByNameAsync(cpf);
        if (user is null)
        {
            // Same generic response as an invalid token below - do not
            // reveal whether a CPF exists in the system to an anonymous caller.
            return BadRequest(new { error = "Token inválido ou expirado." });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NovaSenha);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        // Completing this flow is the only proof of CPF/token ownership
        // this MVP has (there is no separate email-verification loop yet),
        // so it doubles as email confirmation.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        return NoContent();
    }
}
