using Microsoft.OpenApi.Models;

namespace Authentication.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Authentication Multi-Tenant API",
                Version = "v1",
                Description = "Multi-tenant REST API secured with OAuth2 (OpenIddict)."
            });

            options.AddSecurityDefinition("OAuth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Password = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri("/connect/token", UriKind.Relative),
                        Scopes = new Dictionary<string, string> { ["api"] = "Full API access" }
                    },
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri("/connect/token", UriKind.Relative),
                        Scopes = new Dictionary<string, string> { ["api"] = "Full API access" }
                    }
                }
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "OAuth2" }
                    },
                    new[] { "api" }
                }
            });
        });

        return services;
    }
}
