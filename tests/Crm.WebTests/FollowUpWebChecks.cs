using System.Net;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    /// <summary>
    /// مرکز پیگیری over HTTP: list and case page, registration with CSRF and duplicate detection, a drawer action in place,
    /// settings and supervision behind their permissions, and no access for dealer users.
    /// </summary>
    private static async Task CheckFollowUpCenter(HttpClient manager, HttpClient expert, HttpClient dealer)
    {
        string html;
        using (var response = await manager.GetAsync("/follow-ups"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "مرکز پیگیری") && html.Contains("href=\"/follow-ups\"") &&
                      html.Contains("RQ-24085") && html.Contains("href=\"/follow-ups/supervision\"") && html.Contains("href=\"/follow-ups/settings\""),
                "FUW: the manager opens the follow-up list with supervision and settings links.", "fuw-list", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/follow-ups/table?view=nonext")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains("RQ-24087") && !html.Contains("RQ-24085") &&
                  HeaderContains(response, "HX-Replace-Url", "/follow-ups?view=nonext"),
                "FUW: the «بدون اقدام بعدی» view lists only cases without a next action and keeps the URL state.");
        }
        using (var response = await dealer.GetAsync("/follow-ups"))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "FUW: dealer users have no access to the follow-up center.");
        using (var response = await expert.GetAsync("/follow-ups/settings"))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "FUW: settings need FollowUp.Configure.");
        using (var response = await expert.GetAsync("/follow-ups/supervision"))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "FUW: supervision needs FollowUp.Supervise.");

        // ۱. Registration: CSRF is required; a duplicate on the same related record stays in the form (422) with the duplicate listed.
        var token = await GetAntiforgeryToken(manager, "/follow-ups/new?customerId=20000000-0000-4000-8000-000000000001");
        var subject = "پرونده وب " + Guid.NewGuid().ToString("N")[..6];
        (string, string)[] Fields(string s, string? related = null) =>
        [
            ("Subject", s), ("CustomerId", "20000000-0000-4000-8000-000000000001"), ("CaseType", "Proforma"), ("BranchId", "B01"), ("Channel", "Phone"),
            ("Priority", "Normal"), ("Language", "fa"), ("OperationId", Guid.NewGuid().ToString()), ("RelatedCode", related ?? ""),
            ("Parts[0].PartCode", "WEB-1"), ("Parts[0].Quantity", "2"), ("Parts[0].Unit", "عدد")
        ];
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/follow-ups", new FormUrlEncodedContent(Fields(subject).Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))))))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "FUW: registering a case without the anti-forgery token is refused.");
        string caseId;
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/follow-ups", Form(token, Fields(subject)))))
        {
            caseId = response.Headers.TryGetValues("HX-Redirect", out var location) ? Regex.Match(location.Single(), "/follow-ups/([0-9a-f-]{36})").Groups[1].Value : "";
            Check(response.StatusCode == HttpStatusCode.NoContent && caseId.Length == 36, "FUW: a valid registration redirects to the new case.");
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/follow-ups", Form(token, Fields("موضوع دیگر " + subject, "OP-2041")))))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.UnprocessableEntity && html.Contains("RQ-24085") && html.Contains("name=\"DuplicateReason\""),
                "FUW: a case on an already followed record is flagged as a duplicate in the form.", "fuw-duplicate", html);
        }
        // «پیش‌نویس»: kept on the server per user, offered on the next open, restored into the form and deleted on request.
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/follow-ups/drafts/create", Form(token, ("Subject", "پیش‌نویس " + subject),
                   ("CustomerId", "20000000-0000-4000-8000-000000000001"), ("CaseType", "Warranty"), ("Parts[0].PartCode", "DRF-1"), ("Parts[0].Quantity", "3")))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "followUpNotice"), "FUW: saving a draft answers with a notice.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/follow-ups/new/form")))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "بازیابی پیش‌نویس"), "FUW: a saved draft is offered when the registration form opens.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/follow-ups/new/form?restore=true")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(HtmlContains(html, "پیش‌نویس " + subject) && html.Contains("value=\"DRF-1\"") && Regex.IsMatch(html, "value=\"Warranty\"\\s+selected"),
                "FUW: restoring the draft fills subject, type and parts.");
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/follow-ups/drafts/create/delete", Form(token))))
            Check(response.StatusCode == HttpStatusCode.OK, "FUW: the draft can be deleted.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/follow-ups/new/form")))
            Check(!HtmlContains(await response.Content.ReadAsStringAsync(), "بازیابی پیش‌نویس"), "FUW: a deleted draft is no longer offered.");
        if (caseId.Length != 36) return;

        // Case page and a drawer action: planning a call updates the case in place (followUpChanged).
        using (var response = await manager.GetAsync($"/follow-ups/{caseId}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, subject) && html.Contains("data-fu-tabs") && html.Contains("data-checklist") && HtmlContains(html, "پاسخ اولیه به مشتری"),
                "FUW: the case page shows the stages, the active checklist and the first next action.", "fuw-case", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"/follow-ups/{caseId}/plan")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains("name=\"OperationId\"") && html.Contains("name=\"PreChecklist\""),
                "FUW: the plan drawer carries an operation id and the pre-call checklist.");
            token = WebUtility.HtmlDecode(AntiforgeryRegex().Match(html).Groups[1].Value);
        }
        var owner = Regex.Match(html, "name=\"OwnerUserId\"[^>]*>\\s*<option value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var contact = Regex.Match(html, "<option value=\"([0-9a-f-]{36})\" data-phone=\"[^\"]*\" selected").Groups[1].Value;
        Check(contact.Length == 36 && html.Contains("class=\"fu-num\"") && html.Contains("name=\"TimeZone\""),
            "FUW: the plan sheet (۲) preselects the case contact and offers the time zone.");
        var tomorrow = Crm.Domain.Common.TehranTime.Date(DateTimeOffset.UtcNow.AddDays(1));
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"/follow-ups/{caseId}/plan", Form(token, ("Kind", "Call"), ("Title", "تماس وب"),
                   ("OwnerUserId", owner), ("Channel", "Phone"), ("Date", "1405/13/40"), ("Time", "10:00"), ("Priority", "Normal"), ("OperationId", Guid.NewGuid().ToString())))))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "FUW: an invalid action date stays in the drawer (422).");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"/follow-ups/{caseId}/plan", Form(token, ("Kind", "Call"), ("Title", "تماس وب"),
                   ("OwnerUserId", owner), ("Channel", "Phone"), ("Date", tomorrow), ("Time", "10:00"), ("Priority", "Normal"), ("OperationId", Guid.NewGuid().ToString())))))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity && HtmlContains(await response.Content.ReadAsStringAsync(), "مخاطب اقدام را انتخاب کنید"),
                "FUW: a call without a contact is refused while the customer has contacts.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"/follow-ups/{caseId}/plan", Form(token, ("Kind", "Call"), ("Title", "تماس وب"), ("ContactId", contact),
                   ("OwnerUserId", owner), ("Channel", "Phone"), ("Date", tomorrow), ("Time", "10:00"), ("Priority", "Normal"), ("OperationId", Guid.NewGuid().ToString())))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "followUpChanged"),
                "FUW: planning an action closes the drawer and refreshes the case.");
        // Settings and supervision render for the manager.
        using (var response = await manager.GetAsync("/follow-ups/settings"))
            Check(response.StatusCode == HttpStatusCode.OK && HtmlContains(await response.Content.ReadAsStringAsync(), "پیگیری پیش‌فاکتور قطعات"),
                "FUW: settings list the default workflow templates.");
        using (var response = await manager.GetAsync("/follow-ups/supervision"))
            Check(response.StatusCode == HttpStatusCode.OK && HtmlContains(await response.Content.ReadAsStringAsync(), "پرونده‌های نیازمند توجه"),
                "FUW: the supervision board renders.");
        using (var response = await manager.GetAsync("/follow-ups/supervision/export"))
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Check(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "text/csv" &&
                  System.Text.Encoding.UTF8.GetString(bytes).Contains("کد پرونده"), "FUW: the supervision export is a CSV.");
        }
    }
}
