using System.Security.Claims;
using Crm.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Crm.Web.Security;

public sealed class HtmxCookieAuthenticationEvents(IIdentityApplicationService identity) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var rawUserId = context.Principal?.FindFirstValue("crm_user_id");
        var rawSessionId = context.Principal?.FindFirstValue("session_id");
        var rawVersion = context.Principal?.FindFirstValue("security_version");
        var isValidId = Guid.TryParse(rawUserId, out var userId);
        var isValidSessionId = Guid.TryParse(rawSessionId, out var sessionId);
        var isValidVersion = long.TryParse(rawVersion, out var cookieVersion);
        var validation = isValidId && isValidSessionId && isValidVersion
            ? identity.ValidateSession(sessionId, userId, cookieVersion, context.HttpContext.ToIdentityRequestContext())
            : null;

        if (validation?.IsValid != true)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Headers.ContainsKey("HX-Request"))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.Headers.Append("HX-Redirect", "/account/session-expired");
            return Task.CompletedTask;
        }
        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Headers.ContainsKey("HX-Request"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return base.RedirectToAccessDenied(context);
    }
}
