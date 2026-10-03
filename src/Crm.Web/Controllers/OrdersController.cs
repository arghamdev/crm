using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Order.Read")]
public sealed class OrdersController(
    IOrderApplicationService orders,
    ICurrentUserContext current) : Controller
{
    [HttpGet("/orders")]
    public IActionResult Index() => View(orders.GetWorkspace(current.CrmUserId,
        current.RequiredOrganization(), DateTimeOffset.UtcNow));

    [HttpGet("/orders/table")]
    public IActionResult Table() => PartialView("_Table", orders.GetWorkspace(current.CrmUserId,
        current.RequiredOrganization(), DateTimeOffset.UtcNow).Items);

    [HttpGet("/orders/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = orders.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Order.Create")]
    [HttpGet("/orders/create")]
    public IActionResult Create() => PartialView("_Form",
        orders.GetEligibleQuotes(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:Order.Create")]
    [HttpPost("/orders/create")]
    public IActionResult Create(CreateOrderRequestCommand command) => Mutate(() =>
        orders.Create(current.CrmUserId, current.RequiredOrganization(), command, DateTimeOffset.UtcNow),
        "درخواست سفارش از پیشنهاد پذیرفته‌شده ساخته شد.");

    [Authorize(Policy = "perm:Order.CreditCheck")]
    [HttpPost("/orders/{id:guid}/credit-check")]
    public IActionResult CheckCredit(Guid id, CheckOrderCreditCommand command) => Mutate(() =>
        orders.CheckCredit(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "Snapshot اعتبار حسابداری دریافت و تصمیم ثبت شد.");

    [Authorize(Policy = "perm:Order.CreditOverride")]
    [HttpGet("/orders/{id:guid}/credit-override")]
    public IActionResult OverrideCredit(Guid id)
    {
        var details = orders.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        if (details is null) return NotFound();
        ViewBag.OrderId = id;
        return PartialView("_CreditOverrideForm", new OverrideOrderCreditCommand(string.Empty,
            DateTimeOffset.UtcNow.AddDays(2), details.Order.Version));
    }

    [Authorize(Policy = "perm:Order.CreditOverride")]
    [HttpPost("/orders/{id:guid}/credit-override")]
    public IActionResult OverrideCredit(Guid id, OverrideOrderCreditCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
            ModelState.AddModelError(nameof(command.Reason), "دلیل Override الزامی است.");
        if (command.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            ModelState.AddModelError(nameof(command.ExpiresAtUtc), "انقضای مجوز باید در آینده باشد.");
        if (!ModelState.IsValid) return OverrideError(id, command);
        return Mutate(() => orders.OverrideCredit(current.CrmUserId, current.RequiredOrganization(), id,
            command, DateTimeOffset.UtcNow), "توقف اعتباری با مجوز زمان‌دار مالی رفع شد.",
            "_CreditOverrideForm", command, () => ViewBag.OrderId = id);
    }

    [Authorize(Policy = "perm:Order.Submit")]
    [HttpPost("/orders/{id:guid}/queue")]
    public IActionResult Queue(Guid id, QueueOrderSubmissionCommand command) => Mutate(() =>
        orders.QueueSubmission(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "پیام ارسال ERP با Idempotency Key ثابت در Outbox قرار گرفت.");

    [Authorize(Policy = "perm:Order.Integration.Process")]
    [HttpPost("/orders/{id:guid}/process")]
    public IActionResult Process(Guid id, ProcessOrderIntegrationCommand command) => Mutate(() =>
        orders.ProcessIntegration(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "نتیجه تلاش یکپارچه‌سازی ثبت شد.");

    [Authorize(Policy = "perm:Order.Sync")]
    [HttpPost("/orders/{id:guid}/advance")]
    public IActionResult Advance(Guid id, AdvanceOrderProjectionCommand command) => Mutate(() =>
        orders.AdvanceProjection(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "Projection سفارش از ERP به‌روزرسانی شد.");

    private IActionResult OverrideError(Guid id, OverrideOrderCreditCommand command)
    {
        ViewBag.OrderId = id;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_CreditOverrideForm", command);
    }

    private IActionResult Mutate(Func<OrderDetailsDto> operation, string message, string? errorPartial = null,
        object? errorModel = null, Action? prepare = null)
    {
        try
        {
            var result = operation();
            Response.Trigger("orderChanged", message);
            if (Request.IsHtmx()) Response.Headers["HX-Redirect"] = $"/orders/{result.Order.Id}";
            return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Details), new { id = result.Order.Id });
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
}
