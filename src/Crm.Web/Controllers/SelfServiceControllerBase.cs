using Crm.Application.Contracts;
using Crm.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Web.Controllers;

public abstract class SelfServiceControllerBase : Controller
{
    protected bool WantsJson => Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    protected IActionResult Run(Func<IActionResult> action)
    {
        try {
            if (!ModelState.IsValid) throw new ArgumentException("مقادیر فرم معتبر نیست؛ تاریخ و تعداد را بررسی کنید.");
            return action();
        }
        catch (UnauthorizedAccessException) { return WantsJson ? StatusCode(403, new { message="دسترسی یا دامنهٔ فعال معتبر نیست." }) : Forbid(); }
        catch (KeyNotFoundException) { return WantsJson ? NotFound(new { message="رکورد در دامنهٔ شما یافت نشد." }) : NotFound(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) {
            var code = e is SelfServiceConflictException || e.InnerException is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ? 409 : 422;
            if (WantsJson) return StatusCode(code, new { message=e.Message });
            Response.StatusCode=code;
            if (Request.IsHtmx()) { Response.Headers["HX-Retarget"]="#selfServiceErrors"; Response.Headers["HX-Reswap"]="innerHTML"; return PartialView("_SelfServiceError", e.Message); }
            return View("SelfServiceError", e.Message);
        }
    }
    protected IActionResult Go(string path)
    {
        if (!Request.IsHtmx()) return LocalRedirect(path);
        Response.Headers["HX-Redirect"]=ForceNavigation(path);
        return NoContent();
    }

    // Browsers treat a redirect that only differs by #fragment from the current URL as an in-page jump, so the
    // page would not reload and the submitted change would stay invisible. A unique query value forces a real load.
    private string ForceNavigation(string path)
    {
        var hash=path.IndexOf('#');
        if (hash<0) return path;
        var target=path[..hash];
        var current=Uri.TryCreate(Request.Headers["HX-Current-URL"].ToString(), UriKind.Absolute, out var uri) ? uri.PathAndQuery : null;
        if (current is null || !string.Equals(current.Split('?')[0], target.Split('?')[0], StringComparison.OrdinalIgnoreCase)) return path;
        var separator=target.Contains('?') ? '&' : '?';
        return $"{target}{separator}updated={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{path[hash..]}";
    }
}
