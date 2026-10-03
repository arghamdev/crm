using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Sales;

public enum LeadStatus { New, Assigned, Contacted, Qualified, Nurture, Disqualified, Duplicate, Invalid, Converted }

public sealed class Lead(
    Guid id,
    string code,
    string name,
    string contact,
    string source,
    string owner,
    string companyId,
    string branchId,
    string? territoryId,
    Guid? customerId = null,
    Guid? ownerUserId = null,
    string? phone = null,
    string? email = null,
    DateTimeOffset? firstContactDueAtUtc = null) : Entity(id), IOrganizationScoped
{
    public string Code { get; } = Required(code, nameof(code));
    public string Name { get; private set; } = Required(name, nameof(name));
    public string Contact { get; private set; } = contact?.Trim() ?? string.Empty;
    public string Source { get; private set; } = Required(source, nameof(source));
    public string Owner { get; private set; } = owner?.Trim() ?? string.Empty;
    public Guid? OwnerUserId { get; private set; } = ownerUserId;
    public string? Phone { get; private set; } = Optional(phone);
    public string? Email { get; private set; } = NormalizeEmail(email);
    public string CompanyId { get; } = Required(companyId, nameof(companyId));
    public string BranchId { get; } = Required(branchId, nameof(branchId));
    public string? TerritoryId { get; } = Optional(territoryId);
    public int Score { get; private set; } = 50;
    public LeadStatus Status { get; private set; } = LeadStatus.New;
    public Guid? CustomerId { get; private set; } = customerId;
    public Guid? ConvertedOpportunityId { get; private set; }
    public DateTimeOffset? AssignedAtUtc { get; private set; }
    public DateTimeOffset FirstContactDueAtUtc { get; private set; } = firstContactDueAtUtc ?? DateTimeOffset.UtcNow.AddHours(4);
    public DateTimeOffset? FirstContactAtUtc { get; private set; }
    public DateTimeOffset LastActivityAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public string? NextAction { get; private set; }
    public DateTimeOffset? NextActionAtUtc { get; private set; }
    public string? StatusReason { get; private set; }

    private Lead() : this(Guid.Empty, "EF", "EF", string.Empty, "EF", string.Empty, "EF", "EF", null) { }

    public void ApplyScore(int score)
    {
        EnsureOpen();
        Score = Math.Clamp(score, 0, 100);
        Touch();
    }

    public void Assign(Guid ownerUserId, string owner, DateTimeOffset assignedAtUtc, DateTimeOffset firstContactDueAtUtc, string reason)
    {
        EnsureOpen();
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user is required.", nameof(ownerUserId));
        if (firstContactDueAtUtc <= assignedAtUtc) throw new InvalidOperationException("مهلت اولین تماس باید بعد از زمان تخصیص باشد.");
        OwnerUserId = ownerUserId;
        Owner = Required(owner, nameof(owner));
        AssignedAtUtc = assignedAtUtc;
        FirstContactDueAtUtc = firstContactDueAtUtc;
        if (Status == LeadStatus.New) Status = LeadStatus.Assigned;
        StatusReason = Required(reason, nameof(reason));
        LastActivityAtUtc = assignedAtUtc;
        Touch();
    }

    public void MarkContacted(DateTimeOffset contactedAtUtc, string nextAction, DateTimeOffset nextActionAtUtc, string reason)
    {
        EnsureOpen();
        if (Status is not (LeadStatus.New or LeadStatus.Assigned or LeadStatus.Contacted or LeadStatus.Nurture))
            throw new InvalidOperationException("ثبت تماس برای وضعیت فعلی سرنخ مجاز نیست.");
        if (nextActionAtUtc <= contactedAtUtc) throw new InvalidOperationException("زمان اقدام بعدی باید در آینده باشد.");
        Status = LeadStatus.Contacted;
        FirstContactAtUtc ??= contactedAtUtc;
        LastActivityAtUtc = contactedAtUtc;
        NextAction = Required(nextAction, nameof(nextAction));
        NextActionAtUtc = nextActionAtUtc;
        StatusReason = Required(reason, nameof(reason));
        Touch();
    }

    public void Qualify(int score, string reason = "Qualification completed")
    {
        EnsureOpen();
        if (Status != LeadStatus.Contacted)
            throw new InvalidOperationException("سرنخ باید پیش از احراز صلاحیت در وضعیت تماس‌شده باشد.");
        if (score < 60) throw new InvalidOperationException("امتیاز سرنخ واجد شرایط باید حداقل ۶۰ باشد.");
        Score = Math.Clamp(score, 0, 100);
        Status = LeadStatus.Qualified;
        StatusReason = Required(reason, nameof(reason));
        LastActivityAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Nurture(string reason, string nextAction, DateTimeOffset nextActionAtUtc, DateTimeOffset changedAtUtc)
    {
        EnsureOpen();
        if (Status is not (LeadStatus.Contacted or LeadStatus.Qualified or LeadStatus.Nurture))
            throw new InvalidOperationException("پرورش سرنخ پس از تماس یا احراز صلاحیت مجاز است.");
        if (nextActionAtUtc <= changedAtUtc) throw new InvalidOperationException("پرورش سرنخ به اقدام بعدی آینده نیاز دارد.");
        Status = LeadStatus.Nurture;
        StatusReason = Required(reason, nameof(reason));
        NextAction = Required(nextAction, nameof(nextAction));
        NextActionAtUtc = nextActionAtUtc;
        LastActivityAtUtc = changedAtUtc;
        Touch();
    }

    public void Close(LeadStatus status, string reason, DateTimeOffset changedAtUtc)
    {
        EnsureOpen();
        if (status is not (LeadStatus.Disqualified or LeadStatus.Duplicate or LeadStatus.Invalid))
            throw new InvalidOperationException("وضعیت خاتمه سرنخ معتبر نیست.");
        Status = status;
        StatusReason = Required(reason, nameof(reason));
        NextAction = null;
        NextActionAtUtc = null;
        LastActivityAtUtc = changedAtUtc;
        Touch();
    }

    public void Convert(Guid customerId, Guid opportunityId, DateTimeOffset convertedAtUtc)
    {
        if (Status != LeadStatus.Qualified) throw new InvalidOperationException("فقط سرنخ واجد شرایط قابل تبدیل است.");
        if (customerId == Guid.Empty || opportunityId == Guid.Empty)
            throw new InvalidOperationException("CustomerId و OpportunityId برای تبدیل الزامی‌اند.");
        Status = LeadStatus.Converted;
        CustomerId = customerId;
        ConvertedOpportunityId = opportunityId;
        StatusReason = "Converted to customer and opportunity";
        NextAction = null;
        NextActionAtUtc = null;
        LastActivityAtUtc = convertedAtUtc;
        Touch();
    }

    public void ReassignCustomer(Guid customerId)
    {
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId is required.", nameof(customerId));
        CustomerId = customerId;
        Touch();
    }

    private void EnsureOpen()
    {
        if (Status is LeadStatus.Disqualified or LeadStatus.Duplicate or LeadStatus.Invalid or LeadStatus.Converted)
            throw new InvalidOperationException("سرنخ بسته‌شده قابل تغییر نیست.");
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeEmail(string? value) => Optional(value)?.ToUpperInvariant();
}
