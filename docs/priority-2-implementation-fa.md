# گزارش تحلیل و پیاده‌سازی اولویت ۲ — Authentication & Identity

**وضعیت:** پیاده‌سازی نمونه تکمیل؛ اتصال واقعی سازمانی مشروط به IdP و زیرساخت محیط  
**معماری:** ASP.NET Core MVC، Razor، HTMX، JavaScript و Tailwind-compatible  
**داده:** In-Memory و کاملاً آزمایشی

## نتیجه بررسی پیش از اجرا

نسخه قبلی فقط Cookie آزمایشی، یک رشته Role/Scope داخل User و کنترل ساده SecurityVersion داشت. مدل‌های External Identity، Session، Role Assignment، Audit، اتصال امن هویت، انقضای مطلق نشست، محدودیت هم‌زمانی، پنل جزئیات هویت، Rate Limit و Data Protection قابل استقرار وجود نداشتند.

## موارد اجراشده

| الزام اولویت ۲ | پیاده‌سازی نمونه |
|---|---|
| OIDC استاندارد | Handler اختیاری با Authorization Code، PKCE، `MapInboundClaims=false` و `SaveTokens=false` |
| عدم نگهداری رمز Production | ورود محلی فقط در `Authentication:Mode=Demo`؛ OIDC در حالت سازمانی |
| Binding امن | جست‌وجوی دقیق `Issuer + Subject`؛ اتصال اولیه فقط به Pending User یکتا با ایمیل Verified |
| Cookie حداقلی | فقط `crm_user_id`، `session_id`، `security_version`، `display_name` و `auth_time` |
| Session سمت سرور | Idle=30m، Absolute=8h، سقف سه نشست، Touch با Throttle، Revoke فوری |
| چرخه عمر User | PendingActivation، Active، Suspended، Disabled و Archived |
| RBAC + Scope | Assignment مستقل، اعتبار زمانی، Reason، وضعیت Active/Revoked/Expired |
| Company Context | Company Scope برای صدور نشست الزامی است؛ Branch restriction با آن تقاطع می‌خورد |
| Access Snapshot | TTL پنج دقیقه، نقش/Permission/Scope سمت سرور، Invalidaton فوری روی تغییر |
| Resource Authorization | Query و Lookup مشتری پیش از Materialize شدن بر اساس Branch/Company محدود می‌شوند |
| Optimistic Concurrency | عملیات وضعیت و Role از Expected SecurityVersion استفاده می‌کنند |
| Audit | SignIn success/failure، Link، تغییر وضعیت/نقش، Revoke، AccessDenied و Session failure |
| HTMX expiry | پاسخ به درخواست HTMX با `HX-Redirect` به Session Expired؛ Login داخل Partial رندر نمی‌شود |
| Web hardening | Anti-forgery سراسری، Rate Limit ورود، CSP، HSTS Production، Secure/HttpOnly/SameSite Cookie |
| Reverse Proxy | پشتیبانی اختیاری با `KnownProxies` صریح و ForwardLimit؛ حالت Trust-all وجود ندارد |
| Data Protection | Application Name ثابت و Key Ring Path قابل تنظیم برای استقرار چند Node |
| UI مدیریت | فهرست، ایجاد Pending، جزئیات، Role/Scope، Session، External Identity و Audit |
| Self-service | صفحه نشست‌های کاربر و ابطال همه نشست‌های دیگر |
| قرارداد اولویت ۳ | `ICurrentUserContext` شامل User، Session و Company منتخب؛ در اولویت ۳ به Context ذخیره‌شده در Session ارتقا یافت |

## قواعد حساس پیاده‌سازی‌شده

