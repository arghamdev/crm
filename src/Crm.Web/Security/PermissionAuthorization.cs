using System.Security.Claims;
using Crm.Application.Abstractions;
using Crm.Application.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Crm.Web.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(
    IAccessSnapshotService access,
    ICurrentUserContext currentUser,
    IIdentityApplicationService identity,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var rawUserId = context.User.FindFirstValue("crm_user_id");
        if (Guid.TryParse(rawUserId, out var userId))
        {
            if (currentUser.SelectedCompanyId is { } companyId && access.HasPermission(userId, companyId, requirement.Permission))
                context.Succeed(requirement);
            else if (httpContextAccessor.HttpContext is { } httpContext)
                identity.RecordAccessDenied(userId, requirement.Permission, httpContext.ToIdentityRequestContext());
        }
        return Task.CompletedTask;
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return base.GetPolicyAsync(policyName);

        var permission = policyName[Prefix.Length..];
        var policy = new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
