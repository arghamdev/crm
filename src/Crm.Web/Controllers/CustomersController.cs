using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Web.Presentation;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

[Authorize(Policy = "perm:Customer.Read")]
public sealed class CustomersController(
    ICrmApplicationService crm,
    ICustomer360Service customer360,
    ICustomerListService customerList,
    IAccessSnapshotService access,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    [HttpGet("/customers")]
    public async Task<IActionResult> Index(string? view = null, string? q = null, string? branchId = null, Crm.Domain.Customers.CustomerKind? kind = null,
        Crm.Domain.Accounts.AccountRelationship? relationship = null, string? segment = null, Crm.Domain.Customers.CustomerStatus? status = null,
        string? owner = null, string? sort = null, string? layout = null, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
        View(await LoadList(new CustomerListQuery(view, q, branchId, kind, relationship, segment, status, owner, sort, page, pageSize), layout, cancellationToken));

    /// <summary>The list workspace fragment (tabs, counters, filters, rows, pager); the address bar follows the state.</summary>
    [HttpGet("/customers/table")]
    public async Task<IActionResult> Table(string? view = null, string? q = null, string? branchId = null, Crm.Domain.Customers.CustomerKind? kind = null,
        Crm.Domain.Accounts.AccountRelationship? relationship = null, string? segment = null, Crm.Domain.Customers.CustomerStatus? status = null,
        string? owner = null, string? sort = null, string? layout = null, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var model = await LoadList(new CustomerListQuery(view, q, branchId, kind, relationship, segment, status, owner, sort, page, pageSize), layout, cancellationToken);
        Response.Headers["HX-Replace-Url"] = model.State.Href();
        return PartialView("_List", model);
    }

    private async Task<CustomerListPage> LoadList(CustomerListQuery query, string? layout, CancellationToken cancellationToken)
    {
        SetCustomerCapabilities();
        var list = await customerList.GetAsync(current.CrmUserId, current.RequiredOrganization(), query, cancellationToken: cancellationToken);
        var context = organization.GetCurrent(current.CrmUserId, current.SessionId);
        return CustomerListPage.Create(list, context?.Branches ?? [], layout, DateTimeOffset.UtcNow);
    }

    [Authorize(Policy = "perm:Activity.Create")]
    [HttpGet("/customers/bulk/task")]
    public IActionResult BulkTask([FromQuery] Guid[] ids)
    {
        if (ids.Length == 0)
        {
            Response.Trigger("customersRefreshed", "ابتدا حداقل یک حساب را انتخاب کنید.");
            return PartialView("_BulkResult", new BulkResultView("عملیات گروهی", 0, ["هیچ حسابی انتخاب نشده است."]));
        }
        return PartialView("_BulkTaskForm", new BulkAccountTaskFormModel(ids, "پیگیری حساب",
            Crm.Domain.Common.TehranTime.Date(DateTimeOffset.UtcNow.AddDays(1)), "10:00"));
    }

    [Authorize(Policy = "perm:Activity.Create")]
    [HttpPost("/customers/bulk/task")]
    [ValidateAntiForgeryToken]
    public IActionResult BulkTask(BulkAccountTaskFormModel form)
    {
        if (string.IsNullOrWhiteSpace(form.Subject)) ModelState.AddModelError(nameof(form.Subject), "عنوان وظیفه الزامی است.");
        if (string.IsNullOrWhiteSpace(form.DueDate)) ModelState.AddModelError(nameof(form.DueDate), "تاریخ مهلت الزامی است.");
        if (ModelState.IsValid)
        {
            try
            {
                var result = customerList.PlanTasks(current.CrmUserId, current.RequiredOrganization(),
                    new BulkAccountTaskCommand(form.Ids, form.Subject!, form.DueDate, form.DueTime, form.Priority), DateTimeOffset.UtcNow);
                var message = $"برای {result.Succeeded} حساب وظیفه پیگیری ثبت شد.";
                if (result.Failures.Count == 0)
                {
                    Response.Trigger("customerChanged", message);
                    return Request.IsHtmx() ? NoContent() : RedirectToAction(nameof(Index));
                }
                Response.Trigger("customersRefreshed", message);
                return PartialView("_BulkResult", new BulkResultView("ثبت وظیفه گروهی", result.Succeeded, result.Failures));
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        }
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView("_BulkTaskForm", form);
    }

    [HttpGet("/customers/{id:guid}/activity")]
    public IActionResult Activity(Guid id)
    {
        var customer = customer360.Get(current.CrmUserId, current.RequiredOrganization(), id, includeRelatedActivity: true);
        if (customer is null) return NotFound();
        return Request.IsHtmx() ? PartialView("_Activity", customer) : View(customer);
    }

    [HttpGet("/customers/duplicate-check")]
    public IActionResult DuplicateCheck(string name, string city, string? nationalId, string? primaryPhone, string? primaryEmail,
        [FromQuery(Name = "Kind")] Crm.Domain.Customers.CustomerKind? kind = null,
        [FromQuery(Name = "Profile.LegalNationalId")] string? legalNationalId = null,
        [FromQuery(Name = "Profile.NationalCode")] string? nationalCode = null,
        [FromQuery(Name = "Profile.Phone1")] string? phone1 = null,
        [FromQuery(Name = "Profile.Mobile1")] string? mobile1 = null)
    {
        // The full form posts kind-specific identifier fields; the short form posts NationalId/PrimaryPhone directly.
        nationalId ??= Crm.Domain.Common.PersianText.Digits(kind == Crm.Domain.Customers.CustomerKind.Individual ? nationalCode : legalNationalId);
        primaryPhone ??= Crm.Domain.Common.PersianText.Digits(phone1) ?? Crm.Domain.Common.IranianIdentifiers.NormalizeMobile(mobile1);
        return PartialView("_DuplicateCheck", customer360.CheckDuplicates(current.CrmUserId, current.RequiredOrganization(),
            name ?? string.Empty, city ?? string.Empty, nationalId, primaryPhone, primaryEmail));
    }

    [HttpGet("/customers/cities")]
    public IActionResult Cities([FromQuery(Name = "Profile.Province")] string? province) =>
        Content(string.Concat(IranDivisions.CitiesOf(province).Select(x => $"<option value=\"{System.Net.WebUtility.HtmlEncode(x)}\"></option>")),
            "text/html; charset=utf-8");

    [Authorize(Policy = "perm:Customer.Create")]
    [HttpGet("/customers/contact-row")]
    public IActionResult ContactRow() => PartialView("_ContactRow", new ContactRowModel(Guid.NewGuid().ToString("N")[..8], -1, new ContactPersonInput()));

    [HttpGet("/customers/{id:guid}/logo")]
    public IActionResult Logo(Guid id)
    {
        var logo = customer360.GetLogo(current.CrmUserId, current.RequiredOrganization(), id);
        if (logo is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=300";
        return File(logo.Content, logo.ContentType);
    }

    [Authorize(Policy = "perm:Customer.Create")]
    [HttpGet("/customers/create")]
    public IActionResult Create()
    {
        var branchId = PrepareBranches();
        return PartialView("_Form", new CreateCustomerCommand("", "تهران", "سارا احمدی", branchId, "استاندارد",
            Profile: new CustomerProfileInput(ActivityType: "سایر موارد", Position: "نامشخص", Province: "تهران"), Contacts: [new ContactPersonInput()]));
    }

    [Authorize(Policy = "perm:Customer.Create")]
    [HttpPost("/customers/create")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Create(CreateCustomerCommand command, IFormFile? logo)
    {
        var upload = await ReadLogo(logo);
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
            crm.CreateCustomer(current.CrmUserId, current.RequiredOrganization(), command, upload);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            AddErrors(exception);
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
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> Edit(Guid id, UpdateCustomerCommand command, IFormFile? logo, bool removeLogo = false)
    {
        var upload = await ReadLogo(logo);
        ValidateCustomer(command.Name, command.Owner, command.BranchId);
        if (ModelState.IsValid)
        {
            try
            {
                customer360.Update(current.CrmUserId, current.RequiredOrganization(), id, command, DateTimeOffset.UtcNow, upload, removeLogo);
                return CustomerChanged(id, "اطلاعات مشتری به‌روزرسانی شد.");
            }
            catch (InvalidOperationException exception) { AddErrors(exception); }
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
            ExpectedVersion = command.ExpectedVersion,
            Profile = command.Profile ?? source.Profile
        });
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/contacts/create")]
    public IActionResult CreateContact(Guid id) => ContactForm(id, null);

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/contacts/{contactId:guid}/edit")]
    public IActionResult EditContact(Guid id, Guid contactId) => ContactForm(id, contactId);

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/contacts/create")]
    public IActionResult CreateContact(Guid id, SaveContactPersonCommand command) => SaveContact(id, null, command, "رابط مشتری افزوده شد.");

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/contacts/{contactId:guid}/edit")]
    public IActionResult EditContact(Guid id, Guid contactId, SaveContactPersonCommand command) => SaveContact(id, contactId, command, "رابط مشتری ویرایش شد.");

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpPost("/customers/{id:guid}/contacts/{contactId:guid}/deactivate")]
    public IActionResult DeactivateContact(Guid id, Guid contactId, long expectedVersion)
    {
        try
        {
            customer360.DeactivateContact(current.CrmUserId, current.RequiredOrganization(), id, contactId, expectedVersion, DateTimeOffset.UtcNow);
            return CustomerChanged(id, "رابط مشتری غیرفعال شد.");
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return OperationError(exception.Message); }
    }

    private IActionResult ContactForm(Guid id, Guid? contactId)
    {
        var model = customer360.GetContactForm(current.CrmUserId, current.RequiredOrganization(), id, contactId);
        return model is null ? NotFound() : PartialView("_ContactForm", model);
    }

    private IActionResult SaveContact(Guid id, Guid? contactId, SaveContactPersonCommand command, string message)
    {
        command = command with { Contact = command.Contact ?? new ContactPersonInput() };
        try
        {
            customer360.SaveContactPerson(current.CrmUserId, current.RequiredOrganization(), id, contactId, command, DateTimeOffset.UtcNow);
            return CustomerChanged(id, message);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception)
        {
            AddErrors(exception, "Contacts[0].", "Contact.");
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return PartialView("_ContactForm", new CustomerContactFormDto(id, contactId, command.Contact ?? new ContactPersonInput(),
                command.ConsentStatus, command.ExpectedVersion));
        }
    }

    /// <summary>Field-level errors of the customer form go next to their inputs; anything else into the summary.</summary>
    private void AddErrors(InvalidOperationException exception, string? fromPrefix = null, string? toPrefix = null)
    {
        if (exception is not CustomerValidationException validation)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return;
        }
        ModelState.AddModelError(string.Empty, validation.Message);
        foreach (var (field, message) in validation.Errors)
            ModelState.AddModelError(fromPrefix is not null && field.StartsWith(fromPrefix, StringComparison.Ordinal)
                ? toPrefix + field[fromPrefix.Length..] : field, message);
    }

    private async Task<CustomerLogoUpload?> ReadLogo(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > Crm.Domain.Customers.CustomerLogo.MaxBytes)
        {
            ModelState.AddModelError("logo", "حجم تصویر باید حداکثر ۵۱۲ کیلوبایت باشد.");
            return null;
        }
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        if (Crm.Domain.Customers.CustomerLogo.Detect(bytes) is null)
        {
            ModelState.AddModelError("logo", "فقط تصویر PNG، JPEG یا WebP پذیرفته می‌شود.");
            return null;
        }
        return new CustomerLogoUpload(bytes);
    }

    [Authorize(Policy = "perm:Customer.Update")]
    [HttpGet("/customers/{id:guid}/addresses/create")]
    public IActionResult CreateAddress(Guid id, Crm.Domain.Customers.CustomerAddressType type = Crm.Domain.Customers.CustomerAddressType.Registered)
    {
        if (customer360.GetEdit(current.CrmUserId, current.RequiredOrganization(), id) is null) return NotFound();
        ViewBag.CustomerId = id;
        return PartialView("_AddressForm", new AddCustomerAddressCommand(type, "", "", "", "", "", false));
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
