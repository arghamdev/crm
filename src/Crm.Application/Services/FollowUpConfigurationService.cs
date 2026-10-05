using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.FollowUps;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using static Crm.Application.Services.FollowUpSupport;
using P = Crm.Application.Services.FollowUpPermissions;

namespace Crm.Application.Services;

public interface IFollowUpConfigurationService
{
    FollowUpConfigurationDto GetOverview(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc);
    /// <summary>Installs the default SLA policies and one published template per follow-up type when the company has none.</summary>
    bool InstallDefaults(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc);
    FollowUpTemplateDto GetTemplate(Guid userId, OrganizationSelection organization, Guid? templateId);
    Guid CreateTemplate(Guid userId, OrganizationSelection organization, SaveFollowUpTemplateCommand command, DateTimeOffset nowUtc);
    Guid NewTemplateVersion(Guid userId, OrganizationSelection organization, Guid templateId, DateTimeOffset nowUtc);
    void SaveTemplate(Guid userId, OrganizationSelection organization, Guid templateId, SaveFollowUpTemplateCommand command, DateTimeOffset nowUtc);
    void PublishTemplate(Guid userId, OrganizationSelection organization, Guid templateId, DateTimeOffset nowUtc);
    SlaPolicyDto? GetPolicy(Guid userId, OrganizationSelection organization, Guid policyId);
    Guid SavePolicy(Guid userId, OrganizationSelection organization, Guid? policyId, SaveSlaPolicyCommand command, DateTimeOffset nowUtc);
    SlaPreviewDto PreviewPolicy(Guid userId, OrganizationSelection organization, SaveSlaPolicyCommand command, string? startDate, string? startTime, DateTimeOffset nowUtc);
    FollowUpQueueDto? GetQueue(Guid userId, OrganizationSelection organization, Guid queueId, DateTimeOffset nowUtc);
    Guid SaveQueue(Guid userId, OrganizationSelection organization, Guid? queueId, SaveFollowUpQueueCommand command, DateTimeOffset nowUtc);
    AssignmentSuggestionDto TestAssignment(Guid userId, OrganizationSelection organization, string? branchId, string? caseType, string? partFamily, string? language,
        Guid? queueId, DateTimeOffset nowUtc);
    /// <summary>Hands the open cases of an unavailable member to the queue's eligible members, so nobody's cases are left without an owner.</summary>
    int Redistribute(Guid userId, OrganizationSelection organization, Guid queueId, Guid memberUserId, string? reason, DateTimeOffset nowUtc);
}

