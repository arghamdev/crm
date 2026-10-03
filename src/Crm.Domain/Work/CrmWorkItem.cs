using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.Work;

public sealed class CrmWorkItem(
    Guid id,
    string title,
    string priority,
    DateTimeOffset dueAtUtc,
    Guid assignedToUserId,
    string companyId,
    string branchId,
    string? territoryId) : Entity(id), IOrganizationScoped
{
    public string Title { get; } = title.Trim();
    public string Priority { get; } = priority;
    public DateTimeOffset DueAtUtc { get; } = dueAtUtc;
    public Guid AssignedToUserId { get; } = assignedToUserId;
    public string CompanyId { get; } = companyId.Trim();
    public string BranchId { get; } = branchId.Trim();
    public string? TerritoryId { get; } = string.IsNullOrWhiteSpace(territoryId) ? null : territoryId.Trim();
    public bool IsDone { get; private set; }

    private CrmWorkItem() : this(Guid.Empty, "EF", string.Empty, DateTimeOffset.MinValue, Guid.Empty, "EF", "EF", null) { }

    public void Complete() { IsDone = true; Touch(); }
}
