using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckCustomerForm(HttpClient manager)
    {
        string html;
        using (var response = await manager.GetAsync("/customers/create"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("name=\"Profile.NationalCode\"") && html.Contains("name=\"Profile.LegalNationalId\"") &&
                      html.Contains("name=\"Contacts.Index\"") && html.Contains("enctype=\"multipart/form-data\"") && HtmlContains(html, "اطلاعات تکمیلی") &&
                      HtmlContains(html, "اطلاعات رابط‌های مشتری") && html.Contains("name=\"logo\""),
                "CUSTF: the create form renders individual/legal fields, contact rows and the logo picker.", "custf-create", html);
        }
        using (var response = await manager.GetAsync("/customers/cities?Profile.Province=" + Uri.EscapeDataString("اصفهان")))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "کاشان"), "CUSTF: cities are offered for the chosen province.");
        using (var response = await manager.GetAsync("/customers/contact-row"))
            Check(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsStringAsync()).Contains("data-remove-row"),
                "CUSTF: a new contact row can be appended.");

        var token = await GetAntiforgeryToken(manager, "/customers/create");
        MultipartFormDataContent Form(params (string Name, string Value)[] fields)
        {
            var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
            foreach (var (name, value) in fields) content.Add(new StringContent(value), name);
            return content;
        }
        (string, string)[] Base(string name) => [("Name", name), ("City", "اصفهان"), ("Owner", "سارا احمدی"), ("BranchId", "B02"), ("Segment", "استاندارد")];

        using (var response = await manager.PostAsync("/customers/create", Form([.. Base("فروشگاه بدون نام"), ("Kind", "Individual"),
                   ("Profile.Mobile1", "12345"), ("Profile.BirthDate", "1370/13/40")])))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.UnprocessableEntity && HtmlContains(html, "نام مشتری حقیقی الزامی است") &&
                      HtmlContains(html, "شمارهٔ همراه باید ۱۱ رقم") && HtmlContains(html, "تاریخ را به شکل شمسی"),
                "CUSTF: invalid individual fields are reported next to their inputs.", "custf-invalid", html);
        }

        var name = "فروشگاه تست فرم " + Guid.NewGuid().ToString("N")[..6];
        var nationalCode = ValidNationalCode("0" + Random.Shared.Next(10_000_000, 99_999_999));
        using (var content = Form([.. Base(name), ("Kind", "Individual"), ("Profile.Title", "آقای"), ("Profile.FirstName", "امید"),
                   ("Profile.LastName", "فرمی"), ("Profile.Mobile1", "۰۹۱۳۱۲۳۴۵۶۷"), ("Profile.NationalCode", nationalCode), ("Profile.Province", "اصفهان"),
                   ("Profile.Address", "خیابان چهارباغ"), ("Profile.PostalCode", "8134567891"), ("Profile.BirthDate", "۱۳۶۸/۰۴/۱۰"),
                   ("Profile.EmployeeCount", "۱ تا ۱۰ نفر"), ("Contacts.Index", "a"), ("Contacts[a].FirstName", "زهرا"), ("Contacts[a].LastName", "منشی"),
                   ("Contacts[a].Mobile", "09130000002"), ("Contacts[a].Extension", "3"), ("Contacts.Index", "b")]))
        {
            var logo = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13]);
            logo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(logo, "logo", "logo.png");
            using var response = await manager.PostAsync("/customers/create", content);
            Check(response.StatusCode == HttpStatusCode.Redirect, $"CUSTF: a valid individual customer with contact and logo is created (got {(int)response.StatusCode}).");
        }
        using (var response = await manager.GetAsync("/customers/table?q=" + Uri.EscapeDataString(name))) html = await response.Content.ReadAsStringAsync();
        var id = Regex.Match(html, "/customers/([0-9a-f-]{36})").Groups[1].Value;
        Check(id.Length == 36, "CUSTF: the new customer is listed.");
        if (id.Length != 36) return;
        using (var response = await manager.GetAsync($"/customers/{id}"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "اطلاعات شخص") && HtmlContains(html, "1368/04/10") && HtmlContains(html, "زهرا منشی") &&
                      html.Contains($"/customers/{id}/logo") && html.Contains($"/customers/{id}/contacts/"),
                "CUSTF: details show the profile, Jalali birth date, contact person and logo.", "custf-details", html);
        }
        using (var response = await manager.GetAsync($"/customers/{id}/logo"))
            Check(response.Content.Headers.ContentType?.MediaType == "image/png", "CUSTF: the logo is served as PNG.");
        using (var response = await manager.GetAsync($"/customers/{id}/edit"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(HtmlContains(html, "value=\"امید\"") && html.Contains("data-kind=\"Individual\"") && html.Contains("name=\"removeLogo\""),
                "CUSTF: the edit form is pre-filled with the profile.", "custf-edit", html);
        }
    }

    private static string ValidNationalCode(string nine)
    {
        nine = nine[..9];
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (nine[i] - '0') * (10 - i);
        var r = sum % 11;
        return nine + (r < 2 ? r : 11 - r);
    }
}
