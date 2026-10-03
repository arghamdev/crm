# برگه تحویل اولویت ۸ — مدیریت شبکه نمایندگان

## وضعیت تحویل

اولویت ۸ در قالب نمونهٔ اجرایی تکمیل شده است. پیاده‌سازی با معماری ASP.NET Core MVC، Razor، HTMX، CSS سازگار با Tailwind و JavaScript بدون وابستگی به فریم‌ورک کلاینت انجام شده است.

## قابلیت‌های تحویل‌شده

- پروندهٔ مستقل نماینده با وضعیت‌های پیش‌نویس، در انتظار تأیید، فعال، تعلیق و خاتمه‌یافته
- دامنهٔ دسترسی مستقل Dealer در کنار Company، Branch و Territory
- قرارداد نماینده با تفکیک وظیفهٔ درخواست‌کننده و تأییدکننده
- تخصیص Territory با کنترل هم‌پوشانی و انحصار
- هدف فروش CRM-owned و Snapshot عملکرد ERP/BI-owned
- Snapshot مالی فقط‌خواندنی از Accounting با کنترل مجوز و تازگی داده
- سبد مشتریان نماینده و اتصال آن به Customer 360
- انتقال و بازیابی تخصیص نماینده هنگام Merge و Unmerge مشتری
- بستن اتمیک قراردادها، Territoryها و سبد مشتری هنگام خاتمهٔ همکاری
- جلوگیری از پایان آخرین قرارداد یا Territory معتبر نمایندهٔ فعال
- کنترل هم‌زمانی خوش‌بینانه با `ExpectedVersion`
- ثبت Actor، دلیل و زمان در تاریخچهٔ وضعیت
- فرم‌های Razor با Anti-forgery، اعتبارسنجی `422` برای HTMX و Redirect استاندارد برای ارسال معمولی
- Migration و اسکریپت‌های SQL Server برای Apply و Rollback
- تست‌های معماری، MVC/HTMX، Persistence و مرورگر Chromium در Quality Gate

## حساب‌های نمونه

| کاربر | گذرواژه | کاربرد |
|---|---|---|
| `sales.manager` | `Demo@1405` | مشاهده و مدیریت تجاری نمونه |
| `channel.manager` | `Demo@1405` | مدیریت قرارداد، قلمرو، هدف و تأیید نماینده |
| `dealer.user` | `Demo@1405` | مشاهدهٔ محدود به نمایندهٔ `P-D01` |

این حساب‌ها فقط برای محیط Demo هستند و نباید در محیط Production استفاده شوند.

## اجرای کنترل کیفیت

```bash
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln --configuration Release --no-restore
dotnet run --project tests/Crm.ArchitectureTests --configuration Release --no-build
dotnet run --project tests/Crm.WebTests --configuration Release --no-build
dotnet run --project tests/Crm.PersistenceTests --configuration Release --no-build
dotnet run --project tests/Crm.BrowserTests --configuration Release --no-build -- install --with-deps chromium
dotnet run --project tests/Crm.BrowserTests --configuration Release --no-build
```

اجرای SQL Server به متغیر `CRM_TEST_SQLSERVER` نیاز دارد. Workflow تجمعی فعلی `.github/workflows/priority9-quality-gate.yml` همین مسیر و آزمون‌های اولویت ۹ را با .NET 10، SQL Server 2022 و Chromium اجرا می‌کند.

## فایل‌های مرجع

- تحلیل و جزئیات پیاده‌سازی: `docs/priority-8-implementation-fa.md`
- معیارهای پذیرش و تست: `docs/priority-8-quality-gate-fa.md`
- Runbook استقرار و Rollback: `docs/runbooks/dealer-channel-deployment-fa.md`
- پیش‌نمایش مستقل: `preview/crm-priority8-dealers.html`

## نتیجهٔ کنترل محلی

- کنترل ساختار، جهت وابستگی‌ها و قراردادهای HTMX: موفق
- اسکن کلید خصوصی و اعتبارنامهٔ غیرنمونه: موفق
- Syntax فایل JavaScript اصلی و پیش‌نمایش: موفق
- کامپایل مستقیم لایه‌های Domain، Application، Infrastructure، Web و سورس تست‌ها: موفق
- تست رفتاری معماری، Scope نماینده، چرخهٔ قرارداد و Territory، Merge/Unmerge و خاتمهٔ همکاری: موفق
- ساخت مدل EF Core: موفق

محیط محلی تحویل، SDK کامل .NET 10، SQL Server و Chromium قابل اجرا نداشت؛ بنابراین اجرای واقعی Migration/Rollback روی SQL Server و Smoke مرورگر در Workflow افزوده شده و باید در CI نهایی تأیید شود.
