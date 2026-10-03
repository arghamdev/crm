using Crm.Domain.Notifications;

namespace Crm.Application.Contracts;

public sealed record NotificationSendResult(bool Succeeded, string? ProviderMessageId, string? Error);

public sealed record NotificationDispatchResult(int Sent, int Failed, int DeadLettered);

public sealed record NotificationItemDto(Guid Id, NotificationChannel Channel, NotificationCategory Category, string Subject, string Body,
    NotificationStatus Status, string Address, DateTimeOffset CreatedAtUtc, DateTimeOffset? SentAtUtc, string? LastError, string? SourceReference);

public sealed record NotificationSettingsDto(string Email, string? Mobile, bool EmailEnabled, bool SmsEnabled,
    IReadOnlyList<NotificationCategory> MutedCategories, IReadOnlyList<NotificationItemDto> Recent, bool SmsConfigured, bool EmailConfigured);

public sealed record SaveNotificationSettingsCommand(string? Mobile, bool EmailEnabled, bool SmsEnabled, IReadOnlyList<NotificationCategory>? MutedCategories);
