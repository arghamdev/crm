using System.Net;

namespace Crm.WebTests;

internal static partial class TestRunner
{
    /// <summary>The organizational font is first in the stack; its @font-face sheet is linked only when the licensed files exist.</summary>
    private static async Task CheckBrandFont(HttpClient client)
    {
        using (var css = await client.GetAsync("/css/site.css"))
            Check(css.StatusCode == HttpStatusCode.OK && (await css.Content.ReadAsStringAsync()).Contains("--font: \"IRANSansX\", \"IRANSans\"", StringComparison.Ordinal),
                "FONT: IRANSans is the first font of the site stack.");
        using (var fonts = await client.GetAsync("/css/fonts.css"))
            Check(fonts.StatusCode == HttpStatusCode.OK && (await fonts.Content.ReadAsStringAsync()).Contains("url(\"../fonts/IRANSansX-Regular.woff2\")", StringComparison.Ordinal),
                "FONT: the @font-face sheet points at the licensed IRANSansX files.");
        using var login = await client.GetAsync("/account/login");
        var html = await login.Content.ReadAsStringAsync();
        var present = File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts", "IRANSansX-Regular.woff2"));
        Check(present || !html.Contains("/css/fonts.css", StringComparison.Ordinal),
            "FONT: without the font files the page does not link fonts.css (no 404 for missing fonts).");
    }
}
