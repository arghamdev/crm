using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>
/// The account file (پرونده یکپارچه حساب): one page per account with KPIs, quick actions, the activity panel and
/// lazily loaded related-record sections. Every endpoint delegates to the account services, which check scope and
/// the area permission on the server; the page only hides what the user cannot do.
/// </summary>
[Authorize(Policy = "perm:Customer.Read")]
public sealed class AccountFileController(
    IAccountFileService files,
    IAccountActivityService activities,
    IAccountNoteService notes,
    IAccountRecordService records,
    ICustomer360Service customer360,
    ICurrentUserContext current) : Controller
{
    private const long MaxUpload = 11 * 1024 * 1024;

    private Guid UserId => current.CrmUserId;
    private OrganizationSelection Org => current.RequiredOrganization();
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    // ───────────── page and lazy panels ─────────────

    [HttpGet("/customers/{id:guid}")]
    public IActionResult Index(Guid id, Guid? contact = null) => Guarded(() =>
    {
        ViewBag.Contact = contact;
        var file = files.GetFile(UserId, Org, id, Now);
        ViewBag.Customer360 = customer360.Get(UserId, Org, id, includeRelatedActivity: false);
        return View(file);
    });

    [HttpGet("/customers/{id:guid}/file/kpis")]
    public IActionResult Kpis(Guid id) => Guarded(() => PartialView("_Kpis", files.GetFile(UserId, Org, id, Now)));

    [HttpGet("/customers/{id:guid}/file/sections")]
    public IActionResult SectionNav(Guid id) => Guarded(() => PartialView("_SectionNav", files.GetFile(UserId, Org, id, Now)));

    [HttpGet("/customers/{id:guid}/sections/{key}")]
    public IActionResult Section(Guid id, string key, string? q = null, string? status = null, int page = 1) =>
        Guarded(() =>
        {
            ViewBag.AccountId = id;
            return PartialView("_Section", files.GetSection(UserId, Org, id, key, q, status, page, Now));
        });

    [HttpGet("/customers/{id:guid}/timeline")]
    public IActionResult Timeline(Guid id) => Guarded(() => PartialView("_Timeline", activities.GetTimeline(UserId, Org, id, Now, 80)));

    [HttpGet("/customers/{id:guid}/history")]
    public IActionResult History(Guid id) => Guarded(() => PartialView("_Timeline", files.GetHistory(UserId, Org, id, 150)));

    /// <summary>The «پیگیری» side panel: overdue, today and later follow-ups of the account.</summary>
    [HttpGet("/customers/{id:guid}/follow-ups")]
    public IActionResult FollowUps(Guid id) =>
        Guarded(() =>
        {
            ViewBag.AccountId = id;
            ViewBag.NowUtc = Now;
            return PartialView("_FollowUps", activities.GetFollowUps(UserId, Org, id, Now));
        });

    // ───────────── activities ─────────────

    [HttpGet("/customers/{id:guid}/activities")]
    public IActionResult Activities(Guid id, string? type = null, string? state = null, Guid? owner = null, string? from = null, string? to = null, Guid? contact = null) =>
        Guarded(() =>
        {
            var filter = new ActivityFilter(Enum.TryParse<ActivityType>(type, out var t) ? t : null, string.IsNullOrWhiteSpace(state) ? null : state, owner,
                JalaliDate.TryParse(from, out var f) ? f : null, JalaliDate.TryParse(to, out var e) ? e : null, contact);
            ViewBag.AccountId = id;
            return PartialView("_ActivityPanel", activities.GetActivities(UserId, Org, id, filter, Now));
        });

    [HttpGet("/customers/{id:guid}/activities/new/{type}")]
    public IActionResult NewActivity(Guid id, ActivityType type) => Guarded(() => PartialView("_ActivityForm", activities.GetActivityForm(UserId, Org, id, null, type, Now)));

    [HttpPost("/customers/{id:guid}/activities")]
    public IActionResult CreateActivity(Guid id, SaveActivityCommand command) => SaveActivity(id, null, command);

    [HttpGet("/customers/{id:guid}/activities/{activityId:guid}/edit")]
    public IActionResult EditActivity(Guid id, Guid activityId) =>
        Guarded(() => PartialView("_ActivityForm", activities.GetActivityForm(UserId, Org, id, activityId, ActivityType.Task, Now)));

    [HttpPost("/customers/{id:guid}/activities/{activityId:guid}/edit")]
    public IActionResult UpdateActivity(Guid id, Guid activityId, SaveActivityCommand command) => SaveActivity(id, activityId, command);

    private IActionResult SaveActivity(Guid id, Guid? activityId, SaveActivityCommand command)
    {
        command = WithRelated(command);
        return SaveActivityCore(id, activityId, command);
    }

    /// <summary>The form posts one "Related" choice (Opportunity:id, Project:id, Contract:id); the service validates it belongs to the account.</summary>
    private SaveActivityCommand WithRelated(SaveActivityCommand command)
    {
        var raw = Request.HasFormContentType ? Request.Form["Related"].ToString() : "";
        var parts = raw.Split(':');
        return parts.Length == 2 && Enum.TryParse<ActivityRelatedKind>(parts[0], out var kind) && Guid.TryParse(parts[1], out var relatedId)
            ? command with { RelatedKind = kind, RelatedId = relatedId }
            : command with { RelatedKind = ActivityRelatedKind.None, RelatedId = null };
    }

    private IActionResult SaveActivityCore(Guid id, Guid? activityId, SaveActivityCommand command) => Mutation(() =>
    {
        var saved = activities.SaveActivity(UserId, Org, id, activityId, command, Now);
        return Changed(activityId is null ? command.AlreadyDone ? $"{AccountActivityService.Label(command.Type)} انجام‌شده ثبت شد." :
            $"{AccountActivityService.Label(command.Type)} «{saved.Subject}» برنامه‌ریزی شد." : "فعالیت ویرایش شد.");
    }, message =>
    {
        var form = activities.GetActivityForm(UserId, Org, id, activityId, command.Type, Now);
        return FormError("_ActivityForm", form with { Command = command }, message);
    });

    [HttpGet("/customers/{id:guid}/activities/{activityId:guid}")]
    public IActionResult ActivityDetails(Guid id, Guid activityId) => Guarded(() => PartialView("_ActivityDetails", activities.GetActivity(UserId, Org, id, activityId, Now)));

    [HttpGet("/customers/{id:guid}/activities/{activityId:guid}/{step:regex(^(complete|cancel|reschedule)$)}")]
    public IActionResult ActivityStep(Guid id, Guid activityId, string step) => Guarded(() =>
    {
        ViewBag.Step = step;
        return PartialView("_ActivityStep", activities.GetActivity(UserId, Org, id, activityId, Now));
    });

    [HttpPost("/customers/{id:guid}/activities/{activityId:guid}/complete")]
    public IActionResult Complete(Guid id, Guid activityId, CompleteActivityCommand command) => Mutation(() =>
    {
        var (_, followUp) = activities.Complete(UserId, Org, id, activityId, command, Now);
        return Changed(followUp is null ? "نتیجه ثبت و فعالیت بسته شد." : $"نتیجه ثبت شد؛ اقدام بعدی «{followUp.Subject}» برنامه‌ریزی شد.");
    }, message => StepError(id, activityId, "complete", message));

    [HttpPost("/customers/{id:guid}/activities/{activityId:guid}/cancel")]
    public IActionResult Cancel(Guid id, Guid activityId, CancelActivityCommand command) => Mutation(() =>
    {
        activities.Cancel(UserId, Org, id, activityId, command, Now);
        return Changed("فعالیت لغو شد.");
    }, message => StepError(id, activityId, "cancel", message));

    [HttpPost("/customers/{id:guid}/activities/{activityId:guid}/reschedule")]
    public IActionResult Reschedule(Guid id, Guid activityId, RescheduleActivityCommand command) => Mutation(() =>
    {
        var item = activities.Reschedule(UserId, Org, id, activityId, command, Now);
        return Changed($"زمان جدید: {item.When}");
    }, message => StepError(id, activityId, "reschedule", message));

    private IActionResult StepError(Guid id, Guid activityId, string step, string message)
    {
        ViewBag.Step = step;
        return FormError("_ActivityStep", activities.GetActivity(UserId, Org, id, activityId, Now), message);
    }

    // ───────────── notes and documents ─────────────

    [HttpGet("/customers/{id:guid}/notes")]
    public IActionResult Notes(Guid id, string? q = null) => Guarded(() =>
    {
        ViewBag.AccountId = id;
        ViewBag.Query = q;
        return PartialView("_Notes", notes.GetNotes(UserId, Org, id, q));
    });

    [HttpGet("/customers/{id:guid}/notes/new")]
    public IActionResult NewNote(Guid id) => Guarded(() =>
    {
        var file = files.GetFile(UserId, Org, id, Now);
        if (!file.Quick.Note) return Forbid();
        return PartialView("_NoteForm", new NoteFormModel(id, null, new SaveNoteCommand(null, null, NoteVisibility.Public, Guid.NewGuid()), [], file.Account.Name));
    });

    [HttpPost("/customers/{id:guid}/notes")]
    [RequestSizeLimit(5 * MaxUpload)]
    public Task<IActionResult> CreateNote(Guid id, SaveNoteCommand command, List<IFormFile>? attachments) => SaveNote(id, null, command, attachments);

    [HttpGet("/customers/{id:guid}/notes/{noteId:guid}/edit")]
    public IActionResult EditNote(Guid id, Guid noteId) => Guarded(() =>
    {
        var note = notes.GetNotes(UserId, Org, id, take: 500).SingleOrDefault(x => x.Id == noteId);
        if (note is null) return NotFound();
        if (!note.CanEdit) return Forbid();
        return PartialView("_NoteForm", new NoteFormModel(id, noteId, new SaveNoteCommand(note.Title, note.Body, note.Visibility, Guid.NewGuid()), note.Attachments, null));
    });

    [HttpPost("/customers/{id:guid}/notes/{noteId:guid}/edit")]
    [RequestSizeLimit(5 * MaxUpload)]
    public Task<IActionResult> UpdateNote(Guid id, Guid noteId, SaveNoteCommand command, List<IFormFile>? attachments) => SaveNote(id, noteId, command, attachments);

    private async Task<IActionResult> SaveNote(Guid id, Guid? noteId, SaveNoteCommand command, List<IFormFile>? attachments)
    {
        IReadOnlyList<FileUpload> uploads;
        try { uploads = await ReadFiles(attachments); }
        catch (InvalidOperationException exception) { return FormError("_NoteForm", new NoteFormModel(id, noteId, command, [], null), exception.Message); }
        return Mutation(() =>
        {
            notes.SaveNote(UserId, Org, id, noteId, command, uploads, Now);
            return Changed(noteId is null ? "یادداشت ثبت شد." : "یادداشت ویرایش شد.");
        }, message => FormError("_NoteForm", new NoteFormModel(id, noteId, command, [], null), message));
    }

    [HttpPost("/customers/{id:guid}/notes/{noteId:guid}/delete")]
    public IActionResult DeleteNote(Guid id, Guid noteId) => Mutation(() =>
    {
        notes.DeleteNote(UserId, Org, id, noteId, Now);
        return Changed("یادداشت حذف شد.");
    }, RowError);

    [HttpGet("/customers/{id:guid}/documents/{documentId:guid}")]
    public IActionResult Download(Guid id, Guid documentId) => Guarded(() =>
    {
        var (name, type, content) = notes.Download(UserId, Org, id, documentId);
        Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return File(content, type, name);
    });

    // ───────────── related records ─────────────

    [HttpGet("/customers/{id:guid}/quick/{kind:regex(^(opportunity|lead)$)}")]
    public IActionResult Quick(Guid id, string kind) => NewRecord(id, kind == "opportunity" ? "opportunities" : "leads");

    [HttpGet("/customers/{id:guid}/records/{key}/new")]
    public IActionResult NewRecord(Guid id, string key) =>
        Guarded(() => PartialView("_RecordForm", records.GetCreateForm(UserId, Org, id, key, QueryValues(), Now)));

    [HttpPost("/customers/{id:guid}/records/{key}/new")]
    [RequestSizeLimit(MaxUpload)]
    public async Task<IActionResult> CreateRecord(Guid id, string key, IFormFile? file)
    {
        var fields = FormValues();
        IReadOnlyList<FileUpload> uploads;
        try { uploads = await ReadFiles(file is null ? null : [file]); }
        catch (InvalidOperationException exception) { return FormError("_RecordForm", records.GetCreateForm(UserId, Org, id, key, fields, Now), exception.Message); }
        var command = new AccountRecordCommand(fields, Guid.TryParse(fields.GetValueOrDefault("operationId"), out var op) ? op : Guid.Empty, uploads);
        return Mutation(() =>
        {
            records.Create(UserId, Org, id, key, command, Now);
            return Changed(key switch
            {
                "opportunities" => "فرصت فروش ایجاد شد.", "leads" => "سرنخ برای حساب ثبت شد.", "documents" => "سند بارگذاری شد.", _ => "ثبت شد."
            }, key);
        }, message => FormError("_RecordForm", records.GetCreateForm(UserId, Org, id, key, fields, Now), message));
    }

    [HttpGet("/customers/{id:guid}/records/{key}/link")]
    public IActionResult LinkForm(Guid id, string key, string? q = null) => Guarded(() =>
    {
        var model = records.GetLinkForm(UserId, Org, id, key, q, Now);
        return Request.Headers.ContainsKey("HX-Target") && Request.Headers["HX-Target"] == "linkOptions"
            ? PartialView("_LinkOptions", model) : PartialView("_LinkForm", model);
    });

    [HttpPost("/customers/{id:guid}/records/{key}/link")]
    public IActionResult Link(Guid id, string key, Guid recordId) => Mutation(() =>
    {
        records.Link(UserId, Org, id, key, recordId, new AccountRecordCommand(FormValues(), Guid.Empty), Now);
        return Changed("رکورد به حساب متصل شد.", key);
    }, message => FormError("_LinkForm", records.GetLinkForm(UserId, Org, id, key, null, Now), message));

    [HttpPost("/customers/{id:guid}/records/{key}/{recordId:guid}/{operation}")]
    public IActionResult Act(Guid id, string key, Guid recordId, string operation) => Mutation(() =>
    {
        records.Act(UserId, Org, id, key, recordId, operation, new AccountRecordCommand(FormValues(), Guid.Empty), Now);
        return Changed(operation switch
        {
            "unlink" or "unlink-parent" or "unlink-child" => "ارتباط قطع شد؛ خود رکورد حذف نشد.", "delete" => "حذف شد.", "approve" => "تأیید شد.",
            "deactivate" => "غیرفعال شد.", _ => "وضعیت به‌روزرسانی شد."
        }, key);
    }, RowError);

    [HttpGet("/customers/{id:guid}/account/{form:regex(^(status|classify)$)}")]
    public IActionResult AccountForm(Guid id, string form) => Guarded(() => PartialView("_RecordForm", records.GetAccountForm(UserId, Org, id, form, null)));

    [HttpPost("/customers/{id:guid}/account/{form:regex(^(status|classify)$)}")]
    public IActionResult UpdateAccount(Guid id, string form) => Mutation(() =>
    {
        records.UpdateAccount(UserId, Org, id, form, new AccountRecordCommand(FormValues(), Guid.Empty), Now);
        Response.Trigger("customerChanged", form == "status" ? "وضعیت حساب تغییر کرد." : "نوع رابطه و برچسب‌ها ذخیره شد.");
        Response.Headers.Append("HX-Redirect", $"/customers/{id}");
        return NoContent();
    }, message => FormError("_RecordForm", records.GetAccountForm(UserId, Org, id, form, FormValues()), message));

    // ───────────── helpers ─────────────

    private IActionResult Guarded(Func<IActionResult> action)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return RowError(exception.Message); }
    }

    /// <summary>Runs a change; validation messages go back into the open form (422), access problems become 403/404.</summary>
    private IActionResult Mutation(Func<IActionResult> action, Func<string, IActionResult> onError)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            try { return onError(exception.Message); }
            catch (Exception inner) when (inner is InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException) { return RowError(exception.Message); }
        }
    }

    /// <summary>Success: the drawer closes, a toast shows and the account panels refresh in place (no full reload).</summary>
    private IActionResult Changed(string message, string? section = null)
    {
        if (!Request.IsHtmx()) return Redirect(Request.Headers.Referer.FirstOrDefault() ?? "/customers");
        var payload = System.Text.Json.JsonSerializer.Serialize(new { accountChanged = new { message, section } });
        Response.Headers.Append("HX-Trigger", payload);
        return NoContent();
    }

    private IActionResult FormError(string view, object model, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return PartialView(view, model);
    }

    /// <summary>Error of an inline action (no form to re-render): shown as a toast.</summary>
    private IActionResult RowError(string message)
    {
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        if (Request.IsHtmx())
        {
            Response.Headers.Append("HX-Trigger", System.Text.Json.JsonSerializer.Serialize(new { accountError = new { message } }));
            Response.Headers.Append("HX-Reswap", "none");
        }
        return Content($"<div class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(message)}</div>", "text/html; charset=utf-8");
    }

    private Dictionary<string, string?> FormValues() => Request.HasFormContentType
        ? Request.Form.Where(x => x.Key != "__RequestVerificationToken").ToDictionary(x => x.Key, x => (string?)string.Join(",", x.Value.ToArray()))
        : [];

    private Dictionary<string, string?> QueryValues() => Request.Query.ToDictionary(x => x.Key, x => (string?)x.Value.ToString());

    private static async Task<IReadOnlyList<FileUpload>> ReadFiles(IEnumerable<IFormFile>? files)
    {
        var result = new List<FileUpload>();
        foreach (var file in files ?? [])
        {
            if (file.Length == 0) continue;
            if (file.Length > CrmDocument.MaxBytes) throw new InvalidOperationException($"حجم «{file.FileName}» بیش از ۱۰ مگابایت است.");
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            result.Add(new FileUpload(Path.GetFileName(file.FileName), buffer.ToArray()));
        }
        return result;
    }
}

public sealed record NoteFormModel(Guid AccountId, Guid? NoteId, SaveNoteCommand Command, IReadOnlyList<(Guid Id, string Name)> Attachments, string? AccountName);

public sealed record QuickActionModel(string Label, string Icon, string Path, bool Allowed, bool AccountActive);
