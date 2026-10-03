using Crm.Application.Abstractions;

namespace Crm.Web.Security;

public sealed class OrganizationContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ICurrentUserContext currentUser)
    {
        if (context.User.Identity?.IsAuthenticated == true && RequiresContext(context.Request.Path) &&
            currentUser.SelectedCompanyId is null)
        {
            var returnUrl = $"{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            var target = "/context/select?returnUrl=" + Uri.EscapeDataString(returnUrl);
            if (context.Request.Headers.ContainsKey("HX-Request"))
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.Headers.Append("HX-Redirect", target);
                return;
            }
            context.Response.Redirect(target);
            return;
        }
        await next(context);
    }

    private static bool RequiresContext(PathString path) =>
        !path.StartsWithSegments("/context") &&
        !path.StartsWithSegments("/account") &&
        !path.StartsWithSegments("/signin-oidc") &&
        !path.StartsWithSegments("/health") &&
        !path.StartsWithSegments("/css") &&
        !path.StartsWithSegments("/js") &&
        !path.StartsWithSegments("/images") &&
        !path.StartsWithSegments("/favicon.ico");
}
