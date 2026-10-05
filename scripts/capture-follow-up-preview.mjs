// Captures the real follow-up forms (۱–۱۲) from a running Crm.Web instance into preview/follow-up-snapshots.js,
// so the offline preview shows exactly the same markup as the application.
// Usage: start the app (demo data), then: CRM_PREVIEW_SOURCE=http://localhost:5085 node scripts/capture-follow-up-preview.mjs
import {writeFileSync, readFileSync} from 'node:fs';
import {resolve, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomUUID} from 'node:crypto';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const base = process.env.CRM_PREVIEW_SOURCE || 'http://localhost:5085';
const jar = new Map();

async function request(path, {method = 'GET', form, htmx = false} = {}) {
  const headers = {cookie: [...jar].map(([k, v]) => `${k}=${v}`).join('; ')};
  if (htmx) headers['HX-Request'] = 'true';
  let body;
  if (form) { body = new URLSearchParams(form).toString(); headers['content-type'] = 'application/x-www-form-urlencoded'; }
  const response = await fetch(base + path, {method, headers, body, redirect: 'manual'});
  for (const cookie of response.headers.getSetCookie()) {
    const pair = cookie.split(';')[0], i = pair.indexOf('=');
    jar.set(pair.slice(0, i), pair.slice(i + 1));
  }
  return response;
}
const text = async (path, options) => {
  const response = await request(path, options);
  if (response.status >= 400) throw new Error(`${path} → ${response.status}`);
  return response.text();
};
const tokenOf = html => html.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/)?.[1] ?? html.match(/value="([^"]+)" name="__RequestVerificationToken"/)?.[1];

// ── Sign in as the sales manager and enter company C01 ──
let html = await text('/account/login');
await request('/account/login', {method: 'POST', form: {userName: 'sales.manager', password: 'Demo@1405', __RequestVerificationToken: tokenOf(html)}});
html = await text('/context/select');
await request('/context/select', {method: 'POST', form: {CompanyId: 'C01', __RequestVerificationToken: tokenOf(html)}});

// ── Cases: code ↔ id ──
const list = await text('/follow-ups/table?pageSize=50', {htmx: true});
const ids = {};
for (const m of list.matchAll(/class="entity-cell entity-link" href="\/follow-ups\/([0-9a-f-]{36})"[\s\S]*?<bdi>(RQ-\d+)<\/bdi>/g)) ids[m[2]] = m[1];
const caseId = ids['RQ-24085'];
if (!caseId) throw new Error('RQ-24085 was not found; start the app with the demo data.');

const snapshots = {};
// The token value is session-bound; the preview gets an empty one.
const clean = h => h.replace(/(name="__RequestVerificationToken" type="hidden" value=")[^"]+"/g, '$1"').replace(/\r/g, '');

snapshots.create = clean(await text('/follow-ups/new/form?customerId=20000000-0000-4000-8000-000000000001', {htmx: true}));
for (const kind of ['Call', 'Meeting', 'Visit', 'Task']) snapshots['plan' + kind] = clean(await text(`/follow-ups/${caseId}/plan?kind=${kind}`, {htmx: true}));

// Form ۳ needs a planned call: plan one with the values the form offers.
const plan = await text(`/follow-ups/${caseId}/plan?kind=Call`, {htmx: true});
const value = name => plan.match(new RegExp(`name="${name}"[^>]*value="([^"]*)"`))?.[1] ?? '';
const selected = name => plan.match(new RegExp(`name="${name}"[\\s\\S]*?<option value="([^"]*)"[^>]*selected`))?.[1] ?? '';
await request(`/follow-ups/${caseId}/plan`, {method: 'POST', htmx: true, form: {__RequestVerificationToken: tokenOf(plan), Kind: 'Call', Title: 'تماس هماهنگی شرایط پرداخت',
  OwnerUserId: selected('OwnerUserId'), ContactId: selected('ContactId'), Channel: 'Phone', Date: value('Date'), Time: value('Time'), Priority: 'Normal', TimeZone: 'Asia/Tehran',
  DurationMinutes: '15', OperationId: randomUUID()}});
const body = await text(`/follow-ups/${caseId}/body`, {htmx: true});
const resultPath = body.match(/hx-get="(\/follow-ups\/[0-9a-f-]{36}\/activities\/[0-9a-f-]{36}\/result)"/)?.[1];
if (!resultPath) throw new Error('No activity to record a result for.');
snapshots.result = clean(await text(resultPath, {htmx: true}));
snapshots.refer = clean(await text(`/follow-ups/${caseId}/refer`, {htmx: true}));
snapshots.wait = clean(await text(`/follow-ups/${caseId}/wait`, {htmx: true}));
snapshots.documents = clean(await text(`/follow-ups/${caseId}/documents?tab=request`, {htmx: true}));
snapshots.close = clean(await text(`/follow-ups/${caseId}/close`, {htmx: true}));

// ── Pages ۹–۱۲: the main area without the breadcrumb ──
const main = page => clean(page.match(/<main class="view-host">([\s\S]*?)<\/main>/)[1].replace(/<nav class="breadcrumb">[\s\S]*?<\/nav>/, '').trim());
const settings = await text('/follow-ups/settings');
const templateId = settings.match(/href="\/follow-ups\/settings\/templates\/([0-9a-f-]{36})"[^>]*>پیگیری پیش‌فاکتور/)?.[1]
  ?? settings.match(/href="\/follow-ups\/settings\/templates\/([0-9a-f-]{36})"/)[1];
// A draft version shows the editable designer with «انتشار نسخه N», as in image ۹.
const templatePage = await text(`/follow-ups/settings/templates/${templateId}`);
const made = await request(`/follow-ups/settings/templates/${templateId}/version`, {method: 'POST', form: {__RequestVerificationToken: tokenOf(templatePage)}});
const draftPath = made.headers.get('location') ?? `/follow-ups/settings/templates/${templateId}`;
snapshots.template = main(await text(draftPath));
const policies = await text('/follow-ups/settings?tab=policies');
snapshots.policy = main(await text(policies.match(/href="(\/follow-ups\/settings\/policies\/[0-9a-f-]{36})"/)[1]));
const queues = await text('/follow-ups/settings?tab=queues');
snapshots.queue = main(await text(queues.match(/href="(\/follow-ups\/settings\/queues\/[0-9a-f-]{36})"/)[1]));
snapshots.supervision = main(await text('/follow-ups/supervision'));

const meta = {version: readFileSync(resolve(root, 'Directory.Build.props'), 'utf8').match(/<Version>([^<]+)<\/Version>/)[1], caseCode: 'RQ-24085', caseId,
  customer: 'صنایع غذایی سپهر', subject: 'پیش‌فاکتور قطعات یدکی خط بسته‌بندی', ids: Object.fromEntries(Object.entries(ids).map(([code, id]) => [id, code]))};
const out = `/* Generated by scripts/capture-follow-up-preview.mjs from the running application (v${meta.version}); do not edit by hand.
   The offline preview shows these exact forms (۱–۱۲) so it looks the same as the application. */
const FU_SNAPSHOTS = ${JSON.stringify({meta, ...snapshots}, null, 0)};
`;
writeFileSync(resolve(root, 'preview/follow-up-snapshots.js'), out);
console.log(`Captured ${Object.keys(snapshots).length} follow-up forms into preview/follow-up-snapshots.js`);
