using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.FollowUps;
using Crm.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>
/// تنظیمات مرکز پیگیری: workflow templates with versions (۹), SLA policies and escalation (۱۰), queues and assignment rules (۱۱),
/// and the supervision board (۱۲). The services enforce FollowUp.Configure / FollowUp.Supervise again on every call.
/// </summary>
public sealed class FollowUpSettingsController(
    IFollowUpConfigurationService settings,
    IFollowUpSupervisionService supervision,
    ICurrentUserContext current) : Controller
{
    private Guid UserId => current.CrmUserId;
    private OrganizationSelection Org => current.RequiredOrganization();
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings")]
    public IActionResult Index(string tab = "templates") => Guarded(() =>
    {
        ViewBag.Tab = tab is "policies" or "queues" ? tab : "templates";
        return View(settings.GetOverview(UserId, Org, Now));
    });

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/defaults")]
    [ValidateAntiForgeryToken]
    public IActionResult InstallDefaults() => Guarded(() =>
    {
        settings.InstallDefaults(UserId, Org, Now);
        return Redirect("/follow-ups/settings");
    });

    // ───────────── ۹. طراحی الگوی گردش کار ─────────────

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/templates/new")]
    public IActionResult NewTemplate() => Guarded(() => View("Template", new FollowUpTemplatePage(settings.GetTemplate(UserId, Org, null), null)));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/templates/{id:guid}")]
    public IActionResult Template(Guid id) => Guarded(() => View("Template", new FollowUpTemplatePage(settings.GetTemplate(UserId, Org, id), null)));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/templates")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateTemplate(SaveFollowUpTemplateCommand command) => Save(
        () => Redirect($"/follow-ups/settings/templates/{settings.CreateTemplate(UserId, Org, Clean(command), Now)}?saved=1"),
        message => TemplateError(null, command, message));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/templates/{id:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveTemplate(Guid id, SaveFollowUpTemplateCommand command, string? publish) => Save(() =>
    {
        settings.SaveTemplate(UserId, Org, id, Clean(command), Now);
        if (publish == "true") settings.PublishTemplate(UserId, Org, id, Now);
        return Redirect($"/follow-ups/settings/templates/{id}?{(publish == "true" ? "published" : "saved")}=1");
    }, message => TemplateError(id, command, message));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/templates/{id:guid}/version")]
    [ValidateAntiForgeryToken]
    public IActionResult NewVersion(Guid id) => Guarded(() => Redirect($"/follow-ups/settings/templates/{settings.NewTemplateVersion(UserId, Org, id, Now)}"));

    /// <summary>Drops empty stage rows and builds the rule text from the «اگر / آنگاه» rows.</summary>
    private SaveFollowUpTemplateCommand Clean(SaveFollowUpTemplateCommand command)
    {
        var conditions = Request.Form["RuleCondition"];
        var actions = Request.Form["RuleAction"];
        var rules = Enumerable.Range(0, Math.Min(conditions.Count, actions.Count))
            .Where(i => !string.IsNullOrWhiteSpace(conditions[i]) && !string.IsNullOrWhiteSpace(actions[i]))
            .Select(i => $"{conditions[i]!.Trim()} => {actions[i]!.Trim()}");
        return command with
        {
            Stages = (command.Stages ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList(),
            Rules = conditions.Count > 0 ? string.Join('\n', rules) : command.Rules
        };
    }

    private IActionResult TemplateError(Guid? id, SaveFollowUpTemplateCommand command, string message)
    {
        var dto = settings.GetTemplate(UserId, Org, id);
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return View("Template", new FollowUpTemplatePage(dto, command));
    }

    // ───────────── ۱۰. مهلت‌ها و هشدارها ─────────────

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/policies/new")]
    public IActionResult NewPolicy() => Guarded(() => View("Policy", new FollowUpPolicyPage(null, DefaultPolicy(), null)));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/policies/{id:guid}")]
    public IActionResult Policy(Guid id) => Guarded(() =>
    {
        var policy = settings.GetPolicy(UserId, Org, id) ?? throw new KeyNotFoundException();
        return View("Policy", new FollowUpPolicyPage(policy.Id, Command(policy), null));
    });

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/policies")]
    [ValidateAntiForgeryToken]
    public IActionResult SavePolicy(Guid? id, SaveSlaPolicyCommand command) => Save(() =>
    {
        var saved = settings.SavePolicy(UserId, Org, id, Steps(command), Now);
        return Redirect($"/follow-ups/settings/policies/{saved}?saved=1");
    }, message =>
    {
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return View("Policy", new FollowUpPolicyPage(id, command, null));
    });

    /// <summary>«پیش‌نمایش محاسبه»: where the due times and escalations of a case registered at the chosen moment would fall.</summary>
    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/policies/preview")]
    [ValidateAntiForgeryToken]
    public IActionResult Preview(SaveSlaPolicyCommand command, string? previewDate, string? previewTime)
    {
        try { return PartialView("_SlaPreview", settings.PreviewPolicy(UserId, Org, Steps(command), previewDate, previewTime, Now)); }
        catch (InvalidOperationException exception) { return Content($"<p class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(exception.Message)}</p>", "text/html; charset=utf-8"); }
    }

    /// <summary>Escalation rows arrive as EscalationOffset[] / EscalationTarget[] pairs.</summary>
    private SaveSlaPolicyCommand Steps(SaveSlaPolicyCommand command)
    {
        var offsets = Request.Form["EscalationOffset"];
        var targets = Request.Form["EscalationTarget"];
        var steps = new List<SlaEscalation>();
        for (var i = 0; i < Math.Min(offsets.Count, targets.Count); i++)
            if (int.TryParse(Crm.Domain.Common.PersianText.Normalize(offsets[i]), out var offset) && Enum.TryParse<EscalationTarget>(targets[i], out var target))
                steps.Add(new SlaEscalation(offset, target));
        return command with { Escalations = steps };
    }

    private static SaveSlaPolicyCommand DefaultPolicy() => new("سیاست جدید", FollowUpPriority.Normal, null, "Asia/Tehran",
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday], "08:00", "17:00", null, 2, 8, 24, true, false,
        [new(-30, EscalationTarget.StageOwner), new(0, EscalationTarget.Supervisor), new(120, EscalationTarget.BranchManager)], true, 0);

    private static SaveSlaPolicyCommand Command(SlaPolicyDto p) => new(p.Name, p.Priority, p.CaseType, p.TimeZoneId, p.WorkDays, p.WorkStart, p.WorkEnd, p.Holidays,
        p.FirstResponseHours, p.StageHours, p.ResolutionHours, p.PauseOnWaitingCustomer, p.PauseOnWaitingInternal, p.Escalations, p.IsActive, p.Version);

    // ───────────── ۱۱. صف‌ها و تخصیص کار ─────────────

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/queues/new")]
    public IActionResult NewQueue() => Guarded(() => View("Queue", QueuePage(null, null)));

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpGet("/follow-ups/settings/queues/{id:guid}")]
    public IActionResult Queue(Guid id, string tab = "queue") => Guarded(() =>
    {
        ViewBag.Tab = tab;
        var queue = settings.GetQueue(UserId, Org, id, Now) ?? throw new KeyNotFoundException();
        return View("Queue", QueuePage(queue, Suggest(queue)));
    });

    /// <summary>«پیشنهاد تخصیص»: who the next case entering this queue would go to right now (no case is created).</summary>
    private AssignmentSuggestionDto? Suggest(FollowUpQueueDto queue)
    {
        var branch = queue.BranchId ?? settings.GetOverview(UserId, Org, Now).Branches.FirstOrDefault()?.Value;
        if (branch is null) return null;
        try { return settings.TestAssignment(UserId, Org, branch, queue.CaseType ?? "Proforma", queue.PartFamily, queue.Language, queue.Id, Now); }
        catch (InvalidOperationException) { return null; }
    }

    private FollowUpQueuePage QueuePage(FollowUpQueueDto? queue, AssignmentSuggestionDto? test) =>
        new(queue, settings.GetOverview(UserId, Org, Now), test);

    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/queues")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveQueue(Guid? id, SaveFollowUpQueueCommand command) => Save(() =>
    {
        var members = new List<(Guid, int, bool, string?)>();
        var users = Request.Form["MemberUserId"];
        var capacities = Request.Form["MemberCapacity"];
        var notes = Request.Form["MemberNote"];
        var available = Request.Form["MemberAvailable"].Select(x => x ?? "").ToHashSet();
        for (var i = 0; i < users.Count; i++)
            if (Guid.TryParse(users[i], out var user) && user != Guid.Empty)
                members.Add((user, int.TryParse(i < capacities.Count ? capacities[i] : null, out var cap) ? cap : 10, available.Contains(users[i] ?? ""),
                    i < notes.Count ? notes[i] : null));
        var saved = settings.SaveQueue(UserId, Org, id, command with { Members = members }, Now);
        return Redirect($"/follow-ups/settings/queues/{saved}?saved=1");
    }, message =>
    {
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return View("Queue", QueuePage(id is { } q ? settings.GetQueue(UserId, Org, q, Now) : null, null));
    });

    /// <summary>«آزمون تخصیص»: which queue and person a case with these attributes would go to, without creating anything.</summary>
    [Authorize(Policy = "perm:FollowUp.Configure")]
    [HttpPost("/follow-ups/settings/queues/test")]
    [ValidateAntiForgeryToken]
    public IActionResult TestAssignment(string? branchId, string? caseType, string? partFamily, string? language, Guid? queueId)
    {
        try { return PartialView("_AssignmentTest", settings.TestAssignment(UserId, Org, branchId, caseType, partFamily, language, queueId, Now)); }
        catch (InvalidOperationException exception) { return Content($"<p class=\"validation-summary\">{System.Net.WebUtility.HtmlEncode(exception.Message)}</p>", "text/html; charset=utf-8"); }
    }

    [Authorize(Policy = "perm:FollowUp.Assign")]
    [HttpPost("/follow-ups/settings/queues/{id:guid}/members/{userId:guid}/redistribute")]
    [ValidateAntiForgeryToken]
    public IActionResult Redistribute(Guid id, Guid userId, string? reason) => Guarded(() =>
    {
        var moved = settings.Redistribute(UserId, Org, id, userId, reason, Now);
        return Redirect($"/follow-ups/settings/queues/{id}?moved={moved}");
    });

    // ───────────── ۱۲. نظارت مدیر ─────────────

    [Authorize(Policy = "perm:FollowUp.Supervise")]
    [HttpGet("/follow-ups/supervision")]
    public IActionResult Supervision(string? branchId = null, Guid? queueId = null, string? period = null) =>
        Guarded(() => View("Supervision", supervision.Get(UserId, Org, new FollowUpSupervisionQuery(branchId, queueId, period), Now)));

    [Authorize(Policy = "perm:FollowUp.Supervise")]
    [HttpGet("/follow-ups/supervision/export")]
    public IActionResult Export(string? branchId = null, Guid? queueId = null, string? period = null) => Guarded(() =>
    {
        var csv = supervision.ExportCsv(UserId, Org, new FollowUpSupervisionQuery(branchId, queueId, period), Now);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8",
            $"follow-up-supervision-{DateTime.UtcNow:yyyyMMdd}.csv");
    });

    [Authorize(Policy = "perm:FollowUp.Supervise")]
    [HttpPost("/follow-ups/supervision/views")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveView(string? name, string? branchId, Guid? queueId, string? period) => Guarded(() =>
    {
        supervision.SaveView(UserId, Org, name, new FollowUpSupervisionQuery(branchId, queueId, period));
        return Redirect($"/follow-ups/supervision?branchId={Uri.EscapeDataString(branchId ?? "")}&queueId={queueId}&period={period}");
    });

    [Authorize(Policy = "perm:FollowUp.Supervise")]
    [HttpPost("/follow-ups/supervision/views/{viewId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteView(Guid viewId) => Guarded(() =>
    {
        supervision.DeleteView(UserId, Org, viewId);
        return Redirect("/follow-ups/supervision");
    });

    // ───────────── helpers ─────────────

    private IActionResult Guarded(Func<IActionResult> action)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception)
        {
            TempData["FollowUpError"] = exception.Message;
            return Redirect(Request.Headers.Referer.FirstOrDefault() ?? "/follow-ups/settings");
        }
    }

    private IActionResult Save(Func<IActionResult> action, Func<string, IActionResult> onError)
    {
        try { return action(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return onError(exception.Message); }
    }
}

public sealed record FollowUpTemplatePage(FollowUpTemplateDto Template, SaveFollowUpTemplateCommand? Posted);
public sealed record FollowUpPolicyPage(Guid? Id, SaveSlaPolicyCommand Command, SlaPreviewDto? Preview);
public sealed record FollowUpQueuePage(FollowUpQueueDto? Queue, FollowUpConfigurationDto Overview, AssignmentSuggestionDto? Test);
