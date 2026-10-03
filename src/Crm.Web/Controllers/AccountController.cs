using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Crm.Web.Controllers;

public sealed class AccountController(IIdentityApplicationService identity, IConfiguration configuration, Crm.Application.Abstractions.IAccessSnapshotService access) : Controller
{
    [AllowAnonymous]
    [HttpGet("/account/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.UseOidc = IsOidc;
        return View(model: returnUrl);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("/account/login")]
    public async Task<IActionResult> Login(string userName, string password, string? returnUrl = null)
    {
        if (IsOidc) return BadRequest("Local password sign-in is disabled in OIDC mode.");
        var result = identity.AuthenticateDemo(userName, password, HttpContext.ToIdentityRequestContext());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            ViewBag.UseOidc = false;
            ViewBag.UserName = userName;
            return View(model: returnUrl);
        }
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = result.AbsoluteExpiresAtUtc
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            CrmPrincipalFactory.Create(result, DateTimeOffset.UtcNow), properties);
        var defaultPath = result.User is { } user && result.SelectedCompanyId is { } company && access.HasPermission(user.Id, company, "Portal.Read") ? "/portal" : "/";
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : defaultPath;
        if (result.RequiresOrganizationSelection)
            return LocalRedirect($"/context/select?returnUrl={Uri.EscapeDataString(target)}");
        return LocalRedirect(target);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("/account/login/oidc")]
    public IActionResult Oidc(string? returnUrl = null)
    {
        if (!IsOidc) return RedirectToAction(nameof(Login), new { returnUrl });
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        return Challenge(new AuthenticationProperties { RedirectUri = target }, OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpPost("/account/logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.CrmUserId();
        var sessionId = User.CrmSessionId();
        if (userId != Guid.Empty && sessionId != Guid.Empty)
            identity.RevokeSession(userId, sessionId, userId, HttpContext.ToIdentityRequestContext());
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (IsOidc)
            return SignOut(new AuthenticationProperties { RedirectUri = "/account/signed-out" }, OpenIdConnectDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(SignedOut));
    }

    [HttpGet("/account/access-denied")]
    public IActionResult AccessDenied() => View();

    [AllowAnonymous]
    [HttpGet("/account/session-expired")]
    public IActionResult SessionExpired() => View();

    [AllowAnonymous]
    [HttpGet("/account/link-error")]
    public IActionResult LinkError(string? reason = null)
    {
        ViewBag.Reason = reason;
        return View();
    }

    [AllowAnonymous]
    [HttpGet("/account/inactive")]
    public IActionResult Inactive() => View();

    [AllowAnonymous]
    [HttpGet("/account/signed-out")]
    public IActionResult SignedOut() => View();

    [HttpGet("/account/security")]
    public IActionResult Security()
    {
        var model = identity.GetUser(User.CrmUserId());
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("/account/security/revoke-others")]
    public IActionResult RevokeOtherSessions()
    {
        var count = identity.RevokeOtherSessions(User.CrmUserId(), User.CrmSessionId(), HttpContext.ToIdentityRequestContext());
        TempData["SecurityMessage"] = $"{count} نشست دیگر باطل شد.";
        return RedirectToAction(nameof(Security));
    }

    private bool IsOidc => string.Equals(configuration["Authentication:Mode"], "Oidc", StringComparison.OrdinalIgnoreCase);
}
