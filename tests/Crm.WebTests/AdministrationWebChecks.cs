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
    }
}
