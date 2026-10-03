# صورت‌جلسه بستن مرحله صفر — نسخه نمونه و آزمایشی

**وضعیت:** بسته‌شده برای Demo  
**دامنه اعتبار:** فقط نسخه نمونه؛ جایگزین تصمیم Production نیست  
**تاریخ مبنا:** ۱۴۰۵/۰۶/۳۰

## تصمیم‌های بسته‌شده

| حوزه | تصمیم نسخه نمونه |
|---|---|
| معماری | Modular Monolith با لایه‌های Domain، Application، Infrastructure و Web |
| بستر | .NET 10 LTS و ASP.NET Core MVC/Razor |
| رابط | فارسی، RTL، HTMX و Tailwind-compatible |
| داده | In-Memory Store با Seed ثابت؛ Reset با راه‌اندازی مجدد برنامه |
| احراز هویت | Cookie Login آزمایشی؛ Production با OIDC Code Flow + PKCE |
| سازمان پایلوت | `P-C01` شرکت اصلی، `P-B01` مرکزی، `P-B02` فروش مستقیم، `P-B03` کانال، `P-D01` نماینده |
| Customer SoR | CRM برای رابطه/Prospect؛ ERP Mock برای حساب مالی |
| قیمت | Base Price از ERP Mock؛ Discount Rule و Approval در CRM |
| Integration | Port/Adapter و Job-based Mock؛ بدون Message Broker خارجی |
| Observability | Logging ساخت‌یافته، Correlation ID و Health Check |

## Workflowهای نمونه

### Lead

`New → Contacted → Qualified → Converted`

مسیرهای جایگزین: `Nurture` و `Disqualified`.

### Opportunity

`Discovery → Solution → Negotiation → Quote → Won/Lost`

### Quote

`Draft → Submitted → PendingApproval → Approved/Rejected → Sent → Accepted/Expired`

## ماتریس اختیار تخفیف نمونه

| نقش | سقف تخفیف | حداقل Margin | تأیید بعدی |
|---|---:|---:|---|
| کارشناس فروش | ۵٪ | ۲۵٪ | سرپرست فروش |
| سرپرست فروش | ۸٪ | ۲۲٪ | مدیر تجاری |
| مدیر تجاری | ۱۲٪ | ۱۸٪ | مدیر فروش + مالی |
| مدیر فروش + مالی | ۲۰٪ | ۱۵٪ | تصمیم مشترک و Audit |

تخفیف بیش از ۲۰٪ یا Margin کمتر از ۱۵٪ در نسخه نمونه رد می‌شود.

## حجم و SLA آزمایشی

| شاخص | فرض نمونه |
|---|---:|
| مشتری | ۱۰٬۰۰۰ رکورد |
| سرنخ | ۵۰٬۰۰۰ رکورد |
| فرصت | ۳۰٬۰۰۰ رکورد |
| کاربر هم‌زمان | ۲۰۰ کاربر |
| P95 صفحه کامل | حداکثر ۲ ثانیه |
| P95 پاسخ HTMX | حداکثر ۸۰۰ میلی‌ثانیه |
| P95 جست‌وجو | حداکثر ۱٫۵ ثانیه |

اعداد بالا Performance Budget هستند و ادعای نتیجه Load Test محسوب نمی‌شوند.

## داده نمونه

- کاربران، مشتریان، سرنخ‌ها، فرصت‌ها، پیشنهادها و اقدامات کاملاً ساختگی‌اند.
- هیچ داده شخصی یا مالی واقعی در Seed قرار نمی‌گیرد.
- شناسه‌ها پایدار و قابل تکرار هستند تا تست UI و Acceptance آسان باشد.
- وضعیت مالی فقط Projection آزمایشی و Read-only است.

## معیار خروج مرحله صفر نمونه

- دامنه و اقلام خارج MVP مشخص است. **انجام شد.**
- System of Record مشخص است. **انجام شد.**
- Role + Scope پایه مشخص است. **انجام شد.**
- Workflow و Approval نمونه مشخص است. **انجام شد.**
- حجم و SLA فرضی ثبت شده است. **انجام شد.**
- روش Authentication نمونه و Production تفکیک شده است. **انجام شد.**
- محدودیت‌های Demo صریح ثبت شده است. **انجام شد.**

مرحله صفر برای ساخت و ارزیابی نمونه بسته است. ورود Production نیازمند جایگزینی فرض‌های نمونه با اطلاعات واقعی سازمان و امضای مالکان کسب‌وکار است.
