using System.Security.Claims;

namespace Crm.Web.Security;

public static class HttpContextExtensions
{
    public static bool IsHtmx(this HttpRequest request) => request.Headers.ContainsKey("HX-Request");

    public static Guid CrmUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("crm_user_id");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static void Trigger(this HttpResponse response, string eventName, string message)
    {
        var safeMessage = System.Text.Json.JsonSerializer.Serialize(message);
        response.Headers.Append("HX-Trigger", $"{{\"{eventName}\":{{\"message\":{safeMessage}}}}}");
    }
}
