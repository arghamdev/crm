using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

/// <summary>Each signed-in user's notification settings (email/SMS) and recent notifications.</summary>
[Authorize]
public sealed class NotificationsController(INotificationService notifications, ICurrentUserContext current) : Controller
{
    [HttpGet("/account/notifications")]
    public IActionResult Index() => View(notifications.Get(current.CrmUserId));

    [HttpPost("/account/notifications")]
    [ValidateAntiForgeryToken]
    public IActionResult Save([FromForm] string? mobile, [FromForm] bool emailEnabled, [FromForm] bool smsEnabled,
        [FromForm] List<NotificationCategory>? enabledCategories)
    {
        // The form lists categories to receive; the stored preference keeps the ones the user muted.
        var muted = Enum.GetValues<NotificationCategory>().Except(enabledCategories ?? []).ToList();
        try
        {
            notifications.Save(current.CrmUserId, new SaveNotificationSettingsCommand(mobile, emailEnabled, smsEnabled, muted));
            TempData["NotificationMessage"] = "تنظیمات اعلان ذخیره شد.";
        }
        catch (InvalidOperationException exception) { TempData["NotificationError"] = exception.Message; }
        return Redirect("/account/notifications");
    }
}
