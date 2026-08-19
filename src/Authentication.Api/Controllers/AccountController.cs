using Authentication.Api.Contracts.Admin;
using Authentication.Api.Extensions;
using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Common;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Authentication.Api.Controllers;

/// <summary>
/// Account activation (public) and the CPF-Global "which tenant/empresa"
/// step (authenticated with a selection token) - see AuthorizationController
/// for where that token comes from. No class-level [AllowAnonymous]: each
/// action opts in individually, since AllowAnonymous on the controller
/// would silently override the [Authorize] on Contexts too.
/// </summary>
[ApiController]
[Route("auth")]
[EnableRateLimiting(RateLimitingExtensions.TokenEndpointPolicy)]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _dbContext;

    public AccountController(UserManager<ApplicationUser> userManager, IApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpGet("contexts")]
    [Authorize(Policy = "TenantSelection")]
    [ProducesResponseType(typeof(IReadOnlyList<ContextoDisponivel>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ContextoDisponivel>>> Contexts(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst(Claims.Subject)!.Value);

        // IgnoreQueryFilters: no tenant is resolved on this DbContext (the
        // caller holds a selection token, not a tenant-scoped one) - the
        // whole point of this endpoint is to look across every tenant.
        var vinculos = await _dbContext.Vinculos.IgnoreQueryFilters()
            .Where(v => v.UsuarioId == userId && v.Status == VinculoStatus.Ativo)
            .ToListAsync(cancellationToken);

        var tenantIds = vinculos.Select(v => v.TenantId).ToList();
        var tenants = await _dbContext.Tenants.IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var vinculoIds = vinculos.Select(v => v.Id).ToList();
        var empresasPorVinculo = await _dbContext.VinculoEmpresas.IgnoreQueryFilters()
            .Where(ve => vinculoIds.Contains(ve.VinculoId))
            .Join(_dbContext.Empresas.IgnoreQueryFilters(), ve => ve.EmpresaId, e => e.Id,
                (ve, e) => new { ve.VinculoId, e.Id, e.RazaoSocial })
            .ToListAsync(cancellationToken);

        var contextos = vinculos
            .Where(v => tenants.ContainsKey(v.TenantId))
            .Select(v => new ContextoDisponivel(
                v.TenantId,
                tenants[v.TenantId].Name,
                empresasPorVinculo
                    .Where(e => e.VinculoId == v.Id)
                    .Select(e => new EmpresaDisponivel(e.Id, e.RazaoSocial))
                    .ToList()))
            .ToList();

        return Ok(contextos);
    }

    [HttpPost("set-password")]
    [AllowAnonymous]
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
