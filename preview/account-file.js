/* Offline preview of v1.1–v1.8 features: account file, customer form, service desk, commissions, notifications and version.
   Data lives in the shared preview store (browser storage). The MVC application remains the real implementation. */
function createAccountFilePreview(ctx) {
  'use strict';
  const {store, escapeHtml: e, header, button, showForm, amount, idFor, save, toast, closeDrawer, link} = ctx;
  const render = () => ctx.render();
  const user = () => ctx.account();
  const names = {'sales.manager':'مهدی نادری','sales.expert':'سارا احمدی','finance.manager':'لیلا کریمی','channel.manager':'امیرحسین مرادی','reporting.ceo':'مدیرعامل','dealer.user':'کاربر نماینده'};
  const me = () => names[user()] || user();
  const can = {
    write: () => !['reporting.ceo','dealer.user'].includes(user()),
    activity: () => ['sales.manager','sales.expert'].includes(user()),
    note: () => ['sales.manager','sales.expert','finance.manager'].includes(user()),
    payment: () => ['sales.manager','sales.expert','finance.manager'].includes(user()),
    approve: () => ['sales.manager','finance.manager'].includes(user()),
    manage: () => user() === 'sales.manager',
    commission: () => ['sales.manager','channel.manager','finance.manager','reporting.ceo'].includes(user())
  };
  const pad = n => String(n).padStart(2, '0');
  const local = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  const shift = (days, hours = 0) => { const d = new Date(); d.setDate(d.getDate() + days); d.setHours(hours || d.getHours(), hours ? 0 : d.getMinutes(), 0, 0); return local(d); };
  const now = () => local(new Date());
  const jDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', {year: 'numeric', month: '2-digit', day: '2-digit'});
  const jTime = new Intl.DateTimeFormat('fa-IR', {hour: '2-digit', minute: '2-digit', hour12: false});
  const jalali = value => { const d = new Date(value); return Number.isNaN(d.getTime()) ? '—' : jDate.format(d); };
  const when = value => { const d = new Date(value); return Number.isNaN(d.getTime()) ? '—' : `${jDate.format(d)} · ${jTime.format(d)}`; };
  const money = (n, cur = 'IRR') => `${amount(n)} ${cur}`;
  const opt = (items, selected) => items.map(([v, l]) => `<option value="${e(v)}" ${String(v) === String(selected ?? '') ? 'selected' : ''}>${e(l)}</option>`).join('');
  const field = (label, html, wide = false) => `<label class="${wide ? 'u-wide' : ''}">${e(label)}${html}</label>`;
  const opField = () => `<input type="hidden" name="op" value="op-${Date.now()}-${Math.random().toString(36).slice(2)}">`;
  const nextId = key => idFor(key);
  const ui = {tab: 'overview', side: 'followups', type: '', state: '', open: new Set(['opportunities', 'payments']), search: {}};

  // ── Seed (once; merged into older saved stores) ──
  const c0 = store.customers[0], c1 = store.customers[1];
  store.ops ??= [];
  store.contacts ??= [
    {id: 1, customerId: c0.id, name: 'علی رستگار', role: 'مدیر تدارکات', mobile: '09121234567', email: 'a.rostegar@sepehr.test', primary: true, active: true},
    {id: 2, customerId: c0.id, name: 'مینا کاظمی', role: 'مالی', mobile: '09123334455', email: 'finance@sepehr.test', primary: false, active: true},
    {id: 3, customerId: c1.id, name: 'رضا محمودی', role: 'مدیر فروش', mobile: '09131112233', email: 'r.mahmoudi@arya.test', primary: true, active: true}];
  store.activities ??= [
    {id: 1, customerId: c0.id, type: 'call', subject: 'تماس پیگیری پرداخت فاکتور', contact: 'مینا کاظمی', owner: 'سارا احمدی', start: shift(-1, 10), duration: 10, status: 'planned', priority: 'بالا', reminder: 15},
    {id: 2, customerId: c0.id, type: 'meeting', subject: 'جلسهٔ ارائهٔ راهکار بسته‌بندی', contact: 'علی رستگار', owner: 'سارا احمدی', start: shift(1, 11), end: shift(1, 12), location: 'دفتر مرکزی سپهر', participants: ['علی رستگار', 'مهدی نادری'], status: 'planned', priority: 'عادی', reminder: 30, opportunityId: 21},
    {id: 3, customerId: c0.id, type: 'task', subject: 'ارسال پیش‌فاکتور اصلاحی', owner: 'سارا احمدی', start: shift(3, 17), status: 'planned', priority: 'بالا', opportunityId: 21},
    {id: 4, customerId: c0.id, type: 'call', subject: 'پیگیری نیاز خرید فصل', contact: 'علی رستگار', owner: 'سارا احمدی', start: shift(-5, 10), duration: 18, status: 'done', result: 'پاسخ داد', outcome: 'نیاز ۲۰ تن تأیید شد؛ منتظر پیش‌فاکتور هستند.', opportunityId: 21}];
  store.notes ??= [{id: 1, customerId: c0.id, title: 'ترجیحات خرید', body: 'تحویل فقط صبح‌ها در انبار کرج؛ هماهنگی با آقای رستگار.', visibility: 'عمومی', author: 'مهدی نادری', at: shift(-2, 9)}];
  store.payments ??= [
    {id: 1, customerId: c0.id, direction: 'دریافت', method: 'حواله بانکی', amount: 1200000000, currency: 'IRR', date: shift(-20).slice(0, 10), reference: 'TRX-558812', status: 'تأییدشده', by: 'سارا احمدی'},
    {id: 2, customerId: c0.id, direction: 'دریافت', method: 'چک', amount: 300000000, currency: 'IRR', date: shift(-12).slice(0, 10), reference: 'CHQ-771245', status: 'برگشتی', by: 'سارا احمدی', note: 'برگشت چک به دلیل کسری موجودی'},
    {id: 3, customerId: c0.id, direction: 'دریافت', method: 'چک', amount: 450000000, currency: 'IRR', date: shift(-1).slice(0, 10), reference: 'CHQ-771300', status: 'ثبت‌شده', by: 'سارا احمدی'},
    {id: 4, customerId: c0.id, direction: 'دریافت', method: 'حواله بانکی', amount: 12000, currency: 'USD', date: shift(-30).slice(0, 10), reference: 'SWIFT-2291', status: 'ثبت‌شده', by: 'سارا احمدی'}];
  store.documents ??= [{id: 1, name: 'قرارداد پشتیبانی ۱۴۰۵.pdf', size: '۲۴۰ KB', by: 'مهدی نادری', at: shift(-8, 10), links: [c0.id]}, {id: 2, name: 'کاتالوگ محصولات.pdf', size: '۱٫۲ MB', by: 'سارا احمدی', at: shift(-40, 10), links: [c1.id]}];
  store.campaigns ??= [{id: 1, name: 'نمایشگاه ایران‌فودتک ۱۴۰۵', type: 'رویداد'}, {id: 2, name: 'پیامک جشنواره پاییز', type: 'پیامک'}];
  store.campaignMembers ??= [{id: 1, campaignId: 1, customerId: c0.id, status: 'پاسخ داده'}];
  store.accountHistory ??= [{id: 1, customerId: c0.id, at: shift(-30, 9), by: 'مهدی نادری', title: 'حساب ایجاد شد'}];
  store.serviceCases ??= [
    {id: 1, code: 'SC-1405-001', customerId: c0.id, subject: 'تأخیر در تحویل سفارش', priority: 'بالا', status: 'باز', opened: shift(-1, 9), due: shift(0, 17), owner: 'سارا احمدی', source: 'تماس'},
    {id: 2, code: 'SC-1405-002', customerId: c1.id, subject: 'درخواست آموزش کاربری', priority: 'عادی', status: 'بسته', opened: shift(-6, 9), due: shift(-3, 17), owner: 'محمد رضایی', source: 'پرتال نماینده', csat: 4}];
  store.commissions ??= [];
  store.notifications ??= [{id: 1, at: shift(-1, 9), category: 'یادآور فعالیت', title: 'یادآور تماس: تماس پیگیری پرداخت فاکتور', to: 'سارا احمدی', channel: 'ایمیل'}];
  for (const c of store.customers) { c.relationship ??= 'مشتری'; c.tags ??= c.id === c0.id ? ['کلیدی', 'صنایع غذایی'] : []; }
  save();

  const op = data => { if (!data.op) return false; if (store.ops.includes(data.op)) return true; store.ops.push(data.op); if (store.ops.length > 300) store.ops.shift(); return false; };
  const log = (customerId, title) => store.accountHistory.push({id: nextId('accountHistory'), customerId, at: now(), by: me(), title});
  const notify = (category, title, to) => store.notifications.push({id: nextId('notifications'), at: now(), category, title, to, channel: 'ایمیل'});
  const account = () => store.customers.find(x => x.id === ctx.selected()) || null;
  const label = {call: 'تماس', meeting: 'جلسه', task: 'وظیفه'};
  const iconOf = {call: 'clock', meeting: 'users', task: 'check'};
  const overdue = a => a.status === 'planned' && new Date(a.type === 'meeting' && a.end ? a.end : a.start) < new Date();
  const activitiesOf = c => store.activities.filter(a => a.customerId === c.id);
  const oppsOf = c => store.opportunities.filter(o => o.customer === c.name);
  const sum = list => Object.entries(list.reduce((m, p) => ({...m, [p.currency]: (m[p.currency] || 0) + (p.direction === 'دریافت' ? p.amount : -p.amount)}), {}));

  // ── Account file ──
  function kpis(c) {
    const open = oppsOf(c).filter(o => !['برنده', 'باخته'].includes(o.stage));
    const acts = activitiesOf(c), planned = acts.filter(a => a.status === 'planned');
    const done = [...acts.filter(a => a.status === 'done').map(a => ({at: a.start, t: label[a.type] + ': ' + a.subject})), ...store.notes.filter(n => n.customerId === c.id).map(n => ({at: n.at, t: 'یادداشت: ' + n.title}))].sort((a, b) => b.at.localeCompare(a.at))[0];
    const next = planned.filter(a => !overdue(a)).sort((a, b) => a.start.localeCompare(b.start))[0];
    const receipts = sum(store.payments.filter(p => p.customerId === c.id && p.status === 'تأییدشده'));
    const pending = store.payments.filter(p => p.customerId === c.id && p.status === 'ثبت‌شده').length;
    const tile = (k, l, v, d, def, tone = '') => `<article class="kpi ${tone}" data-kpi="${k}"><div class="kpi__head"><span class="kpi__label">${l}</span><span class="kpi__info" title="${e(def)}">ⓘ</span></div><strong class="kpi__value"><bdi dir="ltr">${v}</bdi></strong><small class="kpi__detail">${d}</small></article>`;
    return `<section class="account-kpis">${[
      tile('open', 'فرصت‌های باز', amount(open.length), open.length ? money(open.reduce((s, o) => s + o.value, 0)) : 'فرصت بازی نیست', 'فرصت‌هایی که هنوز برنده یا باخته نشده‌اند؛ مبلغ به تفکیک ارز.'),
      tile('overdue', 'فعالیت‌های معوق', amount(planned.filter(overdue).length), amount(planned.length) + ' فعالیت برنامه‌ریزی‌شده', 'فعالیت انجام‌نشده‌ای که زمان یا مهلت آن گذشته است.', planned.some(overdue) ? 'kpi--danger' : ''),
      tile('last', 'آخرین تعامل', done ? jalali(done.at) : '—', done ? e(done.t) : 'تعاملی ثبت نشده', 'آخرین فعالیت انجام‌شده یا یادداشت.'),
      tile('next', 'اقدام بعدی', next ? when(next.start) : '—', next ? e(label[next.type] + ': ' + next.subject) : 'اقدامی برنامه‌ریزی نشده', 'نزدیک‌ترین فعالیت برنامه‌ریزی‌شدهٔ غیرمعوق.', next ? '' : 'kpi--warning'),
      tile('receipts', 'دریافت‌های تأییدشده', receipts.map(([cur, n]) => money(n, cur)).join(' + ') || '—', pending ? amount(pending) + ' مورد در انتظار تأیید' : 'ثبت‌شده در CRM', 'فقط وضعیت «تأییدشده»؛ ثبت‌شده، برگشتی و لغوشده در جمع نیستند. ارزها جمع زده نمی‌شوند.'),
      tile('balance', 'مانده حساب', money(c.balance || 0), 'منبع: حسابداری نمونه', 'ماندهٔ بدهی از سامانهٔ مالی با منبع و زمان همگام‌سازی.')].join('')}</section>`;
  }
  function quick(c) {
    const active = c.status !== 'غیرفعال';
    const q = (label, ic, action, allowed) => allowed && active
      ? `<button type="button" class="quick-action" data-u-action="${action}" data-id="${c.id}"><span class="quick-action__icon">${icon(ic)}</span><span>${label}</span></button>`
      : `<span class="quick-action is-disabled" title="${active ? 'برای این عملیات مجوز ندارید' : 'حساب غیرفعال است'}"><span class="quick-action__icon">${icon(ic)}</span><span>${label}</span><small>${active ? 'بدون مجوز' : 'حساب غیرفعال'}</small></span>`;
    return `<section class="panel quick-panel"><div class="quick-panel__head"><strong>عملیات اصلی</strong><small>حساب جاری از پیش انتخاب شده است</small></div><div class="quick-actions">
      ${q('ایجاد فرصت', 'target', 'af-new-opportunity', can.activity())}${q('برنامه‌ریزی تماس', 'clock', 'af-new-call', can.activity())}${q('برنامه‌ریزی جلسه', 'users', 'af-new-meeting', can.activity())}
      ${q('ایجاد وظیفه', 'check', 'af-new-task', can.activity())}${q('ایجاد یادداشت', 'file', 'af-new-note', can.note())}${q('ایجاد سرنخ', 'lead', 'af-new-lead', can.activity())}</div></section>`;
  }
  const icon = name => `<svg aria-hidden="true"><use href="#i-${name}"></use></svg>`;
  function activityItem(a) {
    const closed = a.status !== 'planned', late = overdue(a);
    const state = late ? ['danger', 'معوق'] : a.status === 'planned' ? ['info', 'برنامه‌ریزی‌شده'] : a.status === 'done' ? ['success', 'انجام‌شده'] : ['neutral', 'لغوشده'];
    const opp = a.opportunityId ? store.opportunities.find(o => o.id === a.opportunityId) : null;
    const actions = !closed && can.activity() ? `<div class="act__actions"><button class="chip-action chip-action--primary" data-u-action="af-complete" data-id="${a.id}">ثبت نتیجه</button><button class="chip-action" data-u-action="af-reschedule" data-id="${a.id}">زمان جدید</button><button class="chip-action chip-action--danger" data-u-action="af-cancel" data-id="${a.id}">لغو</button></div>` : '';
    return `<article class="act ${late ? 'act--overdue' : ''} ${closed ? 'act--closed' : ''}" data-activity="${a.id}"><span class="act__icon">${icon(iconOf[a.type])}</span><div class="act__body"><strong class="act__title">${label[a.type]}: ${e(a.subject)}</strong>
      <div class="act__meta"><span class="badge badge--${state[0]}"><span></span>${state[1]}</span><span>${when(a.start)}${a.type === 'meeting' && a.end ? ' تا ' + jTime.format(new Date(a.end)) : ''}</span><span>${e(a.owner)}</span>${a.contact ? `<span>رابط: ${e(a.contact)}</span>` : ''}${opp ? `<span>فرصت ${e(opp.code)}</span>` : ''}${a.location ? `<span>${e(a.location)}</span>` : ''}${a.result ? `<span>${e(a.result)}</span>` : ''}</div>
      ${a.outcome || a.reason ? `<p class="act__outcome"><b>${a.status === 'cancelled' ? 'دلیل لغو:' : 'نتیجه:'}</b> ${e(a.outcome || a.reason)}</p>` : ''}</div>${actions}</article>`;
  }
  function activityPanel(c) {
    let list = activitiesOf(c);
    if (ui.type) list = list.filter(a => a.type === ui.type);
    if (ui.state === 'overdue') list = list.filter(overdue); else if (ui.state) list = list.filter(a => a.status === ui.state);
    const chip = (key, value, text) => `<button class="chip-action ${ui[key] === value ? 'chip-action--primary' : ''}" data-u-action="af-filter" data-id="${key}:${value}">${text}</button>`;
    const group = (title, items, css) => items.length ? `<section class="act-group ${css}"><h4>${title} <b>${amount(items.length)}</b></h4>${items.map(activityItem).join('')}</section>` : '';
    const late = list.filter(overdue).sort((a, b) => a.start.localeCompare(b.start));
    const open = list.filter(a => a.status === 'planned' && !overdue(a)).sort((a, b) => a.start.localeCompare(b.start));
    const past = list.filter(a => a.status !== 'planned').sort((a, b) => b.start.localeCompare(a.start));
    return `<div class="act-chips">${chip('type', '', 'همه')}${chip('type', 'call', 'تماس')}${chip('type', 'meeting', 'جلسه')}${chip('type', 'task', 'وظیفه')}</div><div class="act-chips">${chip('state', '', 'همهٔ وضعیت‌ها')}${chip('state', 'overdue', 'معوق')}${chip('state', 'planned', 'برنامه‌ریزی‌شده')}${chip('state', 'done', 'انجام‌شده')}</div>
      ${list.length ? group('معوق', late, 'act-group--overdue') + group('فعالیت‌های باز', open, '') + group('فعالیت‌های گذشته', past, '') : '<p class="state-empty">فعالیتی با این فیلتر نیست.</p>'}`;
  }
  function notesPanel(c) {
    const notes = store.notes.filter(n => n.customerId === c.id && (n.visibility === 'عمومی' || n.author === me() || (n.visibility === 'محدود' && can.manage())));
    return notes.length ? notes.sort((a, b) => b.at.localeCompare(a.at)).map(n => `<article class="note"><header><strong>${e(n.title)}</strong><span class="badge badge--${n.visibility === 'عمومی' ? 'neutral' : 'warning'}">${e(n.visibility)}</span></header><p>${e(n.body)}</p><footer>${e(n.author)} · ${when(n.at)}</footer></article>`).join('') : '<p class="state-empty">یادداشتی ثبت نشده است.</p>';
  }
  const row = (title, sub, status, tone, amt, date, actions = []) => `<li class="rec"><div class="rec__main"><span class="rec__title">${title}</span>${sub ? `<small>${sub}</small>` : ''}</div><div class="rec__meta">${status ? `<span class="badge badge--${tone}"><span></span>${e(status)}</span>` : ''}${amt ? `<b class="rec__amount" dir="ltr">${amt}</b>` : ''}${date ? `<span>${date}</span>` : ''}</div>${actions.length ? `<div class="rec__actions">${actions.join('')}</div>` : ''}</li>`;
  const act = (label, action, id, danger = false) => `<button class="chip-action ${danger ? 'chip-action--danger' : ''}" data-u-action="${action}" data-id="${e(id)}">${label}</button>`;
  function sections(c) {
    const q = key => (ui.search[key] || '').trim();
    const match = (key, ...values) => !q(key) || values.some(v => String(v ?? '').includes(q(key)));
    const active = c.status !== 'غیرفعال' && can.write();
    const payments = store.payments.filter(p => p.customerId === c.id);
    const list = [
      {key: 'contacts', title: 'افراد رابط', group: 'ارتباطات', icon: 'users', create: active && can.activity(), rows: store.contacts.filter(x => x.customerId === c.id && x.active && match('contacts', x.name, x.role, x.mobile)).map(x => row(e(x.name), e(x.role) + ' · <bdi dir="ltr">' + e(x.mobile) + '</bdi>', x.primary ? 'رابط اصلی' : '', 'success', '', '', active && can.activity() ? [act('قطع ارتباط (غیرفعال)', 'af-contact-off', x.id, true)] : []))},
      {key: 'opportunities', title: 'فرصت‌های فروش', group: 'فروش', icon: 'target', create: active && can.activity(), totals: oppsOf(c).filter(o => !['برنده', 'باخته'].includes(o.stage)).reduce((s, o) => s + o.value, 0), totalsLabel: 'ارزش فرصت‌های باز', rows: oppsOf(c).filter(o => match('opportunities', o.name, o.code)).map(o => row(link('opportunities', o.name), e(o.code) + ' · احتمال ' + amount(o.probability) + '٪', o.stage, 'info', money(o.value, o.currency || 'IRR'), '', active && !['برنده', 'باخته'].includes(o.stage) ? [act('ایجاد پیش‌فاکتور', 'af-quote', o.id)] : []))},
      {key: 'quotes', title: 'پیش‌فاکتورها', group: 'فروش', icon: 'file', rows: store.quotes.filter(x => x.customer === c.name && match('quotes', x.code)).map(x => row(e(x.code), e(x.opportunity), x.status, 'info', money(x.amount * (1 - x.discount / 100)), e(x.valid)))},
      {key: 'leads', title: 'سرنخ‌ها', group: 'فروش', icon: 'lead', create: active && can.activity(), linkable: active && can.activity(), rows: store.leads.filter(x => x.customerId === c.id && match('leads', x.name, x.code)).map(x => row(e(x.name), e(x.code) + ' · ' + e(x.source), x.status, 'info', '', '', active && x.status !== 'تبدیل‌شده' ? [act('قطع ارتباط', 'af-lead-unlink', x.id, true)] : []))},
      {key: 'orders', title: 'سفارش‌های فروش', group: 'فروش', icon: 'inbox', rows: (store.orders || []).filter(x => x.customer === c.name).map(x => row(e(x.code), 'مرجع ERP ' + e(x.erp || '—'), x.status, 'info', money(x.amount), ''))},
      {key: 'payments', title: 'پرداخت‌ها و دریافت‌ها', group: 'مالی', icon: 'check', create: active && can.payment(), sums: sum(payments.filter(p => p.status === 'تأییدشده')), totalsLabel: 'خالص تأییدشده (هر ارز جدا)', note: 'فقط «تأییدشده» در جمع است؛ ثبت‌کننده نمی‌تواند پرداخت خود را تأیید کند.',
        rows: payments.filter(p => match('payments', p.reference, p.method)).sort((a, b) => b.date.localeCompare(a.date)).map(p => row(e(p.direction + ' · ' + p.method), e(p.reference || '') + (p.note ? ' · ' + e(p.note) : ''), p.status, p.status === 'تأییدشده' ? 'success' : p.status === 'ثبت‌شده' ? 'warning' : 'neutral', money(p.amount, p.currency), jalali(p.date),
          active && can.approve() ? [p.status === 'ثبت‌شده' && p.by !== me() ? act('تأیید', 'af-pay-approve', p.id) : '', p.status === 'ثبت‌شده' ? act('لغو', 'af-pay-cancel', p.id, true) : '', p.status === 'تأییدشده' ? act('ثبت برگشتی', 'af-pay-return', p.id, true) : ''].filter(Boolean) : []))},
      {key: 'documents', title: 'اسناد و پیوست‌ها', group: 'اسناد', icon: 'file', create: active && can.note(), linkable: active && can.note(), note: 'قطع ارتباط فقط پیوند را برمی‌دارد و فایل در حساب‌های دیگر باقی می‌ماند.', rows: store.documents.filter(d => d.links.includes(c.id) && match('documents', d.name)).map(d => row(e(d.name), e(d.size + ' · ' + d.by), '', '', '', jalali(d.at), active && can.note() ? [act('قطع ارتباط', 'af-doc-unlink', d.id, true)] : []))},
      {key: 'campaigns', title: 'کمپین‌ها', group: 'بازاریابی', icon: 'bell', linkable: active && can.manage(), rows: store.campaignMembers.filter(m => m.customerId === c.id).map(m => { const k = store.campaigns.find(x => x.id === m.campaignId); return row(e(k?.name || '—'), e(k?.type || ''), m.status, 'info', '', '', active && can.manage() ? [act('خروج از کمپین', 'af-campaign-leave', m.id, true)] : []); })},
      {key: 'service', title: 'درخواست‌های خدمات', group: 'خدمات', icon: 'shield', create: active && can.write(), rows: store.serviceCases.filter(s => s.customerId === c.id).map(s => row(e(s.subject), e(s.code) + ' · اولویت ' + e(s.priority), s.status, s.status === 'باز' ? (new Date(s.due) < new Date() ? 'danger' : 'info') : 'success', '', jalali(s.opened)))},
      {key: 'hierarchy', title: 'حساب مادر و زیرمجموعه', group: 'ساختار', icon: 'building', linkable: active && can.manage(), rows: [...(c.parentId ? [store.customers.find(x => x.id === c.parentId)].filter(Boolean).map(p => row(link('customer', p.name, p.id), e(p.code), 'حساب مادر', 'info', '', '', can.manage() ? [act('جدا کردن', 'af-parent-unlink', c.id, true)] : [])) : []),
        ...store.customers.filter(x => x.parentId === c.id).map(x => row(link('customer', x.name, x.id), e(x.code), 'زیرمجموعه', 'neutral', '', '', can.manage() ? [act('جدا کردن زیرمجموعه', 'af-parent-unlink', x.id, true)] : []))]}
    ];
    const groups = [...new Set(list.map(s => s.group))];
    return `<div class="related-tools panel"><span>${amount(list.reduce((n, s) => n + s.rows.length, 0))} رکورد مرتبط در ${amount(list.length)} بخش</span><div class="related-tools__buttons"><button class="link-button" data-u-action="af-open-all" data-id="1">باز کردن همه</button><button class="link-button" data-u-action="af-open-all" data-id="0">بستن همه</button></div></div>` +
      groups.map(g => `<div class="section-group"><h3 class="section-group__title">${g}</h3>${list.filter(s => s.group === g).map(s => `<details class="acc-section" id="sec-${s.key}" ${ui.open.has(s.key) ? 'open' : ''}><summary data-u-action="af-toggle" data-id="${s.key}"><span class="acc-section__icon">${icon(s.icon)}</span><span class="acc-section__title">${s.title}</span><b class="acc-section__count">${amount(s.rows.length)}</b><span class="acc-section__tools">${s.create ? `<button class="tool-button" data-u-action="af-create" data-id="${s.key}" title="ایجاد در ${s.title}">${icon('plus')}</button>` : ''}${s.linkable ? `<button class="tool-button tool-button--link" data-u-action="af-link" data-id="${s.key}" title="اتصال رکورد موجود">🔗</button>` : ''}</span></summary>
        <div class="acc-section__body"><form class="sec-filter" data-u-form="af-search" data-id="${s.key}"><input name="q" value="${e(q(s.key))}" placeholder="جست‌وجو در ${s.title} (Enter)"></form>${s.note ? `<p class="sec-note">${s.note}</p>` : ''}${s.sums?.length ? `<div class="sec-totals"><span>${s.totalsLabel}:</span>${s.sums.map(([cur, n]) => `<b dir="ltr">${money(n, cur)}</b>`).join('')}</div>` : s.totals ? `<div class="sec-totals"><span>${s.totalsLabel}:</span><b dir="ltr">${money(s.totals)}</b></div>` : ''}
        ${s.rows.length ? `<ul class="rec-list">${s.rows.join('')}</ul>` : `<p class="state-empty">${q(s.key) ? 'موردی با این جست‌وجو پیدا نشد.' : 'موردی ثبت نشده است.'}</p>`}</div></details>`).join('')}</div>`).join('') +
      '<p class="u-period">در نسخهٔ MVC، ۲۷ بخش (قرارداد، پروژه، تضمین، حساب بانکی، نظرسنجی، مشارکت، رزرو/هدیه/نمونه، شعب و …) با بارگذاری تدریجی و صفحه‌بندی موجود است.</p>';
  }
  function timeline(c, historyOnly) {
    const items = historyOnly
      ? store.accountHistory.filter(h => h.customerId === c.id).map(h => ({at: h.at, title: h.title, by: h.by}))
      : [...activitiesOf(c).filter(a => a.status !== 'planned').map(a => ({at: a.start, title: `${label[a.type]} ${a.status === 'done' ? 'انجام شد' : 'لغو شد'}: ${a.subject}`, text: a.outcome || a.reason, by: a.owner})),
         ...store.notes.filter(n => n.customerId === c.id && n.visibility === 'عمومی').map(n => ({at: n.at, title: 'یادداشت: ' + n.title, text: n.body, by: n.author})),
         ...store.payments.filter(p => p.customerId === c.id).map(p => ({at: p.date + 'T09:00', title: `${p.direction} ${money(p.amount, p.currency)}: ${p.status}`, by: p.by}))];
    items.sort((a, b) => b.at.localeCompare(a.at));
    return items.length ? `<ol class="timeline-list">${items.map(x => `<li><strong>${e(x.title)}</strong>${x.text ? `<p>${e(x.text)}</p>` : ''}<small>${when(x.at)} · ${e(x.by || 'سیستم')}</small></li>`).join('')}</ol>` : '<p class="state-empty">رویدادی ثبت نشده است.</p>';
  }
  // «پیگیری» (v1.9.2): planned activities and the next steps of open opportunities and leads, grouped by due time.
  function followUps(c) {
    const endOfToday = (() => { const d = new Date(); d.setHours(24, 0, 0, 0); return d.getTime(); })();
    const items = [
      ...activitiesOf(c).filter(a => a.status === 'planned').map(a => ({source: label[a.type], title: a.subject, detail: a.owner + (a.contact ? ' · رابط: ' + a.contact : ''), due: Date.parse(a.start), late: overdue(a), ic: {call: 'phone', meeting: 'calendar'}[a.type] || 'task', activity: a.id})),
      ...oppsOf(c).filter(o => !['برنده', 'باخته'].includes(o.stage)).map(o => ({source: 'فرصت فروش', title: o.next || 'اقدام بعدی تعیین نشده', detail: o.name, due: o.nextAt ? Date.parse(o.nextAt) : null, late: o.nextAt ? Date.parse(o.nextAt) < Date.now() : false, ic: 'target'})),
      ...store.leads.filter(l => l.customerId === c.id && !['تبدیل‌شده', 'ردشده', 'تکراری', 'نامعتبر'].includes(l.status)).map(l => ({source: 'سرنخ', title: l.next || 'تماس اولیه', detail: l.name + ' · ' + l.code, due: l.dueAt ? Date.parse(l.dueAt) : null, late: l.dueAt ? Date.parse(l.dueAt) < Date.now() : false, ic: 'lead'}))];
    const dated = items.filter(x => x.due).sort((a, b) => a.due - b.due);
    const groups = [['معوق', dated.filter(x => x.late)], ['امروز', dated.filter(x => !x.late && x.due < endOfToday)], ['پیش رو', dated.filter(x => !x.late && x.due >= endOfToday)], ['بدون زمان مشخص', items.filter(x => !x.due)]];
    const dueText = x => !x.due ? ['زمان تعیین نشده', 'muted'] : x.late ? ['معوق · ' + when(new Date(x.due)), 'danger'] : x.due < endOfToday ? ['امروز، ' + jTime.format(new Date(x.due)), 'success'] : [when(new Date(x.due)), 'info'];
    const late = groups[0][1].length, today = groups[1][1].length;
    return `<div class="follow-head"><span class="follow-head__counts">${late ? `<span class="pill pill--danger pill--sm">${icon('alert')}${amount(late)} معوق</span>` : ''}${today ? `<span class="pill pill--success pill--sm">${icon('calendar')}${amount(today)} امروز</span>` : ''}${items.length ? '' : '<span class="muted">موردی برای پیگیری نیست</span>'}</span>
      ${c.status !== 'غیرفعال' && can.activity() ? `<button type="button" class="link-button" data-u-action="af-new-task" data-id="${c.id}">${icon('plus')}پیگیری جدید</button>` : ''}</div>
      ${items.length ? '' : '<p class="state-empty">برای این حساب تماس، جلسه، وظیفه یا اقدام بعدی بازی ثبت نشده است.</p>'}
      ${groups.filter(([, list]) => list.length).map(([title, list]) => `<section class="follow-group"><h4>${title} <b>${amount(list.length)}</b></h4>${list.map(x => { const [t, tone] = dueText(x); return `<article class="follow ${x.late ? 'follow--overdue' : ''}"><span class="follow__icon">${icon(x.ic)}</span><div class="follow__body"><span class="follow__title">${e(x.title)}</span><small><b>${e(x.source)}</b> · ${e(x.detail)}</small><span class="due due--${tone}">${icon(tone === 'danger' ? 'alert' : 'calendar')}${e(t)}</span></div>${x.activity && can.activity() ? `<button type="button" class="chip-action chip-action--primary" data-u-action="af-complete" data-id="${x.activity}">${icon('check')}انجام شد</button>` : ''}</article>`; }).join('')}</section>`).join('')}`;
  }
  function overview(c) {
    // The account card above already shows the account information; the overview summarises the account's records.
    const counts = [['افراد رابط', 'users', store.contacts.filter(x => x.customerId === c.id && x.active).length], ['فرصت‌های فروش', 'target', oppsOf(c).length],
      ['پیش‌فاکتورها', 'file', store.quotes.filter(x => x.customer === c.name).length], ['سرنخ‌ها', 'lead', store.leads.filter(x => x.customerId === c.id).length],
      ['پرداخت‌ها و دریافت‌ها', 'check', store.payments.filter(p => p.customerId === c.id).length], ['اسناد و پیوست‌ها', 'file', store.documents.filter(d => d.links.includes(c.id)).length],
      ['درخواست‌های خدمات', 'shield', store.serviceCases.filter(x => x.customerId === c.id).length]].filter(([, , n]) => n > 0);
    const profile = c.profile ? Object.entries(c.profile).filter(([, v]) => v) : [];
    return `<section class="panel u-panel"><h2>خلاصهٔ رکوردهای مرتبط</h2><div class="overview-sections">${counts.length ? counts.map(([t, ic, n]) => `<button type="button" class="overview-sections__item" data-u-action="af-tab" data-id="related">${icon(ic)}<span>${t}</span><b>${amount(n)}</b></button>`).join('') : '<p class="empty-inline">هنوز رکورد مرتبطی ثبت نشده است.</p>'}</div></section>
      ${profile.length ? `<details class="panel info-panel"><summary class="info-panel__summary"><span><strong>${c.type === 'حقیقی' ? 'اطلاعات شخص' : 'اطلاعات شرکت و نماینده'}</strong><small>از فرم تعریف مشتری</small></span></summary><dl class="info-grid">${profile.map(([k, v]) => `<div><dt>${e(k)}</dt><dd>${e(v)}</dd></div>`).join('')}</dl></details>` : ''}`;
  }
  function accountView() {
    const c = account();
    if (!c) return header('حساب پیدا نشد', '', link('customers', 'فهرست مشتریان'));
    const active = c.status !== 'غیرفعال';
    const tab = (key, text) => `<button class="tabbar__tab ${ui.tab === key ? 'is-active' : ''}" data-u-action="af-tab" data-id="${key}">${text}</button>`;
    const side = (key, text) => `<button class="tabbar__tab ${ui.side === key ? 'is-active' : ''}" data-u-action="af-side" data-id="${key}">${text}</button>`;
    const contact = (ic, label, value, href) => `<li><svg aria-hidden="true"><use href="#i-${ic}"></use></svg><span class="sr-only">${label}</span>${value ? (href ? `<a href="${e(href)}" dir="ltr" ${ic === 'globe' ? 'target="_blank" rel="noopener noreferrer"' : ''}>${e(value)}</a>` : `<span>${e(value)}</span>`) : '<span>—</span>'}</li>`;
    const tone = c.status === 'فعال' ? 'success' : c.status === 'غیرفعال' ? 'neutral' : 'warning';
    const site = c.website || (c.email && c.email.includes('@') ? 'www.' + c.email.split('@')[1] : '');
    return `<header class="account-head"><section class="account-card" aria-label="اطلاعات حساب"><div class="account-card__identity"><span class="account-card__mark" aria-hidden="true"><span class="account-card__icon">${icon('building')}</span></span>
      <div class="account-card__text"><div class="account-card__title"><h1>${e(c.name)}</h1><span class="account-card__status account-card__status--${tone}">${e(c.status)}</span></div>
      <dl class="account-card__facts"><div><dt>کد حساب:</dt><dd><bdi dir="ltr">${e(c.code)}</bdi></dd></div><div><dt>صنعت:</dt><dd>${e(c.industry || c.profile?.['نوع فعالیت'] || (c.segment ? 'بخش ' + c.segment : '—'))}</dd></div><div><dt>منطقه:</dt><dd>${e(c.city || '—')}</dd></div></dl></div></div>
      <ul class="account-card__contacts">${contact('phone', 'تلفن:', c.phone, c.phone ? 'tel:' + digits(c.phone) : '')}${contact('mail', 'ایمیل:', c.email, c.email ? 'mailto:' + c.email : '')}${contact('globe', 'وب‌سایت:', site, site ? 'https://' + site.replace(/^https?:\/\//, '') : '')}${contact('pin', 'نشانی:', c.address || [c.city, c.branch && 'شعبه ' + c.branch].filter(Boolean).join('، '), '')}</ul></section>
      <div class="account-head__bar"><div class="account-head__meta"><span class="badge badge--info">${e(c.relationship)}</span><span class="badge badge--neutral">${e(c.type)}</span><span><b>مالک:</b> ${e(c.owner)}</span><span><b>شعبه:</b> ${e(c.branch)}</span>${(c.tags || []).map(t => `<span class="chip">#${e(t)}</span>`).join('')}</div>
      <div class="account-head__actions">${can.manage() && active ? button('af-classify', 'نوع رابطه و برچسب', c.id) : ''}${can.manage() ? button('af-status', active ? 'غیرفعال‌سازی' : 'فعال‌سازی', c.id) : ''}${link('customers', 'بازگشت به فهرست')}</div></div></header>
      ${active ? '' : '<p class="account-banner">این حساب غیرفعال است: سوابق می‌ماند ولی ثبت رکورد جدید بسته است. حذف حساب دارای سابقه مجاز نیست.</p>'}
      ${kpis(c)}
      <div class="account-layout"><aside class="account-side">${quick(c)}<section class="panel side-tabs"><div class="tabbar">${side('followups', 'پیگیری')}${side('notes', 'یادداشت‌ها')}</div><div class="side-tabs__body">${ui.side === 'notes' ? notesPanel(c) : followUps(c)}</div></section></aside>
      <div class="account-main"><div class="tabbar tabbar--main">${tab('overview', 'نمای کلی')}${tab('activities', 'فعالیت‌ها و تعاملات')}${tab('related', 'رکوردهای مرتبط')}${tab('history', 'تاریخچه تغییرات')}</div>
      ${ui.tab === 'related' ? sections(c) : ui.tab === 'activities' ? `<section class="panel u-panel activity-main"><h2>فعالیت‌ها</h2>${activityPanel(c)}</section><details class="panel u-panel timeline-panel--collapsible"><summary><h2>خط زمانی تعاملات</h2></summary>${timeline(c, false)}</details>` : ui.tab === 'history' ? `<section class="panel u-panel"><h2>تاریخچه تغییرات</h2>${timeline(c, true)}</section>` : overview(c)}</div></div>`;
  }

  // ── Forms ──
  const contactOptions = c => [['', '— بدون رابط —'], ...store.contacts.filter(x => x.customerId === c.id && x.active).map(x => [x.name, x.name])];
  const ownerOptions = () => (can.manage() ? Object.values(names).slice(0, 2) : [me()]).map(x => [x, x]);
  const ctxLine = c => `<p class="form-context">حساب: <b>${e(c.name)}</b></p>${opField()}`;
  function activityForm(c, type) {
    const opps = oppsOf(c).filter(o => !['برنده', 'باخته'].includes(o.stage));
    showForm(type === 'call' ? 'برنامه‌ریزی تماس' : type === 'meeting' ? 'برنامه‌ریزی جلسه' : 'ایجاد وظیفه', ctxLine(c) +
      field(type === 'task' ? 'عنوان وظیفه *' : 'موضوع *', '<input name="subject" required maxlength="200">', true) +
      (type !== 'meeting' ? field('فرد رابط', `<select name="contact">${opt(contactOptions(c), store.contacts.find(x => x.customerId === c.id && x.primary)?.name)}</select>`) : '') +
      field('مسئول *', `<select name="owner">${opt(ownerOptions(), me())}</select>`) +
      field(type === 'task' ? 'مهلت *' : 'شروع *', `<input type="datetime-local" name="start" required value="${type === 'task' ? shift(1, 10) : shift(0, new Date().getHours() + 1)}">`) +
      (type === 'call' ? field('مدت (دقیقه)', '<input type="number" name="duration" min="1" max="600" value="15">') + field('جهت', `<select name="direction">${opt([['خروجی', 'خروجی'], ['ورودی', 'ورودی']])}</select>`) : '') +
      (type === 'meeting' ? field('پایان *', `<input type="datetime-local" name="end" required value="${shift(0, new Date().getHours() + 2)}">`) + field('مکان یا پیوند', '<input name="location">', true) +
        `<fieldset class="u-wide pick-list"><legend>شرکت‌کنندگان</legend>${store.contacts.filter(x => x.customerId === c.id && x.active).map(x => `<label class="consent-row"><input type="checkbox" name="p_${x.id}" value="${e(x.name)}"> ${e(x.name)}</label>`).join('')}</fieldset>` : '') +
      (type === 'task' ? field('اولویت', `<select name="priority">${opt([['عادی', 'عادی'], ['بالا', 'بالا'], ['فوری', 'فوری'], ['کم', 'کم']], 'عادی')}</select>`) : field('یادآوری', `<select name="reminder">${opt([['', 'بدون یادآوری'], ['15', '۱۵ دقیقه قبل'], ['30', '۳۰ دقیقه قبل'], ['60', '۱ ساعت قبل']], '15')}</select>`)) +
      field('مرتبط با فرصت', `<select name="opportunityId">${opt([['', '— فقط حساب —'], ...opps.map(o => [o.id, o.code + ' · ' + o.name])])}</select>`, true) +
      field(type === 'meeting' ? 'دستور جلسه' : 'توضیحات', '<textarea name="description" rows="3"></textarea>', true) +
      (type !== 'task' ? `<fieldset class="u-wide pick-list"><legend>${type === 'call' ? 'این تماس انجام شده است؟' : 'این جلسه برگزار شده است؟'}</legend><label class="consent-row"><input type="checkbox" name="done" value="1"> بله، نتیجه را همین حالا ثبت کن</label>${type === 'call' ? field('نتیجهٔ تماس', `<select name="result">${opt([['', '—'], ['پاسخ داد', 'پاسخ داد'], ['پاسخ نداد', 'پاسخ نداد'], ['مشغول', 'مشغول'], ['پیغام گذاشته شد', 'پیغام گذاشته شد']])}</select>`) : ''}${field('شرح نتیجه', '<textarea name="outcome" rows="2"></textarea>', true)}</fieldset>` : ''),
      'af-activity', type + ':' + c.id);
  }
  function stepForm(a, step) {
    const c = store.customers.find(x => x.id === a.customerId);
    const head = `<p class="form-context">${label[a.type]}: <b>${e(a.subject)}</b> · ${when(a.start)}</p>${opField()}`;
    if (step === 'complete') showForm('ثبت نتیجه', head + (a.type === 'call' ? field('نتیجهٔ تماس *', `<select name="result" required>${opt([['', '—'], ['پاسخ داد', 'پاسخ داد'], ['پاسخ نداد', 'پاسخ نداد'], ['مشغول', 'مشغول'], ['پیغام گذاشته شد', 'پیغام گذاشته شد']])}</select>`) : '') +
      field(a.type === 'task' ? 'توضیح انجام کار' : 'شرح نتیجه *', `<textarea name="outcome" rows="3" ${a.type === 'task' ? '' : 'required'}></textarea>`, true) +
      `<fieldset class="u-wide pick-list"><legend>اقدام بعدی (اختیاری)</legend>${field('نوع', `<select name="nextType">${opt([['', '— بدون اقدام بعدی —'], ['call', 'تماس'], ['meeting', 'جلسه'], ['task', 'وظیفه']])}</select>`)}${field('موضوع', '<input name="nextSubject">')}${field('زمان', `<input type="datetime-local" name="nextStart" value="${shift(2, 10)}">`)}</fieldset>`, 'af-complete', a.id);
    else if (step === 'reschedule') showForm('زمان جدید', head + field('شروع جدید *', `<input type="datetime-local" name="start" required value="${a.start}">`) + (a.type === 'meeting' ? field('پایان', `<input type="datetime-local" name="end" value="${a.end || ''}">`) : ''), 'af-reschedule', a.id);
    else showForm('لغو فعالیت', head + field('دلیل لغو *', '<textarea name="reason" rows="3" required></textarea>', true) + '<p class="u-period">فعالیت حذف نمی‌شود؛ با وضعیت «لغوشده» در سوابق می‌ماند.</p>', 'af-cancel', a.id);
    return c;
  }
  function createForm(c, key) {
    const forms = {
      opportunities: () => showForm('ایجاد فرصت فروش', ctxLine(c) + field('عنوان فرصت *', '<input name="name" required>', true) + field('فرد رابط', `<select name="contact">${opt(contactOptions(c))}</select>`) +
        field('مرحله', `<select name="stage">${opt([['شناسایی', 'شناسایی'], ['کشف', 'نیازسنجی'], ['ارائه راهکار', 'ارائه راهکار'], ['مذاکره', 'مذاکره']], 'شناسایی')}</select>`) + field('مبلغ *', '<input name="value" required inputmode="numeric" placeholder="2,500,000,000">') +
        field('واحد پول', `<select name="currency">${opt([['IRR', 'ریال (IRR)'], ['USD', 'دلار (USD)'], ['EUR', 'یورو (EUR)']], 'IRR')}</select>`) + field('احتمال (٪)', '<input type="number" name="probability" min="1" max="99" value="20">') +
        field('تاریخ پیش‌بینی بسته‌شدن *', `<input type="date" name="expectedClose" required value="${shift(30).slice(0, 10)}">`) + field('مسئول', `<select name="owner">${opt(ownerOptions(), me())}</select>`) + field('اقدام بعدی *', '<input name="next" required>', true), 'af-opportunity', c.id),
      leads: () => showForm('ایجاد سرنخ برای این حساب', ctxLine(c) + field('عنوان سرنخ / نیاز *', '<input name="name" required>', true) + field('فرد رابط', `<select name="contact">${opt(contactOptions(c))}</select>`) +
        field('منبع', `<select name="source">${opt(['مشتری فعلی', 'تماس ورودی', 'وب‌سایت', 'نمایشگاه', 'معرفی'].map(x => [x, x]), 'مشتری فعلی')}</select>`) + field('مسئول پیگیری', `<select name="owner">${opt(ownerOptions(), me())}</select>`) + '<p class="u-period">سرنخ به همین حساب متصل می‌ماند.</p>', 'af-lead', c.id),
      payments: () => showForm('ثبت پرداخت / دریافت', ctxLine(c) + field('نوع', `<select name="direction">${opt([['دریافت', 'دریافت از حساب'], ['پرداخت', 'پرداخت به حساب']], 'دریافت')}</select>`) +
        field('روش', `<select name="method">${opt(['حواله بانکی', 'چک', 'نقد', 'کارتخوان'].map(x => [x, x]), 'حواله بانکی')}</select>`) + field('مبلغ *', '<input name="amount" required inputmode="numeric">') +
        field('واحد پول', `<select name="currency">${opt([['IRR', 'ریال (IRR)'], ['USD', 'دلار (USD)'], ['EUR', 'یورو (EUR)']], 'IRR')}</select>`) + field('تاریخ *', `<input type="date" name="date" required value="${shift(0).slice(0, 10)}">`) +
        field('شماره چک / کد پیگیری', '<input name="reference">') + '<p class="u-period">برای چک و حواله، شماره الزامی است. ثبت با وضعیت «ثبت‌شده» انجام می‌شود.</p>', 'af-payment', c.id),
      contacts: () => showForm('تعریف فرد رابط', ctxLine(c) + field('نام و نام خانوادگی *', '<input name="name" required>') + field('سمت', '<input name="role">') + field('همراه', '<input name="mobile" inputmode="tel" placeholder="09xxxxxxxxx">') + field('ایمیل', '<input name="email" type="email">') + '<label class="consent-row"><input type="checkbox" name="primary" value="1"> رابط اصلی</label>', 'af-contact', c.id),
      documents: () => showForm('بارگذاری سند', ctxLine(c) + field('نام سند *', '<input name="name" required placeholder="مثلاً قرارداد.pdf">', true) + '<p class="u-period">در پیش‌نمایش فقط نام سند ثبت می‌شود؛ بارگذاری فایل واقعی در نسخهٔ MVC است.</p>', 'af-document', c.id),
      service: () => showForm('ثبت درخواست خدمات', ctxLine(c) + field('موضوع *', '<input name="subject" required>', true) + field('اولویت', `<select name="priority">${opt(['عادی', 'بالا', 'بحرانی'].map(x => [x, x]), 'عادی')}</select>`), 'sv-new', c.id)
    };
    (forms[key] || (() => toast('ایجاد این بخش در پیش‌نمایش فعال نیست.', 'warning')))();
  }
  function linkForm(c, key) {
    const sources = {
      leads: store.leads.filter(l => !l.customerId && l.status !== 'تبدیل‌شده').map(l => [l.id, l.code + ' · ' + l.name]),
      documents: store.documents.filter(d => !d.links.includes(c.id)).map(d => [d.id, d.name]),
      campaigns: store.campaigns.filter(k => !store.campaignMembers.some(m => m.campaignId === k.id && m.customerId === c.id)).map(k => [k.id, k.name + ' · ' + k.type]),
      hierarchy: store.customers.filter(x => x.id !== c.id && !x.parentId && x.id !== c.parentId).map(x => [x.id, x.name])
    }[key] || [];
    if (!sources.length) { toast('رکورد قابل اتصالی وجود ندارد.', 'warning'); return; }
    showForm('اتصال رکورد موجود', ctxLine(c) + field('رکورد *', `<select name="recordId" required>${opt(sources)}</select>`, true) + (key === 'hierarchy' ? field('نوع ارتباط', `<select name="role">${opt([['child', 'حساب انتخابی زیرمجموعهٔ این حساب شود'], ['parent', 'حساب انتخابی مادر این حساب شود']], 'child')}</select>`, true) : '') + '<p class="u-period">اتصال، رکورد را کپی نمی‌کند؛ همان رکورد به این حساب هم وصل می‌شود.</p>', 'af-link', key + ':' + c.id);
  }

  // ── Customer form (v1.2): individual / legal fields and a contact person ──
  const digits = v => String(v || '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d)).replace(/\D/g, '');
  const validNationalCode = v => { const s = digits(v); if (!/^\d{10}$/.test(s) || /^(\d)\1{9}$/.test(s)) return false; const sum = [...s.slice(0, 9)].reduce((t, d, i) => t + Number(d) * (10 - i), 0) % 11; return Number(s[9]) === (sum < 2 ? sum : 11 - sum); };
  const validLegalId = v => { const s = digits(v); if (!/^\d{11}$/.test(s)) return false; const k = Number(s[9]) + 2, w = [29, 27, 23, 19, 17]; const sum = [...s.slice(0, 10)].reduce((t, d, i) => t + (Number(d) + k) * w[i % 5], 0) % 11; return Number(s[10]) === (sum === 10 ? 0 : sum); };
  function customerForm(kind = 'حقوقی') {
    const legal = kind === 'حقوقی';
    showForm('تعریف مشتری جدید', opField() + `<p class="form-context">نوع شخص: <b>${legal ? 'حقوقی' : 'حقیقی'}</b> ${button('af-customer-kind', legal ? 'تغییر به حقیقی' : 'تغییر به حقوقی', legal ? 'حقیقی' : 'حقوقی')}</p>` +
      (legal ? field('نام شرکت *', '<input name="name" required>', true) + field('شناسه ملی (۱۱ رقم) *', '<input name="nationalId" required inputmode="numeric">') + field('کد اقتصادی', '<input name="economicCode" inputmode="numeric">') + field('شماره ثبت', '<input name="registration" inputmode="numeric">') + field('نماینده', '<input name="representative">')
        : field('نام *', '<input name="firstName" required>') + field('نام خانوادگی *', '<input name="lastName" required>') + field('کد ملی (۱۰ رقم) *', '<input name="nationalId" required inputmode="numeric">') + field('تاریخ تولد (شمسی)', '<input name="birthDate" placeholder="1368/04/10">')) +
      field('تلفن همراه *', '<input name="mobile" required inputmode="tel" placeholder="09xxxxxxxxx">') + field('شهر', '<input name="city">') + field('شعبه', `<select name="branch">${opt(['مرکزی', 'اصفهان', 'اهواز', 'شیراز', 'تبریز'].map(x => [x, x]), 'مرکزی')}</select>`) +
      `<fieldset class="u-wide pick-list"><legend>فرد رابط</legend>${field('نام و نام خانوادگی', '<input name="contactName">')}${field('سمت', '<input name="contactRole">')}${field('همراه', '<input name="contactMobile" inputmode="tel">')}</fieldset>`, 'af-customer', kind);
  }

  // ── Other pages ──
  function versionView() {
    const releases = ctx.releases || [];
    return header('نسخه و تغییرات', 'فهرست نسخه‌های ارقام CRM؛ تازه‌ترین نسخه در بالا.') +
      `<section class="version-hero panel"><span class="version-hero__mark">آ</span><div><small>نسخهٔ جاری</small><strong dir="ltr">v${e(ctx.version)}</strong><span>${releases[0] ? 'انتشار ' + e(releases[0].date) : ''}</span></div></section>
      <ol class="release-list">${releases.map((r, i) => `<li class="release ${i === 0 ? 'release--current' : ''}"><div class="release__head"><span class="release__version" dir="ltr">v${e(r.version)}</span><strong>${e(r.title)}</strong>${i === 0 ? '<span class="badge badge--success"><span></span>نسخهٔ جاری</span>' : ''}<time>${e(r.date)}</time></div><ul>${r.changes.map(x => `<li>${e(x)}</li>`).join('')}</ul></li>`).join('')}</ol>`;
  }
  function serviceView() {
    const cases = store.serviceCases.filter(s => ctx.visible(store.customers.find(c => c.id === s.customerId) || {}));
    const sla = s => s.status !== 'باز' ? ['success', 'بسته'] : new Date(s.due) < new Date() ? ['danger', 'نقض SLA'] : ['info', 'در مهلت'];
    const open = cases.filter(s => s.status === 'باز'), closed = cases.filter(s => s.csat);
    return header('خدمات و SLA', 'درخواست‌های خدمات، مهلت پاسخ و رضایت مشتری؛ شکایت پرتال نماینده هم اینجا پرونده می‌شود.', can.write() ? button('sv-create', 'ثبت درخواست') : '') +
      `<section class="u-grid"><article class="u-card"><span>پرونده‌های باز</span><strong>${amount(open.length)}</strong></article><article class="u-card"><span>نقض SLA</span><strong>${amount(open.filter(s => new Date(s.due) < new Date()).length)}</strong></article><article class="u-card"><span>میانگین رضایت</span><strong>${closed.length ? amount(Math.round(10 * closed.reduce((t, s) => t + s.csat, 0) / closed.length) / 10) + ' از ۵' : '—'}</strong></article></section>` +
      `<section class="panel u-panel">${ctx.table(['پرونده', 'مشتری', 'اولویت', 'مهلت', 'وضعیت SLA', 'منبع', 'عملیات'], cases.map(s => { const c = store.customers.find(x => x.id === s.customerId); const st = sla(s); return `<tr><td>${e(s.code)}<small class="cell-sub">${e(s.subject)}</small></td><td>${c ? link('customer', c.name, c.id) : '—'}</td><td>${e(s.priority)}</td><td>${when(s.due)}</td><td><span class="badge badge--${st[0]}"><span></span>${st[1]}</span></td><td>${e(s.source || '')}</td><td>${s.status === 'باز' && can.write() ? button('sv-close', 'بستن با رضایت', s.id) : s.csat ? 'رضایت ' + amount(s.csat) + '/۵' : ''}</td></tr>`; }).join(''))}</section>`;
  }
  function commissionView() {
    const dealers = store.dealers.filter(ctx.visible);
    const rate = d => { const p = d.target ? d.actual / d.target : 0; return p >= 1 ? 3 : p >= .8 ? 2 : 1; };
    const ranked = [...dealers].sort((a, b) => (b.target ? b.actual / b.target : 0) - (a.target ? a.actual / a.target : 0));
    return header('کمیسیون و رتبه‌بندی نمایندگان', 'دورهٔ ماه شمسی جاری · پلکان: زیر ۸۰٪ هدف ۱٪، ۸۰ تا ۱۰۰٪ دو درصد، بالای هدف ۳٪ فروش.', ['channel.manager', 'sales.manager'].includes(user()) ? button('cm-calc', 'محاسبهٔ کمیسیون دوره') : '') +
      `<section class="panel u-panel"><h2>صورت‌حساب کمیسیون</h2>${ctx.table(['نماینده', 'فروش دوره', 'تحقق هدف', 'نرخ', 'کمیسیون', 'وضعیت', 'عملیات'], store.commissions.map(m => { const d = store.dealers.find(x => x.id === m.dealerId); return `<tr><td>${e(d?.name || '—')}</td><td>${money(m.sales)}</td><td>${amount(m.achievement)}٪</td><td>${amount(m.rate)}٪</td><td>${money(m.amount)}</td><td>${ctx.statusBadge(m.status)}</td><td>${m.status === 'در انتظار تأیید' && user() === 'finance.manager' ? button('cm-approve', 'تأیید و ارسال به حسابداری', m.id) : m.sent ? 'ارسال‌شده به حسابداری (نمایشی)' : ''}</td></tr>`; }).join(''))}</section>` +
      `<section class="panel u-panel"><h2>رتبه‌بندی</h2>${ctx.table(['رتبه', 'نماینده', 'هدف', 'عملکرد', 'تحقق', 'نرخ پلکان'], ranked.map((d, i) => `<tr><td>${amount(i + 1)}</td><td>${e(d.name)}</td><td>${money(d.target)}</td><td>${money(d.actual)}</td><td>${amount(d.target ? Math.round(100 * d.actual / d.target) : 0)}٪</td><td>${amount(rate(d))}٪</td></tr>`).join(''))}<p class="u-period">تضمین‌ها، آموزش‌ها و تقسیم اعتبار فروش در نسخهٔ MVC موجود است.</p></section>`;
  }
  function notificationView() {
    const mine = store.notifications.filter(n => can.manage() || user() === 'finance.manager' || n.to === me()).sort((a, b) => b.at.localeCompare(a.at));
    return header('اعلان‌ها', 'صف اعلان ایمیل/پیامک: یادآور فعالیت، نقض SLA و تأیید کمیسیون. در پیش‌نمایش ارسال واقعی انجام نمی‌شود.') +
      `<section class="panel u-panel">${ctx.table(['زمان', 'دسته', 'عنوان', 'گیرنده', 'کانال'], mine.map(n => `<tr><td>${when(n.at)}</td><td>${e(n.category)}</td><td>${e(n.title)}</td><td>${e(n.to)}</td><td>${e(n.channel)}</td></tr>`).join(''))}</section>`;
  }

  // ── Actions and submits ──
  function action(name, id) {
    if (!name.startsWith('af-') && !name.startsWith('sv-') && !name.startsWith('cm-')) return false;
    const c = account(), num = Number(String(id).split(':').pop());
    const find = (key, x = num) => store[key].find(r => r.id === x);
    const done = msg => { save(); render(); if (msg) toast(msg); return true; };
    switch (name) {
      case 'af-tab': ui.tab = id; return done();
      case 'af-side': ui.side = id; return done();
      case 'af-filter': { const [k, v] = String(id).split(':'); ui[k] = v || ''; return done(); }
      case 'af-toggle': ui.open.has(id) ? ui.open.delete(id) : ui.open.add(id); return done();
      case 'af-open-all': ui.open = id === '1' ? new Set(['contacts', 'opportunities', 'quotes', 'leads', 'orders', 'payments', 'documents', 'campaigns', 'service', 'hierarchy']) : new Set(); return done();
      case 'af-new-opportunity': createForm(c, 'opportunities'); return true;
      case 'af-new-lead': createForm(c, 'leads'); return true;
      case 'af-new-call': activityForm(c, 'call'); return true;
      case 'af-new-meeting': activityForm(c, 'meeting'); return true;
      case 'af-new-task': activityForm(c, 'task'); return true;
      case 'af-new-note': showForm('ایجاد یادداشت', ctxLine(c) + field('عنوان *', '<input name="title" required>', true) + field('متن *', '<textarea name="body" rows="5" required></textarea>', true) + field('سطح دسترسی', `<select name="visibility">${opt([['عمومی', 'عمومی'], ['محدود', 'محدود (نویسنده و مدیران)'], ['خصوصی', 'خصوصی (فقط نویسنده)']], 'عمومی')}</select>`), 'af-note', c.id); return true;
      case 'af-create': ui.open.add(id); createForm(c, id); return true;
      case 'af-link': linkForm(c, id); return true;
      case 'af-complete': case 'af-reschedule': case 'af-cancel': { const a = find('activities'); if (a && a.status === 'planned' && can.activity()) stepForm(a, name.slice(3)); return true; }
      case 'af-classify': showForm('نوع رابطه و برچسب‌ها', ctxLine(c) + field('نوع رابطه', `<select name="relationship">${opt(['مشتری', 'مشتری بالقوه', 'تأمین‌کننده', 'شریک تجاری'].map(x => [x, x]), c.relationship)}</select>`) + field('برچسب‌ها (با ویرگول)', `<input name="tags" value="${e((c.tags || []).join('، '))}">`, true), 'af-classify', c.id); return true;
      case 'af-status': showForm(c.status === 'غیرفعال' ? 'فعال‌سازی حساب' : 'غیرفعال‌سازی حساب', ctxLine(c) + field('دلیل *', '<textarea name="reason" required rows="3"></textarea>', true) + '<p class="u-period">حساب حذف نمی‌شود؛ همهٔ سوابق می‌ماند.</p>', 'af-status', c.id); return true;
      case 'af-quote': navigateQuote(c); return true;
      case 'af-contact-off': { const x = find('contacts'); if (x) { x.active = false; log(c.id, 'رابط ' + x.name + ' غیرفعال شد'); } return done('رابط غیرفعال شد؛ سوابق او حذف نشد.'); }
      case 'af-lead-unlink': { const x = store.leads.find(l => l.id === num); if (x) { delete x.customerId; log(c.id, 'ارتباط سرنخ ' + x.code + ' قطع شد'); } return done('ارتباط قطع شد؛ سرنخ حذف نشد.'); }
      case 'af-doc-unlink': { const d = find('documents'); if (d) { d.links = d.links.filter(x => x !== c.id); log(c.id, 'ارتباط سند «' + d.name + '» قطع شد'); } return done('ارتباط قطع شد؛ فایل حذف نشد.'); }
      case 'af-campaign-leave': store.campaignMembers = store.campaignMembers.filter(m => m.id !== num); log(c.id, 'خروج از کمپین'); return done('عضویت حذف شد؛ کمپین باقی است.');
      case 'af-parent-unlink': { const x = store.customers.find(k => k.id === num); if (x) { delete x.parentId; log(c.id, 'ارتباط مادر/زیرمجموعه با ' + x.name + ' قطع شد'); } return done('ارتباط قطع شد.'); }
      case 'af-pay-approve': { const p = find('payments'); if (!p || !can.approve()) return true; if (p.by === me()) { toast('ثبت‌کننده نمی‌تواند پرداخت خود را تأیید کند.', 'warning'); return true; } p.status = 'تأییدشده'; log(c.id, `${p.direction} ${money(p.amount, p.currency)}: تأییدشده`); return done('پرداخت تأیید شد.'); }
      case 'af-pay-cancel': case 'af-pay-return': showForm(name === 'af-pay-cancel' ? 'لغو پرداخت' : 'ثبت برگشتی', ctxLine(c) + field('دلیل *', '<textarea name="reason" required rows="2"></textarea>', true), 'af-pay-reason', (name === 'af-pay-cancel' ? 'cancel:' : 'return:') + num); return true;
      case 'af-customer-kind': customerForm(id); return true;
      case 'sv-create': showForm('ثبت درخواست خدمات', opField() + field('مشتری', `<select name="customerId">${opt(store.customers.filter(ctx.visible).map(x => [x.id, x.name]))}</select>`, true) + field('موضوع *', '<input name="subject" required>', true) + field('اولویت', `<select name="priority">${opt(['عادی', 'بالا', 'بحرانی'].map(x => [x, x]), 'عادی')}</select>`), 'sv-new', ''); return true;
      case 'sv-close': showForm('بستن پرونده', opField() + field('رضایت مشتری (۱ تا ۵) *', '<input type="number" name="csat" min="1" max="5" required value="5">'), 'sv-close', num); return true;
      case 'cm-calc': { store.commissions = store.commissions.filter(m => m.status !== 'در انتظار تأیید'); for (const d of store.dealers) { if (store.commissions.some(m => m.dealerId === d.id && m.status === 'تأییدشده')) continue; const ach = d.target ? Math.round(100 * d.actual / d.target) : 0, r = ach >= 100 ? 3 : ach >= 80 ? 2 : 1; store.commissions.push({id: nextId('commissions'), dealerId: d.id, sales: d.actual, achievement: ach, rate: r, amount: Math.round(d.actual * r / 100), status: 'در انتظار تأیید'}); } notify('تأیید کمیسیون', 'کمیسیون دوره برای تأیید آماده است', 'لیلا کریمی'); return done('کمیسیون دوره محاسبه شد و برای تأیید مالی ارسال شد.'); }
      case 'cm-approve': { const m = find('commissions'); if (m && user() === 'finance.manager') { m.status = 'تأییدشده'; m.sent = true; } return done('کمیسیون تأیید و به صف ارسال حسابداری رفت.'); }
    }
    return true;
  }
  function navigateQuote(c) { ctx.navigate('quotes'); toast('پیش‌فاکتور برای «' + c.name + '» از صفحهٔ پیشنهادها ساخته می‌شود.'); }
  function submit(kind, data, id) {
    if (!kind.startsWith('af-') && !kind.startsWith('sv-')) return false;
    if (kind === 'af-search') { ui.search[id] = String(data.q || '').trim(); ui.open.add(id); render(); return true; }
    if (op(data)) { closeDrawer(); return true; }
    const finish = (msg, keepTab) => { save(); closeDrawer(); if (keepTab) ui.tab = keepTab; render(); toast(msg); return true; };
    const [part, rawId] = String(id).includes(':') ? String(id).split(':') : ['', id];
    const c = store.customers.find(x => x.id === Number(rawId)) || account();
    const text = v => String(v ?? '').trim();
    const num = v => Number(digits(v));
    if (kind.startsWith('af-') && c && c.status === 'غیرفعال' && !['af-status', 'af-customer'].includes(kind)) throw new Error('حساب غیرفعال است؛ ابتدا آن را فعال کنید.');
    switch (kind) {
      case 'af-customer': {
        const legal = id === 'حقوقی', nid = digits(data.nationalId), mobile = digits(data.mobile);
        if (legal ? !validLegalId(nid) : !validNationalCode(nid)) throw new Error(legal ? 'شناسهٔ ملی شرکت معتبر نیست (۱۱ رقم با رقم کنترل).' : 'کد ملی معتبر نیست (۱۰ رقم با رقم کنترل).');
        if (!/^09\d{9}$/.test(mobile)) throw new Error('شمارهٔ همراه باید ۱۱ رقم و با ۰۹ شروع شود.');
        if (store.customers.some(x => digits(x.nationalId) === nid)) throw new Error('مشتری با این شناسه قبلاً ثبت شده است.');
        const name = legal ? text(data.name) : `${text(data.firstName)} ${text(data.lastName)}`;
        const newId = nextId('customers');
        store.customers.push({id: newId, code: 'CUS-DEMO-' + newId, name, type: id, segment: 'استاندارد', city: text(data.city), owner: me(), branch: data.branch, phone: mobile, email: '', nationalId: nid, status: 'فعال', quality: 70, balance: 0, credit: 0, initials: name.slice(0, 2), color: 'teal', relationship: 'مشتری', tags: [],
          profile: legal ? {'کد اقتصادی': text(data.economicCode), 'شماره ثبت': text(data.registration), 'نماینده': text(data.representative)} : {'تاریخ تولد': text(data.birthDate)}});
        if (text(data.contactName)) store.contacts.push({id: nextId('contacts'), customerId: newId, name: text(data.contactName), role: text(data.contactRole), mobile: digits(data.contactMobile), email: '', primary: true, active: true});
        log(newId, 'حساب ایجاد شد');
        save(); closeDrawer(); ctx.navigate('customer', String(newId)); toast('مشتری ثبت شد.'); return true;
      }
      case 'af-activity': {
        const type = part, start = text(data.start);
        if (!start) throw new Error('زمان الزامی است.');
        if (type === 'meeting' && (!data.end || data.end <= start)) throw new Error('پایان جلسه باید بعد از شروع باشد.');
        const doneNow = data.done === '1';
        if (doneNow && start > now()) throw new Error('فعالیت انجام‌شده نمی‌تواند زمان آینده داشته باشد.');
        if (doneNow && !text(data.outcome)) throw new Error('شرح نتیجه را وارد کنید.');
        if (doneNow && type === 'call' && !data.result) throw new Error('نتیجهٔ تماس را انتخاب کنید.');
        const a = {id: nextId('activities'), customerId: c.id, type, subject: text(data.subject), contact: data.contact || '', owner: data.owner || me(), start, end: data.end || '', duration: Number(data.duration) || null, location: text(data.location),
          participants: Object.entries(data).filter(([k]) => k.startsWith('p_')).map(([, v]) => v), priority: data.priority || 'عادی', reminder: Number(data.reminder) || null, opportunityId: Number(data.opportunityId) || null, description: text(data.description),
          status: doneNow ? 'done' : 'planned', result: doneNow ? data.result || '' : '', outcome: doneNow ? text(data.outcome) : ''};
        store.activities.push(a);
        if (a.reminder && !doneNow) notify('یادآور فعالیت', `یادآور ${label[type]}: ${a.subject}`, a.owner);
        log(c.id, `${label[type]} ${doneNow ? 'انجام‌شده ثبت شد' : 'برنامه‌ریزی شد'}: ${a.subject}`);
        ui.side = 'followups'; ui.type = ''; ui.state = '';
        return finish(doneNow ? 'فعالیت انجام‌شده ثبت شد.' : `${label[type]} «${a.subject}» برنامه‌ریزی شد.`);
      }
      case 'af-complete': {
        const a = store.activities.find(x => x.id === Number(id));
        if (!a || a.status !== 'planned') return finish('فعالیت قبلاً بسته شده است.');
        if (a.type !== 'task' && !text(data.outcome)) throw new Error('شرح نتیجه را وارد کنید.');
        if (a.type === 'call' && !data.result) throw new Error('نتیجهٔ تماس را انتخاب کنید.');
        if (data.nextType && (!text(data.nextSubject) || !data.nextStart || data.nextStart <= now())) throw new Error('برای اقدام بعدی موضوع و زمان آینده را وارد کنید.');
        Object.assign(a, {status: 'done', outcome: text(data.outcome), result: data.result || ''});
        log(a.customerId, `${label[a.type]} انجام شد: ${a.subject}`);
        if (data.nextType) { store.activities.push({id: nextId('activities'), customerId: a.customerId, type: data.nextType, subject: text(data.nextSubject), contact: a.contact, owner: a.owner, start: data.nextStart, end: data.nextType === 'meeting' ? data.nextStart : '', status: 'planned', priority: 'عادی', opportunityId: a.opportunityId, followUpOf: a.id}); log(a.customerId, 'اقدام بعدی برنامه‌ریزی شد: ' + text(data.nextSubject)); }
        return finish(data.nextType ? 'نتیجه ثبت و اقدام بعدی برنامه‌ریزی شد.' : 'نتیجه ثبت شد.');
      }
      case 'af-reschedule': { const a = store.activities.find(x => x.id === Number(id)); if (!a || a.status !== 'planned') return finish('فعالیت بسته شده است.'); if (data.end && data.end <= data.start) throw new Error('پایان باید بعد از شروع باشد.'); const before = when(a.start); a.start = data.start; if (a.type === 'meeting') a.end = data.end || a.end; log(a.customerId, `زمان ${label[a.type]} تغییر کرد: ${before} ← ${when(a.start)}`); return finish('زمان جدید ثبت شد.'); }
      case 'af-cancel': { const a = store.activities.find(x => x.id === Number(id)); if (!a || a.status !== 'planned') return finish('فعالیت بسته شده است.'); if (!text(data.reason)) throw new Error('دلیل لغو الزامی است.'); Object.assign(a, {status: 'cancelled', reason: text(data.reason)}); log(a.customerId, `${label[a.type]} لغو شد: ${a.subject}`); return finish('فعالیت لغو شد.'); }
      case 'af-note': { const n = {id: nextId('notes'), customerId: c.id, title: text(data.title), body: text(data.body), visibility: data.visibility || 'عمومی', author: me(), at: now()}; store.notes.push(n); log(c.id, 'یادداشت ثبت شد: ' + n.title); ui.side = 'notes'; return finish('یادداشت ثبت شد.'); }
      case 'af-opportunity': {
        const value = num(data.value), p = Number(data.probability);
        if (!value) throw new Error('مبلغ را عددی وارد کنید.');
        if (!(p >= 1 && p <= 99)) throw new Error('احتمال باید بین ۱ تا ۹۹ باشد.');
        const oid = nextId('opportunities');
        store.opportunities.push({id: oid, code: 'OP-DEMO-' + oid, name: text(data.name), customer: c.name, value, currency: data.currency || 'IRR', owner: data.owner || me(), probability: p, stage: data.stage || 'شناسایی', next: text(data.next), age: 0, risk: 'کم', branch: c.branch, expectedClose: data.expectedClose, contact: data.contact || ''});
        log(c.id, 'فرصت ایجاد شد: ' + text(data.name)); ui.open.add('opportunities');
        return finish('فرصت فروش ایجاد شد.');
      }
      case 'af-lead': { const lid = nextId('leads'); store.leads.push({id: lid, code: 'LD-DEMO-' + lid, name: text(data.name), contact: data.contact || c.name, source: data.source, owner: data.owner || me(), score: 60, age: 0, status: 'تخصیص‌یافته', sla: 'نزدیک', next: 'تماس اولیه', date: 'امروز', created: now().slice(0, 10), branch: c.branch, customerId: c.id}); log(c.id, 'سرنخ برای حساب ثبت شد: ' + text(data.name)); ui.open.add('leads'); return finish('سرنخ برای حساب ثبت شد.'); }
      case 'af-payment': {
        const value = num(data.amount);
        if (!value) throw new Error('مبلغ را عددی وارد کنید.');
        if (['چک', 'حواله بانکی'].includes(data.method) && !text(data.reference)) throw new Error('برای چک و حواله، شماره یا کد پیگیری الزامی است.');
        if (data.date > now().slice(0, 10)) throw new Error('تاریخ پرداخت نمی‌تواند در آینده باشد.');
        store.payments.push({id: nextId('payments'), customerId: c.id, direction: data.direction, method: data.method, amount: value, currency: data.currency || 'IRR', date: data.date, reference: text(data.reference), status: 'ثبت‌شده', by: me()});
        log(c.id, `${data.direction} ${money(value, data.currency || 'IRR')} ثبت شد`); ui.open.add('payments'); return finish('پرداخت با وضعیت «ثبت‌شده» ذخیره شد.');
      }
      case 'af-pay-reason': { const p = store.payments.find(x => x.id === Number(rawId)); if (!p) return finish(''); if (!text(data.reason)) throw new Error('دلیل الزامی است.'); p.status = part === 'cancel' ? 'لغوشده' : 'برگشتی'; p.note = text(data.reason); log(p.customerId, `${p.direction} ${money(p.amount, p.currency)}: ${p.status}`); return finish('وضعیت پرداخت به‌روز شد.'); }
      case 'af-contact': { if (data.mobile && !/^09\d{9}$/.test(digits(data.mobile))) throw new Error('شمارهٔ همراه معتبر نیست.'); if (data.primary === '1') store.contacts.filter(x => x.customerId === c.id).forEach(x => { x.primary = false; }); store.contacts.push({id: nextId('contacts'), customerId: c.id, name: text(data.name), role: text(data.role), mobile: digits(data.mobile), email: text(data.email), primary: data.primary === '1', active: true}); log(c.id, 'رابط تعریف شد: ' + text(data.name)); ui.open.add('contacts'); return finish('فرد رابط افزوده شد.'); }
      case 'af-document': { store.documents.push({id: nextId('documents'), name: text(data.name), size: '—', by: me(), at: now(), links: [c.id]}); log(c.id, 'سند ثبت شد: ' + text(data.name)); ui.open.add('documents'); return finish('سند ثبت شد.'); }
      case 'af-link': {
        const rid = Number(data.recordId);
        if (part === 'leads') { const l = store.leads.find(x => x.id === rid); if (l) l.customerId = c.id; }
        if (part === 'documents') { const d = store.documents.find(x => x.id === rid); if (d && !d.links.includes(c.id)) d.links.push(c.id); }
        if (part === 'campaigns' && !store.campaignMembers.some(m => m.campaignId === rid && m.customerId === c.id)) store.campaignMembers.push({id: nextId('campaignMembers'), campaignId: rid, customerId: c.id, status: 'هدف‌گذاری‌شده'});
        if (part === 'hierarchy') {
          const other = store.customers.find(x => x.id === rid);
          const ancestors = x => { const list = []; let p = x.parentId; while (p && !list.includes(p)) { list.push(p); p = store.customers.find(k => k.id === p)?.parentId; } return list; };
          if (data.role === 'parent') { if (ancestors(other).includes(c.id)) throw new Error('این ارتباط حلقه ایجاد می‌کند.'); c.parentId = other.id; } else { if (ancestors(c).includes(other.id)) throw new Error('این ارتباط حلقه ایجاد می‌کند.'); other.parentId = c.id; }
        }
        log(c.id, 'رکورد موجود متصل شد (' + part + ')'); ui.open.add(part); return finish('رکورد به حساب متصل شد.');
      }
      case 'af-classify': c.relationship = data.relationship; c.tags = String(data.tags || '').split(/[,،]/).map(x => x.trim()).filter(Boolean).slice(0, 10); log(c.id, 'نوع رابطه / برچسب‌ها تغییر کرد'); return finish('ذخیره شد.');
      case 'af-status': if (!text(data.reason)) throw new Error('دلیل الزامی است.'); c.status = c.status === 'غیرفعال' ? 'فعال' : 'غیرفعال'; log(c.id, (c.status === 'فعال' ? 'حساب فعال شد: ' : 'حساب غیرفعال شد: ') + text(data.reason)); return finish('وضعیت حساب تغییر کرد.');
      case 'sv-new': { const cid = Number(data.customerId || rawId), sid = nextId('serviceCases'), hours = data.priority === 'بحرانی' ? 4 : data.priority === 'بالا' ? 8 : 24, due = new Date(Date.now() + hours * 3600000); store.serviceCases.push({id: sid, code: 'SC-DEMO-' + sid, customerId: cid, subject: text(data.subject), priority: data.priority, status: 'باز', opened: now(), due: local(due), owner: me(), source: 'CRM'}); log(cid, 'درخواست خدمات ثبت شد: ' + text(data.subject)); return finish('درخواست خدمات ثبت شد؛ مهلت SLA ' + amount(hours) + ' ساعت.'); }
      case 'sv-close': { const s = store.serviceCases.find(x => x.id === Number(id)); const score = Number(data.csat); if (!(score >= 1 && score <= 5)) throw new Error('رضایت باید بین ۱ تا ۵ باشد.'); if (s) Object.assign(s, {status: 'بسته', csat: score}); return finish('پرونده بسته شد.'); }
    }
    return false;
  }
  return {
    views: {customer: accountView, version: versionView, service: serviceView, commissions: commissionView, notifications: notificationView},
    routes: ['version', 'service', 'commissions', 'notifications'],
    allowed: route => route === 'version' || (route === 'commissions' ? can.commission() : ['service', 'notifications'].includes(route) ? user() !== 'dealer.user' : true),
    action, submit, customerForm, resetUi: () => { ui.tab = 'overview'; ui.side = 'followups'; ui.type = ''; ui.state = ''; ui.search = {}; },
    search: (key, q) => { ui.search[key] = q; ui.open.add(key); render(); }
  };
}
