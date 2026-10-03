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
    protected IActionResult Go(string path) { if (!Request.IsHtmx()) return LocalRedirect(path); Response.Headers["HX-Redirect"]=path; return NoContent(); }
}
