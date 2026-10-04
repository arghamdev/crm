import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
let html = readFileSync(resolve(root, 'preview/crm-demo-standalone.html'), 'utf8');
// Version and release notes come from the same sources as the MVC app (Directory.Build.props + CHANGELOG.md).
const version = readFileSync(resolve(root, 'Directory.Build.props'), 'utf8').match(/<Version>([^<]+)<\/Version>/)?.[1] ?? '0.0.0';
const releases = [];
for (const line of readFileSync(resolve(root, 'CHANGELOG.md'), 'utf8').split(/\r?\n/)) {
  if (line.startsWith('## ')) { const [v, date = '', ...title] = line.slice(3).split('—').map(x => x.trim()); releases.push({version: v, date, title: title.join(' — '), changes: []}); }
  else if (line.startsWith('- ') && releases.length) releases.at(-1).changes.push(line.slice(2).trim().replaceAll('`', ''));
}
if (releases[0]?.version !== version) throw new Error(`CHANGELOG.md top entry (${releases[0]?.version}) must equal <Version> ${version}.`);
const versionScript = `window.CRM_VERSION=${JSON.stringify(version)};window.CRM_RELEASES=${JSON.stringify(releases)};`;
const script = versionScript + '\n' + readFileSync(resolve(root, 'preview/self-service.js'), 'utf8') + '\n' + readFileSync(resolve(root, 'preview/account-file.js'), 'utf8') + '\n' + readFileSync(resolve(root, 'preview/unified.js'), 'utf8');
// The account-file and version styles are taken from the MVC stylesheet so both look the same.
const siteCss = readFileSync(resolve(root, 'src/Crm.Web/wwwroot/css/site.css'), 'utf8');
const start = siteCss.indexOf('/* ───────────── Account file'), end = siteCss.length;
if (start < 0) throw new Error('Account-file CSS markers not found in site.css.');
const css = readFileSync(resolve(root, 'preview/unified.css'), 'utf8') + '\n' + siteCss.slice(start, end);
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
  .replace('نسخه نمایشی ۱.۰ · کلیه حقوق برای ارقام سیستم محفوظ است.', `ارقام CRM · نسخه v${version} · ${releases[0].date} · پیش‌نمایش آفلاین`)
  .replace('نسخه نمایشی ۱.۰', 'پیش‌نمایش یکپارچه تا اولویت ۱۰')
  .replace('<span>۹۲٪</span><p>پوشش فعالیت‌های فروش</p>', '<span>۹</span><p>شاخص مدیریتی</p>')
  .replace('<span>۲.۴×</span><p>سرعت بیشتر پیگیری</p>', '<span>۱</span><p>فضای کاری مشترک</p>')
  .replace('ورود امن سازمانی', 'ورود به مدل آزمایشی')
  .replace('نسخه سازمانی</small>', `نسخه <bdi dir="ltr">${version}</bdi> · پیش‌نمایش</small>`)
  .replace('<div class="topbar__left">', `<div class="topbar__left"><a class="version-pill" href="#/version" data-route="version" title="نسخه و تغییرات"><span class="version-pill__dot" aria-hidden="true"></span><span class="version-pill__label">نسخه</span><bdi dir="ltr">v${version}</bdi></a>`)
  .replace('<button class="nav-item" data-route="identity">', '<a class="nav-item" href="#/service" data-route="service"><svg><use href="#i-shield"/></svg><span>خدمات و SLA</span></a><a class="nav-item" href="#/commissions" data-route="commissions"><svg><use href="#i-chart"/></svg><span>کمیسیون و رتبه‌بندی</span></a><a class="nav-item" href="#/notifications" data-route="notifications"><svg><use href="#i-bell"/></svg><span>اعلان‌ها</span></a><a class="nav-item" href="#/version" data-route="version"><svg><use href="#i-refresh"/></svg><span>نسخه و تغییرات</span></a><button class="nav-item" data-route="identity">')
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
