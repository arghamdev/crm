using System.Net;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckDealerIncentives(HttpClient channel, HttpClient finance, HttpClient expert, HttpClient dealer)
    {
        string html;
        using (var response = await channel.GetAsync("/dealers/commissions"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "طرح کمیسیون پله‌ای ۱۴۰۵") &&
                      html.Contains("/dealers/commissions/calculate") && html.Contains("/dealers/commissions/plan") && !html.Contains("/decide\""),
                "DLR7: channel manager sees the plan editor and the calculate action but no approval forms.", "dlr7-channel", html);
        }
        using (var response = await expert.GetAsync("/dealers/commissions")) Check(response.StatusCode != HttpStatusCode.OK, "DLR7: sales experts cannot open dealer commissions.");
        using (var response = await dealer.GetAsync("/dealers/ranking")) Check(response.StatusCode != HttpStatusCode.OK, "DLR7: dealer users cannot open the dealer ranking.");

        var period = Crm.Domain.Channel.ChannelPeriod.Key(DateTimeOffset.UtcNow);
        using (var response = await channel.PostAsync("/dealers/commissions/calculate", new FormUrlEncodedContent(new Dictionary<string, string> { ["period"] = period })))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "DLR7: calculation without an anti-forgery token is rejected.");
        var financeToken = await GetAntiforgeryToken(finance, "/dealers/commissions");
        using (var response = await finance.PostAsync("/dealers/commissions/calculate", Form(financeToken, ("period", period))))
            Check(Denied(response), "DLR7: finance cannot calculate commissions.");

        var token = await GetAntiforgeryToken(channel, "/dealers/commissions");
        using (var response = await channel.PostAsync("/dealers/commissions/calculate", Form(token, ("period", period))))
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == $"/dealers/commissions?period={period}",
                "DLR7: calculation redirects back to the period workspace.");
        using (var response = await channel.GetAsync($"/dealers/commissions?period={period}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "3 صورت کمیسیون محاسبه شد") && html.Contains("data-dealer=\"DLR-0002\"") && HtmlContains(html, "217,000,000"),
                "DLR7: the calculated statements and the outcome message are shown.", "dlr7-calculated", html);
        }

        using (var response = await finance.GetAsync($"/dealers/commissions?period={period}")) html = await response.Content.ReadAsStringAsync();
        var row = Regex.Match(html, "data-dealer=\"DLR-0002\"[\\s\\S]*?/dealers/commissions/([0-9a-f-]{36})/decide[\\s\\S]*?name=\"expectedVersion\" value=\"(\\d+)\"");
        CheckHtml(row.Success, "DLR7: finance gets the approve/reject form for a calculated statement.", "dlr7-finance", html);
        if (!row.Success) return;
        financeToken = await GetAntiforgeryToken(finance, "/dealers/commissions");
        using (var response = await channel.PostAsync($"/dealers/commissions/{row.Groups[1].Value}/decide",
                   Form(token, ("period", period), ("approve", "true"), ("expectedVersion", row.Groups[2].Value))))
            Check(Denied(response), "DLR7: the channel manager cannot approve commissions.");
        using (var response = await finance.PostAsync($"/dealers/commissions/{row.Groups[1].Value}/decide",
                   Form(financeToken, ("period", period), ("approve", "true"), ("note", "کنترل اسناد انجام شد"), ("expectedVersion", row.Groups[2].Value))))
            Check(response.StatusCode == HttpStatusCode.Redirect, "DLR7: finance approval redirects back.");
        using (var response = await finance.GetAsync($"/dealers/commissions?period={period}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "به مبلغ 217,000,000 ریال تأیید شد") && HtmlContains(html, "کنترل اسناد انجام شد"),
                "DLR7: the approval is confirmed and the decision note is shown.", "dlr7-approved", html);
        }

        using (var response = await channel.PostAsync("/dealers/commissions/plan",
                   Form(token, ("period", period), ("name", "طرح نادرست"), ("thresholds", "10"), ("rates", "1"), ("expectedVersion", "1"))))
            Check(response.StatusCode == HttpStatusCode.Redirect, "DLR7: an invalid plan redirects back with the error.");
        using (var response = await channel.GetAsync($"/dealers/commissions?period={period}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "اولین پله باید از تحقق ۰٪ شروع شود") && HtmlContains(html, "طرح کمیسیون پله‌ای ۱۴۰۵"),
                "DLR7: plan validation errors are shown and the stored plan is unchanged.", "dlr7-plan-error", html);
        }

        using (var response = await finance.GetAsync("/dealers/ranking"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "هنوز ارزیابی برای این دوره اجرا نشده است") && !html.Contains("/dealers/ranking/run"),
                "DLR7: finance reads the ranking but cannot run an evaluation.", "dlr7-ranking-finance", html);
        }
        using (var response = await finance.PostAsync("/dealers/ranking/run", Form(financeToken, ("period", period))))
            Check(Denied(response), "DLR7: finance cannot run the dealer evaluation.");
        token = await GetAntiforgeryToken(channel, "/dealers/ranking");
        using (var response = await channel.PostAsync("/dealers/ranking/run", Form(token, ("period", period))))
            Check(response.StatusCode == HttpStatusCode.Redirect, "DLR7: the evaluation run redirects back to the ranking.");
        using (var response = await channel.GetAsync($"/dealers/ranking?period={period}"))
        {
            html = await response.Content.ReadAsStringAsync();
            var first = Regex.Match(html, "<tr data-dealer=\"(DLR-\\d+)\">");
            CheckHtml(HtmlContains(html, "ارزیابی 3 نماینده انجام") && first.Success && first.Groups[1].Value == "DLR-0002",
                "DLR7: the ranking lists the over-achieving dealer first.", "dlr7-ranking", html);
        }
        using (var response = await channel.GetAsync("/dealers"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(html.Contains("href=\"/dealers/commissions\"") && html.Contains("href=\"/dealers/ranking\""),
                "DLR7: the dealer workspace links to commissions and ranking.", "dlr7-dealers", html);
        }
    }

    private static bool Denied(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.Forbidden ||
        response.StatusCode == HttpStatusCode.Redirect &&
        response.Headers.Location?.OriginalString.Contains("/account/access-denied", StringComparison.OrdinalIgnoreCase) == true;

    private static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] fields) =>
        new(fields.Select(x => new KeyValuePair<string, string>(x.Name, x.Value))
            .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token)));
}
