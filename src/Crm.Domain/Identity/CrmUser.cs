using Crm.Domain.Common;

namespace Crm.Domain.Identity;

public enum UserStatus { PendingActivation, Active, Suspended, Disabled, Archived }

public sealed class CrmUser(
    Guid id,
    string displayName,
    string userName,
    string normalizedEmail,
    string? employeeNumber = null,
    UserStatus status = UserStatus.PendingActivation,
    DateTimeOffset? accessValidFromUtc = null,
    DateTimeOffset? accessValidToUtc = null) : Entity(id)
{
    public string DisplayName { get; private set; } = Required(displayName, nameof(displayName));
    public string UserName { get; } = userName.Trim().ToLowerInvariant();
    public string NormalizedEmail { get; private set; } = NormalizeEmail(normalizedEmail);
    public string? EmployeeNumber { get; private set; } = employeeNumber?.Trim();
    public string Culture { get; private set; } = "fa-IR";
    public string TimeZoneId { get; private set; } = "Asia/Tehran";
    public UserStatus Status { get; private set; } = status;
    public DateTimeOffset AccessValidFromUtc { get; } = accessValidFromUtc ?? DateTimeOffset.MinValue;
    public DateTimeOffset? AccessValidToUtc { get; } = accessValidToUtc;
    public long SecurityVersion { get; private set; } = 1;
    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    private CrmUser() : this(Guid.Empty, "EF", "ef", "EF@LOCAL") { }

    public void Activate()
    {
        if (Status is UserStatus.Disabled or UserStatus.Archived)
            throw new InvalidOperationException("Disabled or archived users cannot be activated.");
        SetStatus(UserStatus.Active);
    }

    public void Suspend() => SetStatus(UserStatus.Suspended);
    public void Disable() => SetStatus(UserStatus.Disabled);

    public void RecordSuccessfulLogin(DateTimeOffset nowUtc)
    {
        if (!IsActiveAt(nowUtc)) throw new InvalidOperationException("Only active users inside their access window can sign in.");
        LastLoginAtUtc = nowUtc;
        Touch();
    }

    public void IncrementSecurityVersion()
    {
        SecurityVersion++;
        Touch();
    }

    public bool IsActiveAt(DateTimeOffset nowUtc) => Status == UserStatus.Active && AccessValidFromUtc <= nowUtc &&
        (AccessValidToUtc is not { } validToUtc || nowUtc < validToUtc);

    private void SetStatus(UserStatus status)
    {
        if (Status == status) return;
        Status = status;
        IncrementSecurityVersion();
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();

    private static string NormalizeEmail(string email) => Required(email, nameof(email)).ToUpperInvariant();
}
