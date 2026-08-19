using Authentication.Application.Common.Interfaces;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Authentication.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<AuditableEntitySaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Default"),
                npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null));

            options.AddInterceptors(sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>());

            // Never leak parameter values into logs/exceptions outside Development.
            options.EnableSensitiveDataLogging(environment.IsDevelopment());

            options.UseOpenIddict<Guid>();
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Password/account-lockout policy: OWASP ASVS-aligned defaults.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOpenIddict()
            .AddCore(options =>
            {
                // Token/authorization/application data lives in Postgres via
                // this DbContext, so any API instance behind the load
                // balancer sees the same state - no sticky sessions needed.
                options.UseEntityFrameworkCore()
                    .UseDbContext<ApplicationDbContext>()
                    .ReplaceDefaultEntities<Guid>();
            })
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token");

                options.AllowClientCredentialsFlow();
                options.AllowPasswordFlow();
                options.AllowRefreshTokenFlow();

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(14));

                // Rolling refresh tokens (each refresh issues a new refresh
                // token and revokes the previous one, limiting the blast
                // radius of a leaked token) are OpenIddict's default - no
                // opt-in call needed; DisableRollingRefreshTokens would opt out.

                var signingCertPath = configuration["OpenIddict:SigningCertificate:Path"];
                var encryptionCertPath = configuration["OpenIddict:EncryptionCertificate:Path"];

                if (!string.IsNullOrEmpty(signingCertPath) && !string.IsNullOrEmpty(encryptionCertPath))
                {
                    var password = configuration["OpenIddict:SigningCertificate:Password"];
                    options.AddSigningCertificate(new System.Security.Cryptography.X509Certificates.X509Certificate2(signingCertPath, password));
                    options.AddEncryptionCertificate(new System.Security.Cryptography.X509Certificates.X509Certificate2(encryptionCertPath, password));
                }
                else
                {
                    // Ephemeral dev-only keys; production MUST supply real
                    // certificates above (see appsettings + README).
                    options.AddDevelopmentEncryptionCertificate();
                    options.AddDevelopmentSigningCertificate();
                }

                var aspNetCoreBuilder = options.UseAspNetCore().EnableTokenEndpointPassthrough();

                if (environment.IsDevelopment())
                {
                    // Local HTTP-only dev loop only; every other environment
                    // must terminate TLS in front of this API.
                    aspNetCoreBuilder.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }
}
