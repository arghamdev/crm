using System.Net;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private const string SepehrCustomerId = "20000000-0000-4000-8000-000000000001"; // C01 / B01
    private const string NakhlCustomerId = "20000000-0000-4000-8000-000000000003";  // C01 / B03

    private static async Task CheckServiceDesk(HttpClient manager, HttpClient expert, HttpClient dealer, HttpClient finance)
    {
        using (var response = await manager.GetAsync("/service"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "خدمات و SLA") &&
                HtmlContains(html, "CS-1405-1001") && html.Contains("/service/escalate"),
                "SVC: manager sees the service desk, seeded cases and the escalation action.", "svc-index", html);
            Check(HeaderContains(response, "Content-Security-Policy", "script-src 'self';"),
                "CSP must only allow same-origin scripts (no unused third-party CDN).");
        }
        using (var response = await dealer.GetAsync("/service")) Check(response.StatusCode != HttpStatusCode.OK, "SVC: dealer users cannot open the internal service desk.");
        using (var response = await finance.GetAsync("/service")) Check(response.StatusCode != HttpStatusCode.OK, "SVC: finance has no Service.Read permission.");

        // Branch scope: the B01 expert must not see the B03 case even by direct id.
        using (var response = await expert.GetAsync("/service/a0000000-0000-4000-8000-000000000002"))
            Check(response.StatusCode == HttpStatusCode.NotFound, "SVC: out-of-branch case is hidden from a branch-scoped expert.");
        using (var response = await expert.GetAsync("/service"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "CS-1405-1001") && !HtmlContains(html, "CS-1405-1002") &&
                !html.Contains("/service/escalate"),
                "SVC: expert sees only owned in-scope cases and no escalation action.", "svc-expert-index", html);
        }

        var token = await GetAntiforgeryToken(manager, "/service");
        using (var response = await manager.PostAsync("/service/create", new FormUrlEncodedContent(new Dictionary<string, string>
               { ["CustomerId"] = SepehrCustomerId, ["Subject"] = "no csrf" })))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "SVC: case creation requires antiforgery.");

        var subject = "SVC HTTP " + Guid.NewGuid().ToString("N")[..8];
        using (var response = await HtmxPost(manager, "/service/create", new()
               {
                   ["__RequestVerificationToken"] = token, ["CustomerId"] = SepehrCustomerId, ["Subject"] = subject,
                   ["Description"] = "<script>alert('svc')</script>", ["Category"] = "Complaint", ["Channel"] = "Phone", ["Priority"] = "High"
               }))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "serviceChanged"),
                "SVC: HTMX create returns 204 with serviceChanged trigger.");
        using (var response = await HtmxPost(manager, "/service/create", new()
               { ["__RequestVerificationToken"] = token, ["CustomerId"] = SepehrCustomerId, ["Subject"] = "" }))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "SVC: invalid create returns 422 with the form.");

        string listHtml;
        using (var response = await manager.GetAsync("/service/table?q=" + Uri.EscapeDataString(subject))) listHtml = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(listHtml, "/service/([0-9a-f-]{36})");
        Check(match.Success, "SVC: created case appears in the filtered table.");
        if (!match.Success) return;
        var id = match.Groups[1].Value;

        using (var response = await manager.GetAsync($"/service/{id}"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("&lt;script&gt;") && !html.Contains("<script>alert('svc')</script>"),
                "SVC: case description is HTML encoded.", "svc-details", html);
        }

        // Triage to the B01 expert, then the expert (now owner) works the case.
        using (var response = await HtmxPost(manager, $"/service/{id}/triage", new()
               {
                   ["__RequestVerificationToken"] = token, ["Priority"] = "Critical", ["OwnerUserId"] = "10000000-0000-4000-8000-000000000002",
                   ["Note"] = "ارجاع به کارشناس شعبه", ["ExpectedVersion"] = "1"
               }))
            Check(response.StatusCode == HttpStatusCode.NoContent, "SVC: manager can triage and assign an eligible owner.");
        using (var response = await HtmxPost(manager, $"/service/{id}/triage", new()
               {
                   ["__RequestVerificationToken"] = token, ["Priority"] = "Low", ["OwnerUserId"] = "10000000-0000-4000-8000-000000000002", ["ExpectedVersion"] = "1"
               }))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "SVC: stale version is rejected with 422.");

        var expertToken = await GetAntiforgeryToken(expert, "/service");
        async Task<HttpStatusCode> Act(HttpClient client, string csrf, string action, long version, Dictionary<string, string>? extra = null)
        {
            var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = csrf, ["Action"] = action, ["ExpectedVersion"] = version.ToString() };
            foreach (var pair in extra ?? []) fields[pair.Key] = pair.Value;
            using var response = await HtmxPost(client, $"/service/{id}/action", fields);
            return response.StatusCode;
        }
        Check(await Act(expert, expertToken, "Resolve", 2, new() { ["RootCause"] = "x", ["CorrectiveAction"] = "y", ["Resolution"] = "z" }) == HttpStatusCode.UnprocessableEntity,
            "SVC: a case cannot be resolved before work starts.");
        Check(await Act(expert, expertToken, "StartWork", 2) == HttpStatusCode.NoContent, "SVC: owner starts work (first response).");
        Check(await Act(expert, expertToken, "WaitOnCustomer", 3) == HttpStatusCode.NoContent, "SVC: owner pauses SLA while waiting on the customer.");
        Check(await Act(expert, expertToken, "StartWork", 4) == HttpStatusCode.NoContent, "SVC: owner resumes work.");
        Check(await Act(expert, expertToken, "Resolve", 5, new() { ["RootCause"] = "", ["CorrectiveAction"] = "y", ["Resolution"] = "z" }) == HttpStatusCode.UnprocessableEntity,
            "SVC: root cause is mandatory to resolve.");
        Check(await Act(expert, expertToken, "Resolve", 5, new() { ["RootCause"] = "خطای ثبت سفارش", ["CorrectiveAction"] = "کنترل دوم ثبت", ["Resolution"] = "اصلاح سفارش" }) == HttpStatusCode.NoContent,
            "SVC: owner resolves with root cause and corrective action.");
        Check(await Act(expert, expertToken, "Reopen", 6, new() { ["Note"] = "x" }) == HttpStatusCode.Forbidden,
            "SVC: reopen requires Service.Triage.");
        Check(await Act(expert, expertToken, "Close", 6, new() { ["SatisfactionScore"] = "5", ["Note"] = "عالی" }) == HttpStatusCode.NoContent,
            "SVC: owner closes with a CSAT score.");
        Check(await Act(manager, token, "Reopen", 7, new() { ["Note"] = "مشکل تکرار شد" }) == HttpStatusCode.NoContent,
            "SVC: manager can reopen a recently closed case.");

        using (var response = await manager.GetAsync($"/service/{id}"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "بازگشایی") && HtmlContains(html, "حل پرونده") && HtmlContains(html, "تریاژ") && HtmlContains(html, "خطای ثبت سفارش"),
                "SVC: history records each transition and the root cause.", "svc-history", html);
        }
        using (var response = await manager.GetAsync($"/customers/{SepehrCustomerId}/activity"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "پرونده خدمات"), "SVC: case events reach the Customer 360 timeline.", "svc-timeline", html);
        }

        // The B01 expert cannot open a case for a B03 customer.
        using (var response = await HtmxPost(expert, "/service/create", new()
               { ["__RequestVerificationToken"] = expertToken, ["CustomerId"] = NakhlCustomerId, ["Subject"] = "cross branch" }))
            Check(response.StatusCode == HttpStatusCode.Forbidden, $"SVC: creating a case outside the user's branch scope is forbidden (got {(int)response.StatusCode}).");

        using (var response = await HtmxPost(manager, "/service/escalate", new() { ["__RequestVerificationToken"] = token }))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "serviceChanged"),
                "SVC: manager can run escalation on demand.");
        using (var response = await HtmxPost(expert, "/service/escalate", new() { ["__RequestVerificationToken"] = expertToken }))
            Check(response.StatusCode == HttpStatusCode.Forbidden, "SVC: experts cannot trigger escalation.");
    }

    private static async Task<HttpResponseMessage> HtmxPost(HttpClient client, string path, Dictionary<string, string> values)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(values) };
        request.Headers.Add("HX-Request", "true");
        return await client.SendAsync(request);
    }
}
