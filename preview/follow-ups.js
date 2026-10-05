/* Offline preview of the v1.10 follow-up center (مرکز پیگیری): list with views and counters, case page with weighted
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
  const state = {view: 'all', q: '', selected: null, tab: 'stages'};
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
    const stagesPanel = `<div class="fu-stages"><div class="panel"><div class="panel-heading"><div><strong>مراحل پرونده</strong><small>پیشرفت کل = Σ (وزن × پیشرفت مرحله) ÷ ۱۰۰</small></div><span class="fu-total">${fa(p)}٪</span></div>
      <div class="table-wrap"><table class="list-table fu-stage-table"><thead><tr><th>#</th><th>مرحله</th><th>وزن</th><th>پیشرفت</th><th>وضعیت</th></tr></thead><tbody>
      ${c.stages.map((x, i) => `<tr class="${x === s ? 'is-current' : ''}"><td>${fa(i + 1)}</td><td><strong class="cell-title">${e(x.name)}</strong><small class="cell-sub">${e(x.role)}</small></td><td>${fa(x.weight)}٪</td><td>${bar(stageProgress(x))}</td><td>${pill(stageLabel[x.status][0], stageLabel[x.status][1])}</td></tr>`).join('')}</tbody></table></div></div>
      <div class="panel fu-active-stage">${s ? `<div class="panel-heading"><div><strong>مرحلهٔ فعال: ${e(s.name)}</strong><small>مسئول: ${e(c.owner || '—')} · وزن ${fa(s.weight)}٪</small></div>${pill(stageLabel[s.status][0], stageLabel[s.status][1], false)}</div>
        <div class="fu-checklist"><fieldset ${write ? '' : 'disabled'}><legend>وظایف این مرحله</legend>${s.items.map((x, j) => `<label class="fu-check${x.done ? ' is-done' : ''}"><input type="checkbox" data-fu-item="${c.id}:${j}" ${x.done ? 'checked' : ''}><span>${e(x.title)}</span></label>`).join('')}</fieldset>
        ${remaining ? `<p class="fu-warning">${icon('alert')}<span>برای تکمیل مرحله، <b>${fa(remaining)}</b> مورد باقی‌مانده انجام شود.</span></p>` : ''}
        ${write ? `<div class="fu-inline-actions"><button type="button" class="button button--primary" data-u-action="fu-complete" data-id="${c.id}" ${remaining ? 'disabled' : ''}>${icon('lock')}تکمیل مرحله</button></div>` : ''}</div>`
        : `<div class="panel-heading"><div><strong>مرحلهٔ فعال</strong><small>${isOpen(c) ? 'همهٔ مراحل انجام شد؛ پرونده آمادهٔ بستن است.' : 'پرونده بسته است.'}</small></div></div>`}</div></div>`;
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
      <div class="fu-head__actions">${write ? btn('fu-plan', 'برنامه‌ریزی اقدام', c.id, 'button button--primary', 'calendar') + btn('fu-refer', 'ارجاع', c.id, 'button button--ghost', 'send') + btn(isWaiting(c) ? 'fu-resume' : 'fu-wait', isWaiting(c) ? 'ازسرگیری' : 'انتظار', c.id, 'button button--ghost', isWaiting(c) ? 'play' : 'pause') + btn('fu-close', 'بستن پرونده', c.id, 'button button--ghost', 'check') : ''}${btn('fu-back', 'بازگشت به فهرست', '', 'button button--ghost', 'chevron')}</div>
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
    return `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('settings')}</span><div><h1>تنظیمات مرکز پیگیری</h1><p>الگوهای گردش کار نسخه‌دار، سیاست‌های مهلت و هشدار، صف‌ها و قواعد تخصیص</p></div></div><div class="list-head__actions">${btn('fu-back', 'بازگشت', '', 'button button--ghost', 'chevron')}</div></header>
      ${store.fuTemplates.map(t => { const sum = t.stages.reduce((a, s) => a + s.weight, 0); return `<section class="panel"><div class="panel-heading"><div><strong>${e(t.name)}</strong><small>${e(t.code)} · ${e(t.type)} · نسخه ${fa(t.version)} · ${e(t.status)}</small></div><span class="fu-weight-sum ${sum === 100 ? 'is-ok' : 'is-bad'}">مجموع وزن‌ها: <b>${fa(sum)}</b>٪</span></div>
        <div class="table-wrap"><table class="list-table"><thead><tr><th>مرحله</th><th>وزن</th><th>نقش مسئول</th><th>چک‌لیست</th></tr></thead><tbody>${t.stages.map(s => `<tr><td>${e(s.name)}</td><td>${fa(s.weight)}٪</td><td>${e(s.role)}</td><td>${s.items.map(e).join('، ')}</td></tr>`).join('')}</tbody></table></div>
        ${t.rules.length ? `<div class="fu-rules"><strong>${icon('flow')}قواعد</strong>${t.rules.map(([a, b]) => `<span class="chip-action">اگر <b>${e(a)}</b> → ${e(b)}</span>`).join('')}</div>` : ''}</section>`; }).join('')}
      <section class="panel"><div class="panel-heading"><div><strong>مهلت‌ها و هشدارها</strong><small>ساعت کاری شنبه تا چهارشنبه ۸ تا ۱۷ به وقت تهران؛ هشدار مالک پرونده را تغییر نمی‌دهد.</small></div></div><div class="table-wrap"><table class="list-table"><thead><tr><th>سیاست</th><th>پاسخ / مرحله / حل</th><th>توقف مجاز</th><th>هشدار</th></tr></thead><tbody>
        ${store.fuPolicies.map(p => `<tr><td>${e(p.name)}${pill(priority[p.priority][0], priority[p.priority][1])}</td><td>${fa(p.first)} / ${fa(p.stage)} / ${fa(p.resolution)} ساعت کاری</td><td>${p.pauseCustomer ? 'انتظار مشتری' : '—'}</td><td>${p.steps.map(e).join('<br>')}</td></tr>`).join('')}</tbody></table></div></section>
      <section class="panel"><div class="panel-heading"><div><strong>صف‌ها و تخصیص کار</strong><small>پرونده فقط به فرد آماده با ظرفیت آزاد سپرده می‌شود؛ در غیر این صورت صف پشتیبان و اعلان سرپرست.</small></div></div><div class="table-wrap"><table class="list-table"><thead><tr><th>صف</th><th>روش</th><th>افراد و ظرفیت</th></tr></thead><tbody>
        ${store.fuQueues.map(q => `<tr><td>${e(q.name)}<small class="cell-sub">شعبه ${e(q.branch)}</small></td><td>${e(q.method)}</td><td>${q.members.map(([n, load, cap, ok]) => `${e(n)} ${bar(Math.round(100 * load / cap))} ${pill(ok ? 'آماده' : 'خارج از شیفت', ok ? 'success' : 'neutral')}`).join('<br>')}</td></tr>`).join('')}</tbody></table></div></section>`;
  }
  function supervisionView() {
    const open = visibleCases().filter(isOpen), noNext = open.filter(c => !c.nextAt), late = open.filter(overdue), pendingRefs = open.flatMap(c => c.referrals.filter(r => r.status === 'در انتظار پذیرش'));
    const reasons = c => [!c.owner && 'بدون مسئول', !c.nextAt && 'بدون اقدام بعدی', overdue(c) && 'عقب‌افتاده', c.referrals.some(r => r.status === 'در انتظار پذیرش') && 'ارجاع در انتظار پذیرش'].filter(Boolean);
    const rowsHtml = open.filter(c => reasons(c).length).map(c => `<tr><td><button type="button" class="link-button" data-u-action="fu-open" data-id="${c.id}">${e(c.code)}</button><small class="cell-sub">${e(c.subject)}</small></td><td>${e(c.customer)}</td><td>${pill(reasons(c).join(' · '), reasons(c).includes('بدون اقدام بعدی') ? 'danger' : 'warning')}</td><td>${e(c.owner || 'بدون مسئول')}</td><td>${btn('fu-open', 'بررسی پرونده', c.id, 'button button--ghost button--xs')}</td></tr>`).join('');
    return `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('grid')}</span><div><h1>نظارت مدیر</h1><p>برای هر پرونده معلوم است چرا متوقف شده و اقدام بعدی با کیست</p></div></div><div class="list-head__actions">${btn('fu-back', 'بازگشت', '', 'button button--ghost', 'chevron')}</div></header>
      <section class="list-tiles">${[['باز', open.length, 'flow', 'green'], ['عقب‌افتاده', late.length, 'alert', 'red'], ['بدون اقدام بعدی', noNext.length, 'clock', 'amber'], ['ارجاع نپذیرفته', pendingRefs.length, 'send', 'blue']].map(([l, v, ic, tone]) => `<div class="list-tile list-tile--${tone}"><span class="list-tile__icon">${icon(ic)}</span><span class="list-tile__body"><small>${l}</small><strong>${fa(v)}</strong></span></div>`).join('')}</section>
      ${noNext.length ? `<p class="account-banner fu-banner--alert">${icon('alert')}<span><b>${fa(noNext.length)} پرونده</b> نیازمند تعیین اقدام بعدی است.</span></p>` : ''}
      <section class="panel"><div class="panel-heading"><div><strong>پرونده‌های نیازمند توجه</strong></div></div><div class="table-wrap"><table class="list-table"><thead><tr><th>پرونده</th><th>مشتری</th><th>علت</th><th>مسئول</th><th></th></tr></thead><tbody>${rowsHtml || '<tr><td colspan="5"><p class="empty-mini">پروندهٔ نیازمند توجهی نیست.</p></td></tr>'}</tbody></table></div></section>`;
  }

  // ── Actions and drawers ──
  const byId = id => store.followUps.find(x => x.id === Number(String(id).split(':')[0]));
  function action(name, id) {
    if (!name.startsWith('fu-')) return false;
    if (name === 'fu-view') { state.view = id; render(); return true; }
    if (name === 'fu-open') { state.selected = Number(id); state.tab = 'stages'; ctx.navigate('follow-up'); return true; }
    if (name === 'fu-back') { state.selected = null; ctx.navigate('follow-ups'); return true; }
    if (name === 'fu-settings') { ctx.navigate('follow-up-settings'); return true; }
    if (name === 'fu-supervision') { ctx.navigate('follow-up-supervision'); return true; }
    if (name === 'fu-tab') { state.tab = id; render(); return true; }
    const c = byId(id);
    if (name === 'fu-new') { showForm('ثبت پرونده پیگیری', `<label>موضوع پیگیری<input name="subject" required></label><label>مشتری<select name="customer">${store.customers.map(x => `<option>${e(x.name)}</option>`).join('')}</select></label><label>الگو<select name="template">${store.fuTemplates.map(t => `<option value="${t.code}">${e(t.name)} · نسخه ${fa(t.version)}</option>`).join('')}</select></label><label>اولویت<select name="priority"><option value="Normal">عادی</option><option value="High">بالا</option><option value="Critical">بحرانی (توقف تولید)</option></select></label><p class="fu-sla-note">${icon('clock')}<span>مهلت پاسخ اولیه ۲ و حل ۲۴ ساعت کاری؛ مسئول از صف «فروش قطعات صنعتی» با بیشترین ظرفیت آزاد تعیین می‌شود.</span></p>`, 'fu-new'); return true; }
    if (!c) return true;
    if (name === 'fu-plan') { showForm('برنامه‌ریزی اقدام', `<label>نوع<select name="kind"><option>تماس</option><option>جلسه</option><option>بازدید</option><option>کار داخلی</option></select></label><label>عنوان<input name="title" required value="تماس پیگیری"></label><label>زمان<input type="datetime-local" name="at" required></label><fieldset class="pick-list"><legend>چک‌لیست پیش از تماس</legend><label class="consent-row"><input type="checkbox" checked><span>بررسی آخرین سوابق تعامل</span></label><label class="consent-row"><input type="checkbox"><span>آماده بودن پیش‌فاکتور</span></label></fieldset>`, 'fu-plan', c.id); return true; }
    if (name === 'fu-result') { showForm('ثبت نتیجه', `<label>نتیجه<select name="result" required>${results.map(([k, l]) => `<option value="${l}">${l}</option>`).join('')}</select></label><label>شرح نتیجه<textarea name="outcome" required></textarea></label><label>اقدام بعدی<input name="next" required value="ارسال پیش‌فاکتور اصلاحی"></label><label>زمان اقدام بعدی<input type="datetime-local" name="at" required></label><p class="fu-sla-note">${icon('info')}<span>ثبت تماس به‌تنهایی مرحله را تکمیل نمی‌کند.</span></p>`, 'fu-result', id); return true; }
    if (name === 'fu-refer') { showForm('ارجاع و پذیرش مسئولیت', `<label>دامنه<select name="scope"><option>اجرای مرحله</option><option>کل پرونده</option></select></label><label>گیرنده<select name="to">${Object.values(names).map(n => `<option>${e(n)}</option>`).join('')}</select></label><label>دلیل ارجاع<textarea name="reason" required></textarea></label><p class="fu-warning">${icon('alert')}<span>تا پذیرش گیرنده، مسئول فعلی پاسخ‌گو می‌ماند.</span></p>`, 'fu-refer', c.id); return true; }
    if (name === 'fu-accept') { const r = c.referrals[Number(String(id).split(':')[1])]; if (r && r.to === me()) { r.status = 'پذیرفته شد'; if (r.scope === 'کل پرونده') c.owner = me(); log(c, `ارجاع توسط ${me()} پذیرفته شد`); save(); render(); toast('ارجاع پذیرفته شد.'); } return true; }
    if (name === 'fu-wait') { showForm('ثبت انتظار', `<label>وضعیت<select name="status"><option value="WaitingCustomer">در انتظار مشتری (مهلت متوقف)</option><option value="WaitingInternal">در انتظار واحد داخلی (مهلت ادامه)</option><option value="OnHold">متوقف</option></select></label><label>علت انتظار<textarea name="reason" required maxlength="500"></textarea></label><label>موعد بازبینی<input type="datetime-local" name="at" required></label>`, 'fu-wait', c.id); return true; }
    if (name === 'fu-resume') { showForm('ازسرگیری', `<label>اقدام بعدی<input name="next" required></label><label>زمان<input type="datetime-local" name="at" required></label>`, 'fu-resume', c.id); return true; }
    if (name === 'fu-close') {
      const checks = [['تمام مراحل الزامی تکمیل شده', c.stages.every(s => ['Done', 'Skipped'].includes(s.status))], ['فعالیت باز وجود ندارد', c.activities.every(a => a.done)], ['ارجاع در انتظار پذیرش وجود ندارد', c.referrals.every(r => r.status !== 'در انتظار پذیرش')]];
      showForm('بستن پرونده', `<ul class="fu-checks">${checks.map(([l, ok]) => `<li class="${ok ? 'is-ok' : 'is-fail'}">${icon(ok ? 'check' : 'close')}<span>${l}</span></li>`).join('')}</ul><label>نتیجهٔ نهایی<input name="outcome" required value="موفق — نتیجهٔ مورد انتظار حاصل شد"></label><input type="hidden" name="ready" value="${checks.every(x => x[1]) ? 1 : 0}">`, 'fu-close', c.id); return true;
    }
    if (name === 'fu-complete') { const s = active(c); if (s && s.items.every(x => x.done)) { s.status = 'Done'; log(c, `مرحلهٔ «${s.name}» تکمیل شد`); advance(c); save(); render(); toast(`مرحلهٔ «${s.name}» تکمیل شد.`); } return true; }
    return false;
  }
  function submit(kind, data, id) {
    if (!kind.startsWith('fu-')) return false;
    const future = value => { const t = Date.parse(value); if (!Number.isFinite(t) || t <= Date.now()) throw new Error('زمان باید در آینده باشد.'); return new Date(t).toISOString(); };
    if (kind === 'fu-new') {
      const template = store.fuTemplates.find(t => t.code === data.template), nid = Math.max(...store.followUps.map(x => x.id)) + 1;
      if (store.followUps.some(x => isOpen(x) && x.customer === data.customer && x.subject.trim() === String(data.subject).trim())) throw new Error('پروندهٔ باز مشابه برای این مشتری وجود دارد.');
      const c = {id: nid, code: 'RQ-' + nid, subject: String(data.subject).trim(), customer: data.customer, type: template.type, priority: data.priority, status: 'InProgress', owner: 'سارا احمدی', branch: 'مرکزی',
        template: template.code, next: 'پاسخ اولیه به مشتری', nextAt: hours(2), firstDone: false, dueAt: hours(24), stages: instantiate(template), activities: [], referrals: [], items: [], history: []};
      log(c, 'پرونده ثبت شد؛ مسئول از صف و مهلت‌ها از سیاست تعیین شد'); store.followUps.push(c); save(); closeDrawer(); state.selected = nid; state.tab = 'stages'; ctx.navigate('follow-up'); toast(`پرونده ${c.code} ثبت شد.`); return true;
    }
    const c = byId(id); if (!c) return true;
    if (kind === 'fu-plan') { const at = future(data.at); c.activities.unshift({title: String(data.title).trim(), kind: data.kind, at, owner: me(), done: false}); c.next = String(data.title).trim(); c.nextAt = at; log(c, 'اقدام برنامه‌ریزی شد: ' + c.next); }
    if (kind === 'fu-result') { const a = c.activities[Number(String(id).split(':')[1])]; const at = future(data.at); Object.assign(a, {done: true, result: data.result, outcome: String(data.outcome).trim()}); c.firstDone = true; c.next = String(data.next).trim(); c.nextAt = at; log(c, `نتیجه ثبت شد: ${data.result}`); }
    if (kind === 'fu-refer') { if (!String(data.reason).trim()) throw new Error('دلیل ارجاع الزامی است.'); c.referrals.unshift({from: me(), to: data.to, scope: data.scope === 'کل پرونده' ? 'کل پرونده' : `مرحلهٔ «${active(c)?.name || '—'}»`, reason: String(data.reason).trim(), status: 'در انتظار پذیرش', due: hours(4)}); log(c, `ارجاع به ${data.to}؛ تا پذیرش، مسئول فعلی پاسخ‌گو می‌ماند`); }
    if (kind === 'fu-wait') { const at = future(data.at); Object.assign(c, {status: data.status, waitReason: String(data.reason).trim(), paused: data.status === 'WaitingCustomer', next: 'بازبینی: ' + String(data.reason).trim().slice(0, 80), nextAt: at}); log(c, `وضعیت: ${statusLabel[data.status]}`); }
    if (kind === 'fu-resume') { const at = future(data.at); Object.assign(c, {status: 'InProgress', paused: false, waitReason: '', next: String(data.next).trim(), nextAt: at}); log(c, 'کار از سر گرفته شد'); }
    if (kind === 'fu-close') { if (data.ready !== '1') throw new Error('پرونده هنوز آمادهٔ بستن نیست؛ کنترل‌های قرمز را برطرف کنید.'); Object.assign(c, {status: 'Closed', outcome: data.outcome, next: null, nextAt: null}); log(c, 'پرونده بسته شد: ' + data.outcome); }
    save(); closeDrawer(); render(); toast('ثبت شد.'); return true;
  }
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