/// <summary>Settings of the follow-up center: workflow templates (۹), SLA policies (۱۰), queues and assignment rules (۱۱).</summary>
public sealed class FollowUpConfigurationService(ICrmDataStore store, IAccessSnapshotService access) : IFollowUpConfigurationService
{
    private AccessSnapshot Require(Guid userId, OrganizationSelection organization, string permission = P.Configure)
    {
        var snapshot = access.Get(userId) ?? throw new UnauthorizedAccessException("No active access snapshot was found.");
        if (!Has(snapshot, organization.CompanyId, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
        return snapshot;
    }

    private static List<FollowUpOptionDto> Branches(CrmDataSet data, string companyId) =>
        data.Find<OrganizationUnit>(x => x.CompanyId == companyId && x.Type == OrganizationUnitType.Branch).OrderBy(x => x.Name)
            .Select(x => new FollowUpOptionDto(x.UnitId, x.Name)).ToList();

    public FollowUpConfigurationDto GetOverview(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Read(data =>
        {
            var company = organization.CompanyId;
            var templates = data.Find<FollowUpTemplate>(x => x.CompanyId == company);
            var templateIds = templates.Select(x => x.Id).ToArray();
            var stageCounts = data.Find<FollowUpTemplateStage>(x => templateIds.Contains(x.TemplateId)).GroupBy(x => x.TemplateId).ToDictionary(g => g.Key, g => g.Count());
            var open = data.Find<FollowUpCase>(x => x.CompanyId == company && x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled);
            var openByTemplate = open.GroupBy(x => x.TemplateId).ToDictionary(g => g.Key, g => g.Count());
            var drafts = templates.Where(x => x.Status == FollowUpTemplateStatus.Draft).Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // One row per template code: the newest version, flagged when a draft is waiting to be published.
            var summaries = templates.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Status == FollowUpTemplateStatus.Published).ThenByDescending(x => x.TemplateVersion).First())
                .OrderBy(x => x.Name)
                .Select(x => new FollowUpTemplateSummaryDto(x.Id, x.Code, x.Name, x.CaseType, x.TemplateVersion, x.Status, stageCounts.GetValueOrDefault(x.Id),
                    templates.Where(t => AccountGuard.Same(t.Code, x.Code)).Sum(t => openByTemplate.GetValueOrDefault(t.Id)), x.PublishedAtUtc,
                    drafts.Contains(x.Code) && x.Status != FollowUpTemplateStatus.Draft))
                .ToList();
            var policies = data.Find<SlaPolicy>(x => x.CompanyId == company).OrderBy(x => x.Priority).ThenBy(x => x.Name)
                .Select(x => PolicyDto(x, templates)).ToList();
            var loads = Loads(data, company);
            var queues = data.Find<FollowUpQueue>(x => x.CompanyId == company).OrderBy(x => x.RuleOrder).ThenBy(x => x.Name)
                .Select(x => QueueDto(data, x, loads, open, nowUtc)).ToList();
            var users = BranchUsers(data, company, null, null, nowUtc).Select(x => new FollowUpOptionDto(x.Id.ToString(), x.Name)).ToList();
            return new FollowUpConfigurationDto(summaries, policies, queues, users, Branches(data, company));
        });
    }

    public bool InstallDefaults(Guid userId, OrganizationSelection organization, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Write(data => FollowUpDefaults.Install(data, organization.CompanyId, userId, nowUtc));
    }

    // ───────────── ۹. الگوی گردش کار ─────────────

    public FollowUpTemplateDto GetTemplate(Guid userId, OrganizationSelection organization, Guid? templateId)
    {
        Require(userId, organization);
        return store.Read(data =>
        {
            var company = organization.CompanyId;
            var policies = data.Find<SlaPolicy>(x => x.CompanyId == company && x.IsActive).OrderBy(x => x.Priority)
                .Select(x => new FollowUpOptionDto(x.Id.ToString(), $"{x.Name} · {PriorityLabel(x.Priority)}")).ToList();
            var branches = Branches(data, company);
            if (templateId is null)
                return new FollowUpTemplateDto(Guid.Empty, string.Empty, string.Empty, "Proforma", 1, FollowUpTemplateStatus.Draft, "Company", "مدیریت فروش", null, null,
                    true, false, null, null, FollowUpPriority.Normal,
                    [new FollowUpTemplateStageInput("ثبت و بررسی درخواست", 30, "کارشناس فروش", true, null, null, false, null),
                     new FollowUpTemplateStageInput("انجام کار", 50, "کارشناس فروش", true, null, null, false, null),
                     new FollowUpTemplateStageInput("تأیید مشتری", 20, "کارشناس فروش", true, null, null, false, null)], 0, policies, branches, 0);
            var t = data.Find<FollowUpTemplate>(x => x.Id == templateId && x.CompanyId == company).SingleOrDefault()
                ?? throw new KeyNotFoundException("الگوی گردش کار پیدا نشد.");
            var stages = data.Find<FollowUpTemplateStage>(x => x.TemplateId == t.Id).OrderBy(x => x.Order)
                .Select(x => new FollowUpTemplateStageInput(x.Name, x.Weight, x.ResponsibleRole, x.Required, x.Checklist, x.Condition, x.ParallelWithPrevious, x.DurationHours))
                .ToList();
            var published = data.Find<FollowUpTemplate>(x => x.CompanyId == company && x.Code == t.Code && x.Status == FollowUpTemplateStatus.Published)
                .Select(x => x.TemplateVersion).DefaultIfEmpty(0).Max();
            return new FollowUpTemplateDto(t.Id, t.Code, t.Name, t.CaseType, t.TemplateVersion, t.Status, t.Scope, t.OwnerUnit, t.Description, t.ClosingCriteria,
                t.RequireAllRequiredStages, t.RequireCustomerApproval, t.Rules, t.SlaPolicyId, t.DefaultPriority, stages, published, policies, branches, t.Version);
        });
    }

