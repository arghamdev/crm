using Microsoft.AspNetCore.Hosting;

namespace Crm.Web.Presentation;

/// <summary>
/// The organizational font (IRANSansX). The font is commercial, so its files are not part of the repository: the licence
/// holder copies them into <c>wwwroot/fonts</c> (see docs/brand-font-fa.md). The @font-face sheet is linked only when the
/// files are there, so a missing font never causes a 404; without them the stack falls back to an installed IRANSans,
/// then Vazirmatn, Segoe UI and Tahoma.
/// </summary>
public sealed class BrandFont(IWebHostEnvironment environment)
{
    public static readonly string[] Weights = ["Regular", "Medium", "DemiBold", "Bold", "ExtraBold"];

    public bool Available { get; } = environment.WebRootFileProvider.GetFileInfo("fonts/IRANSansX-Regular.woff2").Exists;
}
