using System.Net;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckRoleAdministration(HttpClient manager, HttpClient expert)
    {
        using (var response = await manager.GetAsync("/identity/roles"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "نقش‌ها و مجوزها") && html.Contains("/identity/roles/SalesExpert"),
                "ROLES: administrator sees the role list.", "roles-index", html);
        }
        using (var response = await expert.GetAsync("/identity/roles")) Check(response.StatusCode != HttpStatusCode.OK, "ROLES: non-administrators cannot open role management.");

        string html2;
        using (var response = await manager.GetAsync("/identity/roles/ServiceAgent")) html2 = await response.Content.ReadAsStringAsync();
        var version = Regex.Match(html2, "name=\"expectedVersion\" value=\"(\\d+)\"").Groups[1].Value;
        var token = Regex.Match(html2, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var granted = Regex.Matches(html2, "name=\"permissions\" value=\"([^\"]+)\" checked").Select(m => m.Groups[1].Value).ToList();
        Check(version.Length > 0 && token.Length > 0 && granted.Contains("Service.Read"), "ROLES: the edit page renders the current grants, version and CSRF token.");

        FormUrlEncodedContent Form(IEnumerable<string> permissions, string reason, string expectedVersion, bool csrf = true)
        {
            var fields = new List<KeyValuePair<string, string>> { new("reason", reason), new("expectedVersion", expectedVersion) };
            if (csrf) fields.Add(new("__RequestVerificationToken", token));
            fields.AddRange(permissions.Select(x => new KeyValuePair<string, string>("permissions", x)));
            return new FormUrlEncodedContent(fields);
        }
        using (var response = await manager.PostAsync("/identity/roles/ServiceAgent", Form(granted, "no csrf", version, csrf: false)))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "ROLES: saving requires antiforgery.");
        using (var response = await manager.PostAsync("/identity/roles/ServiceAgent", Form([.. granted, "Made.Up"], "x", version)))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "ROLES: unknown permissions are rejected with 422.");
        using (var response = await manager.PostAsync("/identity/roles/ServiceAgent", Form([.. granted, "Lead.Read"], "افزودن مشاهده سرنخ", version)))
            Check(response.StatusCode == HttpStatusCode.Redirect, "ROLES: a valid change saves and redirects.");
        using (var response = await manager.GetAsync("/identity/roles/ServiceAgent"))
        {
            var html = await response.Content.ReadAsStringAsync();
            Check(Regex.IsMatch(html, "value=\"Lead.Read\" checked") && HtmlContains(html, "1 مجوز افزوده"), "ROLES: the saved grant and a confirmation are shown.");
        }
        using (var response = await manager.PostAsync("/identity/roles/ServiceAgent", Form(granted, "stale", version)))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "ROLES: a stale version is rejected with 422.");
        using (var response = await manager.GetAsync("/identity/roles/DoesNotExist")) Check(response.StatusCode == HttpStatusCode.NotFound, "ROLES: unknown role returns 404.");

        using (var response = await manager.GetAsync("/identity/audit?eventType=RolePermissionsChanged"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("RolePermissionsChanged") && html.Contains("Lead.Read") && !html.Contains(">SignIn<"),
                "AUDIT: the audit log filters by event type and shows the role change diff.", "audit-filtered", html);
        }
        using (var response = await manager.GetAsync("/identity/audit"))
        {
            var html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains("SignIn") && html.Contains("صفحه 1 از"), "AUDIT: the unfiltered log is paged newest-first.");
        }
        using (var response = await expert.GetAsync("/identity/audit")) Check(response.StatusCode != HttpStatusCode.OK, "AUDIT: non-administrators cannot read the audit log.");

        string indexHtml;
        using (var response = await manager.GetAsync("/identity/roles")) indexHtml = await response.Content.ReadAsStringAsync();
        var createToken = Regex.Match(indexHtml, "action=\"/identity/roles\">\\s*<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        if (createToken.Length == 0) createToken = Regex.Match(indexHtml, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        FormUrlEncodedContent NewRole(string key) => new([
            new("__RequestVerificationToken", createToken), new("roleKey", key), new("label", "ناظر کیفیت داده"),
            new("scopeTypes", "Company"), new("scopeTypes", "Branch"), new("copyFrom", "Executive"), new("reason", "آزمون HTTP")]);
        using (var response = await manager.PostAsync("/identity/roles", NewRole("DataSteward")))
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/identity/roles/DataSteward",
                "ROLES: creating a role redirects to its permission editor.");
        using (var response = await manager.PostAsync("/identity/roles", NewRole("DataSteward")))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "ROLES: a duplicate role key is rejected with 422.");
        using (var response = await manager.GetAsync("/identity/users/10000000-0000-4000-8000-000000000003"))
        {
            var html = await response.Content.ReadAsStringAsync();
            Check(html.Contains("value=\"DataSteward\"") && !html.Contains("value=\"DealerUser\""),
                "ROLES: the new role is offered for assignment on the user page; the dealer role is not.");
        }
    }
}
