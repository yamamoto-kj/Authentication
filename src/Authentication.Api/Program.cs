using Authentication.Api.Extensions;
using Authentication.Api.Middleware;
using Authentication.Application;
using Authentication.Domain.Common;
using Authentication.Infrastructure;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.MultiTenancy;
using Authentication.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Logging: structured, environment-enriched, single sink of truth.
// ---------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ---------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Every final token carries tenant_id and empresa_id together (see
    // AuthorizationController.HandleTenantSelectionAsync) - requiring both
    // here is redundant in practice but cheap defense-in-depth against a
    // hand-crafted or future token shape that only sets one.
    options.AddPolicy("RequireTenant", policy => policy
        .RequireClaim(TenantClaimTypes.TenantId)
        .RequireClaim(TenantClaimTypes.EmpresaId));

    // Selection-scoped: intermediate token issued by the password grant
    // when the user has one or more active Vinculo, before a specific
    // tenant/empresa is chosen. Good for exactly two endpoints:
    // GET /auth/contexts and POST /auth/select-context.
    options.AddPolicy("TenantSelection", policy => policy
        .RequireClaim(SelectionClaimTypes.Purpose, SelectionClaimTypes.TenantSelectionPurpose));

    // Platform-level: no tenant context, only used by cross-tenant admin
    // operations (e.g. provisioning a brand-new Usuario).
    options.AddPolicy(PlatformRoles.PlatformAdmin, policy =>
        policy.RequireClaim(PlatformClaimTypes.PlatformRole, PlatformRoles.PlatformAdmin));

    // Permission-scoped: caller must be in a specific tenant (RequireTenant)
    // AND their TenantRole must grant the named Permission - read from the
    // "permissions" claim resolved once at token-issuance time (see
    // AuthorizationController.GetPermissionKeysAsync), not looked up
    // per-request. One named policy per entry in PermissionKeys.
    options.AddPolicy(PermissionKeys.ProdutosGerenciar, policy => policy
        .RequireClaim(TenantClaimTypes.TenantId)
        .RequireClaim(PermissionClaimTypes.Permission, PermissionKeys.ProdutosGerenciar));

    options.AddPolicy(PermissionKeys.UsuariosConvidar, policy => policy
        .RequireClaim(TenantClaimTypes.TenantId)
        .RequireClaim(PermissionClaimTypes.Permission, PermissionKeys.UsuariosConvidar));
});

builder.Services.AddControllers();
builder.Services.AddApiSwagger();
builder.Services.AddApiRateLimiting(builder.Configuration);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "auth-api:";
    });
}
else
{
    // Falls back to an in-process cache for local/dev single-instance runs;
    // production must configure ConnectionStrings:Redis so idempotency and
    // rate-limit state are shared across every instance behind the LB.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services
    .AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default") ?? string.Empty, name: "postgres", tags: new[] { "ready" });

builder.Services.AddHttpClient("resilient")
    .AddPolicyHandler((sp, _) => Authentication.Infrastructure.Resilience.ResiliencePolicies.Timeout())
    .AddPolicyHandler((sp, _) => Authentication.Infrastructure.Resilience.ResiliencePolicies.RetryWithJitter(
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpClient.Retry")))
    .AddPolicyHandler((sp, _) => Authentication.Infrastructure.Resilience.ResiliencePolicies.CircuitBreaker(
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("HttpClient.CircuitBreaker")));

var app = builder.Build();

// ---------------------------------------------------------------------
// Development-only conveniences
// ---------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    await DbSeeder.SeedAsync(scope.ServiceProvider);
}
else
{
    app.UseHsts();
}

// ---------------------------------------------------------------------
// Middleware pipeline (order matters)
// ---------------------------------------------------------------------
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
