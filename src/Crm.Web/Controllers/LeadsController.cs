using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Sales;
using Crm.Web.Presentation;
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
    public async Task<IActionResult> Index(string? view = null, string? q = null, LeadStatus? status = null, string? branchId = null, string? source = null,
        Guid? owner = null, bool includeClosed = false, string? sort = null, string? layout = null, int page = 1, int pageSize = 10,
        CancellationToken cancellationToken = default) =>
        View(await LoadList(new LeadListQuery(view, q, status, branchId, source, owner, includeClosed, page, pageSize, sort), layout, cancellationToken));

    /// <summary>The list workspace fragment (tabs, counters, filters, rows, pager); the address bar follows the state.</summary>
    [HttpGet("/leads/table")]
    public async Task<IActionResult> Table(string? view = null, string? q = null, LeadStatus? status = null, string? branchId = null, string? source = null,
        Guid? owner = null, bool includeClosed = false, string? sort = null, string? layout = null, int page = 1, int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadList(new LeadListQuery(view, q, status, branchId, source, owner, includeClosed, page, pageSize, sort), layout, cancellationToken);
        Response.Headers["HX-Replace-Url"] = model.State.Href();
        return PartialView("_List", model);
    }

    private async Task<LeadListPage> LoadList(LeadListQuery query, string? layout, CancellationToken cancellationToken)
    {
        var list = await pipeline.GetLeadListAsync(current.CrmUserId, current.RequiredOrganization(), query, cancellationToken: cancellationToken);
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        return LeadListPage.Create(list, context?.Branches ?? [], layout, DateTimeOffset.UtcNow);
    }

    [Authorize(Policy = "perm:Lead.Assign")]
    [HttpGet("/leads/bulk/assign")]
    public IActionResult BulkAssign([FromQuery] Guid[] ids)
    {
        if (ids.Length == 0) return BulkEmpty();
        ViewBag.Owners = BulkOwners();
        return PartialView("_BulkAssignForm", new BulkLeadFormModel(ids, null, "ارجاع گروهی توسط سرپرست", null, null, null));
    }

    [Authorize(Policy = "perm:Lead.Assign")]
    [HttpPost("/leads/bulk/assign")]
    [ValidateAntiForgeryToken]
    public IActionResult BulkAssign(BulkLeadFormModel form)
    {
        if (form.OwnerUserId is not { } ownerId || ownerId == Guid.Empty) ModelState.AddModelError(nameof(form.OwnerUserId), "کارشناس مقصد را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(form.Reason)) ModelState.AddModelError(nameof(form.Reason), "دلیل ارجاع الزامی است.");
        if (ModelState.IsValid)
        {
            try
            {
                var result = pipeline.BulkAssignLeads(current.CrmUserId, current.RequiredOrganization(),
                    new BulkLeadAssignCommand(form.Ids, form.OwnerUserId!.Value, form.Reason!, DateTimeOffset.UtcNow.AddHours(Math.Clamp(form.DueHours, 1, 72))),
                    DateTimeOffset.UtcNow);
                return BulkDone(result, "ارجاع گروهی", $"{result.Succeeded} سرنخ ارجاع شد.");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        ViewBag.Owners = BulkOwners();
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_BulkAssignForm", form);
    }

    [Authorize(Policy = "perm:Lead.Update")]
    [HttpGet("/leads/bulk/next-action")]
    public IActionResult BulkNextAction([FromQuery] Guid[] ids)
    {
        if (ids.Length == 0) return BulkEmpty();
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        return PartialView("_BulkNextActionForm", new BulkLeadFormModel(ids, null, null, "تماس پیگیری",
            Crm.Domain.Common.TehranTime.Date(tomorrow), "10:00"));
    }

    [Authorize(Policy = "perm:Lead.Update")]
    [HttpPost("/leads/bulk/next-action")]
    [ValidateAntiForgeryToken]
    public IActionResult BulkNextAction(BulkLeadFormModel form)
    {
        if (string.IsNullOrWhiteSpace(form.NextAction)) ModelState.AddModelError(nameof(form.NextAction), "عنوان فعالیت الزامی است.");
        DateTimeOffset? at = null;
        try { at = Crm.Domain.Common.TehranTime.ToUtc(form.DueDate, string.IsNullOrWhiteSpace(form.DueTime) ? "09:00" : form.DueTime, "تاریخ فعالیت"); }
        catch (InvalidOperationException exception) { ModelState.AddModelError(nameof(form.DueDate), exception.Message); }
        if (at is null && ModelState.IsValid) ModelState.AddModelError(nameof(form.DueDate), "تاریخ فعالیت را به شکل ۱۴۰۵/۰۷/۲۰ وارد کنید.");
        if (ModelState.IsValid)
        {
            try
            {
                var result = pipeline.BulkPlanLeadNextAction(current.CrmUserId, current.RequiredOrganization(),
                    new BulkLeadNextActionCommand(form.Ids, form.NextAction!, at!.Value), DateTimeOffset.UtcNow);
                return BulkDone(result, "افزودن فعالیت", $"برای {result.Succeeded} سرنخ فعالیت بعدی برنامه‌ریزی شد.");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_BulkNextActionForm", form);
    }

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpGet("/leads/import")]
    public IActionResult Import()
    {
        ViewBag.BranchId = PrepareBranches();
        return PartialView("_ImportForm");
    }

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpGet("/leads/import/template")]
    public IActionResult ImportTemplate() =>
        File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(
            "نام سرنخ,شخص تماس,تلفن,ایمیل,منبع\r\nشرکت نمونه,علی رضایی,02112345678,info@example.com,نمایشگاه\r\n")).ToArray(),
            "text/csv; charset=utf-8", "leads-template.csv");

    [Authorize(Policy = "perm:Lead.Create")]
    [HttpPost("/leads/import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, string? branchId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(branchId)) ModelState.AddModelError("branchId", "شعبه الزامی است.");
        if (file is null || file.Length == 0) ModelState.AddModelError("file", "فایل CSV را انتخاب کنید.");
        else if (file.Length > 1024 * 1024) ModelState.AddModelError("file", "حجم فایل حداکثر ۱ مگابایت است.");
        IReadOnlyList<LeadImportRow> rows = [];
        if (ModelState.IsValid)
        {
            using var reader = new StreamReader(file!.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            rows = LeadImportCsv.Parse(await reader.ReadToEndAsync(cancellationToken), out var error);
            if (error is not null) ModelState.AddModelError("file", error);
        }
        if (ModelState.IsValid)
        {
            try
            {
                var result = pipeline.ImportLeads(current.CrmUserId, current.RequiredOrganization(), rows, branchId!, DateTimeOffset.UtcNow);
                return BulkDone(result, "ورود اطلاعات سرنخ", $"{result.Succeeded} سرنخ از فایل ثبت شد.");
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        ViewBag.BranchId = branchId ?? PrepareBranches();
        PrepareBranches();
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_ImportForm");
    }

    /// <summary>All rows succeeded: close the drawer and refresh the list. Otherwise show what failed and refresh the list behind it.</summary>
    private IActionResult BulkDone(BulkResultDto result, string title, string message)
    {
        if (result.Failures.Count == 0)
        {
            Response.Trigger("leadChanged", message);
            return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
        }
        Response.Trigger("leadsRefreshed", message);
        return PartialView("_BulkResult", new BulkResultView(title, result.Succeeded, result.Failures));
    }

    private IActionResult BulkEmpty()
    {
        Response.Trigger("leadsRefreshed", "ابتدا حداقل یک سرنخ را انتخاب کنید.");
        return PartialView("_BulkResult", new BulkResultView("عملیات گروهی", 0, ["هیچ سرنخی انتخاب نشده است."]));
    }

    private IReadOnlyList<SalesOwnerOptionDto> BulkOwners()
    {
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        var branches = current.SelectedBranchId is { } selected ? [selected] : (context?.Branches.Select(x => x.Id).ToList() ?? []);
        var owners = new List<SalesOwnerOptionDto>();
        foreach (var branch in branches)
        {
            try { owners.AddRange(pipeline.GetEligibleOwners(current.CrmUserId, current.RequiredOrganization(), branch)); }
            catch (UnauthorizedAccessException) { }
        }
        return owners.GroupBy(x => x.UserId).Select(x => x.First()).OrderBy(x => x.DisplayName).ToList();
    }

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
