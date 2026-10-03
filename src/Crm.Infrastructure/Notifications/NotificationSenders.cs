using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Crm.Infrastructure.Notifications;

/// <summary>Development/demo sender: records and logs the message instead of delivering it.</summary>
public sealed class DemoNotificationSender(NotificationChannel channel, ILogger<DemoNotificationSender> logger) : INotificationSender
{
    public static readonly ConcurrentQueue<(NotificationChannel Channel, string Address, string Subject)> Delivered = new();

    public NotificationChannel Channel { get; } = channel;
    public bool IsLive => false;

    public NotificationSendResult Send(string address, string subject, string body)
    {
        Delivered.Enqueue((Channel, address, subject));
        while (Delivered.Count > 500) Delivered.TryDequeue(out _);
        logger.LogInformation("Demo {Channel} notification to {Address}: {Subject}", Channel, address, subject);
        return new NotificationSendResult(true, $"DEMO-{Channel}-{Guid.NewGuid():N}"[..24], null);
    }
}

public sealed record SmtpOptions(string Host, int Port, bool EnableSsl, string? UserName, string? Password, string From, string? FromName);

/// <summary>Email over SMTP (Notifications:Email:Mode = Smtp).</summary>
public sealed class SmtpEmailSender(SmtpOptions options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;
    public bool IsLive => true;

    public NotificationSendResult Send(string address, string subject, string body)
    {
        try
        {
            using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl, Timeout = 15_000 };
            if (!string.IsNullOrEmpty(options.UserName)) client.Credentials = new NetworkCredential(options.UserName, options.Password);
            using var message = new MailMessage(new MailAddress(options.From, options.FromName), new MailAddress(address))
            {
                Subject = subject,
                Body = $"<div dir=\"rtl\" style=\"font-family:Tahoma,sans-serif\">{WebUtility.HtmlEncode(body).Replace("\n", "<br>")}</div>",
                IsBodyHtml = true,
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8
            };
            var id = $"<{Guid.NewGuid():N}@crm>";
            message.Headers.Add("Message-ID", id);
            client.Send(message);
            return new NotificationSendResult(true, id, null);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
        {
            return new NotificationSendResult(false, null, exception.Message);
        }
    }
}

public sealed record SmsGatewayOptions(Uri Endpoint, string ApiKey, string? SenderNumber);

/// <summary>
/// SMS through an HTTP gateway (Notifications:Sms:Mode = Http): POSTs {receptor, message, sender} with the API key in the
/// "apikey" header and treats any 2xx as accepted. Adapt the payload to the chosen provider.
/// </summary>
public sealed class HttpSmsSender(HttpClient http, SmsGatewayOptions options) : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Sms;
    public bool IsLive => true;

    public NotificationSendResult Send(string address, string subject, string body)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
            {
                Content = JsonContent.Create(new { receptor = address, message = body, sender = options.SenderNumber })
            };
            request.Headers.Add("apikey", options.ApiKey);
            using var response = http.Send(request);
            return response.IsSuccessStatusCode
                ? new NotificationSendResult(true, response.Headers.TryGetValues("X-Message-Id", out var ids) ? ids.First() : $"SMS-{Guid.NewGuid():N}"[..20], null)
                : new NotificationSendResult(false, null, $"SMS gateway returned {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new NotificationSendResult(false, null, exception.Message);
        }
    }
}
