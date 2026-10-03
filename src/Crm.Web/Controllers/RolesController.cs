using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Administration.Manage")]
public sealed class RolesController(IRoleAdministrationService roles, ICurrentUserContext current) : Controller
{
    [HttpGet("/identity/roles")]
    public IActionResult Index() =>
        View(roles.GetRoles(current.CrmUserId, current.RequiredOrganization().CompanyId, DateTimeOffset.UtcNow));

    [HttpGet("/identity/roles/{roleKey}")]
    public IActionResult Edit(string roleKey)
    {
        var model = roles.GetRole(current.CrmUserId, current.RequiredOrganization().CompanyId, roleKey, DateTimeOffset.UtcNow);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("/identity/roles/{roleKey}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(string roleKey, [FromForm] List<string>? permissions, [FromForm] string? reason, [FromForm] long expectedVersion)
    {
        var companyId = current.RequiredOrganization().CompanyId;
        try
        {
            var result = roles.UpdatePermissions(current.CrmUserId, companyId, roleKey,
                new UpdateRolePermissionsCommand(permissions, reason, expectedVersion), HttpContext.ToIdentityRequestContext());
            TempData["RoleMessage"] = result.Added.Count == 0 && result.Removed.Count == 0
                ? "تغییری در مجوزها ایجاد نشد."
                : $"{result.Added.Count} مجوز افزوده و {result.Removed.Count} مجوز حذف شد؛ دسترسی {result.AffectedUsers} کاربر فوراً به‌روز شد.";
            return Redirect($"/identity/roles/{Uri.EscapeDataString(roleKey)}");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            var model = roles.GetRole(current.CrmUserId, companyId, roleKey, DateTimeOffset.UtcNow);
            if (model is null) return NotFound();
            ModelState.AddModelError(string.Empty, exception.Message);
            ViewBag.Reason = reason;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            // Show the user's unsaved selection, not the stored one, so nothing they ticked is lost.
            return View(model with { Permissions = (permissions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase) });
        }
    }
}
