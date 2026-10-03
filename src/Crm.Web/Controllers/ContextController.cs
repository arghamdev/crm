using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

public sealed class ContextController(IOrganizationContextService organization) : Controller
{
    [HttpGet("/context/select")]
    public IActionResult Select(string? returnUrl = null, string? companyId = null)
    {
        var model = organization.GetForSelection(User.CrmUserId(), User.CrmSessionId(), companyId);
        if (model is null || model.Companies.Count == 0) return Forbid();
        ViewBag.ReturnUrl = SafeReturnUrl(returnUrl);
        return View(model);
    }

    [HttpGet("/context/selector")]
    public IActionResult Selector(string? companyId = null, string? returnUrl = null)
    {
        var model = organization.GetForSelection(User.CrmUserId(), User.CrmSessionId(), companyId);
        if (model is null || model.Companies.Count == 0) return Forbid();
        ViewBag.ReturnUrl = SafeReturnUrl(returnUrl);
        return PartialView("_Selector", model);
    }

    [HttpPost("/context/select")]
    public IActionResult Select(SelectOrganizationContextCommand command)
    {
        var result = organization.Select(User.CrmUserId(), User.CrmSessionId(), command,
            HttpContext.ToIdentityRequestContext());
        var target = SafeReturnUrl(command.ReturnUrl);
        if (!result.Succeeded)
        {
            if (result.Context is null) return Forbid();
            ModelState.AddModelError(string.Empty, result.Message);
            ViewBag.ReturnUrl = target;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Request.IsHtmx() ? PartialView("_Selector", result.Context) : View(result.Context);
        }
        if (Request.IsHtmx())
        {
            Response.Headers.Append("HX-Redirect", target);
            return Ok();
        }
        return LocalRedirect(target);
    }

    private string SafeReturnUrl(string? value) => Url.IsLocalUrl(value) ? value! : "/";
}
