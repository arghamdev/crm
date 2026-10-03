# Runbook چرخش OIDC Credential و IP Hash Salt

## پیش‌شرط

- Credential فقط در Vault/Secret Store نگهداری شود.
- مالک فنی، مالک امنیت و پنجره تغییر مشخص باشند.
- در صورت امکان IdP هم‌زمان دو Credential معتبر را پشتیبانی کند.

## چرخش Client Secret/Certificate

1. Credential جدید در IdP ایجاد شود؛ قبلی هنوز لغو نشود.
2. Secret Version جدید در Vault ثبت و دسترسی Runtime کنترل شود.
3. یک Node Canary با نسخه جدید بالا بیاید.
4. Login، Callback، Logout و Refresh Metadata روی Canary تست شود.
5. استقرار Rolling تکمیل و نرخ `OidcRemoteFailure` پایش شود.
6. Credential قبلی در IdP لغو شود.
7. زمان، Version و تأییدکنندگان در Change Record ثبت شوند؛ مقدار Secret ثبت نشود.

## Rollback

تا قبل از لغو Credential قبلی، Reference نسخه Vault به نسخه قبلی بازگردد و Nodeها Rolling Restart شوند.

## چرخش IP Hash Salt

تغییر Salt باعث تغییر Fingerprint IPهای جدید می‌شود. برای حفظ قابلیت همبستگی Audit، Rotation فقط در مرز Retention یا با `SaltVersion` انجام شود. Salt قدیمی تا پایان Retention به‌صورت محافظت‌شده و Read-only نگهداری شود.
