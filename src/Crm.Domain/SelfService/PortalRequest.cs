using Crm.Domain.Common;
using Crm.Domain.Organization;

namespace Crm.Domain.SelfService;

public enum PortalRequestKind { Order, Lead, Complaint, Claim, AccessInvite, AccessRevoke }
public enum PortalRequestStatus { Submitted, InReview, Accepted, Rejected, Cancelled }

public sealed class PortalRequest : Entity, IOrganizationScoped
{
    public string CompanyId { get; private set; } = "";
    public string BranchId { get; private set; } = "";
    public string? TerritoryId { get; private set; }
    public Guid DealerId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid OperationId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public PortalRequestKind Kind { get; private set; }
    public PortalRequestStatus Status { get; private set; } = PortalRequestStatus.Submitted;
    public string Subject { get; private set; } = "";
    public string Description { get; private set; } = "";
    public Guid? CustomerId { get; private set; }
    public string? ProductCode { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public string? Source { get; private set; }
    public DateTimeOffset? PriceAtUtc { get; private set; }
    public string? Email { get; private set; }
    public Guid? TargetUserId { get; private set; }
    public string PublicReply { get; private set; } = "";
    public Guid? ReviewedByUserId { get; private set; }
    public Guid? LinkedRecordId { get; private set; }
    public DateTimeOffset? ProtectedUntilUtc { get; private set; }
    public string? ProtectionKey { get; private set; }

    private PortalRequest() : base(Guid.Empty) { }
    public PortalRequest(Guid id, string companyId, string branchId, string? territoryId, Guid dealerId,
        Guid actor, Guid operationId, string fingerprint, PortalRequestKind kind, string subject, string description,
        Guid? customerId, string? productCode, decimal quantity, decimal unitPrice, string? source,
        DateTimeOffset? priceAtUtc, string? email, Guid? targetUserId) : base(id)
    {
        CompanyId=companyId; BranchId=branchId; TerritoryId=territoryId; DealerId=dealerId;
        CreatedByUserId=actor; OperationId=operationId; Fingerprint=fingerprint; Kind=kind;
        Subject=subject; Description=description; CustomerId=customerId; ProductCode=productCode;
        Quantity=quantity; UnitPrice=unitPrice; Source=source; PriceAtUtc=priceAtUtc; Email=email; TargetUserId=targetUserId;
    }
    public void Decide(PortalRequestStatus status, string reply, Guid actor, Guid? linkedId = null, DateTimeOffset? protection = null, string? protectionKey = null)
    {
        if (Status is PortalRequestStatus.Accepted or PortalRequestStatus.Rejected or PortalRequestStatus.Cancelled)
            throw new InvalidOperationException("درخواست نهایی شده است.");
        if (status is not (PortalRequestStatus.InReview or PortalRequestStatus.Accepted or PortalRequestStatus.Rejected or PortalRequestStatus.Cancelled))
            throw new InvalidOperationException("انتقال وضعیت مجاز نیست.");
        Status=status; PublicReply=reply; ReviewedByUserId=actor; LinkedRecordId=linkedId; ProtectedUntilUtc=protection; ProtectionKey=protectionKey; Touch();
    }
    public void ReleaseExpiredProtection(DateTimeOffset now)
    {
        if (ProtectionKey is not null && ProtectedUntilUtc <= now) { ProtectionKey=null; Touch(); }
    }
    public void ReassignCustomer(Guid customerId)
    { if(customerId==Guid.Empty)throw new ArgumentException("Customer is required."); CustomerId=customerId;Touch(); }
}
