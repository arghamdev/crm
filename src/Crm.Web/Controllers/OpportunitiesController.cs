using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Sales;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Opportunity.Read")]
public sealed class OpportunitiesController(
    ISalesPipelineService pipeline,
    ICrmApplicationService crm,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/opportunities")]
    public IActionResult Index(string? q = null, bool includeClosed = false) =>
        View(pipeline.GetPipeline(current.CrmUserId, current.RequiredOrganization(), q, includeClosed));

    [HttpGet("/opportunities/board")]
    public IActionResult Board(string? q = null, bool includeClosed = false) =>
        PartialView("_Board", pipeline.GetPipeline(current.CrmUserId, current.RequiredOrganization(), q, includeClosed));

    [HttpGet("/opportunities/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Opportunity.Create")]
    [HttpGet("/opportunities/create")]
    public IActionResult Create()
    {
        var branchId = PrepareCreateOptions();
        var customers = (IReadOnlyList<CustomerDto>)ViewBag.Customers;
        var owners = (IReadOnlyList<SalesOwnerOptionDto>)ViewBag.Owners;
        return PartialView("_CreateForm", new CreateOpportunityCommand(customers.FirstOrDefault()?.Id ?? Guid.Empty,
            "", 1_000_000_000m, owners.FirstOrDefault()?.UserId ?? current.CrmUserId, branchId,
            current.SelectedTerritoryId, DateTimeOffset.UtcNow.AddDays(30), "مراجعه مستقیم",
            "جلسه کشف نیاز", DateTimeOffset.UtcNow.AddDays(2)));
    }

    [Authorize(Policy = "perm:Opportunity.Create")]
    [HttpPost("/opportunities/create")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateOpportunityCommand command)
    {
        try
        {
            pipeline.CreateOpportunity(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow);
            return Changed("فرصت فروش ایجاد شد.", nameof(Index));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            PrepareCreateOptions(command.BranchId);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_CreateForm", command);
        }
    }

    [Authorize(Policy = "perm:Opportunity.Create")]
    [HttpGet("/opportunities/create-options")]
    public IActionResult CreateOptions(string branchId)
    {
        PrepareCreateOptions(branchId);
        return PartialView("_CreateOptions");
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpGet("/opportunities/{id:guid}/edit")]
    public IActionResult Edit(Guid id)
    {
        var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        var x = details.Opportunity;
        ViewBag.Opportunity = x;
        return PartialView("_EditForm", new UpdateOpportunityCommand(x.Title, x.Value,
            x.ExpectedCloseAtUtc ?? DateTimeOffset.UtcNow.AddDays(30), x.Source, x.Competitor,
            x.RiskLevel, x.NextAction ?? "پیگیری بعدی", x.NextActionAtUtc ?? DateTimeOffset.UtcNow.AddDays(1), x.Version));
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpPost("/opportunities/{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(Guid id, UpdateOpportunityCommand command)
    {
        try
        {
            pipeline.UpdateOpportunity(current.CrmUserId, current.RequiredOrganization(), id, command);
            return Changed("مشخصات فرصت به‌روزرسانی شد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Opportunity = details.Opportunity;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_EditForm", command);
        }
    }

    [Authorize(Policy = "perm:Opportunity.Assign")]
    [HttpGet("/opportunities/{id:guid}/assign")]
    public IActionResult Assign(Guid id)
    {
        var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Opportunity = details.Opportunity;
        ViewBag.Owners = details.EligibleOwners;
        return PartialView("_AssignForm", new AssignOpportunityCommand(details.Opportunity.OwnerUserId ?? Guid.Empty,
            "تخصیص توسط سرپرست", details.Opportunity.Version));
    }

    [Authorize(Policy = "perm:Opportunity.Assign")]
    [HttpPost("/opportunities/{id:guid}/assign")]
    [ValidateAntiForgeryToken]
    public IActionResult Assign(Guid id, AssignOpportunityCommand command)
    {
        try
        {
            pipeline.AssignOpportunity(current.CrmUserId, current.RequiredOrganization(), id, command);
            return Changed("مالک فرصت تغییر کرد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Opportunity = details.Opportunity;
            ViewBag.Owners = details.EligibleOwners;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_AssignForm", command);
        }
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpGet("/opportunities/{id:guid}/move")]
    public IActionResult Move(Guid id)
    {
        var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Opportunity = details.Opportunity;
        ViewBag.CanClose = details.CanClose;
        return PartialView("_MoveForm", new MoveOpportunityStageCommand(
            details.Opportunity.Stage == OpportunityStage.Commit && details.CanClose ? OpportunityStage.Won : NextStage(details.Opportunity.Stage),
            "شرایط مرحله بعد تکمیل شد", details.Opportunity.Version));
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpPost("/opportunities/{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public IActionResult Move(Guid id, MoveOpportunityStageCommand command)
    {
        try
        {
            pipeline.MoveOpportunity(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return Changed("مرحله فرصت و Timeline مشتری به‌روزرسانی شد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Opportunity = details.Opportunity;
            ViewBag.CanClose = details.CanClose;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_MoveForm", command);
        }
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpGet("/opportunities/{id:guid}/activities/create")]
    public IActionResult AddActivity(Guid id)
    {
        var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        ViewBag.Opportunity = details.Opportunity;
        return PartialView("_ActivityForm", new AddOpportunityActivityCommand(OpportunityActivityType.Call,
            "تماس پیگیری", "", DateTimeOffset.UtcNow, "پیگیری نتیجه", DateTimeOffset.UtcNow.AddDays(1),
            details.Opportunity.Version));
    }

    [Authorize(Policy = "perm:Opportunity.Update")]
    [HttpPost("/opportunities/{id:guid}/activities/create")]
    [ValidateAntiForgeryToken]
    public IActionResult AddActivity(Guid id, AddOpportunityActivityCommand command)
    {
        try
        {
            pipeline.AddActivity(current.CrmUserId, current.RequiredOrganization(), id, command);
            return Changed("فعالیت و اقدام بعدی ثبت شد.", nameof(Details), id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
            if (details is null) return NotFound();
            ViewBag.Opportunity = details.Opportunity;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ActivityForm", command);
        }
    }

    [Authorize(Policy = "perm:Opportunity.Advance")]
    [HttpPost("/opportunities/{id:guid}/advance")]
    [ValidateAntiForgeryToken]
    public IActionResult Advance(Guid id, long expectedVersion)
    {
        var details = pipeline.GetOpportunity(current.CrmUserId, current.RequiredOrganization(), id);
        if (details is null) return NotFound();
        try
        {
            pipeline.MoveOpportunity(current.CrmUserId, current.RequiredOrganization(), id,
                new MoveOpportunityStageCommand(NextStage(details.Opportunity.Stage), "انتقال سریع برد", expectedVersion), DateTimeOffset.UtcNow);
            return Changed("مرحله فرصت تغییر کرد.", nameof(Index));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            if (Request.IsHtmx()) return UnprocessableEntity(exception.Message);
            TempData["OpportunityError"] = exception.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    private IActionResult Changed(string message, string action, Guid? id = null)
    {
        Response.Trigger("opportunityChanged", message);
        return Request.IsHtmx() ? NoContent() : RedirectToAction(action, id.HasValue ? new { id } : null)!;
    }

    private string PrepareCreateOptions(string? requestedBranch = null)
    {
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        ViewBag.Branches = context?.Branches ?? [];
        ViewBag.Territories = context?.Territories ?? [];
        var branchId = requestedBranch ?? current.SelectedBranchId ?? context?.Branches.FirstOrDefault()?.Id ?? string.Empty;
        ViewBag.Customers = crm.GetCustomers(current.CrmUserId, current.RequiredOrganization())
            .Where(x => x.Status != Crm.Domain.Customers.CustomerStatus.Inactive &&
                x.BranchId.Equals(branchId, StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.Owners = string.IsNullOrWhiteSpace(branchId) ? Array.Empty<SalesOwnerOptionDto>() :
            pipeline.GetEligibleOwners(current.CrmUserId, current.RequiredOrganization(), branchId);
        ViewBag.SelectedOwnerId = current.CrmUserId;
        return branchId;
    }

    private static OpportunityStage NextStage(OpportunityStage stage) => stage switch
    {
        OpportunityStage.Identified => OpportunityStage.Discovery,
        OpportunityStage.Discovery => OpportunityStage.Qualified,
        OpportunityStage.Qualified => OpportunityStage.SolutionOffer,
        OpportunityStage.SolutionOffer => OpportunityStage.Negotiation,
        OpportunityStage.Negotiation => OpportunityStage.Commit,
        _ => stage
    };
}
