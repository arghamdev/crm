using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckListWorkspaces(HttpClient manager, HttpClient expert, HttpClient finance)
    {
        string html;
        // Views and URL state: a tab is a real link, the fragment keeps the address bar in sync.
        using (var response = await manager.GetAsync("/leads?view=mine"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("list-tabs__tab is-active\" href=\"/leads?view=mine\"") &&
                      html.Contains("hx-get=\"/leads/table?view=overdue\"") && html.Contains("list-pager"),
                "LISTW: the lead list renders its tabs as links with the active view and a pager.", "listw-leads", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/leads/table?view=overdue&sort=score&pageSize=20")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains("id=\"leadList\"") &&
                  response.Headers.TryGetValues("HX-Replace-Url", out var url) && url.Single() == "/leads?view=overdue&sort=score&pageSize=20",
                "LISTW: the list fragment replaces the address bar with the canonical state URL.");
        }
        using (var response = await manager.GetAsync("/leads/import/template"))
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Check(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "text/csv" &&
                  bytes.Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()) && Encoding.UTF8.GetString(bytes).Contains("نام سرنخ"),
                "LISTW: the import template is a UTF-8 CSV with a BOM and Persian headers.");
        }

        // Import (ورود اطلاعات): CSRF required; valid rows are created, failing rows are reported by line.
        var token = await GetAntiforgeryToken(manager, "/leads/import");
        var leadName = "واردات وب " + Guid.NewGuid().ToString("N")[..6];
        MultipartFormDataContent ImportForm(bool withToken)
        {
            var content = new MultipartFormDataContent();
            if (withToken) content.Add(new StringContent(token), "__RequestVerificationToken");
            content.Add(new StringContent("B01"), "branchId");
            var file = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(
                $"نام سرنخ,شخص تماس,تلفن,ایمیل,منبع\r\n\"{leadName}\",نگار امینی,0217{Random.Shared.Next(1000000, 9999999)},,نمایشگاه\r\n,بدون نام,02100000001,,\r\n")).ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            content.Add(file, "file", "leads.csv");
            return content;
        }
        using (var content = ImportForm(withToken: false))
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/import", content)))
            Check(response.StatusCode == HttpStatusCode.BadRequest, "LISTW: an import without the anti-forgery token is refused.");
        using (var content = ImportForm(withToken: true))
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/import", content)))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && HeaderContains(response, "HX-Trigger", "leadsRefreshed") &&
                      HtmlContains(html, "1 مورد انجام شد") && HtmlContains(html, "ردیف 3"),
                "LISTW: the import creates the valid row and reports the nameless row by its line.", "listw-import", html);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/leads/table?q=" + Uri.EscapeDataString(leadName)))) html = await response.Content.ReadAsStringAsync();
        var leadId = Regex.Match(html, "href=\"/leads/([0-9a-f-]{36})\"").Groups[1].Value;
        Check(leadId.Length == 36 && HtmlContains(html, leadName), "LISTW: the imported lead is listed and searchable.");
        if (leadId.Length != 36) return;

        // Bulk next action: the drawer carries the selection; invalid dates stay in the drawer (422).
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"/leads/bulk/next-action?ids={leadId}")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains($"name=\"Ids\" value=\"{leadId}\""), "LISTW: the bulk next-action drawer carries the selected ids.");
            token = WebUtility.HtmlDecode(AntiforgeryRegex().Match(html).Groups[1].Value);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/bulk/next-action",
                   Form(token, ("Ids", leadId), ("NextAction", "تماس پیگیری وب"), ("DueDate", "1405/13/40"), ("DueTime", "10:00")))))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity && HtmlContains(await response.Content.ReadAsStringAsync(), "تاریخ"),
                "LISTW: an invalid date in the bulk next-action drawer is reported in place.");
        var tomorrow = Crm.Domain.Common.TehranTime.Date(DateTimeOffset.UtcNow.AddDays(1));
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/bulk/next-action",
                   Form(token, ("Ids", leadId), ("NextAction", "تماس پیگیری وب"), ("DueDate", tomorrow), ("DueTime", "10:00")))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "leadChanged"),
                "LISTW: a valid bulk next action closes the drawer and refreshes the list.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/leads/table?q=" + Uri.EscapeDataString(leadName))))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "تماس پیگیری وب"), "LISTW: the planned next action shows in the row.");

        // Bulk assign: permission enforced on the server, empty selection explained, valid selection applied.
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Get, $"/leads/bulk/assign?ids={leadId}")))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "LISTW: a sales expert cannot open the bulk assign drawer.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, "/leads/bulk/assign")))
            Check(response.StatusCode == HttpStatusCode.OK && HtmlContains(await response.Content.ReadAsStringAsync(), "هیچ سرنخی انتخاب نشده"),
                "LISTW: a bulk action without a selection explains what is missing.");
        string owner;
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"/leads/bulk/assign?ids={leadId}")))
        {
            html = await response.Content.ReadAsStringAsync();
            owner = Regex.Match(html, "name=\"OwnerUserId\"><option value=\"\">[^<]*</option>\\s*<option value=\"([0-9a-f-]{36})\"").Groups[1].Value;
            Check(response.StatusCode == HttpStatusCode.OK && owner.Length == 36, "LISTW: the bulk assign drawer lists eligible owners.");
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/bulk/assign", Form(token, ("Ids", leadId), ("OwnerUserId", ""), ("Reason", "")))))
            Check(response.StatusCode == HttpStatusCode.UnprocessableEntity, "LISTW: bulk assign without owner and reason stays in the drawer.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/leads/bulk/assign",
                   Form(token, ("Ids", leadId), ("OwnerUserId", owner), ("Reason", "ارجاع گروهی وب"), ("DueHours", "8")))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "leadChanged"),
                "LISTW: bulk assign applies to the selected lead and refreshes the list.");

        // Customers: views, filters and the bulk follow-up task.
        using (var response = await manager.GetAsync("/customers?view=followup"))
        {
            html = await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode == HttpStatusCode.OK && html.Contains("list-tile list-tile--blue is-active") && HtmlContains(html, "صنایع غذایی سپهر") &&
                      HtmlContains(html, "پیگیری امروز") && HtmlContains(html, "نیازمند تکمیل"),
                "LISTW: the customer list filters to accounts with a follow-up due today via its counter tile.", "listw-customers", html);
        }
        using (var response = await expert.SendAsync(Htmx(HttpMethod.Get, "/customers/table")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && HtmlContains(html, "از <b>1</b> حساب") && !HtmlContains(html, "بازرگانی نخل جنوب"),
                "LISTW: the expert's customer list stays inside the branch.");
        }
        using (var response = await finance.SendAsync(Htmx(HttpMethod.Get, $"/customers/bulk/task?ids={AccountId}")))
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "LISTW: finance (no Activity.Create) cannot open the bulk task drawer.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"/customers/bulk/task?ids={AccountId}")))
        {
            html = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode == HttpStatusCode.OK && html.Contains($"name=\"Ids\" value=\"{AccountId}\""), "LISTW: the bulk task drawer carries the selected accounts.");
            token = WebUtility.HtmlDecode(AntiforgeryRegex().Match(html).Groups[1].Value);
        }
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Post, "/customers/bulk/task",
                   Form(token, ("Ids", AccountId), ("Subject", "پیگیری گروهی وب"), ("DueDate", tomorrow), ("DueTime", "11:00"), ("Priority", "High")))))
            Check(response.StatusCode == HttpStatusCode.NoContent && HeaderContains(response, "HX-Trigger", "customerChanged"),
                "LISTW: the bulk follow-up task is saved and refreshes the customer list.");
        using (var response = await manager.SendAsync(Htmx(HttpMethod.Get, $"/customers/{AccountId}/activities")))
            Check(HtmlContains(await response.Content.ReadAsStringAsync(), "پیگیری گروهی وب"), "LISTW: the bulk task appears in the account's activities.");
    }
}
