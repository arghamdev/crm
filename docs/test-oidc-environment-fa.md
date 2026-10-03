# چک‌لیست محیط Test OIDC

## اطلاعات موردنیاز

| تنظیم | قاعده |
|---|---|
| Authority | HTTPS و Issuer دقیق محیط Test |
| Client ID | Client اختصاصی CRM Test |
| Credential | فقط از Secret Store؛ ترجیحاً Certificate |
| Redirect URI | `https://<crm-test>/signin-oidc` |
| Post Logout URI | `https://<crm-test>/account/signed-out` |
| Scope | حداقل `openid profile email` |
| Claims | `iss`, `sub`, `email`, `email_verified`, `auth_time` و در صورت نیاز `acr/amr` |

## کاربران تست

1. کاربر Active و از قبل Linked
2. کاربر Pending با ایمیل Verified و Assignment معتبر
3. کاربر ناشناخته
4. کاربر Suspended
5. کاربر نقش حساس با MFA

## سناریوهای قبولی

- Challenge دارای `response_type=code` و PKCE S256 است.
- Callback معتبر برای کاربر Linked نشست ایجاد می‌کند.
- Pending یکتا Link و Active می‌شود.
- ایمیل Unverified، Binding مبهم و کاربر ناشناخته رد می‌شوند.
- `acr/amr` نقش حساس مطابق Policy سازمان است.
- Logout محلی Session را Revoked و End Session IdP را اجرا می‌کند.
- هیچ Token، Code، Cookie، Secret یا Claim خام در Log ثبت نمی‌شود.

`Crm.WebTests` Challenge و PKCE را بدون IdP خارجی کنترل می‌کند؛ Callback و End Session واقعی پس از دریافت Client Test اجرا می‌شوند.
