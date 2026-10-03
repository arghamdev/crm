# گزارش طراحی و پیاده‌سازی اولویت ۷ — درخواست سفارش و یکپارچه‌سازی ERP

**وضعیت:** پیاده‌سازی نمونه تکمیل شده  
**فناوری:** ASP.NET Core MVC، Razor، HTMX، JavaScript و CSS سازگار با Tailwind  
**مرز:** CRM مالک درخواست و گردش کنترل است؛ ERP/Accounting مالک سفارش، تخصیص، تحویل، فاکتور، پرداخت و اعتبار مالی باقی می‌مانند.

## جریان اجرایی

`Accepted Quote → Order Request → Credit Check → ERP Submission → ERP Accepted/Pending/Failed/Rejected → Allocated → Delivered → Invoiced → Paid`

تبدیل فقط از نسخه Quote با وضعیت `Accepted` ممکن است و Index یکتای `QuoteId` ایجاد درخواست تکراری را متوقف می‌کند. وضعیت‌های پس از پذیرش ERP به‌صورت Projection خواندنی در CRM ثبت می‌شوند.

## مدل دامنه

| نوع | مسئولیت |
|---|---|
| `OrderRequest` | Snapshot تجاری Quote، وضعیت، نتیجه اعتبار و مراجع ERP |
| `OrderCreditDecision` | تصمیم Approved/Held/Overridden همراه Actor، منبع و انقضا |
| `OrderStatusHistory` | Audit تغییر وضعیت با Reason و Source |
| `OrderIntegrationMessage` | Outbox، Idempotency، Correlation، Retry و Dead Letter |
| `OrderIntegrationAttempt` | نتیجه مستقل هر تلاش اتصال |

State Machine از حرکت خارج از ترتیب جلوگیری می‌کند. `ExpectedVersion` روی تمام Mutationها کنترل می‌شود.

## اعتبار و Override

- Snapshot از `IAccountingCreditProvider` دریافت می‌شود.
- بدهی سررسیدشده، Hold خارجی یا اعتبار آزاد کمتر از مبلغ سفارش، وضعیت را `CreditHold` می‌کند.
- فقط نقش مالی دارای `Order.CreditOverride` می‌تواند با دلیل و انقضای آینده Hold را رفع کند.
- Hold یک Work Item برای مدیر مالی ایجاد می‌کند و تصمیم اولیه حذف یا بازنویسی نمی‌شود.

## یکپارچه‌سازی قابل بازیابی

- برای هر سفارش یک `SubmissionIdempotencyKey` ثابت و یک `CorrelationId` ایجاد می‌شود.
- Payload دارای SHA-256 Fingerprint است.
- Timeout نمونه، همان پیام را به `RetryScheduled` می‌برد؛ پیام جدید ساخته نمی‌شود.
- Backoff نمونه برای تلاش‌های مجدد ۱۵ و ۳۰ ثانیه است و در تلاش سوم، پس از رسیدن به سقف، پیام `DeadLetter` می‌شود.
- پاسخ Pending با `AwaitingExternal` نگه‌داری و برای Poll بعدی زمان‌بندی می‌شود.
- Adapterهای نمونه `DemoAccountingCreditProvider` و `DemoErpOrderGateway` با Port جایگزین‌پذیر جدا شده‌اند.

## امنیت و دامنه دسترسی

| نقش | دسترسی نمونه |
|---|---|
| SalesManager | ایجاد، اعتبارسنجی، Submit، Worker نمونه و Sync Projection |
| SalesSupervisor | ایجاد، اعتبارسنجی و Submit در Scope خود |
| SalesExpert | عملیات سفارش‌های متعلق به خود در Scope مجاز |
| FinanceManager | خواندن و Override اعتباری؛ بدون اختیار ارسال فروش |

Permission، Company/Branch/Territory Scope، Ownership و Concurrency هم‌زمان اعمال می‌شوند. نمایش دکمه در View جایگزین کنترل سمت سرور نیست.

## MVC، Razor و HTMX

- Workspace سفارش با KPI مبلغ باز، Hold، Pending و خطا؛
- صف Retry/Dead Letter با پیوند به سفارش؛
- Drawer تبدیل Quote پذیرفته‌شده به Order Request؛
- فرم اجرایی Credit Check، Override مالی، Outbox، Worker نمونه و Projection ERP؛
- Anti-forgery در همه POSTها، پاسخ 422 قابل نمایش و fallback عادی؛
- Timeline تصمیم اعتبار، تلاش اتصال و وضعیت؛
- نمایش Order در Customer 360 و انتقال امن در Merge/Unmerge.

## Persistence

Migration `202609260006_OrderIntegrationVisibility` پنج جدول را در Schemaهای `commercial` و `integration` می‌سازد. Indexهای یکتای Quote، Idempotency و شماره تلاش از ثبت دوباره جلوگیری می‌کنند. Model Snapshot، EF Store، Seed و Migration Down به‌روزرسانی شده‌اند.

اسکریپت‌های DBA:

- `scripts/sql/priority7-order-integration-idempotent.sql`
- `scripts/sql/priority7-order-integration-rollback.sql`

## معیارهای پذیرش پوشش‌داده‌شده

| سناریو | نتیجه |
|---|---|
| Quote غیر Accepted یا تبدیل تکراری | رد می‌شود |
| Hold اعتباری | Submit بسته و Work Item مالی ساخته می‌شود |
| Override مالی | دلیل و انقضا Audit می‌شود |
| خطای موقت ERP | Retry با همان Idempotency Key |
| پذیرش پس از Retry | دو Attempt مستقل و یک پیام |
| حرکت مستقیم به Invoice | توسط State Machine رد می‌شود |
| مسیر کامل | Allocated، Delivered، Invoiced و Paid |
| فرم داخلی | Anti-forgery، Version و POST واقعی تست شده است |

## خارج از نسخه نمونه

- Credential و Endpoint واقعی ERP/Accounting؛
- Broker سازمانی، Scheduler و Worker مستقل چند Node؛
- Reconciliation دوره‌ای و Webhook امضاشده؛
- Mapping کد مشتری/کالا، Tax و Inventory واقعی؛
- SLA عملیاتی و Alerting متصل به ابزار مانیتورینگ.

این موارد باید پیش از Production با مالک، SLO، Runbook و تست Failure Injection مشخص شوند.
