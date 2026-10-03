using System.Security.Cryptography;
using System.Text;
using Crm.Application.Contracts;

namespace Crm.Web.Security;

public static class IdentityRequestContextExtensions
{
    public static IdentityRequestContext ToIdentityRequestContext(this HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var salt = configuration["Security:IpHashSalt"] ?? "demo-only-ip-hash-salt";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        var ipHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(ip)))[..16];
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 160) userAgent = userAgent[..160];
        var correlationId = context.Response.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? context.TraceIdentifier;
        return new IdentityRequestContext(DateTimeOffset.UtcNow, ipHash, userAgent, correlationId);
    }

    public static Guid CrmSessionId(this System.Security.Claims.ClaimsPrincipal user)
    {
        var value = user.FindFirst("session_id")?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
