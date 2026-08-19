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
/// Platform-admin-only user provisioning. There is no self-service sign-up
/// anywhere in this API (see the multi-tenant plan) - a Usuario only ever
/// comes into existence here.
///
/// The account is created without a usable password: CreateAsync(user) with
/// no password leaves PasswordHash null, so password-grant login fails
/// until /auth/set-password is called with the reset token this endpoint
/// returns. Returning that token directly in the response is a
/// development-only convenience (there is no email infrastructure yet in
/// this project) - production must deliver it out-of-band (e.g. email) and
/// never echo it back over HTTP.
/// </summary>
[ApiController]
[Route("admin/usuarios")]
[Authorize(Policy = PlatformRoles.PlatformAdmin)]
public class AdminUsuariosController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _dbContext;

    public AdminUsuariosController(UserManager<ApplicationUser> userManager, IApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CriarUsuarioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CriarUsuarioResponse>> Criar(
        [FromBody] CriarUsuarioRequest request, CancellationToken cancellationToken)
    {
        var cpf = CpfValidator.Normalize(request.Cpf);
        if (cpf is null)
        {
            return BadRequest(new { error = "CPF inválido." });
        }

        if (string.IsNullOrWhiteSpace(request.NomePrimeiro) || string.IsNullOrWhiteSpace(request.NomeUltimo))
        {
            return BadRequest(new { error = "Nome (primeiro e último) é obrigatório." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { error = "E-mail é obrigatório." });
        }

        if (await _userManager.FindByNameAsync(cpf) is not null)
        {
            return Conflict(new { error = "Já existe um usuário cadastrado com este CPF." });
        }

        Tenant? tenant = null;
        TenantRole? tenantRole = null;
        List<Empresa>? empresas = null;

        if (request.VinculoInicial is { } vinculoInicial)
        {
            if (vinculoInicial.EmpresaIds.Count == 0)
            {
                return BadRequest(new { error = "É necessário informar ao menos uma empresa para o vínculo inicial." });
            }

            // IgnoreQueryFilters: platform admin has no resolved tenant on
            // this DbContext, so the standard tenant filters would hide
            // every row regardless of the ids requested.
            tenant = await _dbContext.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == vinculoInicial.TenantId && !t.IsDeleted, cancellationToken);
            if (tenant is null)
            {
                return BadRequest(new { error = "Grupo econômico (tenant) informado não existe." });
            }

            tenantRole = await _dbContext.TenantRoles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    r => r.Id == vinculoInicial.TenantRoleId && r.TenantId == tenant.Id && !r.IsDeleted,
                    cancellationToken);
            if (tenantRole is null)
            {
                return BadRequest(new { error = "Papel (role) informado não pertence a este grupo econômico." });
            }

            empresas = await _dbContext.Empresas.IgnoreQueryFilters()
                .Where(e => vinculoInicial.EmpresaIds.Contains(e.Id) && e.TenantId == tenant.Id && !e.IsDeleted)
                .ToListAsync(cancellationToken);
            if (empresas.Count != vinculoInicial.EmpresaIds.Count)
            {
                return BadRequest(new { error = "Uma ou mais empresas informadas não pertencem a este grupo econômico." });
            }
        }

        var user = new ApplicationUser
        {
            UserName = cpf,
            Email = request.Email,
            EmailConfirmed = false,
            NomePrimeiro = request.NomePrimeiro,
            NomeMeio = request.NomeMeio,
            NomeUltimo = request.NomeUltimo,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        // No password: CreateAsync(user) alone leaves the account
        // unusable for password login until /auth/set-password completes it.
        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(e => e.Description) });
        }

        if (tenant is not null && tenantRole is not null && empresas is not null)
        {
            var vinculo = new Vinculo
            {
                UsuarioId = user.Id,
                TenantId = tenant.Id,
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
        }

        var setPasswordToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        // No GET-by-id endpoint exists yet to point a Location header at,
        // so this returns 201 with the resource in the body directly
        // instead of using CreatedAtAction.
        return StatusCode(StatusCodes.Status201Created, new CriarUsuarioResponse(user.Id, cpf, setPasswordToken));
    }
}
