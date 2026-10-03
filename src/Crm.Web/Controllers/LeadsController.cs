using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Sales;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Lead.Read")]
public sealed class LeadsController(
    ISalesPipelineService pipeline,
    ICrmApplicationService crm,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/leads")]
    public IActionResult Index(string? q = null, LeadStatus? status = null, bool includeClosed = false) =>
        View(pipeline.GetLeads(current.CrmUserId, current.RequiredOrganization(), q, status, includeClosed));

    [HttpGet("/leads/table")]
    public IActionResult Table(string? q = null, LeadStatus? status = null, bool includeClosed = false) =>
        PartialView("_Table", pipeline.GetLeads(current.CrmUserId, current.RequiredOrganization(), q, status, includeClosed));

    [HttpGet("/leads/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpGet("/leads/create")]
    public IActionResult Create()
    {
        var branchId = PrepareBranches();
        PrepareOwners(branchId);
        return PartialView("_Form", new CreateLeadCommand("", "", "وب‌سایت", "", branchId,
            current.CrmUserId, TerritoryId: current.SelectedTerritoryId));
    }

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpPost("/leads/create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateLeadCommand command)
    {
        ValidateLead(command);
        if (ModelState.IsValid)
        {
            try
            {
                pipeline.CreateLead(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow);
                return Changed("leadChanged", "سرنخ ثبت و برای تماس اولیه زمان‌بندی شد.", nameof(Index));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        PrepareBranches();
        PrepareOwners(command.BranchId);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_Form", command);
    }

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpGet("/leads/owner-options")]
    public IActionResult OwnerOptions(string branchId)
    {
        var owners = pipeline.GetEligibleOwners(current.CrmUserId, current.RequiredOrganization(), branchId);
        ViewBag.SelectedOwnerId = current.CrmUserId;
        return PartialView("_OwnerOptions", owners);
    }

    [Authorize(Policy = "perm:Lead.Assign")]
    [HttpGet("/leads/{id:guid}/assign")]
    public IActionResult Assign(Guid id)
    {
        var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Lead = details.Lead;
        ViewBag.Owners = details.EligibleOwners;
        return PartialView("_AssignForm", new AssignLeadCommand(details.Lead.OwnerUserId ?? Guid.Empty,
            DateTimeOffset.UtcNow.AddHours(4), "تخصیص توسط سرپرست", details.Lead.Version));
    }

    [Authorize(Policy = "perm:Lead.Assign")]
    [HttpPost("/leads/{id:guid}/assign")]
    [ValidateAntiForgeryToken]
    public IActionResult Assign(Guid id, AssignLeadCommand command)
    {
        try
        {
            pipeline.AssignLead(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("leadChanged", "مالک و SLA تماس سرنخ به‌روزرسانی شد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Lead = details.Lead;
            ViewBag.Owners = details.EligibleOwners;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_AssignForm", command);
        }
    }

    [Authorize(Policy = "perm:Lead.Update")]
    [HttpGet("/leads/{id:guid}/transition")]
    public IActionResult Transition(Guid id)
    {
        var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Lead = details.Lead;
        var target = details.Lead.Status switch
        {
            LeadStatus.Contacted => LeadStatus.Qualified,
            LeadStatus.Qualified => LeadStatus.Nurture,
            _ => LeadStatus.Contacted
        };
        return PartialView("_TransitionForm", new TransitionLeadCommand(target,
            Math.Max(60, details.Lead.Score), "پیگیری انجام شد", "تماس پیگیری", DateTimeOffset.UtcNow.AddDays(1), details.Lead.Version));
    }

    [Authorize(Policy = "perm:Lead.Update")]
    [HttpPost("/leads/{id:guid}/transition")]
    [ValidateAntiForgeryToken]
    public IActionResult Transition(Guid id, TransitionLeadCommand command)
    {
        try
        {
            pipeline.TransitionLead(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("leadChanged", "وضعیت سرنخ و تاریخچه آن به‌روزرسانی شد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Lead = details.Lead;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_TransitionForm", command);
        }
    }

    [Authorize(Policy = "perm:Lead.Convert")]
    [HttpGet("/leads/{id:guid}/convert")]
    public IActionResult Convert(Guid id)
    {
        var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        PrepareConversion(details.Lead);
        return PartialView("_ConvertForm", new ConvertLeadCommand($"فرصت همکاری با {details.Lead.Name}",
            2_500_000_000m, DateTimeOffset.UtcNow.AddDays(30), details.Lead.Version));
    }

    [Authorize(Policy = "perm:Lead.Convert")]
    [HttpPost("/leads/{id:guid}/convert")]
    [ValidateAntiForgeryToken]
    public IActionResult Convert(Guid id, ConvertLeadCommand command)
    {
        try
        {
            pipeline.ConvertLead(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("leadChanged", "سرنخ به مشتری و فرصت فروش تبدیل شد.", "Index", controller: "Opportunities");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetLead(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            PrepareConversion(details.Lead);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ConvertForm", command);
        }
    }

    private IActionResult Changed(string eventName, string message, string action, Guid? id = null, string? controller = null)
    {
        Response.Trigger(eventName, message);
        return Request.IsHtmx() ? NoContent() : RedirectToAction(action, controller, id.HasValue ? new { id } : null)!;
    }

    private string PrepareBranches()
    {
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        ViewBag.Branches = context?.Branches ?? [];
        ViewBag.Territories = context?.Territories ?? [];
        return current.SelectedBranchId ?? context?.Branches.FirstOrDefault()?.Id ?? string.Empty;
    }

    private void PrepareOwners(string branchId)
    {
        ViewBag.SelectedOwnerId = current.CrmUserId;
        ViewBag.Owners = string.IsNullOrWhiteSpace(branchId)
            ? Array.Empty<SalesOwnerOptionDto>()
            : pipeline.GetEligibleOwners(current.CrmUserId, current.RequiredOrganization(), branchId);
    }

    private void PrepareConversion(LeadDto lead)
    {
        ViewBag.Lead = lead;
        ViewBag.Customers = crm.GetCustomers(current.CrmUserId, current.RequiredOrganization())
            .Where(x => x.BranchId.Equals(lead.BranchId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ValidateLead(CreateLeadCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name)) ModelState.AddModelError(nameof(command.Name), "نام سرنخ الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Contact) && string.IsNullOrWhiteSpace(command.Phone) && string.IsNullOrWhiteSpace(command.Email))
            ModelState.AddModelError(nameof(command.Contact), "حداقل شخص تماس، تلفن یا ایمیل را وارد کنید.");
        if (string.IsNullOrWhiteSpace(command.BranchId)) ModelState.AddModelError(nameof(command.BranchId), "شعبه الزامی است.");
    }
}
