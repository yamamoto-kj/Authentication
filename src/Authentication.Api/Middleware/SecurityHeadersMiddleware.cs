namespace Authentication.Api.Middleware;

/// <summary>
/// Defense-in-depth response headers for an API that is never expected to
/// render HTML but may still be probed by browsers (CORS preflights,
/// Swagger UI in non-prod). Complements, not replaces, HSTS/UseHsts.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // Swagger UI (dev-only) needs inline scripts/styles; every other
        // endpoint returns JSON only, so it can be locked down fully.
        headers["Content-Security-Policy"] = context.Request.Path.StartsWithSegments("/swagger")
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:"
            : "default-src 'none'; frame-ancestors 'none'";
        headers.Remove("X-Powered-By");
        headers.Remove("Server");

        return _next(context);
    }
}
