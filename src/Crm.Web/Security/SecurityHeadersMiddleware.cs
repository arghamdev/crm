namespace Crm.Web.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = context.Request.Path.StartsWithSegments("/mobile")
                ? "camera=(), microphone=(), geolocation=(self)" : "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (context.Request.Path.StartsWithSegments("/account") || context.Request.Path.StartsWithSegments("/identity"))
                headers["Cache-Control"] = "no-store, max-age=0";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
