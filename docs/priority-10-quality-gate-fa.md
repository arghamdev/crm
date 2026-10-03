# آزمون‌های اولویت ۱۰

تاریخ گزارش: ۲۸ سپتامبر ۲۰۲۶، تهران. این سند بین آزمون اجراشده و مسیر آمادهٔ اجرا تفکیک می‌کند.

| لایه | پوشش |
|---|---|
| Architecture/Service | Grant دقیق Dealer، رد دامنهٔ شرکت/شعبهٔ دیگر، عدم افشای Cost/Margin، مجوز مستقل مالی، قیمت سمت سرور و عدم ثبت سفارش قطعی از پرتال |
| گردش درخواست | replay یکسان، conflict برای payload متفاوت، لغو با Version، بررسی مجوزهای ایجاد Lead/مدیریت حساب، حفاظت نرمال‌شده، سفارش متصل، حساب PendingActivation و لغو نقش |
| Mobile | مالک بازدید، مشتری مجاز، برنامه‌ریزی تکرارناپذیر، مختصات بدون رضایت/نامعتبر، صف قدیمی، Version قدیمی، شروع/پایان/لغو و ثبت timeline |
| یکپارچگی | انتقال و بازگردانی روابط Portal/Mobile هنگام Merge/Unmerge؛ تغییر نسخه و رد صف قدیمی |
| Web/HTMX | ورود و نمایش Razor، مسیریابی، فرم POST، CSRF، no-store، encoding ورودی، HTMX redirect، خطای 409 و JSON acknowledgment |
| JS | FIFO، تکرار شناسه، تغییر payload، قطع شبکه، عدم حذف عمل قبل از acknowledgment، توقف روی تعارض، محدودیت ۵۰ عمل، TTL و عدم ذخیرهٔ پایدار صف |
| پیش‌نمایش | ورود، مسیرهای قبلی و جدید، نقش نماینده، درخواست → بررسی → Lead مشترک، CSV، بازدید آفلاین و تعارض در harness منطق واقعی اسکریپت |
| EF | نگاشت سه Entity، FK، Version، کلیدهای یکتا، شناسایی شناسه‌های قدیمی، تولید SQL ارتقا و بازگشت بدون اتصال DB |

فرمان‌های اصلی:

```sh
node scripts/verify-structure.mjs
node scripts/scan-repository-secrets.mjs
node scripts/build-self-service-sql.mjs
node scripts/build-unified-preview.mjs
node scripts/test-unified-preview.mjs
node scripts/test-mobile-outbox.mjs
dotnet restore EnterpriseCrm.sln
dotnet build EnterpriseCrm.sln -c Release --no-restore
dotnet run --project tests/Crm.ArchitectureTests -c Release --no-build
dotnet run --project tests/Crm.WebTests -c Release --no-build
dotnet run --project tests/Crm.PersistenceTests -c Release --no-build
```

برای gate پایگاه داده، `CRM_REQUIRE_SQLSERVER=true` و `CRM_TEST_SQLSERVER` با نام DB شامل `Test` یا `Ci` لازم است. آزمون DB را پاک می‌کند؛ فقط روی DB آزمایشی. `CRM_TEST_ROLLBACK=true` بازگشت P10→P8 و سپس ارتقا و بازگشت قدیمی P8→P7 و مسیر اسکریپت‌های مستقل را بررسی می‌کند. Round-trip سه مدل، unique operation و stale concurrency روی SQL واقعی اجرا و موفق شده‌اند.

آزمون Playwright به Chromium نیاز دارد. `VerifyPortalAndMobile` برنامه‌ریزی، قطع ارتباط، دو عمل آفلاین، اتصال و ارسال، reload، پرتال نماینده و دانلود CSV را در viewport موبایل بررسی و screenshot تولید می‌کند. وجود این کد به معنی اجرای موفق مرورگر نیست.

Workflow جاری: `.github/workflows/priority10-quality-gate.yml`؛ نتیجهٔ GitHub CI در این محیط دریافت نشده است. بازآزمایی محلی با SDK 10.0.112، runtime و EF 10.0.12 انجام شد؛ Build هر هشت پروژه بدون خطا و هشدار و آزمون‌های Architecture/Web موفق بود.

SQL Server 2025 Developer نسخهٔ 17.0.5005.3 به‌صورت محلی و با DB دورریختنی اجرا شد. نصب از صفر، seed، ذخیره/بازخوانی، stale concurrency، unique operation، rollback/roll-forward در EF و اسکریپت‌های مستقل موفق بود. آزمون اسکریپت‌های مستقل از baseline `202609210002_Customer360` شروع می‌کند، ارتقاهای P4/P5/P6/P7/P8/P10 را دوبار اجرا می‌کند و سپس rollback/upgrade اولویت ۱۰ را دوبار بررسی می‌کند. همین آزمون‌ها در CI برای SQL Server 2022 تعریف شده‌اند؛ نتیجهٔ آن محیط هنوز در دسترس نیست.

دو کنترل بدون DB نیز اضافه شد: هیچ ویژگی خودکارِ ذخیره‌شدنی Entity بدون نگاشت نماند و `HasPendingModelChanges` مقدار false بدهد. در SQL، بازخوانی ویژگی‌های فقط‌خواندنی با دادهٔ نمونه مقایسه می‌شود.

Chrome 154.0.8037.57 نصب شد، اما شروع Playwright با خطای `process_singleton_posix: socket() failed: Operation not permitted (1)` متوقف شد. بنابراین screenshot، چیدمان موبایل و قطع شبکه در مرورگر واقعی تأیید نشده‌اند. ورود و صفحات Razor در Kestrel از HTTPS با کلاینت واقعی HTTP بررسی و موفق شدند.

در صورت نصب مستقل Chrome، متغیر `CRM_E2E_BROWSER_PATH` مسیر اجرایی آن را برای آزمون تعیین می‌کند؛ در حالت عادی مرورگر خود Playwright انتخاب می‌شود. خطای گواهی توسعه فقط برای آدرس loopback در context آزمون نادیده گرفته می‌شود. این تنظیم سیاست TLS یا کوکی برنامه را تغییر نمی‌دهد.
