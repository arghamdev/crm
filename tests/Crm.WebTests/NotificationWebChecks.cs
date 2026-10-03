using System.Net;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckNotificationSettings(HttpClient client)
    {
        string html;
        using (var response = await client.GetAsync("/account/notifications"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("name=\"smsEnabled\"") && html.Contains("name=\"enabledCategories\"") &&
                      HtmlContains(html, "حالت نمایشی"), "NTF: the settings page renders channels, categories and the demo-mode note.", "ntf-page", html);
        }
        var token = await GetAntiforgeryToken(client, "/account/notifications");
        using (var response = await client.PostAsync("/account/notifications", Form(token, ("emailEnabled", "true"), ("smsEnabled", "true"))))
            Check(response.StatusCode == HttpStatusCode.Redirect, "NTF: saving redirects back.");
        using (var response = await client.GetAsync("/account/notifications"))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "برای دریافت پیامک، شمارهٔ همراه را وارد کنید"), "NTF: SMS without a mobile is refused with a message.");
        using (var response = await client.PostAsync("/account/notifications", Form(token, ("emailEnabled", "true"), ("smsEnabled", "true"),
                   ("mobile", "۰۹۱۲۳۳۳۴۴۵۵"), ("enabledCategories", "ServiceEscalation"))))
            Check(response.StatusCode == HttpStatusCode.Redirect, "NTF: valid settings are saved.");
        using (var response = await client.GetAsync("/account/notifications"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(html.Contains("value=\"09123334455\"") && HtmlContains(html, "تنظیمات اعلان ذخیره شد") &&
                      !html.Contains("value=\"CommissionApproval\" checked"), "NTF: the saved mobile and muted categories are shown.", "ntf-saved", html);
        }
        using (var response = await client.GetAsync("/dashboard"))
            Check((await response.Content.ReadAsStringAsync()).Contains("href=\"/account/notifications\""), "NTF: the top bar bell links to notifications.");
    }
}
