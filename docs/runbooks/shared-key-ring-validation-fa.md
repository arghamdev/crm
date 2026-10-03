# Runbook آزمون Shared Data Protection Key Ring

## پیش‌شرط

- دو Node با `DataProtection:KeyRingPath` مشترک و Application Name یکسان `EnterpriseCrm`
- Session Store مشترک
- ساعت هماهنگ و HTTPS
- مجوز Read/Write فقط برای هویت Runtime

## آزمون

1. Node A و B با Key Ring خالی ولی مشترک راه‌اندازی شوند.
2. از طریق Node A ورود انجام و Cookie دریافت شود.
3. همان Cookie به Node B ارسال شود؛ Dashboard باید بدون Login مجدد باز شود.
4. Role کاربر تغییر کند؛ درخواست بعدی روی هر دو Node باید رد شود.
5. یک Node Restart شود؛ Cookie قبلی باید همچنان معتبر بماند.
6. دسترسی Write به Key Ring موقتاً قطع شود و Alert مربوط ثبت شود؛ سپس دسترسی بازگردد.

## معیار قبولی

- Decryption Cookie روی هر دو Node موفق است.
- Session و SecurityVersion روی هر دو Node نتیجه یکسان دارند.
- Restart باعث خروج عمومی کاربران نمی‌شود.
- فایل/Blob کلید رمزگذاری در حالت سکون، Versioned و Backup شده است.

تست خودکار `Crm.WebTests` همین رفتار را با دو TestServer، Store و مسیر Key Ring مشترک شبیه‌سازی می‌کند؛ آزمون بالا برای زیرساخت واقعی همچنان الزامی است.
