using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Service;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Service.Read")]
public sealed class ServiceCasesController(IServiceCaseService service, ICurrentUserContext current) : Controller
{
    [HttpGet("/service")]
    public async Task<IActionResult> Index(string? q = null, ServiceCaseStatus? status = null, ServiceCasePriority? priority = null,
        bool includeClosed = false, bool mine = false, int page = 1, CancellationToken cancellationToken = default) =>
        View(await service.GetCasesAsync(current.CrmUserId, current.RequiredOrganization(), q, status, priority, includeClosed, mine, page,
            cancellationToken: cancellationToken));

    [HttpGet("/service/table")]
    public async Task<IActionResult> Table(string? q = null, ServiceCaseStatus? status = null, ServiceCasePriority? priority = null,
        bool includeClosed = false, bool mine = false, int page = 1, CancellationToken cancellationToken = default) =>
        PartialView("_Table", await service.GetCasesAsync(current.CrmUserId, current.RequiredOrganization(), q, status, priority, includeClosed,
            mine, page, cancellationToken: cancellationToken));

    [HttpGet("/service/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = service.GetCase(current.CrmUserId, current.RequiredOrganization(), id);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Service.Create")]
    [HttpGet("/service/create")]
    public IActionResult Create(Guid? customerId = null)
    {
        ViewBag.Customers = service.GetCustomerOptions(current.CrmUserId, current.RequiredOrganization());
        return PartialView("_Form", new CreateServiceCaseCommand(customerId ?? Guid.Empty, "", "",
            ServiceCaseCategory.Complaint, ServiceCaseChannel.Phone, ServiceCasePriority.Medium));
    }

    [Authorize(Policy = "perm:Service.Create")]
    [HttpPost("/service/create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateServiceCaseCommand command)
    {
        if (command.CustomerId == Guid.Empty) ModelState.AddModelError(nameof(command.CustomerId), "مشتری الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Subject)) ModelState.AddModelError(nameof(command.Subject), "موضوع پرونده الزامی است.");
        if (ModelState.IsValid)
        {
            try
            {
                var created = service.CreateCase(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow);
                return Changed($"پرونده {created.Code} ثبت شد و مهلت SLA آن آغاز شد.", created.Id);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        ViewBag.Customers = service.GetCustomerOptions(current.CrmUserId, current.RequiredOrganization());
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_Form", command);
    }

    [Authorize(Policy = "perm:Service.Triage")]
    [HttpGet("/service/{id:guid}/triage")]
    public IActionResult Triage(Guid id)
    {
        var details = service.GetCase(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        PrepareTriage(details);
        return PartialView("_TriageForm", new TriageServiceCaseCommand(details.Case.Priority,
            details.Case.OwnerUserId ?? Guid.Empty, null, details.Case.Version));
    }

    [Authorize(Policy = "perm:Service.Triage")]
    [HttpPost("/service/{id:guid}/triage")]
    [ValidateAntiForgeryToken]
    public IActionResult Triage(Guid id, TriageServiceCaseCommand command)
    {
        try
        {
            service.TriageCase(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("تریاژ پرونده ثبت شد.", id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = service.GetCase(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            PrepareTriage(details);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_TriageForm", command);
        }
    }

    [Authorize(Policy = "perm:Service.Update")]
    [HttpGet("/service/{id:guid}/action")]
    public IActionResult Action(Guid id, ServiceCaseAction action)
    {
        var details = service.GetCase(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Case = details.Case;
        return PartialView("_ActionForm", new ServiceCaseActionCommand(action, null, details.Case.RootCause,
            details.Case.CorrectiveAction, details.Case.Resolution, action == ServiceCaseAction.Close ? 5 : null, details.Case.Version));
    }

    [Authorize(Policy = "perm:Service.Update")]
    [HttpPost("/service/{id:guid}/action")]
    [ValidateAntiForgeryToken]
    public IActionResult Action(Guid id, ServiceCaseActionCommand command)
    {
        try
        {
            service.ApplyAction(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("وضعیت پرونده و تاریخچه آن به‌روزرسانی شد.", id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = service.GetCase(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Case = details.Case;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ActionForm", command);
        }
    }

    [Authorize(Policy = "perm:Service.Triage")]
    [HttpPost("/service/escalate")]
    [ValidateAntiForgeryToken]
    public IActionResult Escalate()
    {
        try
        {
            var result = service.RunEscalation(current.CrmUserId, current.RequiredOrganization(), DateTimeOffset.UtcNow);
            var message = result.EscalatedCases == 0
                ? "پرونده‌ای نیازمند ارجاع جدید نبود."
                : $"{result.EscalatedCases} پرونده ارجاع شد و {result.WorkItemsCreated} اقدام در کارتابل ثبت شد.";
            Response.Trigger("serviceChanged", message);
            return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private IActionResult Changed(string message, Guid id)
    {
        Response.Trigger("serviceChanged", message);
        return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Details), new { id });
    }

    private void PrepareTriage(ServiceCaseDetailsDto details)
    {
        ViewBag.Case = details.Case;
        ViewBag.Owners = details.EligibleOwners;
    }
}
