using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private const string AccountId = "20000000-0000-4000-8000-000000000001";
    private const string OtherBranchAccountId = "20000000-0000-4000-8000-000000000002";

    private static HttpRequestMessage Htmx(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("HX-Request", "true");
        return request;
    }

    private static async Task CheckAccountFile(HttpClient manager, HttpClient expert, HttpClient finance, HttpClient dealer)
    {
        var basePath = $"/customers/{AccountId}";
        string html;
        using (var response = await manager.GetAsync(basePath))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && new[] { "ایجاد فرصت", "برنامه‌ریزی تماس", "برنامه‌ریزی جلسه", "ایجاد وظیفه", "ایجاد یادداشت", "ایجاد سرنخ" }
                          .All(x => HtmlContains(html, x)) && HtmlContains(html, "نمای کلی") && HtmlContains(html, "تاریخچه تغییرات") &&
                      html.Contains($"{basePath}/quick/lead") && html.Contains("data-account-refresh"),
                "ACCW: the account page renders the six quick actions, the tabs and lazy panels.", "accw-page", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/sections/payments")))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "خالص دریافت‌های تأییدشده") && HtmlContains(html, "1,200,000,000 IRR"),
                "ACCW: the payments section loads lazily with its approved total.", "accw-payments", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/activities?state=overdue")))
            Check(response.StatusCode == HttpStatusCode.OK && HtmlContains(await response.Content.ReadAsStringAsync(), "تماس پیگیری پرداخت فاکتور"),
                "ACCW: the activity panel filters overdue items.");

        // Quick call: CSRF required, saved once even when submitted twice.
        string form;
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/activities/new/Call"))) form = await response.Content.ReadAsStringAsync();
        var token = AntiforgeryRegex().Match(form).Groups[1].Value;
        var operation = Regex.Match(form, "name=\"OperationId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        var owner = Regex.Match(form, "name=\"OwnerUserId\"[^>]*>\\s*<option value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        Check(token.Length > 0 && operation.Length == 36 && owner.Length == 36, "ACCW: the call drawer carries a CSRF token, a one-time operation id and an owner.");
        var tomorrow = Crm.Domain.Common.TehranTime.Date(DateTimeOffset.UtcNow.AddDays(1));
        (string, string)[] call = [("Type", "Call"), ("OperationId", operation), ("ExpectedVersion", "0"), ("Subject", "تماس آزمون وب"), ("OwnerUserId", owner),
            ("StartDate", tomorrow), ("StartTime", "10:00"), ("DurationMinutes", "15"), ("Direction", "Outbound"), ("Priority", "Normal"), ("ReminderMinutesBefore", "15"),
            ("Related", "")];
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/activities", new FormUrlEncodedContent(call.ToDictionary(x => x.Item1, x => x.Item2)))))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "ACCW: posting an activity without the anti-forgery token is refused.");
        for (var i = 0; i < 2; i++)
            using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/activities", Form(WebUtility.HtmlDecode(token), call))))
                Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "accountChanged"),
                    "ACCW: saving a call closes the drawer and refreshes the account panels (HX-Trigger accountChanged).");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/activities")))
            Check(Regex.Matches(WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), "تماس آزمون وب").Count == 1,
                "ACCW: a double-submitted call is stored and listed once.");

        // Validation stays in the drawer (422) and the typed values are kept.
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/records/payments/new", Form(WebUtility.HtmlDecode(token),
                   ("operationId", Guid.NewGuid().ToString()), ("direction", "Receipt"), ("method", "Cheque"), ("amount", "abc"), ("currency", "IRR"), ("paidOn", tomorrow)))))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.UnprocessableEntity && html.Contains("validation-summary") && html.Contains("value=\"abc\""),
                "ACCW: an invalid payment re-renders the drawer with the error and the typed values.", "accw-payment-422", html);
        }

        // Document upload (multipart) and download with nosniff.
        using (var upload = new MultipartFormDataContent())
        {
            upload.Add(new StringContent(WebUtility.HtmlDecode(token)), "__RequestVerificationToken");
            upload.Add(new StringContent(Guid.NewGuid().ToString()), "operationId");
            upload.Add(new StringContent("گزارش بازدید وب"), "title");
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("visit report"));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            upload.Add(file, "file", "visit.txt");
            using var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/records/documents/new", upload));
            Check(response.StatusCode == HttpStatusCode.NoContent, "ACCW: a document uploads from the account file.");
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/sections/documents")))
        {
            html = await response.Content.ReadAsStringAsync();
            var link = Regex.Match(html, $"href=\"({basePath}/documents/[0-9a-f-]{{36}})\"").Groups[1].Value;
            Check(link.Length > 0, "ACCW: the uploaded document is listed with a download link.");
            if (link.Length > 0)
                using (var download = await manager.GetAsync(link))
                    Check(download.StatusCode == HttpStatusCode.OK && HeaderContains(download, "X-Content-Type-Options", "nosniff") &&
                          (await download.Content.ReadAsStringAsync()) == "visit report", "ACCW: the document downloads with nosniff.");
        }

        // Server-side permission and scope checks (not just hidden buttons).
        using (var response = await expert.GetAsync($"/customers/{OtherBranchAccountId}"))
            Check(response.StatusCode == HttpStatusCode.NotFound, "ACCW: an expert cannot open an account of another branch by URL.");
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/sections/accessGroups")))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "ACCW: access groups are refused to non-administrators.");
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/account/status")))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "ACCW: an expert cannot open the account status form.");
        using (var response = await finance.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/activities/new/Call")))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "ACCW: finance (no Activity.Create) cannot open the call form.");
        using (var response = await dealer.GetAsync(basePath))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Redirect, "ACCW: a dealer user cannot open an internal account file.");
        string expertToken;
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Get, $"{basePath}/activities/new/Task")))
            expertToken = WebUtility.HtmlDecode(AntiforgeryRegex().Match(await response.Content.ReadAsStringAsync()).Groups[1].Value);
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/records/payments/60000000-0000-4000-8000-000000000022/approve",
                   Form(expertToken, ("expectedVersion", "1")))))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "ACCW: an expert posting a payment approval directly is forbidden.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/records/payments/60000000-0000-4000-8000-000000000022/approve",
                   Form(WebUtility.HtmlDecode(token), ("expectedVersion", "1")))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "accountChanged"),
                "ACCW: a row action (approve another user's payment) runs and refreshes the section.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, $"{basePath}/records/payments/60000000-0000-4000-8000-000000000022/approve",
                   Form(WebUtility.HtmlDecode(token), ("expectedVersion", "1")))))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity && HeaderContains(response, "HX-Trigger", "accountError"),
                "ACCW: repeating a row action with a stale version reports an error toast instead of applying twice.");
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Post, $"/customers/{OtherBranchAccountId}/records/payments/new",
                   Form(expertToken, ("operationId", Guid.NewGuid().ToString()), ("direction", "Receipt"), ("method", "Cash"), ("amount", "10"), ("paidOn", tomorrow)))))
            Check(response.StatusCode == HttpStatusCode.NotFound, "ACCW: posting to an out-of-scope account is not found.");
    }
}
