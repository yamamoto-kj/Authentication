using Authentication.Domain.Entities;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Authentication.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds a demo Tenant/Empresa/TenantRole/
/// Vinculo/Usuario so the API is exercisable immediately after
/// `docker compose up`. Runs only when explicitly invoked (see Program.cs) -
/// never automatically in production, and it is idempotent so it is safe to
/// run on every startup of a Development instance.
/// </summary>
public static class DbSeeder
{
    // Commonly used, digits-valid-format demo CPFs (pass the standard
    // check-digit algorithm) - not real people's documents.
    private const string DemoUserCpf = "52998224725";
    private const string PlatformAdminCpf = "11144477735";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        var scopeManager = services.GetRequiredService<IOpenIddictScopeManager>();
        if (await scopeManager.FindByNameAsync("api") is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api",
                DisplayName = "Full API access",
                Resources = { "authentication-api" }
            });
        }

        var tenant = await context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == "demo");
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = "Demo Tenant",
                Slug = "demo",
                Status = TenantStatus.Active,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync(default);
            logger.LogInformation("Seeded demo tenant {TenantId}", tenant.Id);
        }

        var empresa = await context.Empresas.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenant.Id && e.Cnpj == "00000000000191");
        if (empresa is null)
        {
            empresa = new Empresa
            {
                TenantId = tenant.Id,
                RazaoSocial = "Demo Empresa Matriz",
                Cnpj = "00000000000191",
                Status = EmpresaStatus.Ativa,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Empresas.Add(empresa);
            await context.SaveChangesAsync(default);
            logger.LogInformation("Seeded demo empresa {EmpresaId} for tenant {TenantId}", empresa.Id, tenant.Id);
        }

        // A second Empresa in the same tenant so the selection flow's
        // "which empresa" step (POST /auth/select-context with an explicit
        // empresa_id) is actually exercised in Development, not just the
        // single-empresa auto-select path.
        var empresaFilial = await context.Empresas.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TenantId == tenant.Id && e.Cnpj == "00000000000272");
        if (empresaFilial is null)
        {
            empresaFilial = new Empresa
            {
                TenantId = tenant.Id,
                RazaoSocial = "Demo Empresa Filial",
                Cnpj = "00000000000272",
                Status = EmpresaStatus.Ativa,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Empresas.Add(empresaFilial);
            await context.SaveChangesAsync(default);
            logger.LogInformation("Seeded demo empresa filial {EmpresaId} for tenant {TenantId}", empresaFilial.Id, tenant.Id);
        }

        var permission = await context.Permissions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Chave == "produtos:gerenciar");
        if (permission is null)
        {
            permission = new Permission
            {
                Chave = "produtos:gerenciar",
                Descricao = "Criar, editar e remover produtos",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Permissions.Add(permission);
            await context.SaveChangesAsync(default);
        }

        var adminRole = await context.TenantRoles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Nome == "admin");
        if (adminRole is null)
        {
            adminRole = new TenantRole
            {
                TenantId = tenant.Id,
                Nome = "admin",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.TenantRoles.Add(adminRole);
            await context.SaveChangesAsync(default);

            context.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantRoleId = adminRole.Id,
                PermissionId = permission.Id
            });
            await context.SaveChangesAsync(default);
            logger.LogInformation("Seeded demo tenant role {RoleId} (admin) for tenant {TenantId}", adminRole.Id, tenant.Id);
        }

        var applicationManager = services.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applicationManager.FindByClientIdAsync("demo-service-client") is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "demo-service-client",
                ClientSecret = "demo-service-secret-change-me",
                DisplayName = "Demo service client",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.Scope + "api"
                },
                Properties =
                {
                    ["tenant_id"] = System.Text.Json.JsonSerializer.SerializeToElement(tenant.Id.ToString())
                }
            });
            logger.LogInformation("Seeded demo OAuth client for tenant {TenantId}", tenant.Id);
        }

        // Public (no-secret) first-party client for the resource-owner
        // password flow - see AuthorizationController's security notes on
        // why this grant is restricted to trusted first-party clients only.
        if (await applicationManager.FindByClientIdAsync("demo-first-party-client") is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "demo-first-party-client",
                ClientType = ClientTypes.Public,
                DisplayName = "Demo first-party client",
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.Password,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.Prefixes.GrantType + CustomGrantTypes.TenantSelection,
                    Permissions.Prefixes.Scope + "api"
                }
            });
            logger.LogInformation("Seeded demo first-party (password grant) OAuth client");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(DemoUserCpf);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = DemoUserCpf,
                Email = "demo@demo-tenant.local",
                EmailConfirmed = true,
                NomePrimeiro = "Demo",
                NomeUltimo = "User",
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, "ChangeMe!2026#Secure");
            if (!result.Succeeded)
            {
                logger.LogWarning("Failed to seed demo user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Seeded demo usuario {UserId} (CPF login)", user.Id);
        }

        var vinculo = await context.Vinculos
            .FirstOrDefaultAsync(v => v.UsuarioId == user.Id && v.TenantId == tenant.Id);
        if (vinculo is null)
        {
            vinculo = new Vinculo
            {
                UsuarioId = user.Id,
                TenantId = tenant.Id,
                TenantRoleId = adminRole.Id,
                Status = VinculoStatus.Ativo,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            context.Vinculos.Add(vinculo);
            await context.SaveChangesAsync(default);

            context.VinculoEmpresas.Add(new VinculoEmpresa { VinculoId = vinculo.Id, EmpresaId = empresa.Id });
            context.VinculoEmpresas.Add(new VinculoEmpresa { VinculoId = vinculo.Id, EmpresaId = empresaFilial.Id });
            await context.SaveChangesAsync(default);

            logger.LogInformation(
                "Seeded demo vinculo linking usuario {UserId} to tenant {TenantId} as admin", user.Id, tenant.Id);
        }

        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        if (await roleManager.FindByNameAsync(PlatformRoles.PlatformAdmin) is null)
        {
            await roleManager.CreateAsync(new ApplicationRole(PlatformRoles.PlatformAdmin));
        }

        var platformAdmin = await userManager.FindByNameAsync(PlatformAdminCpf);
        if (platformAdmin is null)
        {
            platformAdmin = new ApplicationUser
            {
                UserName = PlatformAdminCpf,
                Email = "platform-admin@demo-tenant.local",
                EmailConfirmed = true,
                NomePrimeiro = "Plataforma",
                NomeUltimo = "Admin",
                IsActive = true
            };

            var result = await userManager.CreateAsync(platformAdmin, "ChangeMe!2026#PlatformAdmin");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(platformAdmin, PlatformRoles.PlatformAdmin);
                logger.LogInformation("Seeded platform admin usuario {UserId} (no Vinculo - global scope)", platformAdmin.Id);
            }
            else
            {
                logger.LogWarning("Failed to seed platform admin: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
