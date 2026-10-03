# Runbook اختلال Identity Provider

## هدف

مدیریت اختلال ورود سازمانی بدون ایجاد حساب محلی یا دورزدن کنترل‌های امنیتی.

## علائم شروع

- افزایش `OidcRemoteFailure` یا خطای Metadata/Certificate
- افزایش زمان P95 ورود یا پاسخ‌های 5xx از Authority
- گزارش کاربران درباره Loop ورود یا صفحه Link Error

## اقدام فوری

1. Incident ID و زمان UTC ایجاد و مسئول رخداد تعیین شود.
2. سلامت DNS، TLS، Metadata Endpoint و Token Endpoint از شبکه هر Node بررسی شود.
3. وضعیت Client Registration، Redirect URI و اعتبار Credential کنترل شود.
4. Correlation ID نمونه‌ها جمع‌آوری شود؛ Token، Code، Cookie یا Claim خام در تیکت درج نشود.
5. در صورت اختلال IdP، پیام عمومی «ورود سازمانی موقتاً در دسترس نیست» فعال شود؛ Login محلی اضطراری ایجاد نشود.
6. حساب Break-glass فقط در خود IdP و مطابق فرایند مصوب سازمان استفاده شود.

## بازیابی

1. Metadata و Signing Keyها از هر Node دوباره قابل دریافت باشند.
2. یک Login آزمایشی با کاربر کم‌اختیار انجام شود.
3. `state`، `nonce`، Callback، Session creation و Logout بررسی شوند.
4. نرخ خطا حداقل ۱۵ دقیقه پایش شود.

## Rollback

- آخرین Configuration سالم Authority/Client به‌صورت Rolling بازگردانده شود.
- Key Ring و Session Store حذف یا Reset نشوند.
- تغییر DNS یا Certificate فقط با ثبت Change و امکان بازگشت انجام شود.

## خاتمه

Timeline، علت ریشه‌ای، تعداد کاربران متاثر، اقدام اصلاحی و مالک پیشگیری ثبت شود.
