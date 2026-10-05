using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.FollowUps;
using Crm.Web.Presentation;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>
/// مرکز پیگیری: list, registration (۱), case page with stages (۵), and drawers for action (۲), result (۳), referral (۴), waiting (۶),
/// documents and approvals (۷) and closing/reopening (۸). Every endpoint delegates to <see cref="IFollowUpService"/>, which checks
/// the branch scope, the permission and the user's involvement on the server.
/// </summary>
[Authorize(Policy = "perm:FollowUp.Read")]
public sealed class FollowUpsController(
    IFollowUpService followUps,
    ICurrentUserContext current,
    IOrganizationContextService organization) : Controller
{
    private const long MaxUpload = 11 * 1024 * 1024;
    private Guid UserId => current.CrmUserId;
    private OrganizationSelection Org => current.RequiredOrganization();
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    // ───────────── List ─────────────

    [HttpGet("/follow-ups")]
    public async Task<IActionResult> Index(string? view = null, string? q = null, string? type = null, FollowUpStatus? status = null, FollowUpPriority? priority = null,
        string? branchId = null, Guid? owner = null, string? sort = null, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
        View(await LoadList(new FollowUpListQuery(view, q, type, status, priority, branchId, owner, sort, page, pageSize), cancellationToken));

    [HttpGet("/follow-ups/table")]
    public async Task<IActionResult> Table(string? view = null, string? q = null, string? type = null, FollowUpStatus? status = null, FollowUpPriority? priority = null,
        string? branchId = null, Guid? owner = null, string? sort = null, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var model = await LoadList(new FollowUpListQuery(view, q, type, status, priority, branchId, owner, sort, page, pageSize), cancellationToken);
        Response.Headers["HX-Replace-Url"] = model.State.Href();
        return PartialView("_List", model);
    }

    private async Task<FollowUpListPage> LoadList(FollowUpListQuery query, CancellationToken cancellationToken)
    {
        var list = await followUps.GetListAsync(UserId, Org, query, Now, cancellationToken);
        var context = organization.GetCurrent(UserId, current.SessionId);
        return FollowUpListPage.Create(list, context?.Branches ?? [], followUps.GetInbox(UserId, Org, Now).Where(x => x.Status == ReferralStatus.Pending).ToList(), Now);
    }

    /// <summary>Open cases of one customer, for the account file.</summary>
    [HttpGet("/follow-ups/customer/{customerId:guid}")]
    public async Task<IActionResult> ForCustomer(Guid customerId, CancellationToken cancellationToken)
    {
        var list = await followUps.GetListAsync(UserId, Org, new FollowUpListQuery(PageSize: 20, CustomerId: customerId), Now, cancellationToken);
        ViewBag.CustomerId = customerId;
        return PartialView("_CustomerCases", list);
    }

    // ───────────── ۱. ثبت پرونده پیگیری ─────────────

    [Authorize(Policy = "perm:FollowUp.Create")]
    [HttpGet("/follow-ups/new")]
    public IActionResult New(Guid? customerId = null, string? caseType = null) => Guarded(() =>
    {
        var command = Blank(customerId, caseType);
        return View("New", CreateModel(command, null));
    });

    /// <summary>Re-renders the registration form when the customer, type or branch changes (contacts, related records, extra fields and SLA follow).</summary>
    [Authorize(Policy = "perm:FollowUp.Create")]
    [HttpGet("/follow-ups/new/form")]
    public IActionResult NewForm(CreateFollowUpCommand command) => Guarded(() => PartialView("_CreateForm", CreateModel(Normalize(command), null)));

    [Authorize(Policy = "perm:FollowUp.Create")]
    [HttpPost("/follow-ups")]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateFollowUpCommand command)
    {
        command = Normalize(command);
        try
        {
            var result = followUps.Create(UserId, Org, command, Now);
            Response.Headers.Append("HX-Redirect", $"/follow-ups/{result.CaseId}?created=1");
            return Request.IsHtmx() ? NoContent() : Redirect($"/follow-ups/{result.CaseId}");
        }
        catch (UnauthorizedAccessException exception) { return FormError("_CreateForm", CreateModel(command, null), exception.Message); }
        catch (InvalidOperationException exception)
        {
            var duplicates = command.CustomerId == Guid.Empty ? [] :
                followUps.FindDuplicates(UserId, Org, command.CustomerId, command.CaseType, command.Subject, command.RelatedId, command.RelatedCode);
            return FormError("_CreateForm", CreateModel(command, duplicates), exception.Message);
        }
    }

    private static CreateFollowUpCommand Blank(Guid? customerId, string? caseType) => new(null, customerId ?? Guid.Empty, null, caseType ?? "Proforma", null, null, null,
        null, FollowUpChannel.Phone, FollowUpPriority.Normal, null, FollowUpRelatedKind.None, null, null, null, null, null, null, "fa", [], null, Guid.NewGuid());

    /// <summary>The related record arrives as one "Kind:Id" choice; the extra fields of the type arrive as Extra_* inputs.</summary>
    private CreateFollowUpCommand Normalize(CreateFollowUpCommand command)
    {
        var related = Request.Query.ContainsKey("Related") ? Request.Query["Related"].ToString() : Request.HasFormContentType ? Request.Form["Related"].ToString() : "";
        var parts = related.Split(':');
        if (parts.Length == 2 && Enum.TryParse<FollowUpRelatedKind>(parts[0], out var kind) && Guid.TryParse(parts[1], out var relatedId))
            command = command with { RelatedKind = kind, RelatedId = relatedId };
        else if (!string.IsNullOrWhiteSpace(command.RelatedCode) && command.RelatedKind == FollowUpRelatedKind.None)
            command = command with { RelatedKind = FollowUpRelatedKind.Invoice };
        var values = Request.HasFormContentType ? Request.Form.Select(x => (x.Key, Value: x.Value.ToString())) : Request.Query.Select(x => (x.Key, Value: x.Value.ToString()));
        var extra = values.Where(x => x.Key.StartsWith("Extra_", StringComparison.Ordinal)).ToDictionary(x => x.Key["Extra_".Length..].Replace('_', ' '), x => (string?)x.Value);
        var partsList = (command.Parts ?? []).Where(x => !string.IsNullOrWhiteSpace(x.PartCode)).ToList();
        return command with
        {
            ExtraFields = extra.Count > 0 ? extra : command.ExtraFields, Parts = partsList,
            OperationId = command.OperationId is { } op && op != Guid.Empty ? op : Guid.NewGuid()
        };
    }

    private FollowUpCreateModel CreateModel(CreateFollowUpCommand command, IReadOnlyList<FollowUpDuplicateDto>? duplicates)
    {
        var options = followUps.GetCreateOptions(UserId, Org, command.CustomerId == Guid.Empty ? null : command.CustomerId, command.CaseType, command.BranchId, Now);
        return new FollowUpCreateModel(command, options, duplicates ?? []);
    }

    // ───────────── Case page ─────────────

    [HttpGet("/follow-ups/{id:guid}")]
    public IActionResult Case(Guid id, bool created = false)
    {
        var dto = followUps.GetCase(UserId, Org, id, Now);
        if (dto is null) return NotFound();
        ViewBag.Created = created;
        return View("Case", dto);
    }

    [HttpGet("/follow-ups/{id:guid}/body")]
    public IActionResult Body(Guid id)
    {
        var dto = followUps.GetCase(UserId, Org, id, Now);
        return dto is null ? NotFound() : PartialView("_Case", dto);
    }

    private FollowUpCaseDto Load(Guid id) => followUps.GetCase(UserId, Org, id, Now) ?? throw new KeyNotFoundException();

    // ───────────── ۲. برنامه‌ریزی اقدام ─────────────

    [HttpGet("/follow-ups/{id:guid}/plan")]
    public IActionResult Plan(Guid id, string kind = "Call", Guid? stageId = null) => Guarded(() =>
    {
        var dto = Load(id);
        // Two hours from now, rounded up to the half hour (Tehran time), so the default is always in the future.
        var at = Now.AddHours(2);
        at = at.AddMinutes(30 - at.Minute % 30).AddSeconds(-at.Second);
        var stage = stageId ?? dto.ActiveStage?.Id;
        return PartialView("_PlanForm", new FollowUpPlanModel(dto, new PlanFollowUpActionCommand(kind, null, stage, dto.OwnerUserId ?? UserId, null,
            kind == "Visit" ? FollowUpChannel.Visit : FollowUpChannel.Phone, Crm.Domain.Common.TehranTime.Date(at), Crm.Domain.Common.TehranTime.Clock(at), kind == "Task" ? null : 15, 15,
            ActivityPriority.Normal, null, null, DefaultChecklist(kind), [], Guid.NewGuid())));
    });

    private static IReadOnlyList<string> DefaultChecklist(string kind) => kind switch
    {
        "Call" => ["بررسی آخرین سوابق تعامل", "آماده بودن پیش‌فاکتور / اطلاعات قیمت", "مشخص بودن هدف تماس"],
        "Meeting" or "Visit" => ["هماهنگی زمان و مکان با مشتری", "آماده‌سازی مدارک و نمونه‌ها", "تعیین شرکت‌کنندگان داخلی"],
        _ => ["مشخص بودن خروجی کار"]
    };

    [HttpPost("/follow-ups/{id:guid}/plan")]
    [ValidateAntiForgeryToken]
    public IActionResult Plan(Guid id, PlanFollowUpActionCommand command) => Mutation(
        () => Changed(followUps.PlanAction(UserId, Org, id, command, Now).Message),
        message => FormError("_PlanForm", new FollowUpPlanModel(Load(id), command), message));

    // ───────────── ۳. ثبت نتیجه ─────────────

    [HttpGet("/follow-ups/{id:guid}/activities/{activityId:guid}/result")]
    public IActionResult Result(Guid id, Guid activityId) => Guarded(() =>
    {
        var dto = Load(id);
        var activity = dto.Activities.SingleOrDefault(x => x.Id == activityId) ?? throw new KeyNotFoundException();
        var next = Now.AddDays(1);
        return PartialView("_ResultForm", new FollowUpResultModel(dto, activity, new RecordFollowUpResultCommand(null, null, null, null, "Call", dto.OwnerUserId ?? UserId,
            Crm.Domain.Common.TehranTime.Date(next), "10:00", false, activity.Version)));
    });

    [HttpPost("/follow-ups/{id:guid}/activities/{activityId:guid}/result")]
    [ValidateAntiForgeryToken]
    public IActionResult Result(Guid id, Guid activityId, RecordFollowUpResultCommand command) => Mutation(
        () => Changed(followUps.RecordResult(UserId, Org, id, activityId, command, Now).Message),
        message =>
        {
            var dto = Load(id);
            return FormError("_ResultForm", new FollowUpResultModel(dto, dto.Activities.Single(x => x.Id == activityId), command), message);
        });

    // ───────────── ۴. ارجاع و پذیرش مسئولیت ─────────────

    [HttpGet("/follow-ups/{id:guid}/refer")]
    public IActionResult Refer(Guid id, string tab = "new", ReferralScope scope = ReferralScope.Stage, string? branchId = null) => Guarded(() =>
    {
        var dto = Load(id);
        var due = Now.AddHours(4);
        var command = new ReferFollowUpCommand(dto.ActiveStage is null ? ReferralScope.Case : scope, dto.ActiveStage?.Id, Guid.Empty, branchId ?? dto.BranchId, null,
            null, Crm.Domain.Common.TehranTime.Date(due), Crm.Domain.Common.TehranTime.Clock(due), true, false, false);
        return PartialView("_ReferForm", ReferModel(dto, command, tab));
    });

    /// <summary>Refreshes the receiver list (and their capacity) when the target branch changes.</summary>
    [HttpGet("/follow-ups/{id:guid}/refer/receivers")]
    public IActionResult Receivers(Guid id, string? toBranchId = null) => Guarded(() =>
        PartialView("_Receivers", followUps.GetReceivers(UserId, Org, id, toBranchId, Now)));

    private FollowUpReferModel ReferModel(FollowUpCaseDto dto, ReferFollowUpCommand command, string tab) =>
        new(dto, command, tab, followUps.GetReceivers(UserId, Org, dto.Id, command.ToBranchId, Now), followUps.GetInbox(UserId, Org, Now));

    [HttpPost("/follow-ups/{id:guid}/refer")]
    [ValidateAntiForgeryToken]
    public IActionResult Refer(Guid id, ReferFollowUpCommand command) => Mutation(
        () => Changed(followUps.Refer(UserId, Org, id, command, Now).Message),
        message => FormError("_ReferForm", ReferModel(Load(id), command, "new"), message));

    [HttpPost("/follow-ups/referrals/{referralId:guid}/{answer:regex(^(accept|reject)$)}")]
    [ValidateAntiForgeryToken]
    public IActionResult Respond(Guid referralId, string answer, string? note, string? reason) => Mutation(() =>
    {
        var result = followUps.RespondReferral(UserId, Org, referralId, answer == "accept", note ?? reason, Now);
        return Changed(result.Message, answer == "accept" ? $"/follow-ups/{result.CaseId}" : null);
    }, RowError);

    // ───────────── ۵. مراحل و وظایف ─────────────

    [HttpPost("/follow-ups/{id:guid}/stages/{stageId:guid}/checklist")]
    [ValidateAntiForgeryToken]
    public IActionResult Checklist(Guid id, Guid stageId, [FromForm] Guid[] done, string? complete) => Mutation(() =>
    {
        var result = followUps.SaveChecklist(UserId, Org, id, stageId, done, Now);
        if (complete == "true") result = followUps.CompleteStage(UserId, Org, id, stageId, Now);
        return Changed(result.Message);
    }, RowError);

    [HttpPost("/follow-ups/{id:guid}/stages/{stageId:guid}/skip")]
    [ValidateAntiForgeryToken]
    public IActionResult Skip(Guid id, Guid stageId, string? reason) => Mutation(() => Changed(followUps.SkipStage(UserId, Org, id, stageId, reason, Now).Message), RowError);

    [HttpPost("/follow-ups/{id:guid}/stages/{stageId:guid}/assign")]
    [ValidateAntiForgeryToken]
    public IActionResult AssignStage(Guid id, Guid stageId, Guid responsibleUserId) =>
        Mutation(() => Changed(followUps.AssignStage(UserId, Org, id, stageId, responsibleUserId, Now).Message), RowError);

    [HttpPost("/follow-ups/{id:guid}/rules/{index:int}")]
    [ValidateAntiForgeryToken]
    public IActionResult Rule(Guid id, int index) => Mutation(() => Changed(followUps.TriggerRule(UserId, Org, id, index, Now).Message), RowError);

    // ───────────── ۶. انتظار و ازسرگیری ─────────────

    [HttpGet("/follow-ups/{id:guid}/wait")]
    public IActionResult Wait(Guid id, string? tab = null) => Guarded(() =>
    {
        var dto = Load(id);
        return PartialView("_WaitForm", WaitModel(dto, null, null, tab ?? (dto.IsWaiting ? "resume" : "wait")));
    });

    private FollowUpWaitModel WaitModel(FollowUpCaseDto dto, WaitFollowUpCommand? wait, ResumeFollowUpCommand? resume, string tab)
    {
        var today = Crm.Domain.Common.TehranTime.Date(Now);
        var review = Crm.Domain.Common.TehranTime.Date(Now.AddDays(2));
        return new FollowUpWaitModel(dto, wait ?? new WaitFollowUpCommand(FollowUpStatus.WaitingCustomer, dto.ActiveStage?.Id, null, null, today, review, "12:00", review, "10:00",
            dto.OwnerUserId ?? UserId), resume ?? new ResumeFollowUpCommand(null, null, Crm.Domain.Common.TehranTime.Date(Now.AddDays(1)), "10:00"), tab);
    }

    [HttpPost("/follow-ups/{id:guid}/wait")]
    [ValidateAntiForgeryToken]
    public IActionResult Wait(Guid id, WaitFollowUpCommand command) => Mutation(
        () => Changed(followUps.Wait(UserId, Org, id, command, Now).Message),
        message => FormError("_WaitForm", WaitModel(Load(id), command, null, "wait"), message));

    [HttpPost("/follow-ups/{id:guid}/resume")]
    [ValidateAntiForgeryToken]
    public IActionResult Resume(Guid id, ResumeFollowUpCommand command) => Mutation(
        () => Changed(followUps.Resume(UserId, Org, id, command, Now).Message),
        message => FormError("_WaitForm", WaitModel(Load(id), null, command, "resume"), message));

    // ───────────── ۷. مدارک و تأییدها ─────────────

    [HttpGet("/follow-ups/{id:guid}/documents")]
    public IActionResult Documents(Guid id, string tab = "documents", Guid? approvalId = null) => Guarded(() =>
        PartialView("_DocumentsForm", DocumentsModel(Load(id), tab, null, null, approvalId)));

    private FollowUpDocumentsModel DocumentsModel(FollowUpCaseDto dto, string tab, RequestFollowUpApprovalCommand? request, DecideFollowUpApprovalCommand? decide, Guid? approvalId)
    {
        var pending = approvalId is { } aid ? dto.Approvals.FirstOrDefault(x => x.Id == aid) : dto.Approvals.FirstOrDefault(x => x.CanDecide);
        var due = Now.AddDays(1);
        return new FollowUpDocumentsModel(dto, tab,
            request ?? new RequestFollowUpApprovalCommand(dto.ActiveStage?.Id, Guid.Empty, "مدیر فنی", dto.ActiveStage?.Checklist.Select(x => x.Title).ToList()),
            decide ?? new DecideFollowUpApprovalCommand(ApprovalDecision.Approved, null, pending?.ReviewItems, dto.OwnerUserId, Crm.Domain.Common.TehranTime.Date(due), "15:00"),
            pending);
    }

    [HttpPost("/follow-ups/{id:guid}/documents")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUpload)]
    public async Task<IActionResult> Upload(Guid id, Guid? stageId, FollowUpDocumentKind kind, string? title, bool needsApproval, IFormFile? file)
    {
        if (file is null || file.Length == 0) return FormError("_DocumentsForm", DocumentsModel(Load(id), "documents", null, null, null), "فایل را انتخاب کنید.");
        if (file.Length > CrmDocument.MaxBytes) return FormError("_DocumentsForm", DocumentsModel(Load(id), "documents", null, null, null), "حجم فایل بیش از ۱۰ مگابایت است.");
        var upload = await Read(file);
        return Mutation(() => Changed(followUps.UploadDocument(UserId, Org, id, stageId, kind, title, needsApproval, upload, Now).Message),
            message => FormError("_DocumentsForm", DocumentsModel(Load(id), "documents", null, null, null), message));
    }

    [HttpGet("/follow-ups/{id:guid}/documents/{documentId:guid}")]
    public IActionResult Download(Guid id, Guid documentId)
    {
        var file = followUps.DownloadDocument(UserId, Org, id, documentId);
        if (file is not { } f) return NotFound();
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(f.Content, f.ContentType, f.FileName);
    }

    [HttpPost("/follow-ups/{id:guid}/approvals")]
    [ValidateAntiForgeryToken]
    public IActionResult RequestApproval(Guid id, RequestFollowUpApprovalCommand command) => Mutation(
        () => Changed(followUps.RequestApproval(UserId, Org, id, command, Now).Message),
        message => FormError("_DocumentsForm", DocumentsModel(Load(id), "request", command, null, null), message));

    [HttpPost("/follow-ups/{id:guid}/approvals/{approvalId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult Decide(Guid id, Guid approvalId, DecideFollowUpApprovalCommand command) => Mutation(
        () => Changed(followUps.DecideApproval(UserId, Org, id, approvalId, command, Now).Message),
        message => FormError("_DocumentsForm", DocumentsModel(Load(id), "decide", null, command, approvalId), message));

    // ───────────── ۸. بستن و بازگشایی ─────────────

    [HttpGet("/follow-ups/{id:guid}/close")]
    public IActionResult Close(Guid id, string? tab = null) => Guarded(() =>
    {
        var dto = Load(id);
        return PartialView("_CloseForm", CloseModel(dto, null, null, tab ?? (dto.IsOpen ? "close" : "reopen")));
    });

    private FollowUpCloseModel CloseModel(FollowUpCaseDto dto, CloseFollowUpCommand? close, ReopenFollowUpCommand? reopen, string tab) => new(dto,
        close ?? new CloseFollowUpCommand(null, null, false, null, dto.OwnerUserId),
        reopen ?? new ReopenFollowUpCommand(null, dto.OwnerUserId ?? UserId, Crm.Domain.Common.TehranTime.Date(Now.AddDays(2)), "12:00"), tab);

    [HttpPost("/follow-ups/{id:guid}/close")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUpload)]
    public async Task<IActionResult> Close(Guid id, CloseFollowUpCommand command, IFormFile? customerApproval)
    {
        FollowUpDocumentUpload? upload = customerApproval is { Length: > 0 } f && f.Length <= CrmDocument.MaxBytes ? await Read(f) : null;
        return Mutation(() =>
        {
            var result = followUps.Close(UserId, Org, id, command, upload, Now);
            return Changed(result.Message, result.NewCaseId is { } next ? $"/follow-ups/{next}" : null);
        }, message => FormError("_CloseForm", CloseModel(Load(id), command, null, "close"), message));
    }

    [HttpPost("/follow-ups/{id:guid}/reopen")]
    [ValidateAntiForgeryToken]
    public IActionResult Reopen(Guid id, ReopenFollowUpCommand command) => Mutation(
        () => Changed(followUps.Reopen(UserId, Org, id, command, Now).Message),
        message => FormError("_CloseForm", CloseModel(Load(id), null, command, "reopen"), message));

    [HttpPost("/follow-ups/{id:guid}/cancel")]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(Guid id, string? reason) => Mutation(() => Changed(followUps.Cancel(UserId, Org, id, reason, Now).Message), RowError);

    // ───────────── مسئول، ادغام و اقلام ─────────────

    [HttpGet("/follow-ups/{id:guid}/reassign")]
    public IActionResult Reassign(Guid id) => Guarded(() => PartialView("_ReassignForm", new FollowUpReassignModel(Load(id), null, null, null, null)));

    [HttpPost("/follow-ups/{id:guid}/reassign")]
    [ValidateAntiForgeryToken]
    public IActionResult Reassign(Guid id, Guid ownerUserId, string? branchId, string? reason) => Mutation(
        () => Changed(followUps.Reassign(UserId, Org, id, ownerUserId, branchId, reason, Now).Message),
        message => FormError("_ReassignForm", new FollowUpReassignModel(Load(id), ownerUserId, branchId, reason, null), message));

    [HttpPost("/follow-ups/{id:guid}/merge")]
    [ValidateAntiForgeryToken]
    public IActionResult Merge(Guid id, string? targetCode, string? mergeReason) => Mutation(() =>
    {
        var result = followUps.Merge(UserId, Org, id, targetCode, mergeReason, Now);
        return Changed(result.Message, $"/follow-ups/{result.CaseId}");
    }, message => FormError("_ReassignForm", new FollowUpReassignModel(Load(id), null, null, null, targetCode), message));

    [HttpPost("/follow-ups/{id:guid}/items")]
    [ValidateAntiForgeryToken]
    public IActionResult AddItem(Guid id, FollowUpPartInput item) => Mutation(() => Changed(followUps.AddItem(UserId, Org, id, item, Now).Message), RowError);

    [HttpPost("/follow-ups/{id:guid}/items/{itemId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateItem(Guid id, Guid itemId, decimal deliveredQuantity, FollowUpItemStatus status, string? note, bool remove = false) =>
        Mutation(() => Changed(followUps.UpdateItem(UserId, Org, id, itemId, deliveredQuantity, status, note, remove, Now).Message), RowError);

    // ───────────── helpers ─────────────

    private static async Task<FollowUpDocumentUpload> Read(IFormFile file)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        return new FollowUpDocumentUpload(Path.GetFileName(file.FileName), string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            buffer.ToArray());
    }

    private IActionResult Guarded(Func<IActionResult> action)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return RowError(exception.Message); }
    }

    private IActionResult Mutation(Func<IActionResult> action, Func<string, IActionResult> onError)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException exception) when (Request.IsHtmx()) { return RowError(exception.Message.Contains("permission") ? "برای این عملیات مجوز ندارید." : exception.Message); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            try { return onError(exception.Message); }
            catch (Exception inner) when (inner is InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException) { return RowError(exception.Message); }
        }
    }

    /// <summary>Success: the drawer closes, a toast shows and the case page refreshes in place (or opens <paramref name="redirect"/>).</summary>
    private IActionResult Changed(string message, string? redirect = null)
    {
        if (!Request.IsHtmx()) return Redirect(redirect ?? Request.Headers.Referer.FirstOrDefault() ?? "/follow-ups");
        Response.Headers.Append("HX-Trigger", System.Text.Json.JsonSerializer.Serialize(new { followUpChanged = new { message } }));
        if (redirect is not null) Response.Headers.Append("HX-Redirect", redirect);
        return NoContent();
    }

    private IActionResult FormError(string view, object model, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView(view, model);
    }

    private IActionResult RowError(string message)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        if (Request.IsHtmx())
        {
            Response.Headers.Append("HX-Trigger", System.Text.Json.JsonSerializer.Serialize(new { followUpError = new { message } }));
            Response.Headers.Append("HX-Reswap", "none");
        }
        return Content($"<div class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(message)}</div>", "text/html; charset=utf-8");
    }
}