    public Guid CreateTemplate(Guid userId, OrganizationSelection organization, SaveFollowUpTemplateCommand command, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Write(data =>
        {
            var company = organization.CompanyId;
            if (!FollowUpCaseTypes.Exists(command.CaseType)) throw new InvalidOperationException("نوع پیگیری معتبر نیست.");
            var code = RecordCodes.Next(data.Find<FollowUpTemplate>(x => x.CompanyId == company).Select(x => x.Code), "WF-", 101, 3);
            var template = new FollowUpTemplate(Guid.NewGuid(), company, code, command.Name ?? string.Empty, command.CaseType!, 1);
            Apply(data, template, command, company);
            data.Append(template);
            ReplaceStages(data, template, command.Stages);
            return template.Id;
        });
    }

    /// <summary>Published versions stay as they are; a new version starts as a draft copy. An existing draft of the same template is reused.</summary>
    public Guid NewTemplateVersion(Guid userId, OrganizationSelection organization, Guid templateId, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Write(data =>
        {
            var source = data.Find<FollowUpTemplate>(x => x.Id == templateId && x.CompanyId == organization.CompanyId).SingleOrDefault()
                ?? throw new KeyNotFoundException("الگوی گردش کار پیدا نشد.");
            var versions = data.Find<FollowUpTemplate>(x => x.CompanyId == source.CompanyId && x.Code == source.Code);
            if (versions.FirstOrDefault(x => x.Status == FollowUpTemplateStatus.Draft) is { } existing) return existing.Id;
            var latest = versions.OrderByDescending(x => x.TemplateVersion).First();
            var draft = latest.NewDraft(Guid.NewGuid());
            data.Append(draft);
            foreach (var s in data.Find<FollowUpTemplateStage>(x => x.TemplateId == latest.Id).OrderBy(x => x.Order))
                data.Append(new FollowUpTemplateStage(Guid.NewGuid(), draft.Id, s.Order, s.Name, s.Weight, s.ResponsibleRole, s.Required, s.Checklist, s.Condition,
                    s.ParallelWithPrevious, s.DurationHours));
            return draft.Id;
        });
    }

