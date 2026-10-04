using System.Net;
using Crm.Web.Presentation;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckVersion(WebApplicationFactory<global::Program> factory, HttpClient signedIn)
    {
        var version = SystemVersion.Current;
        Check(version != "0.0.0" && SystemVersion.Releases.Count > 0 && SystemVersion.Releases[0].Version == version,
            $"VER: the top CHANGELOG.md entry must be the build version ({version}); bump both together.");
        Check(SystemVersion.Releases.All(x => x.Date.Length == 10 && x.Title.Length > 0 && x.Changes.Count > 0),
            "VER: every release has a Jalali date, a title and at least one change.");
        using (var anonymous = CreateClient(factory))
        using (var response = await anonymous.GetAsync("/account/login"))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), $"v{version}"), "VER: the login page shows the version.");
        using (var response = await signedIn.GetAsync("/dashboard"))
        {
            var html = await response.Content.ReadAsStringAsync();
            Check(html.Contains("class=\"version-pill\"") && html.Contains("href=\"/system/version\"") && HtmlContains(html, $"v{version}"),
                "VER: every page shows the version badge linking to the release notes.");
        }
        using (var response = await signedIn.GetAsync("/system/version"))
        {
            var html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "نسخهٔ جاری") && HtmlContains(html, SystemVersion.Releases[0].Title) &&
                      HtmlContains(html, "v1.0.0"), "VER: the release notes page lists all versions with the current one marked.", "ver-page", html);
        }
    }
}