public sealed record FollowUpCreateModel(CreateFollowUpCommand Command, FollowUpCreateOptions Options, IReadOnlyList<FollowUpDuplicateDto> Duplicates);
public sealed record FollowUpPlanModel(FollowUpCaseDto Case, PlanFollowUpActionCommand Command);
public sealed record FollowUpResultModel(FollowUpCaseDto Case, FollowUpActivityDto Activity, RecordFollowUpResultCommand Command);
public sealed record FollowUpReferModel(FollowUpCaseDto Case, ReferFollowUpCommand Command, string Tab, IReadOnlyList<AssignmentCandidateDto> Receivers,
    IReadOnlyList<FollowUpReferralDto> Inbox);
public sealed record FollowUpWaitModel(FollowUpCaseDto Case, WaitFollowUpCommand Wait, ResumeFollowUpCommand Resume, string Tab);
public sealed record FollowUpDocumentsModel(FollowUpCaseDto Case, string Tab, RequestFollowUpApprovalCommand Request, DecideFollowUpApprovalCommand Decide,
    FollowUpApprovalDto? Approval);
public sealed record FollowUpCloseModel(FollowUpCaseDto Case, CloseFollowUpCommand Close, ReopenFollowUpCommand Reopen, string Tab);
public sealed record FollowUpReassignModel(FollowUpCaseDto Case, Guid? OwnerUserId, string? BranchId, string? Reason, string? TargetCode);
