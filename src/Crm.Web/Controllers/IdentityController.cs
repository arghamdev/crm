using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Identity;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Administration.Manage")]
public sealed class IdentityController(
    IIdentityApplicationService identity,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/identity/users")]
    public IActionResult Index(string? q = null, UserStatus? status = null, string? scope = null) =>
        View(FilterUsers(q, status, scope));

    [HttpGet("/identity/users/table")]
    public IActionResult Table(string? q = null, UserStatus? status = null, string? scope = null) =>
        PartialView("_Table", FilterUsers(q, status, scope));

    [HttpGet("/identity/users/create")]
    public IActionResult Create() => PartialView("_Form", new CreatePendingUserCommand("", "", "", ""));

    [HttpPost("/identity/users/create")]
    public IActionResult Create(CreatePendingUserCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName)) ModelState.AddModelError(nameof(command.DisplayName), "نام نمایشی الزامی است.");
        if (string.IsNullOrWhiteSpace(command.UserName)) ModelState.AddModelError(nameof(command.UserName), "نام کاربری الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains('@')) ModelState.AddModelError(nameof(command.Email), "ایمیل معتبر الزامی است.");
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        try
        {
            identity.CreatePendingUser(command, User.CrmUserId(), HttpContext.ToIdentityRequestContext());
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        Response.Trigger("userChanged", "کاربر در انتظار فعال‌سازی ایجاد شد.");
        return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
    }

    [HttpGet("/identity/users/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var model = await identity.GetUserAsync(id, cancellationToken);
        ViewBag.Organization = organization.GetCurrent(User.CrmUserId(), User.CrmSessionId());
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("/identity/users/{id:guid}/status")]
    public IActionResult ChangeStatus(Guid id, UserStatus status, long expectedSecurityVersion)
    {
        try
        {
            identity.ChangeStatus(id, status, expectedSecurityVersion, User.CrmUserId(), HttpContext.ToIdentityRequestContext());
        }
        catch (InvalidOperationException exception)
        {
            TempData["IdentityError"] = exception.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/identity/users/{id:guid}/roles")]
    public IActionResult AssignRole(Guid id, AssignRoleCommand command)
    {
        try
        {
            identity.AssignRole(id, command, User.CrmUserId(), HttpContext.ToIdentityRequestContext());
            TempData["IdentityMessage"] = "نقش و دامنه دسترسی تخصیص یافت؛ نشست‌های قبلی کاربر باطل شدند.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["IdentityError"] = exception.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/identity/users/{id:guid}/roles/{assignmentId:guid}/revoke")]
    public IActionResult RevokeRole(Guid id, Guid assignmentId, long expectedSecurityVersion)
    {
        try
        {
            identity.RevokeRole(id, assignmentId, expectedSecurityVersion, User.CrmUserId(), HttpContext.ToIdentityRequestContext());
        }
        catch (InvalidOperationException exception)
        {
            TempData["IdentityError"] = exception.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/identity/users/{id:guid}/sessions/{sessionId:guid}/revoke")]
    public IActionResult RevokeSession(Guid id, Guid sessionId)
    {
        identity.RevokeSession(id, sessionId, User.CrmUserId(), HttpContext.ToIdentityRequestContext());
        return RedirectToAction(nameof(Details), new { id });
    }

    private IReadOnlyList<UserDto> FilterUsers(string? query, UserStatus? status, string? scope)
    {
        IEnumerable<UserDto> users = identity.GetUsers();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            users = users.Where(x => x.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.UserName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (status is not null) users = users.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(scope)) users = users.Where(x => x.Scopes.Any(value => value.Contains(scope, StringComparison.OrdinalIgnoreCase)));
        return users.ToList();
    }
}
