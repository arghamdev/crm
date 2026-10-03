using System.Security.Claims;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Crm.Web.Security;

public sealed class CrmOpenIdConnectEvents(IIdentityApplicationService identity) : OpenIdConnectEvents
{
    public override Task TokenValidated(TokenValidatedContext context)
    {
        var source = context.Principal!;
        var emailVerified = bool.TryParse(source.FindFirstValue("email_verified"), out var verified) && verified;
        var descriptor = new ExternalIdentityDescriptor(
            "CorporateOidc",
            source.FindFirstValue("iss") ?? context.SecurityToken.Issuer,
            source.FindFirstValue("sub") ?? string.Empty,
            source.FindFirstValue("email") ?? source.FindFirstValue("preferred_username"),
            emailVerified);
        var result = identity.AuthenticateExternal(descriptor, context.HttpContext.ToIdentityRequestContext());
        if (!result.Succeeded)
        {
            context.Fail(result.FailureReason.ToString());
            return Task.CompletedTask;
        }
        var nowUtc = DateTimeOffset.UtcNow;
        var properties = context.Properties ??
            throw new InvalidOperationException("OIDC authentication properties are required.");
        var authTime = long.TryParse(source.FindFirstValue("auth_time"), out var unixAuthTime) &&
                       unixAuthTime >= 0 && unixAuthTime <= nowUtc.AddMinutes(5).ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds(unixAuthTime)
            : nowUtc;
        context.Principal = CrmPrincipalFactory.Create(result, authTime);
        properties.IsPersistent = false;
        properties.AllowRefresh = false;
        properties.ExpiresUtc = result.AbsoluteExpiresAtUtc;
        if (result.RequiresOrganizationSelection)
        {
            var target = properties.RedirectUri;
            if (string.IsNullOrWhiteSpace(target) || !target.StartsWith('/')) target = "/";
            properties.RedirectUri = $"/context/select?returnUrl={Uri.EscapeDataString(target)}";
        }
        return Task.CompletedTask;
    }

    public override Task RemoteFailure(RemoteFailureContext context)
    {
        context.HandleResponse();
        var failureMessage = context.Failure?.Message ?? string.Empty;
        var reason = Enum.GetNames<SignInFailureReason>()
            .FirstOrDefault(x => failureMessage.Contains(x, StringComparison.Ordinal)) ?? "OidcFailure";
        identity.RecordAuthenticationFailure(reason, context.HttpContext.ToIdentityRequestContext());
        var target = reason == nameof(SignInFailureReason.UserInactive)
            ? "/account/inactive"
            : $"/account/link-error?reason={Uri.EscapeDataString(reason)}";
        context.Response.Redirect(target);
        return Task.CompletedTask;
    }
}
