import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
let html = readFileSync(resolve(root, 'preview/crm-demo-standalone.html'), 'utf8');
const script = readFileSync(resolve(root, 'preview/self-service.js'), 'utf8') + '\n' + readFileSync(resolve(root, 'preview/unified.js'), 'utf8');
const css = readFileSync(resolve(root, 'preview/unified.css'), 'utf8');
// One document, one store and one shell. No iframe, fetch, CDN or separate-page dependency.
html = html.replace('<title>راهکار CRM سازمانی | مدل نمایشی</title>', '<title>ارقام CRM | نسخه یکپارچه آزمایشی</title>')
  .replaceAll('argham-crm-demo', 'argham-crm-unified-v1')
  .replace('const saved = readSavedStore();', `const candidate = readSavedStore();
const saved = candidate && typeof candidate === "object" && !Array.isArray(candidate) &&
  [...Object.keys(seed), "orders", "dealers", "invoices", "audit", "portalRequests", "portalAccounts", "visits"].every(key => candidate[key] === undefined ||
    Array.isArray(candidate[key]) && candidate[key].every(row => row && typeof row === "object" && !Array.isArray(row))) ? candidate : null;`)
  .replace('method="post" action="#" novalidate', 'method="post" action="#/login" novalidate onsubmit="event.preventDefault()"')
  .replace('<button class="button button--primary button--wide" type="submit">', '<button id="loginSubmit" class="button button--primary button--wide" type="submit" disabled>')
  .replace('<span>ورود به سامانه</span>', '<span>ورود به نسخه یکپارچه</span>')
  .replace('<div class="login-note">', '<div id="startupMessage" role="status">در حال آماده‌سازی… اگر دکمه ورود فعال نشد، فایل را دانلود و در مرورگر باز کنید.</div><noscript>اجرای JavaScript برای پیش‌نمایش لازم است. فایل را دانلود و با Chrome یا Edge باز کنید.</noscript><div class="login-note">')
  .replace('نسخه نمایشی ۱.۰', 'پیش‌نمایش یکپارچه تا اولویت ۱۰')
  .replace('<span>۹۲٪</span><p>پوشش فعالیت‌های فروش</p>', '<span>۹</span><p>شاخص مدیریتی</p>')
  .replace('<span>۲.۴×</span><p>سرعت بیشتر پیگیری</p>', '<span>۱</span><p>فضای کاری مشترک</p>')
  .replace('ورود امن سازمانی', 'ورود به مدل آزمایشی')
  .replace('نسخه سازمانی</small>', 'نمونه یکپارچه</small>')
  .replace('<button class="nav-item" data-route="workqueue">', '<a class="nav-item" href="#/orders" data-route="orders"><svg><use href="#i-inbox"/></svg><span>درخواست‌های سفارش</span></a><a class="nav-item" href="#/dealers" data-route="dealers"><svg><use href="#i-building"/></svg><span>نمایندگان فروش</span></a><button class="nav-item" data-route="workqueue">')
  .replace('<button class="nav-item" data-route="identity">', '<a class="nav-item" href="#/organization" data-route="organization"><svg><use href="#i-building"/></svg><span>ساختار سازمانی</span></a><button class="nav-item" data-route="identity">')
  .replace('<button class="nav-item" data-route="workqueue">', '<a class="nav-item" href="#/portal" data-route="portal"><svg><use href="#i-building"/></svg><span>پرتال نماینده</span></a><a class="nav-item" href="#/partner-requests" data-route="partner-requests"><svg><use href="#i-inbox"/></svg><span>درخواست‌های نمایندگان</span></a><a class="nav-item" href="#/mobile" data-route="mobile"><svg><use href="#i-target"/></svg><span>بازدیدهای من</span></a><button class="nav-item" data-route="workqueue">')
  .replace(/<button class="(nav-item[^"]*)" data-route="([^"]+)">([\s\S]*?)<\/button>/g, '<a class="$1" href="#/$2" data-route="$2">$3</a>')
  .replace('<main class="view-host"', '<div class="unified-notice">نسخه آزمایشی یکپارچه · همه صفحات از داده مشترک استفاده می‌کنند. امنیت سازمانی در پروژه MVC اجرا می‌شود.</div><main class="view-host"')
  .replace('</style>', () => css + '\n</style>')
  .replace('</body>', () => '<script>\n' + script + '\n</script>\n</body>');
// Inline script boundaries must remain intact, including user-facing strings.
for (const match of html.matchAll(/<script>([\s\S]*?)<\/script>/g)) new Function(match[1]);
const target = resolve(root, 'preview/crm-unified.html');
writeFileSync(target, html);
writeFileSync(resolve(root, 'index.html'), html);
console.log('Unified preview generated: preview/crm-unified.html');