1. کاربر ناشناخته هیچ‌گاه Active ساخته نمی‌شود.
2. ایمیل فقط برای اولین Binding و فقط در صورت `email_verified=true` استفاده می‌شود.
3. پس از Binding، شناسه پایدار فقط `Issuer + Subject` است.
4. Role، Permission یا Group ورودی IdP مجوز CRM ایجاد نمی‌کند.
5. تغییر وضعیت یا Role/Scope، SecurityVersion را افزایش و نشست‌های قبلی را باطل می‌کند.
6. فعال‌سازی دستی بدون External Identity رد می‌شود.
7. Return URL فقط اگر Local باشد پذیرفته می‌شود.
8. IP خام ذخیره نمی‌شود؛ خلاصه Hash و User-Agent پاک‌سازی‌شده در Audit/Session می‌روند.

## تنظیم و اجرای نمونه

حالت پیش‌فرض Demo است:

```text
user: sales.manager
password: Demo@1405
```

برای OIDC، `Authentication:Mode` را `Oidc` و Authority، ClientId، Credential و `Security:IpHashSalt` را از Secret Store تنظیم کنید. Redirect URI باید `/signin-oidc` باشد. Client Secret و Salt واقعی داخل Repository نیستند و Placeholder در حالت OIDC باعث توقف امن Startup می‌شود.

## کنترل‌ها

```bash
node scripts/verify-structure.mjs
node --check src/Crm.Web/wwwroot/js/site.js
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln --configuration Release
dotnet run --project tests/Crm.ArchitectureTests --configuration Release
dotnet run --project tests/Crm.WebTests --configuration Release
```

Console Test سناریوهای Scope، Permission، Binding دقیق، رد ایمیل تأییدنشده، Link کاربر Pending، ایجاد/ابطال Session، تغییر SecurityVersion، Invalidation Snapshot و Audit را پوشش می‌دهد.

`Crm.WebTests` با `WebApplicationFactory` سناریوهای Login، Permission، محدودسازی Branch، Anti-forgery، HTMX expiry، هدرهای امنیتی، Rate Limit، OIDC Code+PKCE و اشتراک Cookie بین دو Node با Store/Key Ring مشترک را پوشش می‌دهد. کد این تست‌ها و CI gate اضافه شده‌اند، اما اجرای باینری آن‌ها در محیط فعلی به‌دلیل نبود .NET SDK ممکن نبود.

Runbookهای اختلال IdP، چرخش Credential، ابطال Session و آزمون Shared Key Ring در `docs/runbooks` تحویل شده‌اند. چک‌لیست Test Provider نیز در `docs/test-oidc-environment-fa.md` قرار دارد.

## موارد وابسته به محیط که هنوز ادعای Done Production ندارند

| مورد | دلیل | اقدام محیط Test/Production |
|---|---|---|
| Login واقعی OIDC | Challenge/PKCE خودکار تست می‌شود؛ Authority و Client سازمانی ارائه نشده | ثبت Client، Redirect URI و تست Callback/Logout واقعی |
| MFA/Step-up | Policy و Claim واقعی `acr/amr` نامشخص است | الزام MFA در IdP و تست نقش حساس |
| Shared Key Ring | تست دو TestServer اضافه شده؛ Storage سازمانی مشخص نیست | Mount/Blob/Redis محافظت‌شده و اجرای Runbook روی دو Node واقعی |
| Persistence | نمونه In-Memory است | EF Core + DB، Indexهای سند و Migration |
| Audit immutable | مخزن نمونه پایدار نیست | Append-only/SIEM، Retention و Alert |
| Reverse Proxy | کد و Fail-fast آماده است؛ IP واقعی Proxy مشخص نیست | ثبت `KnownProxies` و تست Scheme/IP در محیط |
| Build/Security Test | Test runner و CI gate اضافه شده؛ محیط حاضر .NET ندارد | اجرای CI و رفع هر خطای Build/Test |
| E2E/Load Test | محیط اجرایی و Test IdP لازم است | تست Callback، Replay و Load در محیط Test |
| Secret Rotation | Runbook تحویل شده؛ Vault/مالک عملیات مشخص نیست | اجرای Drill چرخش Credential |

این موارد مانع ارزیابی معماری و UI نمونه نیستند، اما پیش از اعلام Production Ready الزامی‌اند.
