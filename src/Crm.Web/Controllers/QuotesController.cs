using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Customers;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Quote.Read")]
public sealed class QuotesController(
    ICrmApplicationService crm,
    IQuoteApplicationService quotes,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/quotes")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await quotes.GetWorkspaceAsync(current.CrmUserId, current.RequiredOrganization(), DateTimeOffset.UtcNow, page,
            cancellationToken: cancellationToken));

    [HttpGet("/quotes/table")]
    public async Task<IActionResult> Table(int page = 1, CancellationToken cancellationToken = default) =>
        PartialView("_Table", await quotes.GetWorkspaceAsync(current.CrmUserId, current.RequiredOrganization(), DateTimeOffset.UtcNow, page,
            cancellationToken: cancellationToken));

    [HttpGet("/quotes/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = quotes.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Quote.Create")]
    [HttpGet("/quotes/create")]
    public IActionResult Create()
    {
        var branchId = PrepareBranches();
        PrepareRelations();
        PrepareProducts();
        return PartialView("_Form", new CreateQuoteDraftCommand(Guid.Empty, Guid.Empty, branchId, "IRR",
            DateTimeOffset.UtcNow.AddDays(30), "تسویه ۳۰ روزه", "PRD-1001", 1, 5));
    }

    [Authorize(Policy = "perm:Quote.Create")]
    [HttpGet("/quotes/opportunity-options")]
    public IActionResult OpportunityOptions(Guid customerId)
    {
        var selected = current.RequiredOrganization();
        var customer = customerId == Guid.Empty ? null : crm.GetCustomer(current.CrmUserId, selected, customerId);
        var opportunities = customer is null || customer.Status == CustomerStatus.Inactive
            ? Array.Empty<OpportunityDto>()
            : crm.GetOpportunities(current.CrmUserId, selected)
                .Where(item => item.CustomerId == customerId)
                .OrderBy(item => item.Title)
                .ToArray();
        return PartialView("_OpportunityOptions", opportunities);
    }

    [Authorize(Policy = "perm:Quote.Create")]
    [HttpPost("/quotes/create")]
    public IActionResult Create(CreateQuoteDraftCommand command)
    {
        if (command.Quantity <= 0) ModelState.AddModelError(nameof(command.Quantity), "مقدار باید بیشتر از صفر باشد.");
        if (command.DiscountPercent is < 0 or > 20) ModelState.AddModelError(nameof(command.DiscountPercent), "تخفیف نسخه نمونه باید بین صفر تا ۲۰ درصد باشد.");
        if (string.IsNullOrWhiteSpace(command.BranchId)) ModelState.AddModelError(nameof(command.BranchId), "شعبه الزامی است.");
        if (command.CustomerId == Guid.Empty) ModelState.AddModelError(nameof(command.CustomerId), "مشتری الزامی است.");
        if (command.OpportunityId == Guid.Empty) ModelState.AddModelError(nameof(command.OpportunityId), "فرصت الزامی است.");
        if (command.ValidUntilUtc <= DateTimeOffset.UtcNow) ModelState.AddModelError(nameof(command.ValidUntilUtc), "تاریخ اعتبار باید در آینده باشد.");
        if (!ModelState.IsValid)
        {
            PrepareBranches();
            PrepareRelations(command.CustomerId);
            PrepareProducts(command.CurrencyCode);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        QuoteDetailsDto created;
        try { created = quotes.CreateDraft(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            PrepareBranches(); PrepareRelations(command.CustomerId);
            PrepareProducts(command.CurrencyCode);
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        Response.Trigger("quoteChanged", "پیشنهاد قیمت ثبت شد.");
        if (Request.IsHtmx()) Response.Headers["HX-Redirect"] = $"/quotes/{created.Quote.Id}";
        return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Details), new { id = created.Quote.Id });
    }

    [Authorize(Policy = "perm:Quote.Update")]
    [HttpGet("/quotes/{id:guid}/lines/create")]
    public IActionResult AddLine(Guid id)
    {
        var details = quotes.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        if (details is null) return NotFound();
        PrepareProducts(details.Quote.CurrencyCode);
        ViewBag.QuoteId = id;
        return PartialView("_LineForm", new AddQuoteLineCommand("PRD-1001", 1, 5, details.Quote.Version));
    }

    [Authorize(Policy = "perm:Quote.Update")]
    [HttpPost("/quotes/{id:guid}/lines/create")]
    public IActionResult AddLine(Guid id, AddQuoteLineCommand command) => Mutate(() =>
        quotes.AddLine(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "ردیف پیشنهاد افزوده شد.", "_LineForm", command, () => { PrepareProducts(); ViewBag.QuoteId = id; });

    [Authorize(Policy = "perm:Quote.Update")]
    [HttpPost("/quotes/{id:guid}/lines/{lineId:guid}/remove")]
    public IActionResult RemoveLine(Guid id, Guid lineId, long expectedVersion) => Mutate(() =>
        quotes.RemoveLine(current.CrmUserId, current.RequiredOrganization(), id, lineId, expectedVersion, DateTimeOffset.UtcNow),
        "ردیف پیشنهاد حذف شد.");

    [Authorize(Policy = "perm:Quote.Submit")]
    [HttpPost("/quotes/{id:guid}/submit")]
    public IActionResult Submit(Guid id, SubmitQuoteCommand command) => Mutate(() =>
        quotes.Submit(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "پیشنهاد برای کنترل تجاری ارسال شد.");

    [HttpPost("/quotes/{id:guid}/decision")]
    public IActionResult Decision(Guid id, DecideQuoteCommand command) => Mutate(() =>
        quotes.Decide(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        command.Decision == Crm.Domain.Commercial.QuoteDecision.Approved ? "مرحله تأیید ثبت شد." : "پیشنهاد رد شد.");

    [Authorize(Policy = "perm:Quote.Send")]
    [HttpPost("/quotes/{id:guid}/send")]
    public IActionResult Send(Guid id, long expectedVersion) => Mutate(() =>
        quotes.MarkSent(current.CrmUserId, current.RequiredOrganization(), id, expectedVersion, DateTimeOffset.UtcNow),
        "ارسال پیشنهاد به مشتری ثبت شد.");

    [Authorize(Policy = "perm:Quote.Accept")]
    [HttpPost("/quotes/{id:guid}/outcome")]
    public IActionResult Outcome(Guid id, QuoteOutcomeCommand command) => Mutate(() =>
        quotes.RecordOutcome(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        command.Accepted ? "پذیرش مشتری ثبت شد." : "انقضای پیشنهاد ثبت شد.");

    [Authorize(Policy = "perm:Quote.Update")]
    [HttpPost("/quotes/{id:guid}/revise")]
    public IActionResult Revise(Guid id, ReviseQuoteCommand command) => Mutate(() =>
        quotes.Revise(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "نسخه جدید پیشنهاد ساخته شد.");

    private IActionResult Mutate(Func<QuoteDetailsDto> operation, string message, string? errorPartial = null,
        object? errorModel = null, Action? prepare = null)
    {
        try
        {
            var result = operation();
            Response.Trigger("quoteChanged", message);
            if (Request.IsHtmx()) Response.Headers["HX-Redirect"] = $"/quotes/{result.Quote.Id}";
            return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Details), new { id = result.Quote.Id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            if (errorPartial is null) return UnprocessableEntity(exception.Message);
            ModelState.AddModelError(string.Empty, exception.Message);
            prepare?.Invoke();
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView(errorPartial, errorModel);
        }
    }

    private string PrepareBranches()
    {
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        ViewBag.Branches = context?.Branches ?? [];
        return current.SelectedBranchId ?? context?.Branches.FirstOrDefault()?.Id ?? string.Empty;
    }

    private void PrepareRelations(Guid customerId = default)
    {
        var selected = current.RequiredOrganization();
        var customers = crm.GetCustomers(current.CrmUserId, selected).Where(item => item.Status != CustomerStatus.Inactive).ToArray();
        ViewBag.Customers = customers;
        ViewBag.Opportunities = customerId == Guid.Empty || customers.All(item => item.Id != customerId)
            ? Array.Empty<OpportunityDto>()
            : crm.GetOpportunities(current.CrmUserId, selected)
                .Where(item => item.CustomerId == customerId)
                .OrderBy(item => item.Title)
                .ToArray();
    }

    private void PrepareProducts(string currencyCode = "IRR") =>
        ViewBag.Products = quotes.GetProducts(current.RequiredOrganization().CompanyId, currencyCode);
}
