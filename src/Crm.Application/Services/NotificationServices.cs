using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Common;
using Crm.Domain.Identity;
using Crm.Domain.Notifications;

namespace Crm.Application.Services;

/// <summary>Writes notification outbox rows inside the business write that raised them, honouring each user's settings.</summary>
public static class NotificationOutbox
{
    public static int Enqueue(CrmDataSet data, string companyId, Guid userId, NotificationCategory category, string subject, string body,
        string? sourceReference, string dedupKey, DateTimeOffset nowUtc)
    {
        var user = data.Find<CrmUser>(x => x.Id == userId).SingleOrDefault();
        if (user is null || !user.IsActiveAt(nowUtc)) return 0;
        var preference = data.Find<NotificationPreference>(x => x.Id == userId).SingleOrDefault() ?? new NotificationPreference(userId);
        if (preference.Mutes(category)) return 0;
        var targets = new List<(NotificationChannel Channel, string Address)>();
        if (preference.EmailEnabled && !string.IsNullOrWhiteSpace(user.NormalizedEmail)) targets.Add((NotificationChannel.Email, user.NormalizedEmail.ToLowerInvariant()));
        if (preference.SmsEnabled && preference.Mobile is { } mobile) targets.Add((NotificationChannel.Sms, mobile));
        var existing = data.Find<NotificationMessage>(x => x.DedupKey == dedupKey && x.RecipientUserId == userId).Select(x => x.Channel).ToHashSet();
        var added = 0;
        foreach (var (channel, address) in targets.Where(x => !existing.Contains(x.Channel)))
        {
            data.Append(new NotificationMessage(Guid.NewGuid(), companyId, userId, channel, address, category, subject,
                channel == NotificationChannel.Sms ? $"{subject}\n{body}" : body, sourceReference, dedupKey, nowUtc));
            added++;
        }
        return added;
    }

    /// <summary>Active internal users of the company whose roles (from the role matrix in data) grant the permission.</summary>
    public static IReadOnlyList<Guid> UsersWithPermission(CrmDataSet data, string companyId, string permission, DateTimeOffset nowUtc)
    {
        var roles = data.Find<RolePermissionGrant>(x => x.Permission == permission).Select(x => x.RoleKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roles.Count == 0) return [];
        var roleKeys = roles.ToArray();
        return data.Find<UserRoleAssignment>(x => x.CompanyId == companyId && roleKeys.Contains(x.RoleKey)).Where(x => x.IsEffective(nowUtc))
            .Select(x => x.CrmUserId).Distinct().ToList();
    }
}

/// <summary>Relays due notifications to the channel senders with backoff (outbox pattern).</summary>
public sealed class NotificationDispatcher(ICrmDataStore store, IEnumerable<INotificationSender> senders)
{
    private readonly Dictionary<NotificationChannel, INotificationSender> _senders = senders.ToDictionary(x => x.Channel);

    public NotificationDispatchResult Dispatch(DateTimeOffset nowUtc, int batchSize = 100) => store.Write(data =>
    {
        int sent = 0, failed = 0, dead = 0;
        var due = data.Find<NotificationMessage>(x => x.Status == NotificationStatus.Pending && x.NextAttemptAtUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc).Take(batchSize).ToList();
        foreach (var message in due)
        {
            NotificationSendResult result;
            if (!_senders.TryGetValue(message.Channel, out var sender)) result = new(false, null, $"فرستندهٔ {message.Channel} پیکربندی نشده است.");
            else
            {
                try { result = sender.Send(message.Address, message.Subject, message.Body); }
                catch (Exception exception) when (exception is not OperationCanceledException) { result = new(false, null, exception.Message); }
            }
            if (result.Succeeded)
            {
                message.MarkSent(result.ProviderMessageId ?? "ok", nowUtc);
                sent++;
            }
            else
            {
                message.MarkFailed(result.Error ?? "ارسال ناموفق", nowUtc);
                if (message.Status == NotificationStatus.DeadLetter) dead++; else failed++;
            }
        }
        return new NotificationDispatchResult(sent, failed, dead);
    });

