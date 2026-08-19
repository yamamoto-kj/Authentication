using Authentication.Api.Contracts.Admin;
using Authentication.Application.Common.Interfaces;
using Authentication.Domain.Common;
using Authentication.Domain.Entities;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Controllers;

/// <summary>
/// Tenant-scoped membership management: an "admin" TenantRole holder in a
/// given Grupo Econômico can grant an *already existing* Usuario (by CPF)
/// access to their own tenant - unlike AdminUsuariosController, this never
/// creates a brand-new global identity, only a new Vinculo. TenantId always
/// comes from the caller's own token (ITenantProvider), never from the
/// request body - a tenant admin can only ever invite people into their
/// own tenant, not any other.
/// </summary>
[ApiController]
[Route("admin/vinculos")]
[Authorize(Policy = "TenantAdmin")]
public class VinculosController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;

    public VinculosController(
        UserManager<ApplicationUser> userManager, IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CriarVinculoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CriarVinculoResponse>> Criar(
        [FromBody] CriarVinculoRequest request, CancellationToken cancellationToken)
    {
        var cpf = CpfValidator.Normalize(request.Cpf);
        if (cpf is null)
        {
            return BadRequest(new { error = "CPF inválido." });
        }

        if (request.EmpresaIds.Count == 0)
        {
            return BadRequest(new { error = "É necessário informar ao menos uma empresa." });
        }

        var tenantId = _tenantProvider.TenantId;

        var user = await _userManager.FindByNameAsync(cpf);
        if (user is null)
        {
            return NotFound(new { error = "Nenhum usuário cadastrado com este CPF. Solicite a um administrador de plataforma o cadastro inicial." });
        }

        // Standard tenant query filters apply here (unlike the platform-admin
        // endpoint) because a tenant IS resolved for this request - the
        // caller's own token proves they belong to it.
        var tenantRole = await _dbContext.TenantRoles
            .FirstOrDefaultAsync(r => r.Id == request.TenantRoleId, cancellationToken);
        if (tenantRole is null)
        {
            return BadRequest(new { error = "Papel (role) informado não pertence ao seu grupo econômico." });
        }

        var empresas = await _dbContext.Empresas
            .Where(e => request.EmpresaIds.Contains(e.Id))
            .ToListAsync(cancellationToken);
        if (empresas.Count != request.EmpresaIds.Count)
        {
            return BadRequest(new { error = "Uma ou mais empresas informadas não pertencem ao seu grupo econômico." });
        }

        var existing = await _dbContext.Vinculos
            .FirstOrDefaultAsync(v => v.UsuarioId == user.Id && v.TenantId == tenantId, cancellationToken);
        if (existing is not null)
        {
            return Conflict(new { error = "Este usuário já possui um vínculo com o seu grupo econômico." });
        }

        var vinculo = new Vinculo
        {
            UsuarioId = user.Id,
            TenantId = tenantId,
            TenantRoleId = tenantRole.Id,
            Status = VinculoStatus.Ativo,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        _dbContext.Vinculos.Add(vinculo);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var empresa in empresas)
        {
            _dbContext.VinculoEmpresas.Add(new VinculoEmpresa { VinculoId = vinculo.Id, EmpresaId = empresa.Id });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new CriarVinculoResponse(vinculo.Id));
    }
}
