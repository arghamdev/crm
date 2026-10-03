using System.Net;
using System.Text.Json;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    private static async Task CheckSelfService(HttpClient manager, HttpClient expert, HttpClient dealer)
    {
        using(var response=await dealer.GetAsync("/portal")) {
            var html=await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode==HttpStatusCode.OK&&HtmlContains(html,"پرتال نماینده")&&html.Contains("/portal/requests"),"P10: portal shell and form must render.","p10-portal",html);
            Check(HeaderContains(response,"Cache-Control","no-store"),"P10: portal cannot be cached.");
        }
        using(var response=await manager.GetAsync("/portal")) Check(response.StatusCode!=HttpStatusCode.OK,"P10: company-wide manager cannot impersonate a dealer.");
        using(var response=await dealer.GetAsync("/mobile")) Check(response.StatusCode!=HttpStatusCode.OK,"P10: dealer cannot see internal mobile workspace.");
        using(var response=await dealer.GetAsync("/partner-requests")) Check(response.StatusCode!=HttpStatusCode.OK,"P10: dealer cannot access internal review desk.");
        var portalToken=await GetAntiforgeryToken(dealer,"/portal");
        using(var response=await dealer.PostAsync("/portal/requests",new FormUrlEncodedContent(new Dictionary<string,string>{["Subject"]="missing csrf"}))) Check(response.StatusCode==HttpStatusCode.BadRequest,"P10: portal submit requires antiforgery.");
        var subject="P10 HTTP "+Guid.NewGuid().ToString("N");
        var operation=Guid.NewGuid().ToString();
        var fields=new Dictionary<string,string>{["__RequestVerificationToken"]=portalToken,["OperationId"]=operation,["Kind"]="Complaint",["Subject"]=subject,["Description"]="<script>alert('sample')</script>"};
        async Task<HttpResponseMessage> Post(HttpClient client,string path,Dictionary<string,string> values,bool json=false) {
            var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new FormUrlEncodedContent(values)};
            request.Headers.Add("HX-Request","true");if(json)request.Headers.Accept.ParseAdd("application/json");
            return await client.SendAsync(request);
        }
        using(var response=await Post(dealer,"/portal/requests",fields)) Check(response.StatusCode==HttpStatusCode.NoContent&&HeaderContains(response,"HX-Redirect","/portal"),"P10: HTMX submit redirects to unified portal.");
        using(var response=await Post(dealer,"/portal/requests",fields)) Check(response.StatusCode==HttpStatusCode.NoContent,"P10: HTTP retry is idempotent.");
        fields["Subject"]=subject+" changed";
        using(var response=await Post(dealer,"/portal/requests",fields)) Check(response.StatusCode==HttpStatusCode.Conflict&&HeaderContains(response,"HX-Retarget","#selfServiceErrors"),"P10: changed retry shows conflict in error region.");
        using(var response=await dealer.GetAsync("/portal")) {
            var html=await response.Content.ReadAsStringAsync();
            Check(html.Contains("&lt;script&gt;")&&!html.Contains("<script>alert('sample')</script>"),"P10: portal input is HTML encoded.");
        }
        using(var response=await manager.GetAsync("/partner-requests")) {
            var html=await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode==HttpStatusCode.OK&&HtmlContains(html,subject),"P10: internal desk sees submitted request.","p10-inbox",html);
        }
        using(var response=await dealer.PostAsync("/portal/invoices/foreign/export",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=portalToken}))) Check(response.StatusCode==HttpStatusCode.NotFound,"P10: invoice ID enumeration denied.");
        using(var response=await expert.GetAsync("/mobile")) {
            var html=await response.Content.ReadAsStringAsync();
            CheckHtml(response.StatusCode==HttpStatusCode.OK&&html.Contains("data-mobile-action")&&html.Contains("/js/mobile.js"),"P10: mobile Razor forms and script render.","p10-mobile",html);
            Check(HeaderContains(response,"Permissions-Policy","geolocation=(self)")&&HeaderContains(response,"Cache-Control","no-store"),"P10: mobile has consented same-origin geolocation and no-store.");
        }
        var token=await GetAntiforgeryToken(expert,"/mobile");
        var plan=new Dictionary<string,string>{["__RequestVerificationToken"]=token,["OperationId"]=Guid.NewGuid().ToString(),["CustomerId"]="20000000-0000-4000-8000-000000000001",["PlannedAtUtc"]=DateTime.UtcNow.AddHours(1).ToString("s"),["Purpose"]="HTTP visit"};
        Guid visitId;
        using(var response=await Post(expert,"/mobile/visits",plan,true)) {
            Check(response.StatusCode==HttpStatusCode.OK,"P10: visit plan HTTP status.");
            if(response.StatusCode!=HttpStatusCode.OK)return;
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());visitId=json.RootElement.GetProperty("visitId").GetGuid();
        }
        var transition=new Dictionary<string,string>{["__RequestVerificationToken"]=token,["OperationId"]=Guid.NewGuid().ToString(),["ExpectedVersion"]="1",["Status"]="CheckedIn",["Outcome"]="شروع",["OccurredAtUtc"]=DateTimeOffset.UtcNow.ToString("O")};
        using(var response=await Post(expert,$"/mobile/visits/{visitId}",transition,true)) Check(response.StatusCode==HttpStatusCode.OK,"P10: checkin JSON endpoint.");
        using(var response=await Post(expert,$"/mobile/visits/{visitId}",transition,true)) {
            var json=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK&&json.Contains("\"replayed\":true"),"P10: receipt survives HTTP retry.");
        }
        transition["OperationId"]=Guid.NewGuid().ToString();transition["Status"]="Completed";transition["Outcome"]="نتیجه";
        using(var response=await Post(expert,$"/mobile/visits/{visitId}",transition,true)) Check(response.StatusCode==HttpStatusCode.Conflict,"P10: stale version returns 409 JSON.");
        transition["ExpectedVersion"]="2";
        using(var response=await Post(expert,$"/mobile/visits/{visitId}",transition,true)) Check(response.StatusCode==HttpStatusCode.OK,"P10: corrected version completes visit.");
        using(var response=await expert.PostAsync($"/mobile/visits/{visitId}",new FormUrlEncodedContent(new Dictionary<string,string>{["OperationId"]=Guid.NewGuid().ToString()}))) Check(response.StatusCode==HttpStatusCode.BadRequest,"P10: mobile endpoint requires CSRF even for offline replay.");
    }
}
