using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.SelfService;

public enum VisitStatus { Planned, CheckedIn, Completed, Cancelled }

public sealed class MobileVisit : Entity, IOrganizationScoped
{
    public string CompanyId { get; private set; } = "";
    public string BranchId { get; private set; } = "";
    public string? TerritoryId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset PlannedAtUtc { get; private set; }
    public string Purpose { get; private set; } = "";
    public VisitStatus Status { get; private set; } = VisitStatus.Planned;
    public DateTimeOffset? CheckedInAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? LastReceivedAtUtc { get; private set; }
    public string Outcome { get; private set; } = "";
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public bool LocationConsent { get; private set; }

    private MobileVisit() : base(Guid.Empty) { }
    public MobileVisit(Guid id, string company, string branch, string? territory, Guid customerId,
        Guid owner, DateTimeOffset plannedAtUtc, string purpose) : base(id)
    { CompanyId=company; BranchId=branch; TerritoryId=territory; CustomerId=customerId; OwnerUserId=owner; PlannedAtUtc=plannedAtUtc; Purpose=purpose; }
    public void Transition(VisitStatus status, DateTimeOffset occurred, DateTimeOffset received, string outcome,
        bool consent, decimal? latitude, decimal? longitude)
    {
        if (status == VisitStatus.CheckedIn && Status == VisitStatus.Planned)
        {
            CheckedInAtUtc=occurred; LocationConsent=consent; Latitude=latitude; Longitude=longitude;
        }
        else if (status == VisitStatus.Completed && Status == VisitStatus.CheckedIn)
        {
            if (occurred < CheckedInAtUtc) throw new InvalidOperationException("پایان بازدید نمی‌تواند قبل از شروع باشد.");
            CompletedAtUtc=occurred;
        }
        else if (status != VisitStatus.Cancelled || Status != VisitStatus.Planned)
            throw new InvalidOperationException("انتقال وضعیت بازدید مجاز نیست.");
        Status=status; Outcome=outcome; LastReceivedAtUtc=received; Touch();
    }
    public void ReassignCustomer(Guid customerId)
    { if(customerId==Guid.Empty)throw new ArgumentException("Customer is required."); CustomerId=customerId;Touch(); }
}

public sealed class MobileOperationReceipt : Entity
{
    public string CompanyId { get; private set; } = "";
    public Guid ActorUserId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid VisitId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public long AppliedVersion { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    private MobileOperationReceipt() : base(Guid.Empty) { }
    public MobileOperationReceipt(Guid id, string company, Guid actor, Guid operation, Guid visit,
        string fingerprint, long version, DateTimeOffset received) : base(id)
    { CompanyId=company; ActorUserId=actor; OperationId=operation; VisitId=visit; Fingerprint=fingerprint; AppliedVersion=version; ReceivedAtUtc=received; }
}