    public void SaveTemplate(Guid userId, OrganizationSelection organization, Guid templateId, SaveFollowUpTemplateCommand command, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        store.Write(data =>
        {
            var template = data.Find<FollowUpTemplate>(x => x.Id == templateId && x.CompanyId == organization.CompanyId).SingleOrDefault()
                ?? throw new KeyNotFoundException("الگوی گردش کار پیدا نشد.");
            if (template.Version != command.ExpectedVersion) throw new InvalidOperationException("الگو در این فاصله تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            if (!FollowUpCaseTypes.Exists(command.CaseType)) throw new InvalidOperationException("نوع پیگیری معتبر نیست.");
            Apply(data, template, command, organization.CompanyId);
            var stageTable = data.Table<FollowUpTemplateStage>();
            foreach (var old in data.Find<FollowUpTemplateStage>(x => x.TemplateId == template.Id)) stageTable.Remove(old);
            ReplaceStages(data, template, command.Stages);
            return true;
        });
    }

    public void PublishTemplate(Guid userId, OrganizationSelection organization, Guid templateId, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        store.Write(data =>
        {
            var template = data.Find<FollowUpTemplate>(x => x.Id == templateId && x.CompanyId == organization.CompanyId).SingleOrDefault()
                ?? throw new KeyNotFoundException("الگوی گردش کار پیدا نشد.");
            var stages = data.Find<FollowUpTemplateStage>(x => x.TemplateId == template.Id);
            template.Publish(stages, userId, nowUtc);
            // Open cases keep the version they started with; only the newest published version is offered to new cases.
            foreach (var previous in data.Find<FollowUpTemplate>(x => x.CompanyId == template.CompanyId && x.Code == template.Code && x.Id != template.Id &&
                         x.Status == FollowUpTemplateStatus.Published))
                previous.Retire();
            return true;
        });
    }

    private static void Apply(CrmDataSet data, FollowUpTemplate template, SaveFollowUpTemplateCommand command, string companyId)
    {
        if (command.SlaPolicyId is { } policyId && data.Find<SlaPolicy>(x => x.Id == policyId && x.CompanyId == companyId).Count == 0)
            throw new InvalidOperationException("سیاست مهلت انتخاب‌شده پیدا نشد.");
        var scope = string.IsNullOrWhiteSpace(command.Scope) || command.Scope == "Company" ? "Company" : command.Scope.Trim();
        if (scope != "Company" && data.Find<OrganizationUnit>(x => x.CompanyId == companyId && x.UnitId == scope).Count == 0)
            throw new InvalidOperationException("دامنهٔ الگو باید کل شرکت یا یکی از شعبه‌ها باشد.");
        foreach (var line in (command.Rules ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!line.Contains("=>", StringComparison.Ordinal))
                throw new InvalidOperationException($"قاعدهٔ «{line}» باید به شکل «شرط => اقدام» نوشته شود.");
        template.Update(command.Name ?? string.Empty, command.CaseType!, scope, command.OwnerUnit ?? string.Empty, command.Description, command.ClosingCriteria,
            command.RequireAllRequiredStages, command.RequireCustomerApproval, command.Rules, command.SlaPolicyId, command.DefaultPriority);
    }

    private static void ReplaceStages(CrmDataSet data, FollowUpTemplate template, IReadOnlyList<FollowUpTemplateStageInput> stages)
    {
        var rows = stages.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        if (rows.Count == 0) throw new InvalidOperationException("الگو باید حداقل یک مرحله داشته باشد.");
        if (rows.Count > 30) throw new InvalidOperationException("حداکثر ۳۰ مرحله مجاز است.");
        if (rows[0].ParallelWithPrevious) rows[0] = rows[0] with { ParallelWithPrevious = false };
        var order = 0;
        foreach (var s in rows)
            data.Append(new FollowUpTemplateStage(Guid.NewGuid(), template.Id, ++order, s.Name!, s.Weight, string.IsNullOrWhiteSpace(s.ResponsibleRole) ? "کارشناس فروش" : s.ResponsibleRole!,
                s.Required, s.Checklist, s.Condition, s.ParallelWithPrevious, s.DurationHours));
    }

    // ───────────── ۱۰. مهلت‌ها و هشدارها ─────────────

    private static SlaPolicyDto PolicyDto(SlaPolicy x, IReadOnlyCollection<FollowUpTemplate> templates)
    {
        var calendar = x.Calendar();
        return new SlaPolicyDto(x.Id, x.Name, x.Priority, x.CaseType, x.TimeZoneId, calendar.Days.OrderBy(d => ((int)d + 1) % 7).ToList(), x.WorkStart, x.WorkEnd,
            x.Holidays, x.FirstResponseHours, x.StageHours, x.ResolutionHours, x.PauseOnWaitingCustomer, x.PauseOnWaitingInternal, x.EscalationSteps(), x.IsActive,
            templates.Count(t => t.SlaPolicyId == x.Id && t.Status != FollowUpTemplateStatus.Retired), x.Version);
    }

    public SlaPolicyDto? GetPolicy(Guid userId, OrganizationSelection organization, Guid policyId)
    {
        Require(userId, organization);
        return store.Read(data => data.Find<SlaPolicy>(x => x.Id == policyId && x.CompanyId == organization.CompanyId).SingleOrDefault() is { } policy
            ? PolicyDto(policy, data.Find<FollowUpTemplate>(x => x.CompanyId == organization.CompanyId))
            : null);
    }

    public Guid SavePolicy(Guid userId, OrganizationSelection organization, Guid? policyId, SaveSlaPolicyCommand command, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Write(data =>
        {
            SlaPolicy policy;
            if (policyId is { } id)
            {
                policy = data.Find<SlaPolicy>(x => x.Id == id && x.CompanyId == organization.CompanyId).SingleOrDefault() ?? throw new KeyNotFoundException("سیاست مهلت پیدا نشد.");
                if (policy.Version != command.ExpectedVersion) throw new InvalidOperationException("سیاست در این فاصله تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            }
            else
            {
                policy = new SlaPolicy(Guid.NewGuid(), organization.CompanyId, command.Name ?? string.Empty, command.Priority, command.CaseType);
                data.Append(policy);
            }
            Update(policy, command);
            if (command.IsActive && data.Find<SlaPolicy>(x => x.CompanyId == organization.CompanyId && x.Id != policy.Id && x.IsActive && x.Priority == command.Priority)
                    .Any(x => AccountGuard.Same(x.CaseType ?? "", policy.CaseType ?? "")))
                throw new InvalidOperationException("برای این اولویت و نوع پیگیری سیاست فعال دیگری وجود دارد؛ یکی را غیرفعال کنید.");
            return policy.Id;
        });
    }

    private static void Update(SlaPolicy policy, SaveSlaPolicyCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.CaseType) && !FollowUpCaseTypes.Exists(command.CaseType)) throw new InvalidOperationException("نوع پیگیری معتبر نیست.");
        policy.Update(command.Name ?? string.Empty, command.Priority, string.IsNullOrWhiteSpace(command.CaseType) ? null : command.CaseType,
            string.IsNullOrWhiteSpace(command.TimeZoneId) ? "Asia/Tehran" : command.TimeZoneId, command.WorkDays ?? [], command.WorkStart ?? "", command.WorkEnd ?? "",
            command.Holidays, command.FirstResponseHours, command.StageHours, command.ResolutionHours, command.PauseOnWaitingCustomer, command.PauseOnWaitingInternal,
            command.Escalations ?? [], command.IsActive);
    }

    /// <summary>Shows where the due times of a case registered at the given moment would fall, without saving anything.</summary>
    public SlaPreviewDto PreviewPolicy(Guid userId, OrganizationSelection organization, SaveSlaPolicyCommand command, string? startDate, string? startTime, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        var policy = new SlaPolicy(Guid.NewGuid(), organization.CompanyId, string.IsNullOrWhiteSpace(command.Name) ? "پیش‌نمایش" : command.Name, command.Priority);
        Update(policy, command with { CaseType = null });
        var start = string.IsNullOrWhiteSpace(startDate) ? nowUtc : TehranTime.ToUtc(startDate, string.IsNullOrWhiteSpace(startTime) ? "09:00" : startTime, "زمان شروع") ?? nowUtc;
        var calendar = policy.Calendar();
        var first = calendar.AddWorkingMinutes(start, policy.FirstResponseHours * 60);
        var stage = calendar.AddWorkingMinutes(start, policy.StageHours * 60);
        var resolution = calendar.AddWorkingMinutes(start, policy.ResolutionHours * 60);
        var steps = policy.EscalationSteps().Select(x => $"{EscalationLabel(x.Target)}: {TehranTime.Format(stage.AddMinutes(x.OffsetMinutes))} " +
            (x.OffsetMinutes < 0 ? $"({-x.OffsetMinutes} دقیقه پیش از سررسید مرحله)" : x.OffsetMinutes == 0 ? "(در لحظهٔ سررسید)" : $"({x.OffsetMinutes} دقیقه پس از سررسید)")).ToList();
        return new SlaPreviewDto(TehranTime.Format(start), TehranTime.Format(first), TehranTime.Format(stage), TehranTime.Format(resolution), steps);
    }

    public static string EscalationLabel(EscalationTarget target) => target switch
    {
        EscalationTarget.StageOwner => "مسئول مرحله",
        EscalationTarget.CaseOwner => "مسئول پرونده",
        EscalationTarget.Supervisor => "سرپرست تیم",
        _ => "مدیر شعبه"
    };

    // ───────────── ۱۱. صف‌ها و تخصیص کار ─────────────

    private static FollowUpQueueDto QueueDto(CrmDataSet data, FollowUpQueue queue, Dictionary<Guid, int> loads, IReadOnlyCollection<FollowUpCase> open, DateTimeOffset nowUtc)
    {
        var members = data.Find<FollowUpQueueMember>(x => x.QueueId == queue.Id);
        var names = AccountGuard.UserNames(data, members.Select(x => x.UserId).ToArray());
        return new FollowUpQueueDto(queue.Id, queue.Name, queue.Country, queue.BranchId, queue.CaseType, queue.PartFamily, queue.Language, queue.Method, queue.Ordering,
            queue.OverflowQueueId, queue.NotifySupervisorOnOverflow, queue.RuleOrder, queue.IsActive,
            members.Select(m => new FollowUpQueueMemberDto(m.UserId, names.GetValueOrDefault(m.UserId, "—"), m.Capacity, loads.GetValueOrDefault(m.UserId), m.IsAvailable,
                m.AvailabilityNote)).OrderBy(x => x.Name).ToList(),
            open.Count(x => x.QueueId == queue.Id && x.Status == FollowUpStatus.AwaitingAssignment), queue.Version);
    }

    public FollowUpQueueDto? GetQueue(Guid userId, OrganizationSelection organization, Guid queueId, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Read(data =>
        {
            var queue = data.Find<FollowUpQueue>(x => x.Id == queueId && x.CompanyId == organization.CompanyId).SingleOrDefault();
            if (queue is null) return null;
            var open = data.Find<FollowUpCase>(x => x.QueueId == queueId && x.Status == FollowUpStatus.AwaitingAssignment);
            return QueueDto(data, queue, Loads(data, organization.CompanyId), open, nowUtc);
        });
    }

    public Guid SaveQueue(Guid userId, OrganizationSelection organization, Guid? queueId, SaveFollowUpQueueCommand command, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        return store.Write(data =>
        {
            var company = organization.CompanyId;
            FollowUpQueue queue;
            if (queueId is { } id)
            {
                queue = data.Find<FollowUpQueue>(x => x.Id == id && x.CompanyId == company).SingleOrDefault() ?? throw new KeyNotFoundException("صف پیدا نشد.");
                if (queue.Version != command.ExpectedVersion) throw new InvalidOperationException("صف در این فاصله تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            }
            else
            {
                queue = new FollowUpQueue(Guid.NewGuid(), company, command.Name ?? string.Empty);
                data.Append(queue);
            }
            if (!string.IsNullOrWhiteSpace(command.CaseType) && !FollowUpCaseTypes.Exists(command.CaseType)) throw new InvalidOperationException("نوع پیگیری معتبر نیست.");
            if (!string.IsNullOrWhiteSpace(command.BranchId) && data.Find<OrganizationUnit>(x => x.CompanyId == company && x.UnitId == command.BranchId).Count == 0)
                throw new InvalidOperationException("شعبهٔ صف معتبر نیست.");
            if (command.OverflowQueueId is { } overflow)
            {
                var target = data.Find<FollowUpQueue>(x => x.Id == overflow && x.CompanyId == company).SingleOrDefault()
                    ?? throw new InvalidOperationException("صف پشتیبان پیدا نشد.");
                if (target.OverflowQueueId == queue.Id) throw new InvalidOperationException("دو صف نمی‌توانند پشتیبان یکدیگر باشند.");
            }
            queue.Update(command.Name ?? string.Empty, command.BranchId, command.CaseType, command.PartFamily, command.Language, command.Method, command.Ordering,
                command.OverflowQueueId, command.NotifySupervisorOnOverflow, command.RuleOrder, command.IsActive);

            var allowed = BranchUsers(data, company, command.BranchId, null, nowUtc).Select(x => x.Id).ToHashSet();
            var wanted = command.Members.GroupBy(x => x.UserId).Select(g => g.Last()).ToList();
            if (wanted.FirstOrDefault(x => !allowed.Contains(x.UserId)) is { UserId: var stranger } && stranger != Guid.Empty)
                throw new InvalidOperationException("اعضای صف باید کاربران فعال شعبهٔ صف باشند.");
            var existing = queueId is null ? [] : data.Find<FollowUpQueueMember>(x => x.QueueId == queue.Id);
            var memberTable = data.Table<FollowUpQueueMember>();
            foreach (var member in existing.Where(m => wanted.All(w => w.UserId != m.UserId))) memberTable.Remove(member);
            foreach (var (memberId, capacity, available, note) in wanted)
            {
                var member = existing.FirstOrDefault(x => x.UserId == memberId);
                if (member is null)
                {
                    member = new FollowUpQueueMember(Guid.NewGuid(), queue.Id, memberId, capacity);
                    data.Append(member);
                }
                else member.SetCapacity(capacity);
                member.SetAvailability(available, note, member.Skills);
            }
            return queue.Id;
        });
    }

    public AssignmentSuggestionDto TestAssignment(Guid userId, OrganizationSelection organization, string? branchId, string? caseType, string? partFamily,
        string? language, Guid? queueId, DateTimeOffset nowUtc)
    {
        Require(userId, organization);
        if (string.IsNullOrWhiteSpace(branchId)) throw new InvalidOperationException("شعبه را برای آزمون تخصیص انتخاب کنید.");
        return store.Read(data => Suggest(data, organization.CompanyId, branchId.Trim(), FollowUpCaseTypes.Exists(caseType) ? caseType! : "General", partFamily,
            string.IsNullOrWhiteSpace(language) ? "fa" : language, nowUtc, queueId));
    }

    public int Redistribute(Guid userId, OrganizationSelection organization, Guid queueId, Guid memberUserId, string? reason, DateTimeOffset nowUtc)
    {
        Require(userId, organization, P.Assign);
        return store.Write(data =>
        {
            var queue = data.Find<FollowUpQueue>(x => x.Id == queueId && x.CompanyId == organization.CompanyId).SingleOrDefault() ?? throw new KeyNotFoundException("صف پیدا نشد.");
            var member = data.Find<FollowUpQueueMember>(x => x.QueueId == queue.Id && x.UserId == memberUserId).SingleOrDefault()
                ?? throw new InvalidOperationException("این کاربر عضو صف نیست.");
            if (member.IsAvailable) member.SetAvailability(false, string.IsNullOrWhiteSpace(reason) ? "خارج از دسترس" : reason, member.Skills);
            var cases = data.Find<FollowUpCase>(x => x.CompanyId == organization.CompanyId && x.OwnerUserId == memberUserId &&
                    x.Status != FollowUpStatus.Closed && x.Status != FollowUpStatus.Cancelled)
                .OrderByDescending(x => x.Priority).ThenBy(x => x.NextActionAtUtc).ToList();
            var names = AccountGuard.UserNames(data, [memberUserId]);
            var moved = 0;
            foreach (var c in cases)
            {
                var suggestion = Suggest(data, c.CompanyId, c.BranchId, c.CaseType, c.PartFamily, c.Language, nowUtc, queue.Id);
                if (suggestion.UserId is not { } next || next == memberUserId) continue;
                c.AssignOwner(next, suggestion.QueueId);
                if (c.NextActionOwnerUserId == memberUserId && c.NextAction is not null)
                    c.PlanNextAction(c.NextAction, c.NextActionAtUtc is { } at && at > nowUtc ? at : nowUtc.AddHours(2), next, nowUtc);
                if (suggestion.QueueId is { } q) MarkAssigned(data, q, next, nowUtc);
                foreach (var stage in data.Find<FollowUpStage>(x => x.CaseId == c.Id && x.ResponsibleUserId == memberUserId).Where(x => x.IsWorking))
                    stage.AssignResponsible(next);
                Log(data, c, "Owner", $"مسئول پرونده: {names.GetValueOrDefault(memberUserId, "—")} → {suggestion.UserName}",
                    $"بازتوزیع به‌علت عدم دسترسی: {reason ?? "خارج از دسترس"} · {suggestion.Explanation}", userId, nowUtc);
                Notify(data, c, [next], $"پرونده {c.Code} به شما سپرده شد", $"{c.Subject} — بازتوزیع از صف «{queue.Name}»",
                    $"fu-owner:{c.Id}:{next}:{nowUtc:yyyyMMddHHmm}", nowUtc);
                moved++;
            }
            return moved;
        });
    }
}
