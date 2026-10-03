using System.Net;
using System.Text.Json;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckReports(HttpClient manager, HttpClient expert, HttpClient dealer, HttpClient finance)
    {
        var contextToken = await GetAntiforgeryToken(manager, "/context/select");
        using var selected = await manager.PostAsync("/context/select", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["__RequestVerificationToken"] = contextToken, ["CompanyId"] = "C01", ["ReturnUrl"] = "/reports" }));
        Check(selected.StatusCode == HttpStatusCode.Redirect, "P9: report test selects C01 context.");
        using var page = await manager.GetAsync("/reports");
        var html = await page.Content.ReadAsStringAsync();
        Check(page.StatusCode == HttpStatusCode.OK && html.Contains("id=\"reportDashboard\""), "P9: MVC dashboard renders.");
        Check(HeaderContains(page, "Cache-Control", "no-store"), "P9: reports must not be cached.");
        Check(!html.Contains("data-report-metric=\"collection\""), "P9: sales manager cannot see financial KPI.");
        await CheckNavigationLinks(manager, html, "manager");
        using var filter = new HttpRequestMessage(HttpMethod.Get, "/reports?BranchId=B01&Profile=executive");
        filter.Headers.Add("HX-Request", "true");
        using var filtered = await manager.SendAsync(filter);
        var partial = await filtered.Content.ReadAsStringAsync();
        Check(filtered.StatusCode == HttpStatusCode.OK && partial.Contains("id=\"reportDashboard\"") && !partial.Contains("<!doctype", StringComparison.OrdinalIgnoreCase), "P9: filter returns only dashboard partial.");
        using var invalid = new HttpRequestMessage(HttpMethod.Get, "/reports?From=not-a-date");
        invalid.Headers.Add("HX-Request", "true");
        using var invalidResponse = await manager.SendAsync(invalid);
        Check(invalidResponse.StatusCode == HttpStatusCode.UnprocessableEntity && HeaderContains(invalidResponse, "HX-Retarget", "#reportErrors"), "P9: binding errors preserve the filters and return 422.");
        using var denied = await expert.GetAsync("/reports?BranchId=B03");
        Check(denied.StatusCode == HttpStatusCode.Redirect || denied.StatusCode == HttpStatusCode.Forbidden, "P9: forged branch filter is forbidden.");
        using var dealerDenied = await dealer.GetAsync("/reports");
        Check(dealerDenied.StatusCode == HttpStatusCode.Redirect || dealerDenied.StatusCode == HttpStatusCode.Forbidden, "P9: dealer cannot access internal analytics.");
        using var drill = await manager.GetAsync("/reports/drilldown?Metric=forecast&PageSize=1");
        Check(drill.StatusCode == HttpStatusCode.OK && (await drill.Content.ReadAsStringAsync()).Contains("id=\"reportDrilldown\""), "P9: drilldown page renders.");
        using var csrf = await manager.PostAsync("/reports/export", new FormUrlEncodedContent(new Dictionary<string,string> { ["Metric"] = "forecast" }));
        Check(csrf.StatusCode == HttpStatusCode.BadRequest, "P9: export requires antiforgery.");
        var token = await GetAntiforgeryToken(manager, "/reports/drilldown?Metric=forecast");
        using var export = await manager.PostAsync("/reports/export", new FormUrlEncodedContent(new Dictionary<string,string> { ["Metric"] = "forecast", ["__RequestVerificationToken"] = token }));
        Check(export.StatusCode == HttpStatusCode.OK && export.Content.Headers.ContentType?.MediaType == "text/csv" && export.Content.Headers.ContentDisposition?.FileName?.Contains("forecast") == true, "P9: CSV download has a safe filename and correct MIME.");
        var biToken = await GetAntiforgeryToken(manager, "/reports");
        using var bi = await manager.PostAsync("/reports/bi/v1", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = biToken }));
        var payload = await bi.Content.ReadAsStringAsync();
        Check(bi.StatusCode == HttpStatusCode.OK && payload.Contains("crm.reporting.v1") && !payload.Contains("DEMO-INV"), "P9: BI export respects finance masking.");
        using var financialPage = await finance.GetAsync("/reports");
        var financeHtml = await financialPage.Content.ReadAsStringAsync();
        Check(financialPage.StatusCode == HttpStatusCode.OK && financeHtml.Contains("data-report-metric=\"collection\""), "P9: finance gets collection KPI.");
        await CheckNavigationLinks(finance, financeHtml, "finance");
        using var dealerPage = await dealer.GetAsync("/dealers");
        await CheckNavigationLinks(dealer, await dealerPage.Content.ReadAsStringAsync(), "dealer");
    }

    private static async Task CheckNavigationLinks(HttpClient client, string html, string role)
    {
        var nav = System.Text.RegularExpressions.Regex.Match(html, "<nav class=\"main-nav\">([\\s\\S]*?)</nav>").Groups[1].Value;
        var links = System.Text.RegularExpressions.Regex.Matches(nav, "href=\"([^\"]+)\"")
            .Select(x => WebUtility.HtmlDecode(x.Groups[1].Value)).Distinct().ToArray();
        Check(links.Length > 0, $"Unified UI: {role} has a shared navigation shell.");
        foreach (var link in links)
        {
            using var response = await client.GetAsync(link);
            Check(response.StatusCode == HttpStatusCode.OK, $"Unified UI: visible {role} navigation {link} must open successfully.");
        }
        if (role == "finance") Check(!links.Contains("/leads"), "Unified UI: finance menu must not link to forbidden leads.");
        if (role == "dealer") Check(!links.Contains("/reports") && !links.Contains("/customers"), "Unified UI: dealer menu must not link to forbidden internal pages.");
    }
}
