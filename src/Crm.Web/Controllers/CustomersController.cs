using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Customer.Read")]
public sealed class CustomersController(
    ICrmApplicationService crm,
    ICustomer360Service customer360,
    IAccessSnapshotService access,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/customers")]
    public IActionResult Index(string? q = null, int page = 1, int pageSize = 20)
    {
        SetCustomerCapabilities();
        return View(crm.SearchCustomers(current.CrmUserId, current.RequiredOrganization(), q, page, pageSize));
    }

    [HttpGet("/customers/table")]
    public IActionResult Table(string? q = null, int page = 1, int pageSize = 20) =>
        PartialView("_Table", crm.SearchCustomers(current.CrmUserId, current.RequiredOrganization(), q, page, pageSize));

    [HttpGet("/customers/{id:guid}")]
    public IActionResult Details(Guid id)
    {
        var customer = customer360.Get(current.CrmUserId, current.RequiredOrganization(), id, includeRelatedActivity: false);
        SetCustomerCapabilities();
        return customer is null ? NotFound() : View(customer);
    }

    [HttpGet("/customers/{id:guid}/activity")]
    public IActionResult Activity(Guid id)
    {
        var customer = customer360.Get(current.CrmUserId, current.RequiredOrganization(), id, includeRelatedActivity: true);
        if (customer is null) return NotFound();
        return Request.IsHtmx() ? PartialView("_Activity", customer) : View(customer);
    }

    [HttpGet("/customers/duplicate-check")]
    public IActionResult DuplicateCheck(string name, string city, string? nationalId, string? primaryPhone, string? primaryEmail) =>
        PartialView("_DuplicateCheck", customer360.CheckDuplicates(current.CrmUserId, current.RequiredOrganization(),
            name ?? string.Empty, city ?? string.Empty, nationalId, primaryPhone, primaryEmail));

    [Authorize(Policy = "perm:Customer.Create")]
    [HttpGet("/customers/create")]
    public IActionResult Create()
    {
        var branchId = PrepareBranches();
        return PartialView("_Form", new CreateCustomerCommand("", "تهران", "سارا احمدی", branchId, "استاندارد"));
    }

    [Authorize(Policy = "perm:Customer.Create")]
    [HttpPost("/customers/create")]
    public IActionResult Create(CreateCustomerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name)) ModelState.AddModelError(nameof(command.Name), "نام مشتری الزامی است.");
        if (string.IsNullOrWhiteSpace(command.Owner)) ModelState.AddModelError(nameof(command.Owner), "مالک حساب الزامی است.");
        if (string.IsNullOrWhiteSpace(command.BranchId)) ModelState.AddModelError(nameof(command.BranchId), "شعبه الزامی است.");
        if (!ModelState.IsValid)
        {
            PrepareBranches();
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        try
        {
            crm.CreateCustomer(current.CrmUserId, current.RequiredOrganization(), command);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            PrepareBranches();
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_Form", command);
        }
        if (Request.IsHtmx())
        {
            Response.Trigger("customerChanged", "مشتری جدید ثبت شد.");
            return NoContent();
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/edit")]
    public IActionResult Edit(Guid id)
    {
        var model = customer360.GetEdit(current.CrmUserId, current.RequiredOrganization(), id);
        return model is null ? NotFound() : PartialView("_EditForm", model);
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/edit")]
    public IActionResult Edit(Guid id, UpdateCustomerCommand command)
    {
        ValidateCustomer(command.Name, command.Owner, command.BranchId);
        if (ModelState.IsValid)
        {
            try
            {
                customer360.Update(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
                return CustomerChanged(id, "اطلاعات مشتری به‌روزرسانی شد.");
            }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }
        var source = customer360.GetEdit(current.CrmUserId, current.RequiredOrganization(), id);
        if (source is null) return NotFound();
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_EditForm", source with
        {
            Name = command.Name,
            City = command.City,
            Owner = command.Owner,
            BranchId = command.BranchId,
            TerritoryId = command.TerritoryId,
            Segment = command.Segment,
            NationalId = command.NationalId,
            PrimaryPhone = command.PrimaryPhone,
            PrimaryEmail = command.PrimaryEmail,
            ExpectedVersion = command.ExpectedVersion
        });
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/contacts/create")]
    public IActionResult CreateContact(Guid id)
    {
        if (customer360.GetEdit(current.CrmUserId, current.RequiredOrganization(), id) is null) return NotFound();
        ViewBag.CustomerId = id;
        return PartialView("_ContactForm", new AddCustomerContactCommand("", "", "", "", false, Crm.Domain.Customers.ContactConsentStatus.Unknown));
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/contacts/create")]
    public IActionResult CreateContact(Guid id, AddCustomerContactCommand command)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.CustomerId = id;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ContactForm", command);
        }
        try
        {
            customer360.AddContact(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return CustomerChanged(id, "شخص تماس افزوده شد.");
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ViewBag.CustomerId = id;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ContactForm", command);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/addresses/create")]
    public IActionResult CreateAddress(Guid id)
    {
        if (customer360.GetEdit(current.CrmUserId, current.RequiredOrganization(), id) is null) return NotFound();
        ViewBag.CustomerId = id;
        return PartialView("_AddressForm", new AddCustomerAddressCommand(Crm.Domain.Customers.CustomerAddressType.Registered,
            "", "", "", "", "", false));
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/addresses/create")]
    public IActionResult CreateAddress(Guid id, AddCustomerAddressCommand command)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.CustomerId = id;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_AddressForm", command);
        }
        try
        {
            customer360.AddAddress(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow);
            return CustomerChanged(id, "آدرس مشتری افزوده شد.");
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            ViewBag.CustomerId = id;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_AddressForm", command);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpGet("/customers/duplicates")]
    public IActionResult Duplicates() => View(customer360.GetDuplicateReviewQueue(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpGet("/customers/duplicates/table")]
    public IActionResult DuplicateTable() => PartialView("_DuplicateTable", customer360.GetDuplicateReviewQueue(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpPost("/customers/duplicates/{candidateId:guid}/review")]
    public IActionResult ReviewDuplicate(Guid candidateId, ReviewDuplicateCommand command)
    {
        if (!ModelState.IsValid) return OperationError("تصمیم، یادداشت و نسخه رکورد الزامی است.");
        try
        {
            customer360.ReviewDuplicate(current.CrmUserId, current.RequiredOrganization(), candidateId, command, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        if (Request.IsHtmx())
        {
            Response.Trigger("duplicateChanged", "پرونده رکورد مشابه بررسی شد.");
            return NoContent();
        }
        return RedirectToAction(nameof(Duplicates));
    }

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpGet("/customers/duplicates/{candidateId:guid}/merge")]
    public IActionResult MergePreview(Guid candidateId, Guid survivorCustomerId)
    {
        try { return PartialView("_MergeForm", customer360.GetMergePreview(current.CrmUserId, current.RequiredOrganization(), candidateId, survivorCustomerId)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
    }

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpPost("/customers/duplicates/{candidateId:guid}/merge")]
    public IActionResult Merge(Guid candidateId, MergeCustomerCommand command)
    {
        try { customer360.Merge(current.CrmUserId, current.RequiredOrganization(), candidateId, command, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        if (Request.IsHtmx()) { Response.Trigger("duplicateChanged", "ادغام مشتری با موفقیت انجام شد."); return NoContent(); }
        return RedirectToAction(nameof(MergeHistory));
    }

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpGet("/customers/merges")]
    public IActionResult MergeHistory() => View("Merges", customer360.GetMergeHistory(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpGet("/customers/merges/table")]
    public IActionResult MergeHistoryTable() => PartialView("_MergeTable",
        customer360.GetMergeHistory(current.CrmUserId, current.RequiredOrganization()));

    [Authorize(Policy = "perm:Customer.MergeReview")]
    [HttpPost("/customers/merges/{operationId:guid}/unmerge")]
    public IActionResult Unmerge(Guid operationId, UnmergeCustomerCommand command)
    {
        try { customer360.Unmerge(current.CrmUserId, current.RequiredOrganization(), operationId, command, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        if (Request.IsHtmx()) { Response.Trigger("mergeChanged", "ادغام با موفقیت بازگردانی شد."); return NoContent(); }
        return RedirectToAction(nameof(MergeHistory));
    }

    [Authorize(Policy = "perm:Customer.Read")]
    [HttpGet("/customers/data-quality")]
    public IActionResult DataQuality()
    {
        SetCustomerCapabilities();
        return View(customer360.GetDataQuality(current.CrmUserId, current.RequiredOrganization()));
    }

    private string PrepareBranches()
    {
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        ViewBag.Branches = context?.Branches ?? [];
        return current.SelectedBranchId ?? context?.Branches.FirstOrDefault()?.Id ?? string.Empty;
    }

    private IActionResult CustomerChanged(Guid id, string message)
    {
        if (Request.IsHtmx())
        {
            Response.Trigger("customerChanged", message);
            Response.Headers.Append("HX-Redirect", $"/customers/{id}");
            return NoContent();
        }
        return Redirect($"/customers/{id}");
    }

    private IActionResult OperationError(string message)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return Content($"<div class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(message)}</div>", "text/html; charset=utf-8");
    }

    private void ValidateCustomer(string name, string owner, string branchId)
    {
        if (string.IsNullOrWhiteSpace(name)) ModelState.AddModelError(nameof(name), "نام مشتری الزامی است.");
        if (string.IsNullOrWhiteSpace(owner)) ModelState.AddModelError(nameof(owner), "مالک حساب الزامی است.");
        if (string.IsNullOrWhiteSpace(branchId)) ModelState.AddModelError(nameof(branchId), "شعبه الزامی است.");
    }

    private void SetCustomerCapabilities()
    {
        var snapshot = access.Get(current.CrmUserId);
        var companyId = current.SelectedCompanyId;
        ViewBag.CanUpdateCustomer = companyId is not null && snapshot?.PermissionsFor(companyId).Contains("Customer.Update") == true;
        ViewBag.CanReviewDuplicates = companyId is not null && snapshot?.PermissionsFor(companyId).Contains("Customer.MergeReview") == true;
    }
}
