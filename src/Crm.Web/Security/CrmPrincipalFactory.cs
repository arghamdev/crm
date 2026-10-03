using System.Globalization;
using System.Security.Claims;
using Crm.Application.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Crm.Web.Security;

public static class CrmPrincipalFactory
{
    public static ClaimsPrincipal Create(SignInResult result, DateTimeOffset authTimeUtc)
    {
        if (!result.Succeeded || result.User is null || result.SessionId is null)
            throw new InvalidOperationException("A successful sign-in result is required.");
        var claims = new[]
        {
            new Claim("crm_user_id", result.User.Id.ToString()),
            new Claim("session_id", result.SessionId.Value.ToString()),
            new Claim("security_version", result.User.SecurityVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim("display_name", result.User.DisplayName),
            new Claim("auth_time", authTimeUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme,
            "display_name", ClaimsIdentity.DefaultRoleClaimType));
    }
}
