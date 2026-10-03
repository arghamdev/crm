using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Notifications;
using Crm.Domain.Service;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Notifications;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class NotificationChecks
{
    private sealed class Failing(NotificationChannel channel) : INotificationSender
    {
        public NotificationChannel Channel { get; } = channel;
        public bool IsLive => true;
        public NotificationSendResult Send(string address, string subject, string body) => new(false, null, "gateway down");
    }

    internal static void Run(Action<bool, string> check)
    {
        var preference = new NotificationPreference(Guid.NewGuid());
        Reject(() => preference.Update(null, true, true, []), "SMS without a mobile", check);
        Reject(() => preference.Update("0212345", true, false, []), "an invalid mobile", check);
        preference.Update("+98 912 000 1122", false, true, [NotificationCategory.GuaranteeExpiry]);
        check(preference is { Mobile: "09120001122", EmailEnabled: false, SmsEnabled: true } && preference.Mutes(NotificationCategory.GuaranteeExpiry) &&
              !preference.Mutes(NotificationCategory.ServiceEscalation), "NTF: preferences normalize the mobile and keep muted categories.");

        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1); var expert = User(2); var finance = User(6); var channel = User(7);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;
        var demo = new INotificationSender[] { new DemoNotificationSender(NotificationChannel.Email, NullLogger<DemoNotificationSender>.Instance),
            new DemoNotificationSender(NotificationChannel.Sms, NullLogger<DemoNotificationSender>.Instance) };
        var settings = new NotificationService(store, demo);

        check(store.Write(d => NotificationOutbox.Enqueue(d, "C01", expert, NotificationCategory.ServiceEscalation, "s", "b", null, "k1", now)) == 1,
            "NTF: by default a user gets email only.");
        settings.Save(expert, new SaveNotificationSettingsCommand("09121112233", true, true, null));
        check(store.Write(d => NotificationOutbox.Enqueue(d, "C01", expert, NotificationCategory.ServiceEscalation, "s", "b", null, "k1", now)) == 1 &&
              store.Write(d => NotificationOutbox.Enqueue(d, "C01", expert, NotificationCategory.ServiceEscalation, "s", "b", null, "k1", now)) == 0,
            "NTF: enabling SMS adds only the missing channel and a repeated key is deduplicated.");
        settings.Save(expert, new SaveNotificationSettingsCommand("09121112233", true, true, [NotificationCategory.CommissionApproval]));
        check(store.Write(d => NotificationOutbox.Enqueue(d, "C01", expert, NotificationCategory.CommissionApproval, "s", "b", null, "k2", now)) == 0,
            "NTF: a muted category is not sent.");
        var view = settings.Get(expert);
        check(view.Mobile == "09121112233" && view.SmsEnabled && view.Recent.Count == 2 && !view.SmsConfigured,
            "NTF: the settings page shows the user's own notifications and that delivery is in demo mode.");

        // Service escalation notifies the escalation target.
        var cases = new ServiceCaseService(store, access);
        var created = cases.CreateCase(manager, org, new CreateServiceCaseCommand(Guid.Parse("20000000-0000-4000-8000-000000000001"), "نقض SLA آزمون", null,
            ServiceCaseCategory.Complaint, ServiceCaseChannel.Phone, ServiceCasePriority.Critical), now.AddHours(-30));
        cases.EscalateBreaches(now);
        check(store.Read(d => d.NotificationMessages.Any(x => x.Category == NotificationCategory.ServiceEscalation && x.SourceReference == $"/service/{created.Id}")),
            "NTF: an SLA escalation notifies its target.");

        // Commission calculation notifies approvers (finance), not the calculator.
        new DealerIncentiveService(store, access).CalculateCommissions(channel, org, now, now);
        var approvals = store.Read(d => d.NotificationMessages.Where(x => x.Category == NotificationCategory.CommissionApproval).ToList());
        check(approvals.Any(x => x.RecipientUserId == finance) && approvals.All(x => x.RecipientUserId != channel),
            "NTF: calculated commissions notify finance approvers.");

        // Guarantee expiry reminders are raised once.
        var dispatcher = new NotificationDispatcher(store, demo);
        var raised = dispatcher.ScanGuaranteeExpiry(now);
        check(raised >= 2 && dispatcher.ScanGuaranteeExpiry(now) == 0 &&
              store.Read(d => d.NotificationMessages.Count(x => x.Category == NotificationCategory.GuaranteeExpiry && x.Subject.Contains("BG-1404-7781"))) >= 2,
            "NTF: a guarantee expiring within 30 days notifies guarantee managers once.");

        var failing = new NotificationDispatcher(store, [new Failing(NotificationChannel.Email), new Failing(NotificationChannel.Sms)]).Dispatch(now);
        var pending = store.Read(d => d.NotificationMessages.Count(x => x.Status == NotificationStatus.Pending));
        var delivered = dispatcher.Dispatch(now.AddMinutes(2));
        check(failing.Failed > 0 && failing.Sent == 0 && delivered.Sent == pending &&
              store.Read(d => d.NotificationMessages.All(x => x.Status == NotificationStatus.Sent && x.AttemptCount >= 1)),
            "NTF: failed deliveries back off and the next relay delivers everything.");
    }

    private static void Reject(Action action, string name, Action<bool, string> check)
    {
        try { action(); check(false, "NTF: " + name + " must be rejected."); }
        catch (InvalidOperationException) { }
    }
}
