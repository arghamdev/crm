using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Channel;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Dealer.Read")]
public sealed class DealersController(IDealerApplicationService dealers, ICurrentUserContext current) : Controller
{
    [HttpGet("/dealers")]
    public IActionResult Index() => View(dealers.GetWorkspace(current.CrmUserId,
        current.RequiredOrganization(), DateTimeOffset.UtcNow));

    [HttpGet("/dealers/table")]
    public IActionResult Table() => PartialView("_Table", dealers.GetWorkspace(current.CrmUserId,
        current.RequiredOrganization(), DateTimeOffset.UtcNow).Items);

    [HttpGet("/dealers/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var model = dealers.Get(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        return model is null ? NotFound() : View(model);
    }

    [Authorize(Policy = "perm:Dealer.Manage")]
    [HttpGet("/dealers/create")]
    public IActionResult Create() => PartialView("_Form", dealers.GetForm(current.CrmUserId,
        current.RequiredOrganization(), null, DateTimeOffset.UtcNow));

    [Authorize(Policy = "perm:Dealer.Manage")]
    [HttpGet("/dealers/{id:guid}/edit")]
    public IActionResult Edit(Guid id)
    {
        try { return PartialView("_Form", dealers.GetForm(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Policy = "perm:Dealer.Manage")]
    [HttpPost("/dealers/save")]
    public IActionResult Save(Guid? id, SaveDealerCommand command)
    {
        ValidateDealer(command);
        if (!ModelState.IsValid)
        {
            var model = dealers.GetForm(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow) with
            {
                DealerId = command.DealerId, Code = command.Code, LegalName = command.LegalName,
                TradeName = command.TradeName, BranchId = command.BranchId, TerritoryId = command.TerritoryId,
                City = command.City, NationalId = command.NationalId, Phone = command.Phone, Email = command.Email,
                ChannelManagerUserId = command.ChannelManagerUserId, ExpectedVersion = command.ExpectedVersion
            };
            return PartialError("_Form", model);
        }
        return Mutate(() => dealers.Save(current.CrmUserId, current.RequiredOrganization(), id, command,
            DateTimeOffset.UtcNow), id is null ? "پرونده نماینده ایجاد شد." : "پرونده نماینده به‌روزرسانی شد.");
    }

    [HttpPost("/dealers/{id:guid}/status")]
    public IActionResult ChangeStatus(Guid id, ChangeDealerStatusCommand command) => Mutate(() =>
        dealers.ChangeStatus(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow),
        "وضعیت نماینده تغییر کرد.");

    [Authorize(Policy = "perm:Dealer.Contract.Request")]
    [HttpGet("/dealers/{id:guid}/contracts/create")]
    public IActionResult CreateContract(Guid id)
    {
        var now = DateTimeOffset.UtcNow;
        return PartialView("_ContractForm", new DealerContractFormDto(id,
            new SaveDealerContractCommand(string.Empty, now.Date, now.Date.AddYears(1), 1,
                "تسویه ۳۰ روزه", 0)));
    }

    [Authorize(Policy = "perm:Dealer.Contract.Request")]
    [HttpPost("/dealers/{id:guid}/contracts/save")]
    public IActionResult SaveContract(Guid id, SaveDealerContractCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ContractNumber)) ModelState.AddModelError(nameof(command.ContractNumber), "شماره قرارداد الزامی است.");
        if (command.ValidToUtc <= command.ValidFromUtc) ModelState.AddModelError(nameof(command.ValidToUtc), "پایان قرارداد باید بعد از شروع باشد.");
        if (command.AnnualTarget <= 0) ModelState.AddModelError(nameof(command.AnnualTarget), "هدف قرارداد باید بیشتر از صفر باشد.");
        if (!ModelState.IsValid) return PartialError("_ContractForm", new DealerContractFormDto(id, command));
        return Mutate(() => dealers.SaveContract(current.CrmUserId, current.RequiredOrganization(), id, null,
            command, DateTimeOffset.UtcNow), "قرارداد پیش‌نویس ثبت شد.", "_ContractForm", new DealerContractFormDto(id, command));
    }

    [Authorize(Policy = "perm:Dealer.Contract.Request")]
    [HttpPost("/dealers/{id:guid}/contracts/{contractId:guid}/submit")]
    public IActionResult SubmitContract(Guid id, Guid contractId, DecideDealerContractCommand command) => Mutate(() =>
        dealers.SubmitContract(current.CrmUserId, current.RequiredOrganization(), id, contractId, command,
            DateTimeOffset.UtcNow), "قرارداد برای تأیید ارسال شد.");

    [Authorize(Policy = "perm:Dealer.Contract.Approve")]
    [HttpPost("/dealers/{id:guid}/contracts/{contractId:guid}/approve")]
    public IActionResult ApproveContract(Guid id, Guid contractId, DecideDealerContractCommand command) => Mutate(() =>
        dealers.ApproveContract(current.CrmUserId, current.RequiredOrganization(), id, contractId, command,
            DateTimeOffset.UtcNow), "قرارداد نماینده تأیید شد.");

    [Authorize(Policy = "perm:Dealer.Contract.Approve")]
    [HttpPost("/dealers/{id:guid}/contracts/{contractId:guid}/end")]
    public IActionResult EndContract(Guid id, Guid contractId, EndDealerRelationshipCommand command) => Mutate(() =>
        dealers.EndContract(current.CrmUserId, current.RequiredOrganization(), id, contractId, command,
            DateTimeOffset.UtcNow), "قرارداد نماینده خاتمه یافت.");

    [Authorize(Policy = "perm:Dealer.Territory.Request")]
    [HttpGet("/dealers/{id:guid}/territories/create")]
    public IActionResult CreateTerritory(Guid id)
    {
        var now = DateTimeOffset.UtcNow;
        return PartialView("_TerritoryForm", new DealerTerritoryFormDto(id,
            new AssignDealerTerritoryCommand(string.Empty, false, now.Date, now.Date.AddYears(1)),
            dealers.GetTerritoryOptions(current.CrmUserId, current.RequiredOrganization(), id, now)));
    }

    [Authorize(Policy = "perm:Dealer.Territory.Request")]
    [HttpPost("/dealers/{id:guid}/territories/create")]
    public IActionResult CreateTerritory(Guid id, AssignDealerTerritoryCommand command)
    {
        var options = dealers.GetTerritoryOptions(current.CrmUserId, current.RequiredOrganization(), id, DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(command.TerritoryId)) ModelState.AddModelError(nameof(command.TerritoryId), "Territory الزامی است.");
        if (command.ValidToUtc is not null && command.ValidToUtc <= command.ValidFromUtc)
            ModelState.AddModelError(nameof(command.ValidToUtc), "پایان اعتبار باید بعد از شروع باشد.");
        var model = new DealerTerritoryFormDto(id, command, options);
        if (!ModelState.IsValid) return PartialError("_TerritoryForm", model);
        return Mutate(() => dealers.RequestTerritory(current.CrmUserId, current.RequiredOrganization(), id,
            command, DateTimeOffset.UtcNow), "درخواست تخصیص Territory ثبت شد.", "_TerritoryForm", model);
    }

    [Authorize(Policy = "perm:Dealer.Territory.Approve")]
    [HttpPost("/dealers/{id:guid}/territories/{assignmentId:guid}/approve")]
    public IActionResult ApproveTerritory(Guid id, Guid assignmentId, DecideDealerTerritoryCommand command) => Mutate(() =>
        dealers.ApproveTerritory(current.CrmUserId, current.RequiredOrganization(), id, assignmentId, command,
            DateTimeOffset.UtcNow), "تخصیص Territory پس از کنترل تعارض تأیید شد.");

    [Authorize(Policy = "perm:Dealer.Territory.Approve")]
    [HttpPost("/dealers/{id:guid}/territories/{assignmentId:guid}/end")]
    public IActionResult EndTerritory(Guid id, Guid assignmentId, EndDealerRelationshipCommand command) => Mutate(() =>
        dealers.EndTerritory(current.CrmUserId, current.RequiredOrganization(), id, assignmentId, command,
            DateTimeOffset.UtcNow), "تخصیص Territory پایان یافت.");

    [Authorize(Policy = "perm:Dealer.Target.Manage")]
    [HttpGet("/dealers/{id:guid}/target")]
    public IActionResult Target(Guid id) => PartialView("_TargetForm", dealers.GetTargetForm(current.CrmUserId,
        current.RequiredOrganization(), id, DateTimeOffset.UtcNow));

    [Authorize(Policy = "perm:Dealer.Target.Manage")]
    [HttpPost("/dealers/{id:guid}/target")]
    public IActionResult Target(Guid id, SaveDealerTargetCommand command)
    {
        if (command.Amount <= 0) ModelState.AddModelError(nameof(command.Amount), "هدف باید بیشتر از صفر باشد.");
        if (command.PeriodToUtc <= command.PeriodFromUtc) ModelState.AddModelError(nameof(command.PeriodToUtc), "پایان دوره باید بعد از شروع باشد.");
        var model = new DealerTargetFormDto(id, command);
        if (!ModelState.IsValid) return PartialError("_TargetForm", model);
        return Mutate(() => dealers.SaveTarget(current.CrmUserId, current.RequiredOrganization(), id, command,
            DateTimeOffset.UtcNow), "هدف فروش نماینده ثبت شد.", "_TargetForm", model);
    }

    [Authorize(Policy = "perm:Dealer.Customer.Assign")]
    [HttpGet("/dealers/{id:guid}/customers/assign")]
    public IActionResult AssignCustomer(Guid id) => PartialView("_CustomerForm", new DealerCustomerFormDto(id,
        new AssignDealerCustomerCommand(Guid.Empty, string.Empty),
        dealers.GetCustomerOptions(current.CrmUserId, current.RequiredOrganization(), id)));

    [Authorize(Policy = "perm:Dealer.Customer.Assign")]
    [HttpPost("/dealers/{id:guid}/customers/assign")]
    public IActionResult AssignCustomer(Guid id, AssignDealerCustomerCommand command)
    {
        var model = new DealerCustomerFormDto(id, command,
            dealers.GetCustomerOptions(current.CrmUserId, current.RequiredOrganization(), id));
        if (command.CustomerId == Guid.Empty) ModelState.AddModelError(nameof(command.CustomerId), "مشتری الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Reason)) ModelState.AddModelError(nameof(command.Reason), "دلیل تخصیص الزامی است.");
        if (!ModelState.IsValid) return PartialError("_CustomerForm", model);
        return Mutate(() => dealers.AssignCustomer(current.CrmUserId, current.RequiredOrganization(), id, command,
            DateTimeOffset.UtcNow), "مشتری به سبد نماینده افزوده شد.", "_CustomerForm", model);
    }

    [Authorize(Policy = "perm:Dealer.Customer.Assign")]
    [HttpPost("/dealers/{id:guid}/customers/{assignmentId:guid}/end")]
    public IActionResult EndCustomerAssignment(Guid id, Guid assignmentId, EndDealerRelationshipCommand command) => Mutate(() =>
        dealers.EndCustomerAssignment(current.CrmUserId, current.RequiredOrganization(), id, assignmentId, command,
            DateTimeOffset.UtcNow), "ارتباط مشتری با نماینده پایان یافت.");

    [Authorize(Policy = "perm:Dealer.Financial.Sync")]
    [HttpPost("/dealers/{id:guid}/financial-sync")]
    public IActionResult SyncFinancial(Guid id, long expectedVersion) => Mutate(() => dealers.SyncFinancial(
        current.CrmUserId, current.RequiredOrganization(), id, expectedVersion, DateTimeOffset.UtcNow),
        "Projection مالی نماینده همگام شد.");

    [Authorize(Policy = "perm:Dealer.Performance.Sync")]
    [HttpPost("/dealers/{id:guid}/performance-sync")]
    public IActionResult SyncPerformance(Guid id, long expectedVersion) => Mutate(() => dealers.SyncPerformance(
        current.CrmUserId, current.RequiredOrganization(), id, expectedVersion, DateTimeOffset.UtcNow),
        "Projection عملکرد نماینده همگام شد.");

    private IActionResult Mutate(Func<DealerDetailsDto> operation, string message,
        string? errorPartial = null, object? errorModel = null)
    {
        try
        {
            var result = operation();
            Response.Trigger("dealerChanged", message);
            if (Request.IsHtmx()) Response.Headers["HX-Redirect"] = $"/dealers/{result.Dealer.Id}";
            return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Details), new { id = result.Dealer.Id });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            if (errorPartial is null) return UnprocessableEntity(exception.Message);
            ModelState.AddModelError(string.Empty, exception.Message);
            return PartialError(errorPartial, errorModel!);
        }
    }

    private IActionResult PartialError(string partial, object model)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView(partial, model);
    }

    private void ValidateDealer(SaveDealerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.DealerId)) ModelState.AddModelError(nameof(command.DealerId), "شناسه نماینده الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Code)) ModelState.AddModelError(nameof(command.Code), "کد نماینده الزامی است.");
        if (string.IsNullOrWhiteSpace(command.LegalName)) ModelState.AddModelError(nameof(command.LegalName), "نام حقوقی الزامی است.");
        if (string.IsNullOrWhiteSpace(command.TradeName)) ModelState.AddModelError(nameof(command.TradeName), "نام تجاری الزامی است.");
        if (string.IsNullOrWhiteSpace(command.BranchId)) ModelState.AddModelError(nameof(command.BranchId), "شعبه الزامی است.");
        if (string.IsNullOrWhiteSpace(command.City)) ModelState.AddModelError(nameof(command.City), "شهر الزامی است.");
        if (command.ChannelManagerUserId == Guid.Empty) ModelState.AddModelError(nameof(command.ChannelManagerUserId), "مدیر کانال الزامی است.");
    }
}
