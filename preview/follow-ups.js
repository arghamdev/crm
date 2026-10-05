/* Offline preview of the v1.10–1.11 follow-up center (مرکز پیگیری): list with views and counters, case page with weighted
   stages and checklist lock, plan / result / referral / wait / close drawers, template and SLA settings and the
   supervision board. Same markup and CSS classes as the MVC pages; data lives in the shared preview store. */
function createFollowUpPreview(ctx) {
  'use strict';
  const {store, escapeHtml: e, save, toast, closeDrawer, showForm} = ctx;
  const render = () => ctx.render();
  const fa = n => new Intl.NumberFormat('fa-IR').format(n);
  const icon = name => `<svg aria-hidden="true"><use href="#i-${name}"></use></svg>`;
  const jDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', {year: 'numeric', month: '2-digit', day: '2-digit'});
  const jTime = new Intl.DateTimeFormat('fa-IR', {hour: '2-digit', minute: '2-digit', hour12: false});
  const when = iso => iso ? `${jDate.format(new Date(iso))} ${jTime.format(new Date(iso))}` : '—';
  const names = {'sales.manager': 'مهدی نادری', 'sales.expert': 'سارا احمدی', 'finance.manager': 'لیلا کریمی', 'channel.manager': 'امیرحسین مرادی', 'reporting.ceo': 'مدیرعامل'};
  const me = () => names[ctx.account()] || 'کاربر نمونه';
  const role = () => ctx.account();
  const can = {
    read: () => role() !== 'dealer.user',
    write: () => ['sales.manager', 'sales.expert'].includes(role()),
    supervise: () => ['sales.manager', 'reporting.ceo'].includes(role()),
    configure: () => role() === 'sales.manager'
  };
  const hours = h => new Date(Date.now() + h * 3600000).toISOString();
  const statusLabel = {AwaitingAssignment: 'در انتظار تخصیص', InProgress: 'در حال انجام', WaitingCustomer: 'در انتظار مشتری', WaitingInternal: 'در انتظار واحد داخلی',
    OnHold: 'متوقف', Resolved: 'حل‌شده، در انتظار تأیید', Closed: 'بسته‌شده', Cancelled: 'لغوشده'};
  const statusTone = {AwaitingAssignment: 'warning', InProgress: 'info', WaitingCustomer: 'violet', WaitingInternal: 'violet', OnHold: 'warning', Resolved: 'primary', Closed: 'success', Cancelled: 'neutral'};
  const priority = {Low: ['کم', 'neutral'], Normal: ['عادی', 'info'], High: ['بالا', 'warning'], Critical: ['بحرانی', 'danger']};
  const stageLabel = {Pending: ['شروع‌نشده', 'neutral'], Active: ['در حال انجام', 'info'], Done: ['تکمیل‌شده', 'success'], Returned: ['برگشت برای اصلاح', 'danger'], Skipped: ['کنار گذاشته', 'neutral']};
  const results = [['Answered', 'پاسخ داد'], ['NoAnswer', 'پاسخ نداد'], ['IncompleteDocs', 'مدرک ناقص'], ['Approved', 'تأیید شد'], ['NeedsCorrection', 'نیازمند اصلاح'], ['NeedsNextAction', 'نیازمند اقدام بعدی']];
  const isOpen = c => !['Closed', 'Cancelled'].includes(c.status);
  const isWaiting = c => ['WaitingCustomer', 'WaitingInternal', 'OnHold'].includes(c.status);
  const overdue = c => isOpen(c) && ((c.nextAt && Date.parse(c.nextAt) < Date.now()) || (!c.paused && c.dueAt && Date.parse(c.dueAt) < Date.now()));

  // ── Seed (once): templates, policies, queues and cases in different states ──
  const tpl = (code, name, type, stages, rules = []) => ({code, name, type, version: 1, status: 'منتشرشده', stages, rules});
  const st = (name, weight, role, items) => ({name, weight, role, items});
  store.fuTemplates ??= [
    tpl('WF-101', 'پیگیری پیش‌فاکتور قطعات', 'پیش‌فاکتور', [st('ثبت درخواست', 10, 'کارشناس فروش', ['دریافت درخواست مشتری', 'ثبت اقلام و تعداد', 'تأیید شخص تماس']),
      st('بررسی فنی', 25, 'کارشناس فنی', ['کنترل کد قطعه و جایگزین', 'کنترل سازگاری با دستگاه', 'تأیید مدارک فنی']),
      st('تأمین موجودی', 20, 'کارشناس انبار', ['استعلام موجودی انبار', 'رزرو موجودی', 'هماهنگی تأمین کسری']),
      st('صدور پیش‌فاکتور', 30, 'کارشناس فروش', ['محاسبهٔ قیمت و تخفیف', 'تأیید مالی', 'صدور و ارسال پیش‌فاکتور']),
      st('تأیید مشتری', 15, 'کارشناس فروش', ['پیگیری پاسخ مشتری', 'دریافت تأیید کتبی مشتری'])], [['موجودی ناکافی بود', 'ایجاد وظیفه تأمین موجودی']]),
    tpl('WF-103', 'تأمین قطعه', 'تأمین قطعه', [st('ثبت نیاز', 15, 'کارشناس فروش', ['ثبت کد قطعه و تعداد']), st('استعلام تأمین‌کننده', 35, 'کارشناس تأمین', ['استعلام از دو تأمین‌کننده', 'انتخاب تأمین‌کننده']),
      st('سفارش و پیگیری ارسال', 35, 'کارشناس تأمین', ['ثبت سفارش خرید', 'پیگیری حمل']), st('تحویل به مشتری', 15, 'کارشناس فروش', ['تأیید تحویل'])]),
    tpl('WF-104', 'رسیدگی به مغایرت ارسال', 'مغایرت ارسال', [st('ثبت مغایرت', 20, 'کارشناس فروش', ['ثبت شمارهٔ محموله']), st('بررسی انبار و حمل', 40, 'کارشناس انبار', ['تطبیق با سند خروج']),
      st('جبران مغایرت', 30, 'کارشناس فروش', ['ارسال تکمیلی']), st('تأیید مشتری', 10, 'کارشناس فروش', ['دریافت تأیید مشتری'])])
  ];
  store.fuPolicies ??= [
    {name: 'مهلت استاندارد فروش قطعات', priority: 'Normal', first: 2, stage: 8, resolution: 24, pauseCustomer: true, pauseInternal: false, steps: ['۳۰ دقیقه قبل → مسئول مرحله', 'در زمان سررسید → سرپرست تیم', '۲ ساعت بعد → مدیر شعبه']},
    {name: 'مهلت اولویت بالا', priority: 'High', first: 1, stage: 4, resolution: 16, pauseCustomer: true, pauseInternal: false, steps: ['۳۰ دقیقه قبل → مسئول مرحله', 'در زمان سررسید → سرپرست تیم']},
    {name: 'توقف تولید مشتری', priority: 'Critical', first: 1, stage: 2, resolution: 8, pauseCustomer: true, pauseInternal: false, steps: ['۱۵ دقیقه قبل → مسئول مرحله', 'در زمان سررسید → سرپرست تیم', '۱ ساعت بعد → مدیر شعبه']}
  ];
  store.fuQueues ??= [{name: 'فروش قطعات صنعتی', branch: 'مرکزی', method: 'بیشترین ظرفیت آزاد', members: [['سارا احمدی', 2, 12, true], ['مهدی نادری', 1, 20, true]]},
    {name: 'فروش شعبه جنوب', branch: 'اهواز', method: 'بیشترین ظرفیت آزاد', members: [['نرگس یوسفی', 1, 8, false]]}];
  function instantiate(template, doneStages = 0, doneItems = 0) {
    return template.stages.map((s, i) => ({name: s.name, weight: s.weight, role: s.role, status: i < doneStages ? 'Done' : i === doneStages ? 'Active' : 'Pending',
      items: s.items.map((title, j) => ({title, done: i < doneStages || (i === doneStages && j < doneItems)}))}));
  }
  if (!store.followUps) {
    const c = n => store.customers[n % store.customers.length]?.name || 'صنایع غذایی سپهر';
    store.followUps = [
      {id: 24085, code: 'RQ-24085', subject: 'پیش‌فاکتور قطعات یدکی خط بسته‌بندی', customer: c(0), type: 'پیش‌فاکتور', priority: 'High', status: 'InProgress', owner: 'سارا احمدی', branch: 'مرکزی',
        template: 'WF-101', next: 'تماس با انبار مرکزی برای رزرو موجودی', nextAt: hours(3), firstDone: true, dueAt: hours(-19), related: 'فرصت OP-2041',
        stages: instantiate(store.fuTemplates[0], 2, 1), activities: [], referrals: [{from: 'سارا احمدی', to: 'مهدی نادری', scope: 'مرحلهٔ «تأمین موجودی»', reason: 'تأیید خرید اضطراری دو قلم کسری', status: 'در انتظار پذیرش', due: hours(3)}],
        items: [['BRG-6205', 'بلبرینگ ۶۲۰۵', 40, 0, 'در انتظار'], ['BLT-A42', 'تسمهٔ A42', 12, 4, 'کسری']], history: [['پرونده ثبت شد', hours(-96)], ['مرحلهٔ «بررسی فنی» تکمیل شد', hours(-48)]]},
      {id: 24086, code: 'RQ-24086', subject: 'تأمین گیربکس جایگزین خط تولید', customer: c(1), type: 'تأمین قطعه', priority: 'Normal', status: 'WaitingCustomer', owner: 'محمد رضایی', branch: 'اصفهان',
        template: 'WF-103', next: 'بازبینی: مدل دقیق دستگاه', nextAt: hours(24), firstDone: true, dueAt: hours(30), paused: true, waitReason: 'مشتری باید مدل دقیق دستگاه و پلاک موتور را ارسال کند.',
        stages: instantiate(store.fuTemplates[1], 0, 1), activities: [], referrals: [], items: [], history: [['در انتظار مشتری', hours(-5)]]},
      {id: 24087, code: 'RQ-24087', subject: 'کسری ۳ کارتن در محمولهٔ مهر', customer: c(2), type: 'مغایرت ارسال', priority: 'High', status: 'InProgress', owner: 'نرگس یوسفی', branch: 'اهواز',
        template: 'WF-104', next: null, nextAt: null, firstDone: true, dueAt: hours(-2), stages: instantiate(store.fuTemplates[2], 0, 0), activities: [], referrals: [], items: [], history: [['پرونده ثبت شد', hours(-50)]]}
    ];
    save();
  }
  const state = {view: 'all', q: '', selected: null, tab: 'stages', settings: 'template', planKind: null};
  const visibleCases = () => store.followUps.filter(c => role() !== 'sales.expert' || c.owner === me() || c.branch === 'مرکزی');
  const progress = c => { const counted = c.stages.filter(s => s.status !== 'Skipped'); const w = counted.reduce((a, s) => a + s.weight, 0);
    return w ? Math.round(counted.reduce((a, s) => a + s.weight * stageProgress(s), 0) / w) : 0; };
  const stageProgress = s => s.status === 'Done' ? 100 : s.status === 'Pending' ? 0 : s.items.length ? Math.min(99, Math.round(100 * s.items.filter(x => x.done).length / s.items.length)) : 0;
  const active = c => c.stages.find(s => ['Active', 'Returned'].includes(s.status));
  const log = (c, text) => c.history.unshift([text, new Date().toISOString()]);
  function advance(c) {
    const next = c.stages.find(s => s.status === 'Pending');
    if (!active(c) && next) { next.status = 'Active'; log(c, `مرحلهٔ «${next.name}» شروع شد`); }
    if (c.stages.every(s => ['Done', 'Skipped'].includes(s.status))) { c.status = 'Resolved'; c.next = 'بستن پرونده و ثبت نتیجهٔ نهایی'; c.nextAt = hours(4); }
  }
  const pill = (text, tone, sm = true) => `<span class="pill${sm ? ' pill--sm' : ''} pill--${tone}">${e(text)}</span>`;
  const bar = value => `<span class="fu-progress" style="--value:${value}"><span></span><b>${fa(value)}٪</b></span>`;
  const btn = (action, label, id, cls = 'button button--ghost button--sm', ic = '') => `<button type="button" class="${cls}" data-u-action="${action}" data-id="${e(id)}">${ic ? icon(ic) : ''}${label}</button>`;
  function due(iso) {
    if (!iso) return ['—', 'muted'];
    const t = Date.parse(iso), h = (Date.now() - t) / 3600000;
    return t < Date.now() ? [h >= 24 ? `${fa(Math.floor(h / 24))} روز تأخیر` : `${fa(Math.max(1, Math.floor(h)))} ساعت تأخیر`, 'danger'] : [when(iso), 'info'];
  }

  // ── List (مرکز پیگیری) ──
  function listView() {
    const all = visibleCases(), open = all.filter(isOpen);
    const sets = {all: open, mine: open.filter(c => c.owner === me()), nonext: open.filter(c => !c.nextAt), overdue: open.filter(overdue), waiting: open.filter(isWaiting),
      referrals: open.filter(c => c.referrals.some(r => r.to === me() && r.status === 'در انتظار پذیرش')), closed: all.filter(c => !isOpen(c))};
    let items = (sets[state.view] || sets.all).filter(c => !state.q || [c.code, c.subject, c.customer].some(x => String(x).includes(state.q)));
    items = items.slice().sort((a, b) => (a.nextAt ? Date.parse(a.nextAt) : -Infinity) - (b.nextAt ? Date.parse(b.nextAt) : -Infinity));
    const tab = (label, view, count) => `<button type="button" class="list-tabs__tab${state.view === view ? ' is-active' : ''}" data-u-action="fu-view" data-id="${view}">${label}${count == null ? '' : `<b>${fa(count)}</b>`}</button>`;
    const tile = (label, value, hint, ic, tone, view) => `<button type="button" class="list-tile list-tile--${tone}${state.view === view ? ' is-active' : ''}" data-u-action="fu-view" data-id="${view}"><span class="list-tile__icon">${icon(ic)}</span><span class="list-tile__body"><small>${label}</small><strong>${fa(value)}</strong><em>${hint}</em></span></button>`;
    return `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('flow')}</span><div><h1>مرکز پیگیری</h1><p>پرونده‌های پیگیری با مسئول، مرحله، اقدام بعدی و مهلت؛ هیچ پرونده‌ای بدون صاحب یا اقدام بعدی رها نمی‌شود</p></div></div>
      <div class="list-head__actions">${can.supervise() ? btn('fu-supervision', 'نظارت مدیر', '', 'button button--ghost button--outline', 'grid') : ''}${can.configure() ? btn('fu-settings', 'تنظیمات پیگیری', '', 'button button--ghost button--outline', 'settings') : ''}${can.write() ? btn('fu-new', 'پرونده پیگیری جدید', '', 'button button--primary', 'plus') : ''}</div></header>
      <div class="list-workspace"><nav class="list-tabs">${tab('پرونده‌های باز', 'all', sets.all.length)}${tab('پرونده‌های من', 'mine', sets.mine.length)}${tab('ارجاع به من', 'referrals', sets.referrals.length)}${tab('در انتظار', 'waiting', sets.waiting.length)}${tab('بسته‌شده', 'closed')}</nav>
      <section class="list-tiles">${tile('پرونده باز', sets.all.length, 'در دامنهٔ شما', 'flow', 'green', 'all')}${tile('عقب‌افتاده', sets.overdue.length, 'مهلت پاسخ، اقدام یا حل گذشته', 'alert', 'red', 'overdue')}${tile('بدون اقدام بعدی', sets.nonext.length, 'نیازمند تعیین اقدام و موعد', 'clock', 'amber', 'nonext')}${tile('ارجاع نپذیرفته', sets.referrals.length, 'منتظر پذیرش شما', 'send', 'blue', 'referrals')}</section>
      <section class="panel list-panel"><div class="list-filters"><label class="list-search">${icon('search')}<input type="search" data-fu-search value="${e(state.q)}" placeholder="جست‌وجوی کد پرونده، موضوع یا مشتری" aria-label="جست‌وجوی پرونده"></label></div>
      <div class="table-wrap"><table class="list-table fu-table"><thead><tr><th>پرونده</th><th class="hide-sm">مشتری</th><th class="hide-sm">وضعیت / مرحله</th><th class="hide-sm">پیشرفت</th><th class="hide-sm">مسئول</th><th class="hide-sm">اقدام بعدی</th><th>سررسید</th></tr></thead><tbody>
      ${items.map(c => { const [text, tone] = due(c.nextAt || c.dueAt); const s = active(c); return `<tr class="${overdue(c) ? 'is-overdue' : ''}"><td><button type="button" class="entity-cell entity-link link-button" data-u-action="fu-open" data-id="${c.id}"><span class="entity-avatar entity-avatar--${['High', 'Critical'].includes(c.priority) ? 'amber' : 'blue'}">${icon('file')}</span><span><strong>${e(c.subject)}</strong><small>${e(c.code)} · ${e(c.type)}</small></span></button></td>
        <td class="hide-sm">${e(c.customer)}<small class="cell-sub">شعبه ${e(c.branch)}</small></td><td class="hide-sm">${pill(statusLabel[c.status], statusTone[c.status], false)}${s ? `<small class="cell-sub">مرحله: ${e(s.name)}</small>` : ''}</td><td class="hide-sm">${bar(progress(c))}</td>
        <td class="hide-sm">${c.owner ? `<strong class="cell-title">${e(c.owner)}</strong>` : pill('بدون مسئول', 'warning')}</td><td class="hide-sm">${c.next ? `<span class="cell-icon">${icon('calendar')}${e(c.next)}</span>` : `<span class="cell-icon text-danger">${icon('alert')}تعیین نشده</span>`}</td>
        <td><span class="due due--${tone}">${icon(tone === 'danger' ? 'alert' : 'calendar')}${e(text)}</span>${c.paused ? '<small class="cell-sub">مهلت متوقف</small>' : ''}</td></tr>`; }).join('') || '<tr><td colspan="7"><p class="empty-mini">پرونده‌ای پیدا نشد.</p></td></tr>'}
      </tbody></table></div></section></div>`;
  }

  // ── Case page ──
  function caseView() {
    const c = store.followUps.find(x => x.id === state.selected);
    if (!c) return listView();
    const s = active(c), p = progress(c), [pl, pt] = priority[c.priority], write = isOpen(c) && can.write();
    const remaining = s ? s.items.filter(x => !x.done).length : 0;
    const tabBtn = (key, label) => `<button type="button" class="tabbar__tab${state.tab === key ? ' is-active' : ''}" data-u-action="fu-tab" data-id="${key}">${label}</button>`;
    const kpi = (label, value, sub, tone = '') => `<article class="fu-kpi${tone ? ' fu-kpi--' + tone : ''}"><small>${label}</small><strong>${e(value)}</strong><span>${e(sub)}</span></article>`;
    // Form ۵ — the same card as Views/FollowUps/_Stages.cshtml.
    const stateCls = {Done: 'done', Active: 'active', Returned: 'returned', Skipped: 'skipped', Pending: 'pending'};
    const stateIco = {Done: 'check', Active: 'more', Returned: 'sort', Skipped: 'ban', Pending: 'clock'};
    const stageIco = n => /نیاز|دریافت|ثبت/.test(n) ? 'inbox' : /فنی|بررسی/.test(n) ? 'flask' : /موجودی|انبار|تأمین/.test(n) ? 'box' : /پیش‌فاکتور|قیمت/.test(n) ? 'file' : /تأیید/.test(n) ? 'shield' : /ارسال|تحویل/.test(n) ? 'send' : 'task';
    const words = n => ['صفر', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه', 'ده'][n] ?? fa(n);
    const pctWords = n => { const ones = ['', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه'], teens = ['ده', 'یازده', 'دوازده', 'سیزده', 'چهارده', 'پانزده', 'شانزده', 'هفده', 'هجده', 'نوزده'], tens = ['', '', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'];
      return n === 0 ? 'صفر' : n === 100 ? 'صد' : n < 10 ? ones[n] : n < 20 ? teens[n - 10] : tens[Math.floor(n / 10)] + (n % 10 ? ' و ' + ones[n % 10] : ''); };
    const total = s ? s.items.length : 0, doneCount = s ? s.items.filter(x => x.done).length : 0, stagePct = total ? Math.round(100 * doneCount / total) : 0;
    const stagesPanel = `<section class="panel fu-stagecard"><div class="fu-stagecard__head"><header class="fu-sheet__head"><span class="fu-chip">${icon('file')}<bdi>${e(c.code)} / ${e(c.customer)}</bdi></span><div class="fu-sheet__title"><h2>مراحل و وظایف پرونده</h2></div><span class="fu-num" aria-hidden="true">۵</span></header></div>
      <div class="fu-stagecard__top"><div class="fu-stagecard__total"><div><span class="fu-total-label">پیشرفت کل</span><b class="fu-total">${fa(p)}٪</b><small>${pctWords(p)} درصد</small></div><div class="fu-progressbar"><span style="width:${p}%"></span></div></div>
        <div class="fu-person"><span class="fu-avatar">${e((c.owner || '؟')[0])}</span><span><small>مسئول پرونده</small><strong>${e(c.owner || 'بدون مسئول')}</strong></span></div></div>
      <div class="table-wrap"><table class="fu-t fu-stage-table"><thead><tr><th>مرحله</th><th>وزن</th><th>پیشرفت</th><th>وضعیت</th></tr></thead><tbody>
      ${c.stages.map(x => `<tr class="${x === s ? 'is-current' : ''}"><td><span class="fu-stage-name"><span class="fu-stage-ico">${icon(stageIco(x.name))}</span><span>${e(x.name)}<small>${e(x.role)}</small></span></span></td><td>${fa(x.weight)}٪</td><td>${bar(stageProgress(x))}</td><td><span class="fu-state fu-state--${stateCls[x.status]}" title="${stageLabel[x.status][0]}">${icon(stateIco[x.status])}</span><span class="sr-only">${stageLabel[x.status][0]}</span></td></tr>`).join('')}</tbody></table></div>
      ${s ? `<div class="fu-active fu-checklist"><h3 class="fu-sec__h">${icon('clipboard')}چک‌لیست مرحله فعال<small class="fu-f__hint">${e(s.name)}</small></h3>
        <div class="fu-g"><div class="fu-person"><span class="fu-avatar">${e((c.owner || '؟')[0])}</span><span><small>مسئول مرحله</small><strong>${e(c.owner || '—')}</strong></span></div><div class="fu-person"><span class="fu-avatar">${icon('clock')}</span><span><small>مهلت مرحله</small><strong>${e(due(c.nextAt || c.dueAt)[0])}</strong></span></div></div>
        <div class="fu-ready"><div class="fu-stage-progress"><span>پیشرفت مرحله : ${fa(stagePct)}٪</span><div class="fu-progressbar"><span style="width:${stagePct}%"></span></div><b>${fa(doneCount)} از ${fa(total)} مورد تکمیل شده</b></div></div>
        <fieldset ${write ? '' : 'disabled'}><legend class="sr-only">وظایف این مرحله</legend>${s.items.map((x, j) => `<label class="fu-check${x.done ? ' is-done' : ''}"><input type="checkbox" data-fu-item="${c.id}:${j}" ${x.done ? 'checked' : ''}><span>${e(x.title)}</span></label>`).join('')}</fieldset>
        <div class="fu-active__docs"><span>${icon('clip')}مستندات مرتبط</span><small class="fu-f__hint">مدرکی برای این مرحله ثبت نشده است.</small>${write ? btn('fu-documents', 'افزودن مدرک', c.id, 'fu-add', 'plus') : ''}</div>
        ${remaining ? `<p class="fu-note fu-note--amber">${icon('alert')}<span>برای تکمیل مرحله، <b>${words(remaining)}</b> مورد باقی‌مانده انجام شود.</span></p>` : ''}
        ${write ? `<div class="fu-inline-actions fu-halves"><button type="button" class="button button--primary" data-u-action="fu-save-tasks" data-id="${c.id}">${icon('save')}ذخیره وضعیت وظایف</button><button type="button" class="button button--plain" data-u-action="fu-complete" data-id="${c.id}" ${remaining ? 'disabled' : ''}>${icon('lock')}تکمیل مرحله</button></div>` : ''}</div>`
        : `<p class="fu-note">${icon('info')}<span>${isOpen(c) ? 'همهٔ مراحل انجام شد؛ پرونده آمادهٔ بستن است.' : 'پرونده بسته است.'}</span></p>`}</section>`;
    const activities = `<div class="panel"><div class="panel-heading"><div><strong>فعالیت‌های پرونده</strong><small>ثبت تماس به‌تنهایی مرحله را تکمیل نمی‌کند؛ هر نتیجه با اقدام بعدی همراه است.</small></div></div>
      <ul class="fu-activities">${c.activities.map((a, i) => `<li class="fu-activity fu-activity--${a.done ? 'success' : 'info'}"><span class="fu-activity__icon">${icon('phone')}</span><div><strong>${e(a.title)}</strong><small>${e(a.kind)} · ${when(a.at)} · ${e(a.owner)}</small>${a.outcome ? `<p class="fu-activity__outcome"><b>${e(a.result)}:</b> ${e(a.outcome)}</p>` : ''}</div>
        <div class="fu-activity__side">${pill(a.done ? 'انجام‌شده' : 'برنامه‌ریزی‌شده', a.done ? 'success' : 'info')}${!a.done && write ? btn('fu-result', 'ثبت نتیجه', `${c.id}:${i}`, 'button button--primary button--xs') : ''}</div></li>`).join('') || '<li class="empty-mini">فعالیتی ثبت نشده است.</li>'}</ul></div>`;
    const items = `<div class="panel"><div class="panel-heading"><div><strong>قطعات و اقلام</strong><small>تحویل ناقص یا کسری پرونده را نمی‌بندد.</small></div></div><div class="table-wrap"><table class="list-table"><thead><tr><th>کد قطعه</th><th>شرح</th><th>تعداد / تحویل</th><th>وضعیت</th></tr></thead><tbody>
      ${c.items.map(([code, desc, qty, delivered, status]) => `<tr><td><bdi>${e(code)}</bdi></td><td>${e(desc)}</td><td>${fa(qty)} / ${fa(delivered)}</td><td>${pill(status, status === 'کسری' ? 'warning' : 'neutral')}</td></tr>`).join('') || '<tr><td colspan="4"><p class="empty-mini">قطعه‌ای ثبت نشده است.</p></td></tr>'}</tbody></table></div></div>`;
    const referrals = `<div class="panel"><div class="panel-heading"><div><strong>ارجاع‌ها</strong><small>تا پذیرش، مسئول فعلی پاسخ‌گو می‌ماند.</small></div></div><div class="table-wrap"><table class="list-table"><thead><tr><th>دامنه</th><th>از / به</th><th>دلیل</th><th>وضعیت</th></tr></thead><tbody>
      ${c.referrals.map((r, i) => `<tr><td>${e(r.scope)}</td><td>${e(r.from)} → <b>${e(r.to)}</b></td><td>${e(r.reason)}</td><td>${pill(r.status, r.status === 'پذیرفته شد' ? 'success' : r.status === 'رد شد' ? 'danger' : 'warning')}${r.status === 'در انتظار پذیرش' && r.to === me() ? btn('fu-accept', 'پذیرش', `${c.id}:${i}`, 'button button--primary button--xs') : ''}</td></tr>`).join('') || '<tr><td colspan="4"><p class="empty-mini">ارجاعی ثبت نشده است.</p></td></tr>'}</tbody></table></div></div>`;
    const history = `<div class="panel"><ol class="fu-timeline">${c.history.map(([t, at]) => `<li><span class="fu-timeline__icon">${icon('info')}</span><div><strong>${e(t)}</strong><small>${when(at)}</small></div></li>`).join('')}</ol></div>`;
    const panels = {stages: stagesPanel, activities, items, referrals, history};
    return `<header class="fu-head panel"><div class="fu-head__main"><span class="fu-head__icon">${icon('file')}</span><div class="fu-head__title"><div class="fu-head__badges">${pill(statusLabel[c.status], statusTone[c.status], false)}${pill('اولویت ' + pl, pt, false)}${overdue(c) ? pill('عقب‌افتاده', 'danger', false) : ''}${c.paused ? pill('مهلت متوقف', 'violet', false) : ''}</div>
      <h1>${e(c.subject)}</h1><p class="fu-head__meta"><span><b>کد:</b> ${e(c.code)}</span><span><b>نوع:</b> ${e(c.type)}</span><span><b>مشتری:</b> ${e(c.customer)}</span>${c.related ? `<span><b>مرتبط:</b> ${e(c.related)}</span>` : ''}<span><b>شعبه:</b> ${e(c.branch)}</span><span><b>الگو:</b> ${e(c.template)} · نسخه ۱</span></p></div></div>
      <div class="fu-head__actions">${write ? btn('fu-plan', 'برنامه‌ریزی اقدام', c.id, 'button button--primary', 'calendar') + btn('fu-refer', 'ارجاع', c.id, 'button button--ghost', 'send') + btn('fu-documents', 'مدارک و تأییدها', c.id, 'button button--ghost', 'file') + btn(isWaiting(c) ? 'fu-resume' : 'fu-wait', isWaiting(c) ? 'ازسرگیری' : 'انتظار', c.id, 'button button--ghost', isWaiting(c) ? 'play' : 'pause') + btn('fu-close', 'بستن پرونده', c.id, 'button button--ghost', 'check') : ''}${btn('fu-back', 'بازگشت به فهرست', '', 'button button--ghost', 'chevron')}</div>
      <div class="fu-head__progress"><div class="fu-bar"><span style="width:${p}%"></span></div><span><b>${fa(p)}٪</b> پیشرفت وزنی</span></div></header>
      ${isWaiting(c) ? `<p class="account-banner fu-banner--wait">${icon('pause')}<span><b>${statusLabel[c.status]}:</b> ${e(c.waitReason || '')} · ${c.paused ? 'مهلت تا ازسرگیری متوقف است.' : 'طبق سیاست، زمان متوقف نمی‌شود.'}</span></p>` : ''}
      ${c.status === 'Closed' ? `<p class="account-banner fu-banner--closed">${icon('check')}<span><b>نتیجهٔ نهایی:</b> ${e(c.outcome || '')}</span></p>` : ''}
      <section class="fu-kpis">${kpi('مسئول پرونده', c.owner || 'بدون مسئول', 'شعبه ' + c.branch)}${kpi('اقدام بعدی', c.next || (isOpen(c) ? 'تعیین نشده' : '—'), c.nextAt ? when(c.nextAt) : 'هر پروندهٔ باز باید اقدام بعدی داشته باشد', !c.next && isOpen(c) ? 'danger' : '')}
        ${kpi('پاسخ اولیه', c.firstDone ? 'پاسخ داده شد' : 'در انتظار', '', c.firstDone ? 'success' : '')}${kpi('مهلت حل پرونده', c.paused ? 'متوقف (انتظار)' : due(c.dueAt)[0], when(c.dueAt), !c.paused && c.dueAt && Date.parse(c.dueAt) < Date.now() ? 'danger' : '')}
        ${kpi('مرحلهٔ فعال', s?.name || '—', s ? s.role : '')}${kpi('ارجاع باز', fa(c.referrals.filter(r => r.status === 'در انتظار پذیرش').length), 'تا پذیرش، مسئولیت تغییر نمی‌کند')}</section>
      <div class="fu-tabs"><div class="tabbar tabbar--main" role="tablist">${tabBtn('stages', 'مراحل و وظایف')}${tabBtn('activities', 'فعالیت‌ها')}${tabBtn('items', 'قطعات')}${tabBtn('referrals', 'ارجاع‌ها')}${tabBtn('history', 'تاریخچه')}</div><section>${panels[state.tab] || stagesPanel}</section></div>`;
  }

  // ── Settings (۹–۱۱) and supervision (۱۲) ──
  function settingsView() {
    // Forms ۹–۱۱: the application's own pages (static demo data; saving is done by the server version).
    const page = ['template', 'policy', 'queue'].includes(state.settings) ? state.settings : 'template';
    const nav = [['template', '۹. طراحی الگوی گردش کار'], ['policy', '۱۰. مهلت‌ها و هشدارها'], ['queue', '۱۱. صف‌ها و تخصیص کار']]
      .map(([key, label]) => `<button type="button" class="list-tabs__tab${page === key ? ' is-active' : ''}" data-u-action="fu-settings" data-id="${key}">${label}</button>`).join('');
    return `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('settings')}</span><div><h1>تنظیمات مرکز پیگیری</h1><p>الگوهای گردش کار نسخه‌دار، سیاست‌های مهلت و هشدار، صف‌ها و قواعد تخصیص</p></div></div><div class="list-head__actions">${btn('fu-back', 'بازگشت', '', 'button button--ghost', 'chevron')}</div></header>
      <nav class="list-tabs fu-settings-nav">${nav}</nav>${S?.[page] ? fill(S[page], null, page) : '<p class="empty-mini">این صفحه در نسخهٔ نمایشی در دسترس نیست.</p>'}`;
  }
  function supervisionView() {
    // Form ۱۲ — the same page as Views/FollowUpSettings/Supervision.cshtml, from the preview data.
    const open = visibleCases().filter(isOpen), noNext = open.filter(c => !c.nextAt), late = open.filter(overdue), pendingRefs = open.flatMap(c => c.referrals.filter(r => r.status === 'در انتظار پذیرش'));
    const now = Date.now();
    const row = c => {
      const ref = c.referrals.find(r => r.status === 'در انتظار پذیرش');
      const lateH = c.nextAt && Date.parse(c.nextAt) < now ? (now - Date.parse(c.nextAt)) / 3600000 : c.dueAt && !c.paused && Date.parse(c.dueAt) < now ? (now - Date.parse(c.dueAt)) / 3600000 : 0;
      const nearH = !lateH && c.nextAt ? (Date.parse(c.nextAt) - now) / 3600000 : 99;
      const [label, tone, ic, sev] = !c.owner ? ['بدون مسئول', 'danger', 'alert', 6] : !c.nextAt ? ['بدون اقدام بعدی', 'warning', 'alert', 5]
        : lateH ? [lateH >= 48 ? `${fa(Math.floor(lateH / 24))} روز تأخیر` : `${fa(Math.max(1, Math.floor(lateH)))} ساعت تأخیر`, 'danger', 'clock', 4]
        : ref ? ['در انتظار پذیرش', 'violet', 'hourglass', 2] : nearH <= 4 ? [`${fa(Math.max(1, Math.ceil(nearH)))} ساعت تا مهلت`, 'warning', 'clock', 2] : [null];
      return label ? {c, label, tone, ic, sev} : null;
    };
    const rows = open.map(row).filter(Boolean).sort((a, b) => b.sev - a.sev);
    const waits = store.fuTemplates.flatMap(t => t.stages.map(x => x.name)).filter((x, i, all) => all.indexOf(x) === i).slice(0, 5)
      .map((name, i) => [name, [72, 48, 26, 12, 6][i]]);
    const time = new Intl.DateTimeFormat('fa-IR', {hour: '2-digit', minute: '2-digit', hour12: false}).format(new Date());
    return `<div class="fu-page"><section class="fu-sheet fu-sheet--card fu-super-sheet">
      <header class="fu-sheet__head"><span class="fu-head-side">${icon('clock')}به‌روزرسانی: ${time}</span><div class="fu-sheet__title"><h2>نظارت مدیر</h2></div><span class="fu-num" aria-hidden="true">۱۲</span></header>
      <div class="fu-sheet__body">
        <div class="fu-g fu-g--3 fu-card fu-super-filters2">
          <label class="fu-f"><span class="fu-f__l"><svg class="fu-l-ico"><use href="#i-globe"/></svg>حوزه</span><span class="fu-f__c"><select><option>همهٔ شعب در دامنهٔ شما</option></select></span></label>
          <label class="fu-f"><span class="fu-f__l"><svg class="fu-l-ico"><use href="#i-users"/></svg>تیم</span><span class="fu-f__c"><select><option>همه</option>${store.fuQueues.map(q => `<option>${e(q.name)}</option>`).join('')}</select></span></label>
          <label class="fu-f"><span class="fu-f__l"><svg class="fu-l-ico"><use href="#i-calendar"/></svg>بازه</span><span class="fu-f__c"><select><option>هفته جاری</option><option>امروز</option><option>۳۰ روز اخیر</option></select></span></label>
        </div>
        <div class="fu-tiles" aria-label="شمارنده‌ها">
          <button type="button" class="fu-tile" data-u-action="fu-view-list" data-id="all"><small>باز</small><strong>${fa(open.length)}</strong>${icon('file')}</button>
          <button type="button" class="fu-tile fu-tile--red" data-u-action="fu-view-list" data-id="overdue"><small>عقب‌افتاده</small><strong>${fa(late.length)}</strong>${icon('clock')}</button>
          <button type="button" class="fu-tile fu-tile--amber" data-u-action="fu-view-list" data-id="nonext"><small>بدون اقدام بعدی</small><strong>${fa(noNext.length)}</strong>${icon('alert')}</button>
          <button type="button" class="fu-tile fu-tile--purple" data-u-action="fu-view-list" data-id="referrals"><small>ارجاع نپذیرفته</small><strong>${fa(pendingRefs.length)}</strong>${icon('ban')}</button>
        </div>
        <div class="fu-stack">
          <section class="fu-sec fu-card"><h2 class="fu-sec__h">${icon('file')}پرونده‌های نیازمند توجه</h2>
            <div class="table-wrap"><table class="fu-t fu-attention"><thead><tr><th>پرونده / مشتری</th><th class="hide-sm">مسئول</th><th>وضعیت</th><th><span class="sr-only">بررسی</span></th></tr></thead><tbody>
            ${rows.map(r => `<tr><td title="${e(r.c.subject)}"><button type="button" class="cell-title link-button" data-u-action="fu-open" data-id="${r.c.id}"><bdi>${e(r.c.code)}</bdi></button><small class="cell-sub">${e(r.c.customer)}</small></td><td class="hide-sm">${e(r.c.owner || 'بدون مسئول')}</td><td><span class="pill pill--sm pill--${r.tone} fu-pill-ico">${icon(r.ic)}${r.label}</span></td><td>${btn('fu-open', 'بررسی پرونده', r.c.id, 'fu-add fu-add--line')}</td></tr>`).join('') || '<tr><td colspan="4"><p class="empty-mini">پروندهٔ نیازمند توجهی نیست.</p></td></tr>'}
            </tbody></table></div></section>
          <section class="fu-sec fu-card"><h2 class="fu-sec__h">${icon('clock')}بیشترین زمان انتظار<small class="fu-f__hint">میانگین در بازه انتخاب‌شده</small></h2>
            <ol class="fu-rank">${waits.map(([name, h], i) => { const tone = i === 0 ? 'is-red' : i === 1 ? 'is-amber' : 'is-blue'; return `<li class="${tone}"><span>${e(name)}</span><b class="${tone}">${fa(h)} ساعت</b></li>`; }).join('')}</ol></section>
        </div>
        ${noNext.length ? `<p class="fu-note fu-note--amber fu-note--strong">${icon('alert')}<span>${fa(noNext.length)} پرونده نیازمند تعیین اقدام بعدی است.</span></p>` : ''}
      </div>
      <footer class="fu-sheet__foot">${btn('fu-save-view', 'ذخیره نما', '', 'button button--primary', 'save')}${btn('fu-export', 'خروجی گزارش', '', 'button button--plain fu-foot__end', 'download')}</footer>
    </section></div>`;
  }

  // ── Actions and drawers ──
  const byId = id => store.followUps.find(x => x.id === Number(String(id).split(':')[0]));
  // v1.11.1: the forms are the application's own markup (preview/follow-up-snapshots.js, captured from Crm.Web), filled with this case.
  const S = typeof FU_SNAPSHOTS === 'undefined' ? null : FU_SNAPSHOTS;
  const latin = v => String(v ?? '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d));
  const jFormat = new Intl.DateTimeFormat('fa-IR-u-ca-persian-nu-latn', {year: 'numeric', month: '2-digit', day: '2-digit'});
  const jDay = offset => jFormat.format(new Date(Date.now() + offset * 86400000));
  function j2g(jy, jm, jd) {
    jy += 1595;
    let days = -355668 + 365 * jy + Math.floor(jy / 33) * 8 + Math.floor((jy % 33 + 3) / 4) + jd + (jm < 7 ? (jm - 1) * 31 : (jm - 7) * 30 + 186);
    let gy = 400 * Math.floor(days / 146097); days %= 146097;
    if (days > 36524) { gy += 100 * Math.floor(--days / 36524); days %= 36524; if (days >= 365) days++; }
    gy += 4 * Math.floor(days / 1461); days %= 1461;
    if (days > 365) { gy += Math.floor((days - 1) / 365); days = (days - 1) % 365; }
    let gd = days + 1, gm = 0;
    const months = [0, 31, (gy % 4 === 0 && gy % 100 !== 0) || gy % 400 === 0 ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    for (gm = 0; gm < 13 && gd > months[gm]; gm++) gd -= months[gm];
    return [gy, gm, gd];
  }
  // «۱۴۰۵/۰۷/۱۴» + «۱۰:۳۰» (Tehran time as typed) → ISO; must be in the future like on the server.
  function futureJ(date, time, label = 'زمان') {
    const d = latin(date).match(/^(\d{4})\/(\d{1,2})\/(\d{1,2})$/), t = latin(time || '09:00').match(/^(\d{1,2}):(\d{2})$/);
    if (!d || !t) throw new Error(`${label}: تاریخ را به شکل ۱۴۰۵/۰۷/۱۲ و ساعت را به شکل ۱۴:۳۰ وارد کنید.`);
    const [gy, gm, gd] = j2g(+d[1], +d[2], +d[3]);
    const at = new Date(gy, gm - 1, gd, +t[1], +t[2]);
    if (!(at > new Date())) throw new Error(`${label} باید در آینده باشد.`);
    return at.toISOString();
  }
  const optText = name => document.querySelector(`#modalBody [name="${name}"]`)?.selectedOptions?.[0]?.textContent?.trim();
  const kindLabel = {Call: 'تماس', Meeting: 'جلسه', Visit: 'بازدید', Task: 'کار داخلی'};
  function formKind(path) {
    if (/\/follow-ups$/.test(path)) return 'fu-new';
    for (const k of ['plan', 'result', 'refer', 'wait', 'resume', 'close', 'reopen', 'documents', 'approvals']) if (new RegExp(`/${k}(/|$)`).test(path)) return 'fu-' + k;
    if (/\/referrals\//.test(path)) return 'fu-referral';
    return 'fu-config';
  }
  // Snapshot → this case: chip, customer and subject, fresh dates, preview form routing and an error line.
  function fill(html, c, id) {
    const m = S.meta;
    if (c) html = html.split(m.caseCode).join(c.code).split(m.customer).join(c.customer).split(m.subject).join(c.subject);
    html = html.replace(/(name="(?:Date|NextDate|ReviewDate|ExpectedDate|AcceptDate|CorrectionDate|DueDate)"[^>]*?value=")[^"]*"/g, `$1${jDay(1)}"`)
      .replace(/(name="(?:DoneDate|RequestedDate)"[^>]*?value=")[^"]*"/g, `$1${jDay(0)}"`)
      .replace(/<form([^>]*?) action="([^"]*)"/g, (all, attrs, path) => `<form${attrs} action="#" novalidate data-u-form="${formKind(path)}" data-id="${e(id ?? '')}"`)
      .replace(/<footer class="fu-sheet__foot/g, '<div class="u-form-error" role="alert"></div><footer class="fu-sheet__foot');
    return html;
  }
  function openSnapshot(key, c, id, adjust = h => h) {
    if (!S?.[key]) { toast('این فرم در نسخهٔ نمایشی در دسترس نیست.', 'warning'); return; }
    closeDrawer();
    const modal = document.getElementById('modal');
    modal.classList.add('modal--form', 'modal--sheet');
    const html = adjust(fill(S[key], c, id));
    document.getElementById('modalTitle').textContent = (html.match(/<span data-drawer-title hidden>([^<]*)/) || [])[1] || 'فرم';
    const footer = document.getElementById('modalFooter'); if (footer) footer.innerHTML = '';
    document.getElementById('modalBody').innerHTML = html;
    modal.classList.add('is-open'); document.getElementById('modalBackdrop').classList.add('is-open'); modal.setAttribute('aria-hidden', 'false');
    // Same client behaviour as the application (tabs, rows, priority dot, counters, capacity card).
    if (typeof CustomEvent === 'function') document.body.dispatchEvent?.(new CustomEvent('htmx:afterSettle', {detail: {target: document.getElementById('modalBody')}}));
  }
  const options = (html, name, items) => html.replace(new RegExp(`(<select name="${name}"[^>]*>)[\\s\\S]*?(</select>)`), (all, open, close) => open + items + close);
  const closeChecks = c => [['تمام مراحل الزامی تکمیل شده', c.stages.every(s => ['Done', 'Skipped'].includes(s.status)), c.stages.filter(s => !['Done', 'Skipped'].includes(s.status)).map(s => s.name).join('، ')],
    ['فعالیت باز وجود ندارد', c.activities.every(a => a.done), ''], ['ارجاع در انتظار پذیرش وجود ندارد', c.referrals.every(r => r.status !== 'در انتظار پذیرش'), '']];

  function action(name, id) {
    if (!name.startsWith('fu-')) return false;
    if (name === 'fu-view') { state.view = id; render(); return true; }
    if (name === 'fu-open') { state.selected = Number(id); state.tab = 'stages'; ctx.navigate('follow-up'); return true; }
    if (name === 'fu-back') { state.selected = null; ctx.navigate('follow-ups'); return true; }
    if (name === 'fu-settings') { state.settings = id || state.settings || 'template'; ctx.navigate('follow-up-settings'); return true; }
    if (name === 'fu-supervision') { ctx.navigate('follow-up-supervision'); return true; }
    if (name === 'fu-tab') { state.tab = id; render(); return true; }
    if (name === 'fu-save-tasks') { toast('وضعیت وظایف ذخیره شد.'); return true; }
    if (name === 'fu-view-list') { state.view = id; ctx.navigate('follow-ups'); return true; }
    if (name === 'fu-save-view') { const n = window.prompt?.('نام نما (مثلاً شعبه مرکزی — هفتگی)'); if (n) toast(`نمای «${n}» ذخیره شد.`); return true; }
    if (name === 'fu-export') { toast('خروجی گزارش در نسخهٔ سرور به‌صورت فایل CSV دریافت می‌شود.'); return true; }
    const c = byId(id);
    if (name === 'fu-new') {
      openSnapshot('create', null, '', h => options(options(h, 'CustomerId', `<option value="">— انتخاب مشتری —</option>${store.customers.map((x, i) => `<option value="${e(x.name)}"${i === 0 ? ' selected' : ''}>${e(x.name)}</option>`).join('')}`),
        'TemplateId', store.fuTemplates.map(t => `<option value="${t.code}">${e(t.name)} · نسخه ${fa(t.version)}</option>`).join('')));
      return true;
    }
    if (!c) return true;
    if (name === 'fu-plan') { openSnapshot('plan' + (['Meeting', 'Visit', 'Task'].includes(state.planKind) ? state.planKind : 'Call'), c, c.id); state.planKind = null; return true; }
    if (name === 'fu-result') {
      const a = c.activities[Number(String(id).split(':')[1])];
      openSnapshot('result', c, id, h => h.replace(/(<div class="fu-done-card">[\s\S]*?<strong>)[^<]*(<\/strong>\s*<small[^>]*>)[^<]*(<\/small>)/, `$1${e(a?.kind || 'تماس')}$2${e(a?.title || '')}$3`));
      return true;
    }
    if (name === 'fu-refer') { openSnapshot('refer', c, c.id); return true; }
    if (name === 'fu-accept') { const r = c.referrals[Number(String(id).split(':')[1])]; if (r && r.to === me()) { r.status = 'پذیرفته شد'; if (r.scope === 'کل پرونده') c.owner = me(); log(c, `ارجاع توسط ${me()} پذیرفته شد`); save(); render(); toast('ارجاع پذیرفته شد.'); } return true; }
    if (name === 'fu-wait' || name === 'fu-resume') {
      // One sheet (۶) with two tabs; while the case waits, «ازسرگیری» is the open tab.
      openSnapshot('wait', c, c.id, h => !isWaiting(c) ? h : h.replace(/class="is-active" data-fu-tab="wait">/, 'class="" data-fu-tab="wait" disabled="disabled">')
        .replace(/class="" data-fu-tab="resume" disabled="disabled">/, 'class="is-active" data-fu-tab="resume">')
        .replace('<form data-fu-panel="wait"', '<form data-fu-panel="wait" hidden="hidden"').replace('<form data-fu-panel="resume" hidden="hidden"', '<form data-fu-panel="resume"'));
      return true;
    }
    if (name === 'fu-documents') { openSnapshot('documents', c, c.id); return true; }
    if (name === 'fu-close') {
      const checks = closeChecks(c), passed = checks.filter(x => x[1]).length, ready = passed === checks.length, p = progress(c);
      openSnapshot('close', c, c.id, h => h
        .replace(/<section class="fu-box [^"]*fu-ready fu-ready--row">[\s\S]*?<\/section>/, `<section class="fu-box ${ready ? 'fu-box--green' : 'fu-box--amber'} fu-ready fu-ready--row"><span class="fu-ready__mark">${icon(ready ? 'check' : 'alert')}</span><div><strong>${ready ? 'آماده بستن' : 'هنوز آماده بستن نیست'}</strong><small>${ready ? 'تمام مراحل تکمیل شده است.' : `${fa(passed)} از ${fa(checks.length)} کنترل برقرار است.`}</small></div><div class="fu-ready__bar"><span>پیشرفت: ${fa(p)}٪</span><div class="fu-progressbar"><span style="width:${p}%"></span></div></div></section>`)
        .replace(/<ul class="fu-ticks fu-close-checks">[\s\S]*?<\/ul>/, `<ul class="fu-ticks fu-close-checks">${checks.map(([l, ok, hint]) => `<li class="${ok ? 'is-ok' : 'is-fail'}">${icon(ok ? 'check' : 'close')}<span>${l}${hint ? `<small>باقی‌مانده: ${e(hint)}</small>` : ''}</span></li>`).join('')}</ul>`)
        .replace(/(<button class="button button--primary" type="submit")( disabled="disabled")?(><svg><use href="#i-lock"\/><\/svg>)/, `$1${ready ? '' : ' disabled="disabled"'}$3`));
      return true;
    }
    if (name === 'fu-complete') { const s = active(c); if (s && s.items.every(x => x.done)) { s.status = 'Done'; log(c, `مرحلهٔ «${s.name}» تکمیل شد`); advance(c); save(); render(); toast(`مرحلهٔ «${s.name}» تکمیل شد.`); } return true; }
    return false;
  }
  function submit(kind, data, id) {
    if (!kind.startsWith('fu-')) return false;
    const future = value => { const t = Date.parse(value); if (!Number.isFinite(t) || t <= Date.now()) throw new Error('زمان باید در آینده باشد.'); return new Date(t).toISOString(); };
    const when2 = (iso, date, time, label) => iso ? future(iso) : futureJ(date, time, label);
    const text = (...values) => String(values.find(v => v != null && String(v).trim() !== '') ?? '').trim();
    if (kind === 'fu-new') {
      const subject = text(data.Subject, data.subject), customer = text(data.CustomerId, data.customer);
      const template = store.fuTemplates.find(t => t.code === text(data.TemplateId, data.template)) || store.fuTemplates[0];
      if (!subject) throw new Error('موضوع پیگیری الزامی است.');
      if (!customer) throw new Error('مشتری را انتخاب کنید.');
      if (store.followUps.some(x => isOpen(x) && x.customer === customer && x.subject.trim() === subject)) throw new Error('پروندهٔ باز مشابه برای این مشتری وجود دارد.');
      const nid = Math.max(...store.followUps.map(x => x.id)) + 1;
      const parts = Object.keys(data).map(k => k.match(/^Parts\[(\d+)\]\.PartCode$/)).filter(Boolean).map(m => [text(data[m[0]]), text(data[`Parts[${m[1]}].Description`]), Number(latin(data[`Parts[${m[1]}].Quantity`])) || 0, 0, 'در انتظار']).filter(x => x[0]);
      const c = {id: nid, code: 'RQ-' + nid, subject, customer, type: template.type, priority: text(data.Priority, data.priority) || 'Normal', status: 'InProgress', owner: optText('OwnerUserId')?.replace(/^—.*/, '') || 'سارا احمدی', branch: 'مرکزی',
        template: template.code, next: 'پاسخ اولیه به مشتری', nextAt: hours(2), firstDone: false, dueAt: hours(24), stages: instantiate(template), activities: [], referrals: [], items: parts, history: []};
      log(c, 'پرونده ثبت شد؛ مسئول از صف و مهلت‌ها از سیاست تعیین شد'); store.followUps.push(c); delete store.fuDraft; save(); closeDrawer(); state.selected = nid; state.tab = 'stages'; ctx.navigate('follow-up'); toast(`پرونده ${c.code} ثبت شد.`); return true;
    }
    const c = byId(id);
    if (['fu-config', 'fu-documents', 'fu-approvals', 'fu-referral'].includes(kind) || !c) { closeDrawer(); toast('در نسخهٔ نمایشی ثبت شد؛ نسخهٔ سرور این تغییر را ذخیره می‌کند.'); return true; }
    if (kind === 'fu-plan') {
      const title = text(data.Title, data.title); if (!title) throw new Error('عنوان اقدام الزامی است.');
      const at = when2(data.at, data.Date, data.Time, 'تاریخ و ساعت اقدام'); const k = kindLabel[data.Kind] || data.kind || 'تماس';
      c.activities.unshift({title, kind: k, at, owner: optText('OwnerUserId') || me(), done: false}); c.next = title; c.nextAt = at; log(c, 'اقدام برنامه‌ریزی شد: ' + title);
    }
    if (kind === 'fu-result') {
      const a = c.activities[Number(String(id).split(':')[1])]; const outcome = text(data.Outcome, data.outcome), next = text(data.NextTitle, data.next);
      const result = data.result || results.find(([k]) => k === data.ResultCode)?.[1]; if (!result) throw new Error('نتیجه پیگیری را انتخاب کنید.');
      if (!outcome) throw new Error('شرح نتیجه الزامی است.'); if (!next) throw new Error('اقدام بعدی الزامی است.');
      const at = when2(data.at, data.NextDate, data.NextTime, 'موعد اقدام بعدی');
      Object.assign(a, {done: true, result, outcome}); c.firstDone = true; c.next = next; c.nextAt = at; log(c, `نتیجه ثبت شد: ${result}`);
    }
    if (kind === 'fu-refer') {
      const reason = text(data.Reason, data.reason); if (!reason) throw new Error('دلیل ارجاع الزامی است.');
      const to = (data.to || optText('ToUserId') || '').split(' — ')[0]; const whole = data.Scope === 'Case' || data.scope === 'کل پرونده';
      c.referrals.unshift({from: me(), to, scope: whole ? 'کل پرونده' : `مرحلهٔ «${active(c)?.name || '—'}»`, reason, status: 'در انتظار پذیرش', due: hours(4)}); log(c, `ارجاع به ${to}`);
    }
    if (kind === 'fu-wait') {
      const status = text(data.Status, data.status), reason = text(data.Reason, data.reason); if (!reason) throw new Error('علت انتظار الزامی است.');
      const at = when2(data.at, data.ReviewDate, data.ReviewTime, 'موعد بازبینی');
      Object.assign(c, {status, waitReason: reason, paused: status === 'WaitingCustomer', next: 'بازبینی: ' + reason.slice(0, 80), nextAt: at}); log(c, `وضعیت: ${statusLabel[status]}`);
    }
    if (kind === 'fu-resume') { const next = text(data.NextTitle, data.next); if (!next) throw new Error('اقدام بعدی الزامی است.'); const at = when2(data.at, data.NextDate, data.NextTime, 'اقدام بعدی'); Object.assign(c, {status: 'InProgress', paused: false, waitReason: '', next, nextAt: at}); log(c, 'کار از سر گرفته شد'); }
    if (kind === 'fu-close') {
      const outcome = text(data.Outcome, data.outcome); if (!outcome) throw new Error('نتیجه نهایی را انتخاب کنید.');
      const ready = data.ready != null ? data.ready === '1' : closeChecks(c).every(x => x[1]);
      if (!ready) throw new Error('پرونده هنوز آمادهٔ بستن نیست؛ کنترل‌های قرمز را برطرف کنید.'); Object.assign(c, {status: 'Closed', outcome, next: null, nextAt: null}); log(c, 'پرونده بسته شد: ' + outcome);
    }
    if (kind === 'fu-reopen') { const reason = text(data.Reason); if (!reason) throw new Error('دلیل بازگشایی الزامی است.'); Object.assign(c, {status: 'InProgress', outcome: null, next: 'پیگیری پس از بازگشایی', nextAt: hours(24)}); log(c, 'پرونده بازگشایی شد: ' + reason); }
    save(); closeDrawer(); render(); toast('ثبت شد.'); return true;
  }
  // Clicks inside the application's markup that would call the server: handled in the preview.
  document.addEventListener('click', event => {
    const t = event.target;
    if (!t?.closest) return;
    if (t.closest('[data-drawer-close]')) { closeDrawer(); return; }
    const remove = t.closest('[data-remove-row]'); if (remove && t.closest('.fu-sheet, .fu-page')) { remove.closest('[data-row]')?.remove(); return; }
    const kindTile = t.closest('#modalBody a[hx-get*="/plan?kind="]');
    if (kindTile) { event.preventDefault(); state.planKind = kindTile.getAttribute('hx-get').match(/kind=(\w+)/)?.[1]; action('fu-plan', state.selected); return; }
    const draft = t.closest('[hx-post*="/drafts/create"]');
    if (draft) { const form = draft.closest('form'); store.fuDraft = form ? Object.fromEntries(new FormData(form)) : {}; save(); toast('پیش‌نویس در این مرورگر ذخیره شد (در نسخهٔ سرور روی سرور ذخیره می‌شود).'); return; }
    const link = t.closest('.fu-sheet a[href^="/follow-ups"], .fu-page a[href^="/follow-ups"]');
    if (link) {
      event.preventDefault();
      const href = link.getAttribute('href'), caseCode = S?.meta.ids[href.match(/\/follow-ups\/([0-9a-f-]{36})/)?.[1]];
      if (/\/settings\/templates/.test(href)) action('fu-settings', 'template');
      else if (/\/settings\/policies/.test(href)) action('fu-settings', 'policy');
      else if (/\/settings\/queues/.test(href)) action('fu-settings', 'queue');
      else if (/\/supervision/.test(href)) action('fu-supervision');
      else if (caseCode) action('fu-open', caseCode.replace('RQ-', ''));
      else if (/\/documents\//.test(href)) toast('فایل‌ها در نسخهٔ نمایشی ذخیره نمی‌شوند.', 'warning');
      else { state.view = href.match(/view=(\w+)/)?.[1] || 'all'; closeDrawer(); ctx.navigate('follow-ups'); }
      return;
    }
    const server = t.closest('.fu-sheet [hx-post], .fu-sheet [hx-get], .fu-page [hx-post], .fu-page [hx-get]');
    if (server && !server.matches('select, input')) { event.preventDefault(); toast('این عملیات در نسخهٔ سرور انجام می‌شود (پیش‌نمایش محاسبه، آزمون تخصیص و مانند آن).'); }
  });
  // «صف مقصد» would navigate to the server page.
  document.addEventListener('change', event => { if (event.target?.matches?.('select[data-navigate]')) { event.stopImmediatePropagation(); toast('در نسخهٔ نمایشی یک صف نمونه نمایش داده می‌شود.'); } }, true);
  document.addEventListener('change', event => {
    const box = event.target.closest?.('[data-fu-item]'); if (!box) return;
    const [cid, j] = box.dataset.fuItem.split(':').map(Number); const c = store.followUps.find(x => x.id === cid); const s = c && active(c);
    if (s && can.write()) { s.items[j].done = box.checked; save(); render(); }
  }, true);
  let timer = 0;
  document.addEventListener('input', event => {
    if (!event.target.matches?.('[data-fu-search]')) return;
    clearTimeout(timer); timer = setTimeout(() => { state.q = event.target.value.trim(); render(); const box = document.querySelector('[data-fu-search]'); if (box) { box.focus(); box.setSelectionRange(box.value.length, box.value.length); } }, 250);
  });
  return {
    routes: ['follow-ups', 'follow-up', 'follow-up-settings', 'follow-up-supervision'],
    allowed: route => !route.startsWith('follow-up') || (route === 'follow-up-settings' ? can.configure() : route === 'follow-up-supervision' ? can.supervise() : can.read()),
    views: {'follow-ups': listView, 'follow-up': caseView, 'follow-up-settings': settingsView, 'follow-up-supervision': supervisionView},
    action, submit, select: id => { if (id) state.selected = Number(id); }, selected: () => state.selected
  };
}