    /// <summary>Raises one reminder per guarantee that expires within 30 days, to users who manage guarantees.</summary>
    public int ScanGuaranteeExpiry(DateTimeOffset nowUtc) => store.Write(data =>
    {
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        var horizon = today.AddDays(DealerAssuranceService.ExpiryWarningDays);
        var expiring = data.Find<DealerGuarantee>(x => x.Status == DealerGuaranteeStatus.Active && x.ExpiresOn != null && x.ExpiresOn >= today &&
            x.ExpiresOn <= horizon).ToList();
        var added = 0;
        foreach (var companyGroup in expiring.GroupBy(x => x.CompanyId))
        {
            var recipients = NotificationOutbox.UsersWithPermission(data, companyGroup.Key, "Dealer.Guarantee.Manage", nowUtc);
            var dealerIds = companyGroup.Select(x => x.DealerId).Distinct().ToArray();
            var dealers = data.Find<Dealer>(x => dealerIds.Contains(x.Id)).ToDictionary(x => x.Id, x => x.TradeName);
            foreach (var guarantee in companyGroup)
                foreach (var user in recipients)
                    added += NotificationOutbox.Enqueue(data, guarantee.CompanyId, user, NotificationCategory.GuaranteeExpiry,
                        $"سررسید تضمین {guarantee.Number}",
                        $"تضمین {guarantee.Number} نماینده {dealers.GetValueOrDefault(guarantee.DealerId, "—")} به مبلغ {guarantee.Amount:N0} ریال در {JalaliDate.Format(guarantee.ExpiresOn!.Value)} سررسید می‌شود.",
                        $"/dealers/{guarantee.DealerId}#assurance", $"guarantee-expiry:{guarantee.Id:N}:{guarantee.ExpiresOn:yyyyMMdd}", nowUtc);
        }
        return added;
    });
}

public interface INotificationService
{
    NotificationSettingsDto Get(Guid userId);
    void Save(Guid userId, SaveNotificationSettingsCommand command);
}

/// <summary>Each user's own notification settings and recent notifications.</summary>
public sealed class NotificationService(ICrmDataStore store, IEnumerable<INotificationSender> senders) : INotificationService
{
    public NotificationSettingsDto Get(Guid userId) => store.Read(data =>
    {
        var user = data.Find<CrmUser>(x => x.Id == userId).SingleOrDefault() ?? throw new UnauthorizedAccessException();
        var preference = data.Find<NotificationPreference>(x => x.Id == userId).SingleOrDefault() ?? new NotificationPreference(userId);
        var recent = data.Find<NotificationMessage>(x => x.RecipientUserId == userId).OrderByDescending(x => x.CreatedAtUtc).Take(30)
            .Select(x => new NotificationItemDto(x.Id, x.Channel, x.Category, x.Subject, x.Body, x.Status, x.Address, x.CreatedAtUtc, x.SentAtUtc,
                x.LastError, x.SourceReference)).ToList();
        var live = senders.Where(x => x.IsLive).Select(x => x.Channel).ToHashSet();
        return new NotificationSettingsDto(user.NormalizedEmail.ToLowerInvariant(), preference.Mobile, preference.EmailEnabled, preference.SmsEnabled,
            Enum.GetValues<NotificationCategory>().Where(preference.Mutes).ToList(), recent, live.Contains(NotificationChannel.Sms), live.Contains(NotificationChannel.Email));
    });

    public void Save(Guid userId, SaveNotificationSettingsCommand command) => store.Write(data =>
    {
        if (data.Find<CrmUser>(x => x.Id == userId).SingleOrDefault() is null) throw new UnauthorizedAccessException();
        var preference = data.Find<NotificationPreference>(x => x.Id == userId).SingleOrDefault();
        if (preference is null)
        {
            preference = new NotificationPreference(userId);
            data.Append(preference);
        }
        preference.Update(command.Mobile, command.EmailEnabled, command.SmsEnabled, command.MutedCategories ?? []);
        return true;
    });
}
