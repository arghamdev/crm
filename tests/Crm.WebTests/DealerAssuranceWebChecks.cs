using System.Net;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckDealerAssurance(HttpClient channel, HttpClient finance, HttpClient expert)
    {
        string html;
        using (var response = await channel.GetAsync("/dealers"))
            html = await response.Content.ReadAsStringAsync();
        var pilot = "a0000000-0000-4000-8000-000000000001";
        using (var response = await channel.GetAsync($"/dealers/{pilot}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(html.Contains($"hx-get=\"/dealers/{pilot}/assurance\""), "DLR7G: the dealer page lazy-loads the guarantees/training panel.", "dlr7g-details", html);
        }
        using (var response = await channel.GetAsync($"/dealers/{pilot}/assurance"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("BG-1404-7781") && HtmlContains(html, "نزدیک سررسید") &&
                      HtmlContains(html, "66.7٪") && html.Contains($"/dealers/{pilot}/guarantees") && html.Contains($"/dealers/{pilot}/trainings"),
                "DLR7G: the channel manager sees guarantees, coverage, expiry warning and both forms.", "dlr7g-panel", html);
        }
        using (var response = await expert.GetAsync($"/dealers/{pilot}/assurance"))
            Check(response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.Redirect,
                $"DLR7G: a sales expert gets no guarantee data (got {(int)response.StatusCode}).");

        var token = await GetAntiforgeryToken(finance, $"/dealers/{pilot}/assurance");
        using (var response = await finance.PostAsync($"/dealers/{pilot}/guarantees", Form(token, ("Type", "CashDeposit"), ("Number", "DEP-77"),
                   ("Amount", "500000000"), ("IssuedOn", "۱۴۰۵/۰۶/۲۰"))))
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString.Contains("#assurance") == true,
                "DLR7G: finance registers a cash deposit and returns to the panel.");
        using (var response = await finance.PostAsync($"/dealers/{pilot}/trainings", Form(token, ("Title", "x"), ("Topic", "Sales"), ("HeldOn", "1405/06/01"),
                   ("Hours", "2"), ("Participants", "2"))))
            Check(Denied(response), "DLR7G: finance cannot record training.");
        using (var response = await finance.GetAsync($"/dealers/{pilot}/assurance"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(html.Contains("DEP-77") && HtmlContains(html, "تضمین DEP-77 به مبلغ 500,000,000 ریال ثبت شد"),
                "DLR7G: the new deposit and confirmation are shown.", "dlr7g-added", html);
            var row = Regex.Match(html, "data-guarantee=\"DEP-77\"[\\s\\S]*?/guarantees/([0-9a-f-]{36})/decide[\\s\\S]*?name=\"ExpectedVersion\" value=\"(\\d+)\"");
            Check(row.Success, "DLR7G: the deposit can be released or forfeited.");
            if (row.Success)
                using (var decide = await finance.PostAsync($"/dealers/{pilot}/guarantees/{row.Groups[1].Value}/decide",
                           Form(token, ("Forfeit", "true"), ("Reason", "تسویه مطالبات معوق"), ("ExpectedVersion", row.Groups[2].Value))))
                    Check(decide.StatusCode == HttpStatusCode.Redirect, "DLR7G: forfeiting redirects back.");
        }
        using (var response = await finance.GetAsync($"/dealers/{pilot}/assurance"))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "ضبط‌شده"), "DLR7G: the forfeited deposit shows its status.");

        var period = Crm.Domain.Channel.ChannelPeriod.Key(DateTimeOffset.UtcNow);
        using (var response = await finance.GetAsync($"/dealers/commissions?period={period}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "حسابداری") && (html.Contains("ACC-CM-") || HtmlContains(html, "در صف ارسال به حسابداری")),
                "DLR7G: the approved commission shows its accounting handoff.", "dlr7g-payout", html);
        }
    }
}
