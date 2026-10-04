/* Offline preview of the v1.9 list workspace (سرنخ‌ها و مشتریان): view tabs, counter tiles, labeled filters,
   row selection with a bulk bar, row menus, list/card layouts and numbered paging. Same markup and CSS as the MVC lists. */
function createListPreview(ctx) {
  'use strict';
  const {store, escapeHtml: e, save, toast, closeDrawer} = ctx;
  const render = () => ctx.render();
  const names = {'sales.manager': 'مهدی نادری', 'sales.expert': 'سارا احمدی', 'finance.manager': 'لیلا کریمی', 'channel.manager': 'امیرحسین مرادی', 'reporting.ceo': 'مدیرعامل'};
  const me = () => names[ctx.account()] || '';
  const canAssign = () => ['sales.manager'].includes(ctx.account());
  const canWrite = () => ['sales.manager', 'sales.expert'].includes(ctx.account());
  const fa = n => new Intl.NumberFormat('fa-IR').format(n);
  const icon = name => `<svg aria-hidden="true"><use href="#i-${name}"></use></svg>`;
  const jDate = new Intl.DateTimeFormat('fa-IR-u-ca-persian', {year: 'numeric', month: '2-digit', day: '2-digit'});
  const jTime = new Intl.DateTimeFormat('fa-IR', {hour: '2-digit', minute: '2-digit', hour12: false});
  const closed = ['تبدیل‌شده', 'ردشده', 'تکراری', 'نامعتبر'];
  const state = {
    leads: {view: 'all', q: '', f: {}, sort: '', layout: 'table', page: 1, size: 10, selected: new Set()},
    customers: {view: 'all', q: '', f: {}, sort: '', layout: 'table', page: 1, size: 10, selected: new Set()}
  };

  // Seeded leads carry a display text ("امروز، ۱۴:۰۰"); give them a real due time once so views and the due column agree.
  const at = (days, hour, minute = 0) => { const d = new Date(); d.setDate(d.getDate() + days); d.setHours(hour, minute, 0, 0); return d.toISOString(); };
  for (const lead of store.leads) {
    if (lead.dueAt !== undefined) continue;
    const clock = String(lead.date || '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).match(/(\d{1,2}):(\d{2})/);
    lead.dueAt = lead.sla === 'گذشته' ? new Date(Date.now() - 2 * 3600000).toISOString()
      : String(lead.date).includes('امروز') ? at(0, clock ? Number(clock[1]) : 16, clock ? Number(clock[2]) : 0)
      : String(lead.date).includes('فردا') ? at(1, clock ? Number(clock[1]) : 10) : lead.next ? at(3, 10) : null;
  }
  save();

  const endOfToday = () => { const d = new Date(); d.setHours(24, 0, 0, 0); return d.getTime(); };
  function due(value) {
    if (!value) return ['—', 'muted'];
    const time = new Date(value).getTime(), now = Date.now();
    if (time < now) { const h = (now - time) / 3600000; return [h >= 24 ? `${fa(Math.floor(h / 24))} روز تأخیر` : h >= 1 ? `${fa(Math.floor(h))} ساعت تأخیر` : 'کمتر از یک ساعت تأخیر', 'danger']; }
    const day = d => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x.getTime(); };
    const days = Math.round((day(time) - day(now)) / 86400000);
    return days === 0 ? [`امروز، ${jTime.format(time)}`, 'success'] : days === 1 ? [`فردا، ${jTime.format(time)}`, 'info'] : days < 7 ? [`${fa(days)} روز دیگر`, 'neutral'] : [jDate.format(time), 'neutral'];
  }
  const dueCell = value => { const [text, tone] = due(value); return `<span class="due due--${tone}">${icon(tone === 'danger' ? 'alert' : 'calendar')}${e(text)}</span>`; };
  const sourceIcon = s => /وب|سایت/.test(s) ? 'globe' : /نمایشگاه/.test(s) ? 'store' : /معرفی/.test(s) ? 'users' : /تماس|تلفن/.test(s) ? 'phone' : /کمپین|تبلیغ/.test(s) ? 'megaphone' : /ورود/.test(s) ? 'upload' : 'link';
  const actionIcon = s => /تماس/.test(s) ? 'phone' : /جلسه|بازدید|دمو/.test(s) ? 'calendar' : /ایمیل|ارسال/.test(s) ? 'send' : /پیشنهاد|قیمت/.test(s) ? 'file' : 'task';
  const leadTone = s => ({'جدید': 'info', 'تخصیص‌یافته': 'violet', 'تماس‌شده': 'warning', 'واجد شرایط': 'success', 'پرورش': 'neutral', 'تبدیل‌شده': 'primary'}[s] || 'danger');
  const leadIcon = s => ({'جدید': 'plus', 'تخصیص‌یافته': 'user', 'تماس‌شده': 'phone', 'واجد شرایط': 'check', 'پرورش': 'clock', 'تبدیل‌شده': 'target', 'تکراری': 'copy'}[s] || 'close');
  const customerTone = s => s === 'فعال' ? 'success' : s === 'غیرفعال' ? 'neutral' : 'warning';
  const customerIcon = s => s === 'فعال' ? 'check' : s === 'غیرفعال' ? 'close' : 'clock';
  const relationLabel = r => ({prospect: 'مشتری بالقوه', supplier: 'تأمین‌کننده', partner: 'شریک'}[r] || 'مشتری');
  const meter = n => `<span class="score-meter score-meter--${n < 70 ? 'low' : n < 90 ? 'mid' : 'high'}" style="--value:${Number(n) || 0}"><span><b>${fa(n)}</b> از ۱۰۰</span></span>`;
  const opt = (items, selected) => `<option value="">همه</option>` + items.map(([v, l]) => `<option value="${e(v)}" ${String(v) === String(selected ?? '') ? 'selected' : ''}>${e(l)}</option>`).join('');
  const uniq = values => [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b, 'fa'));
  const act = (action, id, inner, cls = '', extra = '') => `<button type="button" class="${cls}" data-u-action="${action}" data-id="${e(id)}" ${extra}>${inner}</button>`;

  // ── Lead rows ──
  const isOpen = l => !closed.includes(l.status);
  function leadSets(all) {
    const open = all.filter(isOpen);
    const sameName = l => open.some(o => o.id !== l.id && o.name.trim() === l.name.trim());
    return {
      all: open, mine: open.filter(l => l.owner === me()), nonext: open.filter(l => !l.next),
      overdue: open.filter(l => l.dueAt && new Date(l.dueAt).getTime() < Date.now()),
      needs: open.filter(l => !l.next || (l.dueAt && new Date(l.dueAt).getTime() < endOfToday())),
      duplicates: open.filter(sameName)
    };
  }
  function leadsView() {
    const s = state.leads, all = ctx.rows('leads'), sets = leadSets(all);
    let items = (s.view === 'all' && s.f.closed ? all : sets[s.view] || sets.all).slice();
    if (s.f.source) items = items.filter(l => l.source === s.f.source);
    if (s.f.owner) items = items.filter(l => l.owner === s.f.owner);
    if (s.f.branch) items = items.filter(l => (l.branch || 'مرکزی') === s.f.branch);
    if (s.f.status) items = items.filter(l => l.status === s.f.status);
    if (s.q) items = items.filter(l => [l.name, l.code, l.contact].some(x => String(x || '').includes(s.q)));
    if (s.sort === 'score') items.sort((a, b) => b.score - a.score);
    if (s.sort === 'score-asc') items.sort((a, b) => a.score - b.score);
    if (s.sort === 'due') items.sort((a, b) => (a.dueAt ? Date.parse(a.dueAt) : Infinity) - (b.dueAt ? Date.parse(b.dueAt) : Infinity));
    if (s.sort === 'name') items.sort((a, b) => a.name.localeCompare(b.name, 'fa'));
    const dupIds = new Set(sets.duplicates.map(l => l.id));
    const page = paging(s, items.length), rowsHtml = items.slice((page - 1) * s.size, page * s.size);
    const select = canWrite();
    const statuses = uniq(all.map(l => l.status));
    const header = `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('lead')}</span><div><h1>مدیریت سرنخ‌های فروش</h1><p>ثبت، ارجاع، پیگیری و تبدیل سرنخ‌ها به فرصت فروش؛ هر سرنخ با مالک، مهلت تماس و اقدام بعدی</p></div></div>
      ${canWrite() ? `<div class="list-head__actions">${act('ls-import', 'leads', icon('upload') + 'ورود اطلاعات', 'button button--ghost button--outline')}<button type="button" class="button button--primary" data-action="new-lead">${icon('plus')}سرنخ جدید</button></div>` : ''}</header>`;
    const tabs = tabBar('leads', [['all', 'همه سرنخ‌ها', sets.all.length], ['mine', 'سرنخ‌های من', sets.mine.length], ['nonext', 'بدون اقدام بعدی', sets.nonext.length], ['overdue', 'معوق', sets.overdue.length]]);
    const tiles = tileBar('leads', [['all', 'سرنخ', sets.all.length, 'سرنخ باز قابل مشاهده', 'lead', 'green'], ['needs', 'نیازمند اقدام', sets.needs.length, 'تا پایان امروز یا بدون برنامه', 'clock', 'blue'],
      ['overdue', 'معوق', sets.overdue.length, 'مهلت تماس یا اقدام گذشته', 'alert', 'amber'], ['duplicates', 'مشکوک به تکرار', sets.duplicates.length, 'نام یا تماس مشابه', 'copy', 'red']]);
    const filters = filterBar('leads', 'جست‌وجوی نام، کد یا شخص تماس', [
      ['branch', 'شعبه', uniq(all.map(l => l.branch || 'مرکزی')).map(x => [x, x])], ['owner', 'کارشناس فروش', uniq(all.map(l => l.owner)).map(x => [x, x])],
      ['source', 'منبع', uniq(all.map(l => l.source)).map(x => [x, x])], ['status', 'وضعیت', statuses.map(x => [x, x])]],
      `<label class="list-chip"><input type="checkbox" data-ls-check="leads:closed" ${s.f.closed ? 'checked' : ''}><span>نمایش بسته‌ها</span></label>`);
    const bulk = select ? bulkBar('leads', (canAssign() ? act('ls-bulk-assign', 'leads', icon('send') + 'ارجاع', 'button button--ghost button--sm') : '') + act('ls-bulk-next', 'leads', icon('calendar') + 'افزودن فعالیت', 'button button--ghost button--sm')) : '';
    const check = l => select ? `<input type="checkbox" data-ls-select="leads:${l.id}" ${s.selected.has(l.id) ? 'checked' : ''} aria-label="انتخاب ${e(l.name)}">` : '';
    const menu = l => `<details class="row-menu"><summary aria-label="عملیات ${e(l.name)}">${icon('more')}</summary><div class="row-menu__list">
        ${l.status === 'واجد شرایط' && canWrite() ? `<button type="button" data-action="convert-lead" data-id="${l.id}">${icon('target')}تبدیل به فرصت</button>` : ''}
        ${canAssign() ? act('ls-row-assign', l.id, icon('send') + 'ارجاع') : ''}${canWrite() ? act('ls-row-next', l.id, icon('calendar') + 'افزودن فعالیت') : ''}</div></details>`;
    const body = s.layout === 'cards'
      ? `<div class="list-cards">${rowsHtml.length ? rowsHtml.map(l => `<article class="list-card ${s.selected.has(l.id) ? 'is-selected' : ''}"><header>${check(l)}<span class="entity-cell"><span class="entity-avatar entity-avatar--blue">${e(l.name.slice(0, 1))}</span><span><strong>${e(l.name)}</strong><small>${e(l.contact || l.code)}</small></span></span><span class="pill pill--${leadTone(l.status)}">${icon(leadIcon(l.status))}${e(l.status)}</span></header>
          <dl><div><dt>منبع</dt><dd>${icon(sourceIcon(l.source))}${e(l.source)}</dd></div><div><dt>شعبه / کارشناس</dt><dd>${e(l.branch || 'مرکزی')} · ${e(l.owner)}</dd></div><div><dt>امتیاز</dt><dd>${meter(l.score)}</dd></div><div><dt>اقدام بعدی</dt><dd class="${l.next ? '' : 'muted'}">${icon(actionIcon(l.next || ''))}${e(l.next || 'تعیین نشده')}</dd></div></dl>
          <footer>${dueCell(l.next ? l.dueAt : null)}${dupIds.has(l.id) ? `<span class="pill pill--danger pill--sm">${icon('copy')}مشکوک به تکرار</span>` : ''}</footer></article>`).join('') : empty('سرنخی با این فیلترها پیدا نشد.')}</div>`
      : `<div class="table-wrap"><table class="list-table"><thead><tr>${select ? `<th class="list-table__check"><input type="checkbox" data-ls-all="leads" aria-label="انتخاب همه"></th>` : ''}
          <th>${sortLink('leads', 'name', 'نام سرنخ')}</th><th class="hide-sm">منبع</th><th class="hide-sm">شعبه / کارشناس فروش</th><th class="hide-sm">وضعیت</th><th class="hide-sm">${sortLink('leads', s.sort === 'score' ? 'score-asc' : 'score', 'امتیاز', ['score', 'score-asc'])}</th><th class="hide-sm">اقدام بعدی</th><th>${sortLink('leads', 'due', 'سررسید')}</th><th><span class="sr-only">عملیات</span></th></tr></thead><tbody>
          ${rowsHtml.length ? rowsHtml.map(l => `<tr class="${s.selected.has(l.id) ? 'is-selected' : ''}">${select ? `<td class="list-table__check">${check(l)}</td>` : ''}
            <td><span class="entity-cell"><span class="entity-avatar entity-avatar--blue">${e(l.name.slice(0, 1))}</span><span><strong>${e(l.name)}</strong><small>${e(l.contact ? l.contact + ' · ' : '')}<bdi>${e(l.code)}</bdi></small><span class="pill pill--sm pill--${leadTone(l.status)} show-sm">${e(l.status)}</span></span></span></td>
            <td class="hide-sm"><span class="cell-icon">${icon(sourceIcon(l.source))}${e(l.source)}</span></td><td class="hide-sm"><strong class="cell-title">${e(l.branch || 'مرکزی')}</strong><small class="cell-sub">${e(l.owner)}</small></td>
            <td class="hide-sm"><span class="pill pill--${leadTone(l.status)}">${icon(leadIcon(l.status))}${e(l.status)}</span>${dupIds.has(l.id) ? `<small class="cell-flag">${icon('copy')}مشکوک به تکرار</small>` : ''}</td>
            <td class="hide-sm">${meter(l.score)}</td><td class="hide-sm"><span class="cell-icon ${l.next ? '' : 'muted'}">${icon(actionIcon(l.next || ''))}${e(l.next || 'تعیین نشده')}</span></td><td>${dueCell(l.next ? l.dueAt : null)}</td><td class="list-table__menu">${menu(l)}</td></tr>`).join('')
            : `<tr><td colspan="${select ? 9 : 8}">${empty('سرنخی با این فیلترها پیدا نشد.')}</td></tr>`}</tbody></table></div>`;
    return header + `<div class="list-workspace" data-bulk>${tabs}${tiles}<section class="panel list-panel">${filters}${bulk}${body}${pager('leads', page, items.length)}</section></div>`;
  }

  // ── Customer rows ──
  function customerSets(all) {
    const planned = (store.activities || []).filter(a => a.status === 'planned');
    const nextOf = c => planned.filter(a => a.customerId === c.id).sort((a, b) => Date.parse(a.start) - Date.parse(b.start))[0];
    const pending = (store.duplicates || []).filter(d => d.status === 'در انتظار بررسی').flatMap(d => [d.first, d.second]);
    const active = all.filter(c => c.status !== 'غیرفعال');
    return {
      nextOf, pending: new Set(pending),
      all, mine: all.filter(c => c.owner === me()), inactive: all.filter(c => c.status === 'غیرفعال'),
      nofollowup: active.filter(c => !nextOf(c)), followup: active.filter(c => nextOf(c) && Date.parse(nextOf(c).start) < endOfToday()),
      incomplete: active.filter(c => !c.phone || !c.email || !c.nationalId), duplicates: all.filter(c => pending.includes(c.id))
    };
  }
  function customersView() {
    const s = state.customers, all = ctx.rows('customers'), sets = customerSets(all);
    let items = (sets[s.view] || sets.all).slice();
    if (s.f.branch) items = items.filter(c => c.branch === s.f.branch);
    if (s.f.segment) items = items.filter(c => c.segment === s.f.segment);
    if (s.f.owner) items = items.filter(c => c.owner === s.f.owner);
    if (s.f.status) items = items.filter(c => c.status === s.f.status);
    if (s.f.relationship) items = items.filter(c => (c.relationship || 'customer') === s.f.relationship);
    if (s.q) items = items.filter(c => [c.name, c.code, c.city].some(x => String(x || '').includes(s.q)));
    if (s.sort === 'name') items.sort((a, b) => a.name.localeCompare(b.name, 'fa'));
    const page = paging(s, items.length), rowsHtml = items.slice((page - 1) * s.size, page * s.size);
    const select = canWrite();
    const header = `<header class="list-head"><div class="list-head__title"><span class="list-head__icon">${icon('users')}</span><div><h1>مشتریان و حساب‌ها</h1><p>پرونده یکپارچه مشتریان، مشتریان بالقوه، تأمین‌کنندگان و شرکا؛ با کیفیت داده و پیگیری بعدی هر حساب</p></div></div>
      <div class="list-head__actions"><a class="button button--ghost" href="#/customer-quality" data-route="customer-quality">${icon('check')}کیفیت داده</a>${canWrite() ? `<button type="button" class="button button--primary" data-action="new-customer">${icon('plus')}مشتری جدید</button>` : ''}</div></header>`;
    const tabs = tabBar('customers', [['all', 'همه حساب‌ها', sets.all.length], ['mine', 'حساب‌های من', sets.mine.length], ['nofollowup', 'بدون پیگیری', sets.nofollowup.length], ['inactive', 'غیرفعال', sets.inactive.length]]);
    const tiles = tileBar('customers', [['all', 'حساب', sets.all.length, `${fa(all.filter(c => c.status === 'فعال').length)} حساب فعال`, 'users', 'green'],
      ['followup', 'پیگیری امروز', sets.followup.length, 'فعالیت برنامه‌ریزی‌شده تا پایان امروز', 'calendar', 'blue'], ['incomplete', 'نیازمند تکمیل', sets.incomplete.length, 'شناسه ملی، تلفن یا ایمیل ناقص', 'alert', 'amber'],
      ['duplicates', 'مشکوک به تکرار', sets.duplicates.length, 'در انتظار بررسی و ادغام', 'copy', 'red']]);
    const filters = filterBar('customers', 'جست‌وجوی نام، کد یا شهر', [
      ['branch', 'شعبه', uniq(all.map(c => c.branch)).map(x => [x, x])], ['relationship', 'نوع رابطه', [['customer', 'مشتری'], ['prospect', 'مشتری بالقوه'], ['supplier', 'تأمین‌کننده'], ['partner', 'شریک']]],
      ['segment', 'بخش', uniq(all.map(c => c.segment)).map(x => [x, x])], ['owner', 'مالک', uniq(all.map(c => c.owner)).map(x => [x, x])], ['status', 'وضعیت', uniq(all.map(c => c.status)).map(x => [x, x])]]);
    const bulk = select ? bulkBar('customers', act('ls-bulk-task', 'customers', icon('task') + 'ثبت وظیفه پیگیری', 'button button--ghost button--sm')) : '';
    const check = c => select ? `<input type="checkbox" data-ls-select="customers:${c.id}" ${s.selected.has(c.id) ? 'checked' : ''} aria-label="انتخاب ${e(c.name)}">` : '';
    const nextCell = c => { const a = sets.nextOf(c); return `<span class="cell-icon ${a ? '' : 'muted'}">${icon(a ? ({call: 'phone', meeting: 'calendar'}[a.type] || 'task') : 'task')}${e(a ? a.subject : 'برنامه‌ای ندارد')}</span>`; };
    const menu = c => `<details class="row-menu"><summary aria-label="عملیات ${e(c.name)}">${icon('more')}</summary><div class="row-menu__list"><a href="#/customer?id=${c.id}" data-route="customer" data-record="${c.id}">${icon('file')}پرونده حساب</a>${canWrite() ? act('ls-row-task', c.id, icon('task') + 'ایجاد وظیفه') : ''}</div></details>`;
    const body = s.layout === 'cards'
      ? `<div class="list-cards">${rowsHtml.length ? rowsHtml.map(c => `<article class="list-card ${s.selected.has(c.id) ? 'is-selected' : ''}"><header>${check(c)}<a class="entity-cell entity-link" href="#/customer?id=${c.id}" data-route="customer" data-record="${c.id}"><span class="entity-avatar">${e(c.name.slice(0, 1))}</span><span><strong>${e(c.name)}</strong><small><bdi>${e(c.code)}</bdi> · ${e(c.city)}</small></span></a><span class="pill pill--${customerTone(c.status)}">${icon(customerIcon(c.status))}${e(c.status)}</span></header>
          <dl><div><dt>نوع رابطه</dt><dd>${icon('building')}${relationLabel(c.relationship)} · ${e(c.type)}</dd></div><div><dt>شعبه / مالک</dt><dd>${e(c.branch)} · ${e(c.owner)}</dd></div><div><dt>کیفیت داده</dt><dd>${meter(c.quality)}</dd></div><div><dt>پیگیری بعدی</dt><dd>${nextCell(c)}</dd></div></dl>
          <footer>${dueCell(sets.nextOf(c)?.start)}${sets.pending.has(c.id) ? `<span class="pill pill--danger pill--sm">${icon('copy')}مشکوک به تکرار</span>` : ''}</footer></article>`).join('') : empty('حسابی با این فیلترها پیدا نشد.')}</div>`
      : `<div class="table-wrap"><table class="list-table"><thead><tr>${select ? `<th class="list-table__check"><input type="checkbox" data-ls-all="customers" aria-label="انتخاب همه"></th>` : ''}
          <th>${sortLink('customers', s.sort === 'name' ? '' : 'name', 'حساب', ['name'])}</th><th class="hide-sm">نوع رابطه</th><th class="hide-sm">شعبه / مالک</th><th class="hide-sm">وضعیت</th><th class="hide-sm">کیفیت داده</th><th class="hide-sm">پیگیری بعدی</th><th>سررسید</th><th><span class="sr-only">عملیات</span></th></tr></thead><tbody>
          ${rowsHtml.length ? rowsHtml.map(c => `<tr class="${s.selected.has(c.id) ? 'is-selected' : ''}">${select ? `<td class="list-table__check">${check(c)}</td>` : ''}
            <td><a class="entity-cell entity-link" href="#/customer?id=${c.id}" data-route="customer" data-record="${c.id}"><span class="entity-avatar">${e(c.name.slice(0, 1))}</span><span><strong>${e(c.name)}</strong><small><bdi>${e(c.code)}</bdi> · ${e(c.city)}</small><span class="pill pill--sm pill--${customerTone(c.status)} show-sm">${e(c.status)}</span></span></a></td>
            <td class="hide-sm"><span class="cell-icon">${icon('building')}${relationLabel(c.relationship)}</span><small class="cell-sub">${e(c.type)} · ${e(c.segment)}</small></td><td class="hide-sm"><strong class="cell-title">${e(c.branch)}</strong><small class="cell-sub">${e(c.owner)}</small></td>
            <td class="hide-sm"><span class="pill pill--${customerTone(c.status)}">${icon(customerIcon(c.status))}${e(c.status)}</span>${sets.pending.has(c.id) ? `<small class="cell-flag">${icon('copy')}مشکوک به تکرار</small>` : ''}</td>
            <td class="hide-sm">${meter(c.quality)}</td><td class="hide-sm">${nextCell(c)}</td><td>${dueCell(sets.nextOf(c)?.start)}</td><td class="list-table__menu">${menu(c)}</td></tr>`).join('')
            : `<tr><td colspan="${select ? 9 : 8}">${empty('حسابی با این فیلترها پیدا نشد.')}</td></tr>`}</tbody></table></div>`;
    return header + `<div class="list-workspace" data-bulk>${tabs}${tiles}<section class="panel list-panel">${filters}${bulk}${body}${pager('customers', page, items.length)}</section></div>`;
  }

  // ── Shared pieces (same classes as Views/Shared/_List*.cshtml) ──
  function paging(s, total) { const pages = Math.max(1, Math.ceil(total / s.size)); s.page = Math.min(Math.max(1, s.page), pages); return s.page; }
  const empty = text => `<div class="list-empty"><span>${icon('search')}</span><strong>موردی پیدا نشد</strong><p>${e(text)}</p></div>`;
  const tabBar = (list, tabs) => `<nav class="list-tabs" aria-label="نماها">${tabs.map(([view, label, count]) => act('ls-view', `${list}:${view}`, `${e(label)}<b>${fa(count)}</b>`, `list-tabs__tab ${state[list].view === view ? 'is-active' : ''}`)).join('')}</nav>`;
  const tileBar = (list, tiles) => `<section class="list-tiles">${tiles.map(([view, label, value, hint, ic, tone]) => {
    const active = view !== 'all' && state[list].view === view;
    return act('ls-view', `${list}:${active ? 'all' : view}`, `<span class="list-tile__icon">${icon(ic)}</span><span class="list-tile__body"><small>${e(label)}</small><strong>${fa(value)}</strong><em>${e(hint)}</em></span>`, `list-tile list-tile--${tone} ${active ? 'is-active' : ''}`);
  }).join('')}</section>`;
  const filterBar = (list, placeholder, selects, extra = '') => { const s = state[list]; return `<div class="list-filters"><label class="list-search">${icon('search')}<input type="search" data-ls-search="${list}" value="${e(s.q)}" placeholder="${e(placeholder)}" aria-label="${e(placeholder)}"></label>
    ${selects.map(([key, label, items]) => `<label class="list-filter"><span>${e(label)}</span><select data-ls-filter="${list}:${key}">${opt(items, s.f[key])}</select></label>`).join('')}${extra}
    <div class="list-layout" role="group" aria-label="نحوه نمایش">${act('ls-layout', `${list}:table`, icon('list'), `list-layout__btn ${s.layout === 'table' ? 'is-active' : ''}`, 'aria-label="نمای فهرست"')}${act('ls-layout', `${list}:cards`, icon('grid'), `list-layout__btn ${s.layout === 'cards' ? 'is-active' : ''}`, 'aria-label="نمای کارتی"')}</div></div>`; };
  const bulkBar = (list, buttons) => `<div class="bulk-bar" ${state[list].selected.size ? '' : 'hidden'}><span class="bulk-bar__count"><b>${fa(state[list].selected.size)}</b> مورد انتخاب شد</span><div class="bulk-bar__actions">${buttons}${act('ls-clear', list, icon('close') + 'پاک کردن انتخاب', 'link-button')}</div></div>`;
  const sortLink = (list, key, label, actives = [key]) => act('ls-sort', `${list}:${key}`, `${e(label)}${icon('sort')}`, `sort-link ${actives.includes(state[list].sort) ? 'is-active' : ''}`);
  function pager(list, page, total) {
    const s = state[list], pages = Math.max(1, Math.ceil(total / s.size));
    const wanted = [...new Set([1, pages, page, page - 1, page + 1])].filter(x => x >= 1 && x <= pages).sort((a, b) => a - b);
    let prev = 0, nums = '';
    for (const n of wanted) { if (n - prev > 1) nums += '<span class="pager-gap">…</span>'; nums += n === page ? `<span class="pager-btn is-active">${fa(n)}</span>` : act('ls-page', `${list}:${n}`, fa(n), 'pager-btn'); prev = n; }
    const btn = (n, ic, flip, label) => n >= 1 && n <= pages && n !== page ? act('ls-page', `${list}:${n}`, `<svg class="${flip ? 'flip' : ''}" aria-hidden="true"><use href="#i-${ic}"></use></svg>`, 'pager-btn', `aria-label="${label}"`) : `<span class="pager-btn is-disabled"><svg class="${flip ? 'flip' : ''}"><use href="#i-${ic}"></use></svg></span>`;
    return `<footer class="list-pager"><span class="list-pager__summary">نمایش <b>${fa(total ? (page - 1) * s.size + 1 : 0)}</b> تا <b>${fa(Math.min(total, page * s.size))}</b> از <b>${fa(total)}</b> مورد</span>
      <nav class="list-pager__pages" aria-label="صفحه‌بندی">${btn(1, 'chevrons', false, 'صفحه اول')}${btn(page - 1, 'chevron', false, 'صفحه قبل')}${nums}${btn(page + 1, 'chevron', true, 'صفحه بعد')}${btn(pages, 'chevrons', true, 'صفحه آخر')}</nav>
      <label class="list-pager__size"><select data-ls-size="${list}" aria-label="تعداد در صفحه">${[10, 20, 50, 100].map(n => `<option value="${n}" ${n === s.size ? 'selected' : ''}>${fa(n)}</option>`).join('')}</select><span>مورد در صفحه</span></label></footer>`;
  }

  // ── Interaction ──
  const split = id => { const i = String(id).indexOf(':'); return [String(id).slice(0, i), String(id).slice(i + 1)]; };
  const selection = list => [...state[list].selected];
  const drawer = (title, kind, id, fields) => ctx.showForm(title, fields, kind, id);
  const ownerOptions = () => uniq([...store.leads.map(l => l.owner), 'سارا احمدی', 'مهدی نادری', 'نرگس یوسفی', 'محمد رضایی']).map(x => `<option>${e(x)}</option>`).join('');
  function action(name, id) {
    if (!name.startsWith('ls-')) return false;
    if (name === 'ls-view') { const [list, view] = split(id); Object.assign(state[list], {view, page: 1}); state[list].selected.clear(); render(); return true; }
    if (name === 'ls-layout') { const [list, layout] = split(id); state[list].layout = layout; render(); return true; }
    if (name === 'ls-page') { const [list, page] = split(id); state[list].page = Number(page); render(); return true; }
    if (name === 'ls-sort') { const [list, sort] = split(id); Object.assign(state[list], {sort, page: 1}); render(); return true; }
    if (name === 'ls-clear') { state[id].selected.clear(); render(); return true; }
    if (name === 'ls-bulk-next' || name === 'ls-row-next') {
      const ids = name === 'ls-row-next' ? [Number(id)] : selection('leads'); if (!ids.length) return true;
      drawer('افزودن فعالیت برای سرنخ‌ها', 'ls-next', ids.join(','), `<p class="bulk-note">${icon('calendar')}<span>برای <b>${fa(ids.length)}</b> سرنخ «اقدام بعدی» ثبت می‌شود.</span></p><label>فعالیت<input name="next" required value="تماس پیگیری" list="lsNext"></label><datalist id="lsNext"><option value="تماس پیگیری"></option><option value="ارسال پیشنهاد قیمت"></option><option value="جلسه معرفی محصول"></option></datalist><label>زمان<input type="datetime-local" name="at" required></label>`);
      return true;
    }
    if (name === 'ls-bulk-assign' || name === 'ls-row-assign') {
      const ids = name === 'ls-row-assign' ? [Number(id)] : selection('leads'); if (!ids.length) return true;
      drawer('ارجاع سرنخ‌ها', 'ls-assign', ids.join(','), `<p class="bulk-note">${icon('send')}<span><b>${fa(ids.length)}</b> سرنخ به کارشناس انتخابی ارجاع می‌شود.</span></p><label>کارشناس مقصد<select name="owner">${ownerOptions()}</select></label><label>دلیل ارجاع<textarea name="reason" required>ارجاع گروهی</textarea></label>`);
      return true;
    }
    if (name === 'ls-bulk-task' || name === 'ls-row-task') {
      const ids = name === 'ls-row-task' ? [Number(id)] : selection('customers'); if (!ids.length) return true;
      drawer('ثبت وظیفه پیگیری', 'ls-task', ids.join(','), `<p class="bulk-note">${icon('task')}<span>برای <b>${fa(ids.length)}</b> حساب یک وظیفه به نام شما ثبت می‌شود.</span></p><label>عنوان وظیفه<input name="subject" required value="پیگیری حساب"></label><label>مهلت<input type="datetime-local" name="at" required></label>`);
      return true;
    }
    if (name === 'ls-import') {
      drawer('ورود اطلاعات سرنخ', 'ls-import', '', `<p class="bulk-note">${icon('upload')}<span>هر سطر: نام سرنخ، شخص تماس، منبع (با ویرگول جدا شود). سطرهای بدون نام گزارش می‌شوند.</span></p><label>داده‌ها<textarea name="rows" rows="6" required>شرکت نمونه واردات، مینا شریفی، نمایشگاه</textarea></label>`);
      return true;
    }
    return false;
  }
  function submit(kind, data, id) {
    if (!kind.startsWith('ls-')) return false;
    const ids = String(id || '').split(',').filter(Boolean).map(Number);
    const future = value => { const t = Date.parse(value); if (!Number.isFinite(t) || t <= Date.now()) throw new Error('زمان باید در آینده باشد.'); return new Date(t).toISOString(); };
    if (kind === 'ls-next') { const when = future(data.at); for (const l of store.leads.filter(x => ids.includes(x.id) && isOpen(x))) Object.assign(l, {next: String(data.next).trim(), dueAt: when}); state.leads.selected.clear(); }
    if (kind === 'ls-assign') { if (!String(data.reason || '').trim()) throw new Error('دلیل ارجاع الزامی است.'); for (const l of store.leads.filter(x => ids.includes(x.id) && isOpen(x))) Object.assign(l, {owner: data.owner, status: l.status === 'جدید' ? 'تخصیص‌یافته' : l.status}); state.leads.selected.clear(); }
    if (kind === 'ls-task') { const when = future(data.at); store.activities ??= []; for (const cid of ids) store.activities.push({id: Math.max(0, ...store.activities.map(a => a.id)) + 1, customerId: cid, type: 'task', subject: String(data.subject).trim(), owner: me(), start: when, status: 'planned', priority: 'عادی'}); state.customers.selected.clear(); }
    if (kind === 'ls-import') {
      let ok = 0; const failures = [];
      String(data.rows).split(/\r?\n/).forEach((line, i) => { if (!line.trim()) return; const [name, contact, source] = line.split(/[,،]/).map(x => x?.trim()); if (!name) { failures.push(`ردیف ${fa(i + 1)}: نام سرنخ الزامی است.`); return; }
        const nid = Math.max(0, ...store.leads.map(l => l.id)) + 1; store.leads.push({id: nid, code: 'LD-DEMO-' + nid, name, contact: contact || '', source: source || 'ورود اطلاعات', owner: me() || 'سارا احمدی', score: 40, age: 0, status: 'جدید', sla: 'در مهلت', next: 'تماس اولیه', dueAt: new Date(Date.now() + 4 * 3600000).toISOString(), branch: 'مرکزی'}); ok++; });
      save(); closeDrawer(); render(); toast(failures.length ? `${fa(ok)} سرنخ ثبت شد؛ ${failures.join(' ')}` : `${fa(ok)} سرنخ از داده‌ها ثبت شد.`, failures.length ? 'warning' : undefined); return true;
    }
    save(); closeDrawer(); render(); toast('عملیات گروهی ثبت شد.'); return true;
  }
  // Filters, search, selection and page size (re-render keeps the search box focused).
  document.addEventListener('change', event => {
    const t = event.target;
    if (t.dataset.lsFilter) { const [list, key] = split(t.dataset.lsFilter); state[list].f[key] = t.value; state[list].page = 1; render(); }
    else if (t.dataset.lsCheck) { const [list, key] = split(t.dataset.lsCheck); state[list].f[key] = t.checked; state[list].page = 1; render(); }
    else if (t.dataset.lsSize) { state[t.dataset.lsSize].size = Number(t.value); state[t.dataset.lsSize].page = 1; render(); }
    else if (t.dataset.lsSelect) { const [list, raw] = split(t.dataset.lsSelect); const sel = state[list].selected; if (t.checked) sel.add(Number(raw)); else sel.delete(Number(raw)); render(); }
    else if (t.dataset.lsAll) { const list = t.dataset.lsAll; document.querySelectorAll(`[data-ls-select^="${list}:"]`).forEach(x => { const n = Number(split(x.dataset.lsSelect)[1]); if (t.checked) state[list].selected.add(n); else state[list].selected.delete(n); }); render(); }
  }, true);
  let timer = 0;
  document.addEventListener('input', event => {
    const t = event.target; if (!t.dataset?.lsSearch) return;
    clearTimeout(timer);
    timer = setTimeout(() => { const list = t.dataset.lsSearch; state[list].q = t.value.trim(); state[list].page = 1; render();
      const box = document.querySelector(`[data-ls-search="${list}"]`); if (box) { box.focus(); box.setSelectionRange(box.value.length, box.value.length); } }, 250);
  });
  document.addEventListener('click', event => {
    document.querySelectorAll('details.row-menu[open]').forEach(menu => { if (!menu.contains(event.target) || event.target.closest('.row-menu__list button, .row-menu__list a')) menu.open = false; });
  });
  document.addEventListener('toggle', event => {
    const menu = event.target; if (!menu.matches?.('details.row-menu') || !menu.open) return;
    const list = menu.querySelector('.row-menu__list'), r = menu.querySelector('summary').getBoundingClientRect();
    list.style.left = Math.min(Math.max(8, r.left), innerWidth - list.offsetWidth - 8) + 'px';
    list.style.top = (r.bottom + 4 + list.offsetHeight > innerHeight - 8 ? Math.max(8, r.top - list.offsetHeight - 4) : r.bottom + 4) + 'px';
  }, true);

  return {views: {leads: leadsView, customers: customersView}, action, submit, reset: list => { state[list].selected.clear(); }};
}
