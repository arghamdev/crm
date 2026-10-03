using Crm.Domain.Common;

namespace Crm.Domain.Notifications;

public enum NotificationChannel { Email, Sms }
public enum NotificationStatus { Pending, Sent, DeadLetter }
public enum NotificationCategory { ServiceEscalation, CommissionApproval, GuaranteeExpiry }

/// <summary>A user's delivery settings. Email is on by default; SMS needs a verified-format mobile number.</summary>
public sealed class NotificationPreference : Entity
{
    public NotificationPreference(Guid userId) : base(userId) { }

    private NotificationPreference() : base(Guid.Empty) { }

    public Guid UserId => Id;
    public string? Mobile { get; private set; }
    public bool EmailEnabled { get; private set; } = true;
    public bool SmsEnabled { get; private set; }
    /// <summary>Comma-separated categories the user opted out of.</summary>
    public string? MutedCategories { get; private set; }

    public bool Mutes(NotificationCategory category) =>
        MutedCategories?.Split(',').Contains(category.ToString(), StringComparer.Ordinal) == true;

    public void Update(string? mobile, bool emailEnabled, bool smsEnabled, IEnumerable<NotificationCategory> muted)
    {
        var normalized = IranianIdentifiers.NormalizeMobile(mobile);
        if (normalized is not null && !IranianIdentifiers.IsValidMobile(normalized))
            throw new InvalidOperationException("شمارهٔ همراه باید ۱۱ رقم باشد و با ۰۹ شروع شود.");
        if (smsEnabled && normalized is null) throw new InvalidOperationException("برای دریافت پیامک، شمارهٔ همراه را وارد کنید.");
        Mobile = normalized;
        EmailEnabled = emailEnabled;
        SmsEnabled = smsEnabled;
        var list = muted.Distinct().Order().Select(x => x.ToString()).ToList();
        MutedCategories = list.Count == 0 ? null : string.Join(',', list);
        Touch();
    }
}

/// <summary>
/// Outbox row for one email or SMS. The dedup key (per channel) makes enqueueing idempotent, so a re-run of the job that
/// raised it does not message the user twice. Delivery backs off 1, 5, 15, 60, 240 minutes and dead-letters after 6 tries.
/// </summary>
public sealed class NotificationMessage : Entity
{
    public const int MaxAttempts = 6;

    public NotificationMessage(Guid id, string companyId, Guid recipientUserId, NotificationChannel channel, string address,
        NotificationCategory category, string subject, string body, string? sourceReference, string dedupKey, DateTimeOffset nowUtc) : base(id)
    {
        CompanyId = companyId;
        RecipientUserId = recipientUserId;
        Channel = channel;
        Address = Limit(address, 256) ?? throw new ArgumentException("Address required.", nameof(address));
        Category = category;
        Subject = Limit(subject, 200) ?? throw new ArgumentException("Subject required.", nameof(subject));
        Body = Limit(body, 1000) ?? throw new ArgumentException("Body required.", nameof(body));
        SourceReference = Limit(sourceReference, 120);
        DedupKey = Limit(dedupKey, 160) ?? throw new ArgumentException("Dedup key required.", nameof(dedupKey));
        NextAttemptAtUtc = nowUtc;
    }

    private NotificationMessage() : base(Guid.Empty)
    {
        CompanyId = Address = Subject = Body = DedupKey = "EF";
    }

    public string CompanyId { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Address { get; private set; }
    public NotificationCategory Category { get; private set; }
    public string Subject { get; private set; }
    public string Body { get; private set; }
    public string? SourceReference { get; private set; }
    public string DedupKey { get; private set; }
    public NotificationStatus Status { get; private set; } = NotificationStatus.Pending;
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset? SentAtUtc { get; private set; }

    public void MarkSent(string providerMessageId, DateTimeOffset nowUtc)
    {
        if (Status == NotificationStatus.Sent) return;
        AttemptCount++;
        Status = NotificationStatus.Sent;
        ProviderMessageId = Limit(providerMessageId, 120);
        SentAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        LastError = null;
        Touch();
    }

    public void MarkFailed(string error, DateTimeOffset nowUtc)
    {
        if (Status != NotificationStatus.Pending) return;
        AttemptCount++;
        LastError = error.Length <= 500 ? error : error[..500];
        if (AttemptCount >= MaxAttempts)
        {
            Status = NotificationStatus.DeadLetter;
            NextAttemptAtUtc = null;
        }
        else NextAttemptAtUtc = nowUtc.AddMinutes(AttemptCount switch { 1 => 1, 2 => 5, 3 => 15, 4 => 60, _ => 240 });
        Touch();
    }

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }
}
