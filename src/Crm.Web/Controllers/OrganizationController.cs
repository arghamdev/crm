using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Organization;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Administration.Manage")]
public sealed class OrganizationController(
    IOrganizationAdminService organization,
    ICurrentUserContext currentUser) : Controller
{
    [HttpGet("/organization")]
    public IActionResult Index() => View(Load());

    [HttpGet("/organization/workspace")]
    public IActionResult Workspace() => PartialView("_Workspace", Load());

    [HttpGet("/organization/company/edit")]
    public IActionResult EditCompany()
    {
        var companyId = CompanyId();
        ViewBag.CompanyId = companyId;
        return PartialView("_CompanyForm", organization.GetCompanyForm(companyId, currentUser.CrmUserId));
    }

    [HttpPost("/organization/company/edit")]
    public IActionResult EditCompany(UpdateCompanyCommand command)
    {
        var companyId = CompanyId();
        ValidateCompany(command);
        if (ModelState.IsValid)
        {
            try
            {
                organization.UpdateCompany(companyId, command, currentUser.CrmUserId, HttpContext.ToIdentityRequestContext());
                return Changed("organizationChanged", "مشخصات شرکت به‌روزرسانی شد.");
            }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        ViewBag.CompanyId = companyId;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_CompanyForm", command);
    }

    [HttpGet("/organization/units/create")]
    public IActionResult CreateUnit(OrganizationUnitType type = OrganizationUnitType.Branch) =>
        PartialView("_UnitForm", organization.GetUnitForm(CompanyId(), null, type, currentUser.CrmUserId));

    [HttpGet("/organization/units/{id:guid}/edit")]
    public IActionResult EditUnit(Guid id)
    {
        try { return PartialView("_UnitForm", organization.GetUnitForm(CompanyId(), id, null, currentUser.CrmUserId)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("/organization/units/save")]
    public IActionResult SaveUnit(Guid? id, SaveOrganizationUnitCommand command)
    {
        var companyId = CompanyId();
        ValidateUnit(command);
        if (ModelState.IsValid)
        {
            try
            {
                organization.SaveUnit(companyId, id, command, currentUser.CrmUserId, HttpContext.ToIdentityRequestContext());
                return Changed("organizationChanged", id is null ? "واحد سازمانی ایجاد شد." : "واحد سازمانی ویرایش شد.");
            }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
            catch (KeyNotFoundException) { return NotFound(); }
        }
        var model = organization.GetUnitForm(companyId, id, command.Type, currentUser.CrmUserId) with
        {
            UnitId = command.UnitId,
            Code = command.Code,
            Name = command.Name,
            Type = command.Type,
            ParentUnitId = command.ParentUnitId,
            ExpectedVersion = command.ExpectedVersion
        };
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_UnitForm", model);
    }

    [HttpPost("/organization/units/{id:guid}/status")]
    public IActionResult SetUnitStatus(Guid id, OrganizationStatus status, long expectedVersion)
    {
        try
        {
            organization.SetUnitStatus(CompanyId(), id, status, expectedVersion, currentUser.CrmUserId, HttpContext.ToIdentityRequestContext());
            return Changed("organizationChanged", "وضعیت واحد سازمانی تغییر کرد.");
        }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpGet("/organization/territories/create")]
    public IActionResult CreateTerritory() => PartialView("_TerritoryForm",
        organization.GetTerritoryForm(CompanyId(), null, currentUser.CrmUserId, DateTimeOffset.UtcNow));

    [HttpGet("/organization/territories/{id:guid}/edit")]
    public IActionResult EditTerritory(Guid id)
    {
        try { return PartialView("_TerritoryForm", organization.GetTerritoryForm(CompanyId(), id, currentUser.CrmUserId, DateTimeOffset.UtcNow)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("/organization/territories/save")]
    public IActionResult SaveTerritory(Guid? id, SaveTerritoryCommand command)
    {
        var companyId = CompanyId();
        ValidateTerritory(command);
        if (ModelState.IsValid)
        {
            try
            {
                organization.SaveTerritory(companyId, id, command, currentUser.CrmUserId, HttpContext.ToIdentityRequestContext());
                return Changed("organizationChanged", id is null ? "قلمرو ایجاد شد." : "قلمرو ویرایش شد.");
            }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
            catch (KeyNotFoundException) { return NotFound(); }
        }
        var model = new TerritoryFormDto(companyId, id, command.TerritoryId, command.Code, command.Name,
            command.Dimension, command.ValidFromUtc, command.ValidToUtc, command.ExpectedVersion);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_TerritoryForm", model);
    }

    [HttpPost("/organization/territories/{id:guid}/status")]
    public IActionResult SetTerritoryStatus(Guid id, OrganizationStatus status, long expectedVersion)
    {
        try
        {
            organization.SetTerritoryStatus(CompanyId(), id, status, expectedVersion, currentUser.CrmUserId, HttpContext.ToIdentityRequestContext());
            return Changed("organizationChanged", "وضعیت قلمرو تغییر کرد.");
        }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private OrganizationAdminDto Load() => organization.Get(CompanyId(), currentUser.CrmUserId, DateTimeOffset.UtcNow);
    private string CompanyId() => currentUser.SelectedCompanyId ?? throw new InvalidOperationException("ابتدا شرکت را انتخاب کنید.");

    private IActionResult Changed(string eventName, string message)
    {
        Response.Trigger(eventName, message);
        return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
    }

    private IActionResult OperationError(string message)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return Content($"<div class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(message)}</div>", "text/html; charset=utf-8");
    }

    private void ValidateCompany(UpdateCompanyCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code)) ModelState.AddModelError(nameof(command.Code), "کد الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Name)) ModelState.AddModelError(nameof(command.Name), "نام الزامی است.");
        if (string.IsNullOrWhiteSpace(command.TimeZoneId)) ModelState.AddModelError(nameof(command.TimeZoneId), "منطقه زمانی الزامی است.");
    }

    private void ValidateUnit(SaveOrganizationUnitCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.UnitId)) ModelState.AddModelError(nameof(command.UnitId), "شناسه واحد الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Code)) ModelState.AddModelError(nameof(command.Code), "کد الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Name)) ModelState.AddModelError(nameof(command.Name), "نام الزامی است.");
    }

    private void ValidateTerritory(SaveTerritoryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.TerritoryId)) ModelState.AddModelError(nameof(command.TerritoryId), "شناسه قلمرو الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Code)) ModelState.AddModelError(nameof(command.Code), "کد الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Name)) ModelState.AddModelError(nameof(command.Name), "نام الزامی است.");
        if (command.ValidToUtc is not null && command.ValidToUtc <= command.ValidFromUtc)
            ModelState.AddModelError(nameof(command.ValidToUtc), "پایان اعتبار باید بعد از شروع اعتبار باشد.");
    }
}
