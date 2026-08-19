using Authentication.Domain.Entities;
using Authentication.Domain.Enums;
using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Authentication.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds a demo tenant, OAuth client and user
/// so the API is exercisable immediately after `docker compose up`. Runs
/// only when explicitly invoked (see Program.cs) - never automatically in
/// production, and it is idempotent so it is safe to run on every startup
/// of a Development instance.
/// </summary>
public static class DbSeeder
{
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
                    Permissions.Prefixes.Scope + "api"
                }
            });
            logger.LogInformation("Seeded demo first-party (password grant) OAuth client");
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByNameAsync("demo@demo-tenant.local") is null)
        {
            var user = new ApplicationUser
            {
                UserName = "demo@demo-tenant.local",
                Email = "demo@demo-tenant.local",
                EmailConfirmed = true,
                TenantId = tenant.Id,
                DisplayName = "Demo User",
                IsActive = true
            };

            var result = await userManager.CreateAsync(user, "ChangeMe!2026#Secure");
            if (result.Succeeded)
            {
                logger.LogInformation("Seeded demo user for tenant {TenantId}", tenant.Id);
            }
            else
            {
                logger.LogWarning("Failed to seed demo user: {Errors}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
