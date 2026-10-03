using Crm.Domain.Common;

namespace Crm.Domain.Identity;

public sealed class ExternalIdentity(
    Guid id,
    Guid crmUserId,
    string providerKey,
    string issuer,
    string subject,
    string? emailAtLink,
    DateTimeOffset linkedAtUtc) : Entity(id)
{
    public Guid CrmUserId { get; } = crmUserId;
    public string ProviderKey { get; } = Required(providerKey, nameof(providerKey));
    public string Issuer { get; } = NormalizeIssuer(issuer);
    public string Subject { get; } = Required(subject, nameof(subject));
    public string? EmailAtLink { get; } = emailAtLink?.Trim().ToUpperInvariant();
    public DateTimeOffset LinkedAtUtc { get; } = linkedAtUtc;
    public DateTimeOffset LastSeenAtUtc { get; private set; } = linkedAtUtc;
    public bool IsActive { get; private set; } = true;

    private ExternalIdentity() : this(Guid.Empty, Guid.Empty, "EF", "https://ef.local", "EF", null, DateTimeOffset.MinValue) { }

    public bool Matches(string issuer, string subject) =>
        IsActive && Issuer.Equals(NormalizeIssuer(issuer), StringComparison.OrdinalIgnoreCase) &&
        Subject.Equals(subject.Trim(), StringComparison.Ordinal);

    public void MarkSeen(DateTimeOffset nowUtc)
    {
        LastSeenAtUtc = nowUtc;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    private static string NormalizeIssuer(string value) => Required(value, nameof(value)).TrimEnd('/');
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
