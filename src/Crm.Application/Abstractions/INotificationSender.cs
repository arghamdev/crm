using Crm.Application.Contracts;
using Crm.Domain.Notifications;

namespace Crm.Application.Abstractions;

/// <summary>Delivery port for one channel (SMTP, SMS gateway, …). Implementations must not throw for ordinary delivery failures.</summary>
public interface INotificationSender
{
    NotificationChannel Channel { get; }
    /// <summary>False for the demo sender, so the settings page can say messages are only logged.</summary>
    bool IsLive { get; }
    NotificationSendResult Send(string address, string subject, string body);
}
