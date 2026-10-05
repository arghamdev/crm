/* Offline demonstration only. The MVC application remains the authorization boundary. */
(() => {
  'use strict';
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const today = new Date().toISOString().slice(0, 10);
  const monthStart = today.slice(0, 7) + '-01';
  const nextMonth = new Date(Date.UTC(Number(today.slice(0,4)), Number(today.slice(5,7)) + 1, 0)).toISOString().slice(0,10);
  const branches = ['مرکزی','اصفهان','اهواز','شیراز','تبریز'];
  const accounts = {'sales.manager':'مدیر فروش','sales.expert':'کارشناس فروش','finance.manager':'مدیر مالی','channel.manager':'مدیر کانال','reporting.ceo':'مدیرعامل','dealer.user':'کاربر نماینده'};
  let account = '', signedIn = false, scope = '', reportFilter = {from:monthStart,to:nextMonth,branch:'',profile:'sales'}, drillMetric = 'forecast';
  const legacyRender = render, legacyAction = handleAction, legacyForm = handleForm;
  const financial = () => ['reporting.ceo','finance.manager'].includes(account);
  const canExport = () => account !== 'sales.expert';
  const readOnly = () => account === 'reporting.ceo';
  const branchOf = record => record.branch || store.customers.find(c => c.name === record.customer)?.branch || 'مرکزی';
  const visible = record => (!scope || branchOf(record) === scope) && (account !== 'sales.expert' || branchOf(record) === 'مرکزی');
  const rows = key => (store[key] || []).filter(visible);
  const amount = n => n == null ? '—' : moneyFa.format(n);
  const idFor = key => Math.max(0, ...(store[key] || []).map(x => Number(x.id))) + 1;
  store.orders ??= [{id:91,code:'ORD-1405-001',quoteId:34,customer:store.quotes.find(q=>q.id===34)?.customer || store.customers[0].name,amount:13468000000,status:'ارسال به ERP',erp:'ERP-DEMO-901',history:['ایجاد از پیشنهاد تأییدشده','کنترل اعتبار نمونه','ارسال آزمایشی به ERP']}];
  store.dealers ??= [{id:81,code:'DLR-0001',name:'نماینده پایلوت جنوب',branch:'اهواز',status:'فعال',contract:'قرارداد توزیع ۱۴۰۵',target:7000000000,actual:6850000000,customers:[3,4],synced:today,periodFrom:monthStart,periodTo:nextMonth}];
  store.invoices ??= [{id:'INV-DEMO-1',customer:store.customers[0].name,branch:'مرکزی',principal:1200000000,collected:850000000,date:monthStart,due:monthStart},
    {id:'INV-DEMO-2',customer:store.customers[1].name,branch:'اصفهان',principal:700000000,collected:400000000,date:monthStart,due:monthStart}];
  store.audit ??= [];
  for (const lead of store.leads) { lead.created ??= monthStart; lead.branch ??= 'مرکزی'; }
  for (const opportunity of store.opportunities) opportunity.expectedClose ??= today;

  const definitions = {
    pipeline:['ارزش فرصت‌های باز','ریال','جمع ارزش فرصت‌های باز با تاریخ بستن مورد انتظار در بازه','CRM'],
    forecast:['پیش‌بینی وزنی','ریال','جمع ارزش × احتمال مرحله ÷ ۱۰۰','CRM'],
    won:['فرصت‌های برنده','ریال','جمع ارزش فرصت‌های برنده با تاریخ بسته‌شدن در بازه','CRM'],
    conversion:['تبدیل سرنخ','درصد','تبدیل‌شده ÷ سرنخ‌های ایجادشده در همان بازه × ۱۰۰','CRM'],
    quality:['کامل‌بودن پرونده مشتری','درصد','فیلدهای تکمیل‌شده ÷ (۵ × تعداد مشتری) × ۱۰۰؛ مستقل از تاریخ','CRM'],
    collection:['نسبت وصول فاکتورهای دوره','درصد','وصول تجمعی ÷ مبلغ فاکتورهای همان دوره × ۱۰۰','حسابداری نمونه'],
    overdue:['مانده سررسیدشده','ریال','جمع مانده فاکتورهایی که سررسید آن‌ها گذشته است','حسابداری نمونه'],
    dealerAchievement:['تحقق هدف نمایندگان','درصد','جمع عملکرد ÷ جمع هدف دقیقاً هم‌دوره × ۱۰۰','ERP/BI نمونه + هدف CRM'],
    dealerSales:['فروش خالص نمایندگان','ریال','جمع عملکرد آخرین دوره مشترک؛ بدون جمع با فروش مستقیم','ERP/BI نمونه']
  };
  function report() {
    const facts=[];
    const inside=d=>d>=reportFilter.from && d<=reportFilter.to;
    const allowed=x=>visible(x)&&(!reportFilter.branch||branchOf(x)===reportFilter.branch);
    const add=(metric,x,n,d,source='CRM',route='')=>facts.push({metric,id:x.id,code:x.code||x.id,name:x.name||x.customer,branch:branchOf(x),numerator:n,denominator:d,source,route});
    for(const o of store.opportunities.filter(allowed)) {
      if(!['برنده','باخته'].includes(o.stage)&&inside(o.expectedClose)){add('pipeline',o,o.value,0,'CRM','opportunities');add('forecast',o,o.value*o.probability/100,0,'CRM','opportunities');}
      if(o.stage==='برنده'&&inside(o.closed||today)) add('won',o,o.value,0,'CRM','opportunities');
    }
    for(const lead of store.leads.filter(x=>allowed(x)&&inside(x.created))) add('conversion',lead,lead.status==='تبدیل‌شده'?1:0,1,'CRM','leads');
    for(const c of store.customers.filter(x=>allowed(x)&&x.status!=='غیرفعال')) add('quality',c,[c.name,c.city,c.nationalId,c.phone,c.email].filter(v=>v&&v!=='در انتظار تکمیل').length,5,'CRM','customer');
    if(financial()) for(const i of store.invoices.filter(x=>allowed(x)&&inside(x.date))){add('collection',i,i.collected,i.principal,'حسابداری نمونه','customers');add('overdue',i,i.due<today?i.principal-i.collected:0,0,'حسابداری نمونه','customers');}
    const dealers=store.dealers.filter(x=>allowed(x)&&x.periodFrom>=reportFilter.from&&x.periodTo<=reportFilter.to);
    const latest=dealers.map(x=>x.periodTo).sort().at(-1);
    for(const d of dealers.filter(x=>x.periodTo===latest)){add('dealerSales',d,d.actual,0,'ERP/BI نمونه','dealers');if(d.target>0)add('dealerAchievement',d,d.actual,d.target,'ERP/BI نمونه + هدف CRM','dealers');}
    const keys=Object.keys(definitions).filter(k=>financial()||!['collection','overdue'].includes(k));
    return {facts,keys};
  }
  const value = (key,facts) => {const n=facts.reduce((s,x)=>s+x.numerator,0),d=facts.reduce((s,x)=>s+x.denominator,0);return definitions[key][1]==='درصد'?(d?100*n/d:null):(facts.length?n:null);};
  const link = (route,label,id='') => `<a class="link-button" href="#/${route}${id?'?id='+encodeURIComponent(id):''}" data-route="${route}" data-record="${escapeHtml(id)}">${escapeHtml(label)}</a>`;
  const table = (heads,body) => `<div class="table-wrap"><table><thead><tr>${heads.map(h=>`<th>${escapeHtml(h)}</th>`).join('')}</tr></thead><tbody>${body||`<tr><td colspan="${heads.length}">داده‌ای در این دامنه وجود ندارد.</td></tr>`}</tbody></table></div>`;
  const options = (items,selected='') => items.map(x=>`<option value="${escapeHtml(x)}" ${x===selected?'selected':''}>${escapeHtml(x||'همه شعب مجاز')}</option>`).join('');
  const button = (action,label,id='') => `<button type="button" class="button button--ghost" data-u-action="${action}" data-id="${escapeHtml(id)}">${label}</button>`;
  const header = (title,sub='',actions='')=>pageHeader('ارقام CRM · فضای کاری مشترک',title,sub,actions);

  function reportsView() {
    const r=report(),keys=[...r.keys];
    const preferred=reportFilter.profile==='finance'?['collection','overdue']:reportFilter.profile==='channel'?['dealerAchievement','dealerSales']:['forecast','pipeline'];
    keys.sort((a,b)=>(preferred.includes(a)?preferred.indexOf(a):-1+100)-(preferred.includes(b)?preferred.indexOf(b):-1+100));
    return header('گزارش‌ها و تحلیل','شاخص‌ها از همان دادهٔ صفحات مشتری، فرصت، سرنخ و نماینده محاسبه می‌شوند.')+
      `<section class="panel u-panel"><form id="unifiedReportFilters" class="u-filter" data-u-form="report"><label>نمای گزارش<select name="profile">${[['sales','فروش'],['executive','مدیرعامل'],['branch','شعبه'],['finance','مالی'],['channel','نمایندگان']].map(x=>`<option value="${x[0]}" ${reportFilter.profile===x[0]?'selected':''}>${x[1]}</option>`).join('')}</select></label><label>از تاریخ میلادی (UTC)<input type="date" name="from" value="${reportFilter.from}" required></label><label>تا تاریخ میلادی، شامل<input type="date" name="to" value="${reportFilter.to}" required></label><label>شعبه<select name="branch">${options(['',...branches.filter(b=>!scope||b===scope)],reportFilter.branch)}</select></label><button class="button button--primary">اعمال فیلتر</button>${button('reset-report','بازنشانی')}</form><div id="reportErrors" role="alert"></div><p class="u-period">تصویر فعلی · داده نمونه · انتخاب نما مجوز جدید ایجاد نمی‌کند.</p></section>`+
      `<section class="u-grid">${keys.map(k=>{const f=r.facts.filter(x=>x.metric===k);return `<article class="u-card" data-report-metric="${k}"><span>${definitions[k][0]}</span><strong>${amount(value(k,f))} <small>${definitions[k][1]}</small></strong><small>${fa(f.length)} رکورد</small>${link('report-detail','مشاهده ریزداده',k)}</article>`;}).join('')}</section>`+
      `<section class="panel u-panel"><h2>قیف سرنخ‌های دوره</h2>${[['همه سرنخ‌ها',()=>true],['تماس ثبت‌شده',x=>x.sla==='انجام‌شده'||x.status==='تبدیل‌شده'],['واجد شرایط یا تبدیل‌شده',x=>['واجد شرایط','تبدیل‌شده'].includes(x.status)],['تبدیل‌شده',x=>x.status==='تبدیل‌شده']].map(([name,predicate])=>{const cohort=store.leads.filter(x=>r.facts.some(f=>f.metric==='conversion'&&f.id===x.id));const n=cohort.filter(predicate).length;return `<div class="u-funnel"><span>${name}</span><progress max="${cohort.length||1}" value="${n}"></progress><b>${fa(n)}</b></div>`;}).join('')}</section>`+
      `<section class="panel u-panel"><h2>تفکیک شعب</h2>${table(['شعبه','شاخص','مقدار'],branches.flatMap(b=>keys.map(k=>{const f=r.facts.filter(x=>x.branch===b&&x.metric===k);return f.length?`<tr><td>${b}</td><td>${link('report-detail',definitions[k][0],k)}</td><td>${amount(value(k,f))} ${definitions[k][1]}</td></tr>`:'';})).join(''))}</section>`+
      `<section class="panel u-panel"><h2>فرهنگ شاخص‌ها و منبع</h2>${keys.map(k=>`<details><summary>${definitions[k][0]}</summary><p>${definitions[k][2]}</p><small>${definitions[k][3]}</small></details>`).join('')}<p class="u-period">Forecast تعهد فروش نیست. کیفیت، کامل‌بودن است و صحت اطلاعات را تأیید نمی‌کند. وصول از فاکتور مستقل گرفته می‌شود و از وضعیت سفارش محاسبه نمی‌شود.</p>${canExport()?button('bi','دریافت JSON نمونه BI'):''}</section>`;
  }
  function drillView() {
    const {facts,keys}=report();if(!keys.includes(drillMetric)) return header('شاخص در دسترس نیست')+link('reports','بازگشت به گزارش‌ها');
    const f=facts.filter(x=>x.metric===drillMetric),d=definitions[drillMetric];
    return header(d[0],d[2],link('reports','بازگشت به گزارش‌ها'))+`<section class="panel u-panel"><p>${fa(f.length)} ردیف · مقدار ${amount(value(drillMetric,f))} ${d[1]}</p>${table(['کد / عنوان','شعبه','صورت','مخرج','منبع','رکورد مبدأ'],f.map(x=>`<tr><td>${escapeHtml(x.code)}<small class="cell-sub">${escapeHtml(x.name)}</small></td><td>${escapeHtml(x.branch)}</td><td>${amount(x.numerator)}</td><td>${amount(x.denominator)}</td><td>${x.source}</td><td>${link(x.route,'مشاهده',x.route==='customer'?x.id:'')}</td></tr>`).join(''))}<div class="u-actions">${canExport()?button('csv','دریافت CSV'):''}</div></section>`;
  }
  function ordersView() {
    return header('درخواست‌های سفارش','پیشنهاد تأییدشده ← کنترل اعتبار ← ارسال نمونه به ERP',readOnly()?'':button('new-order','ایجاد از پیشنهاد تأییدشده'))+
      `<section class="panel u-panel">${table(['شماره','مشتری','مبلغ خالص (ریال)','وضعیت','عملیات'],rows('orders').map(o=>`<tr><td>${escapeHtml(o.code)}</td><td>${escapeHtml(o.customer)}</td><td>${amount(o.amount)}</td><td>${statusBadge(o.status)}</td><td>${button('order-detail','مشاهده',o.id)}${!readOnly()&&o.status!=='تکمیل‌شده'?button('order-next','ادامه فرآیند',o.id):''}</td></tr>`).join(''))}</section>`;
  }
  function dealersView() {
    return header('نمایندگان فروش','قرارداد، مشتری، هدف و عملکرد در یک فضای کاری',readOnly()?'':button('new-dealer','ثبت نماینده'))+
      `<section class="panel u-panel">${table(['نماینده','شعبه','وضعیت','هدف (ریال)','عملکرد (ریال)','عملیات'],rows('dealers').map(d=>`<tr><td><b>${escapeHtml(d.name)}</b><small class="cell-sub">${escapeHtml(d.code)}</small></td><td>${escapeHtml(d.branch)}</td><td>${statusBadge(d.status)}</td><td>${amount(d.target)}</td><td>${amount(d.actual)}</td><td>${button('dealer-detail','مشاهده',d.id)}${readOnly()?'':button('dealer-edit','ویرایش / هدف',d.id)}</td></tr>`).join(''))}</section>`;
  }
  function organizationView(){return header('ساختار سازمانی','دامنه نمایشی مشترک برای همه صفحات')+`<section class="panel u-panel">${table(['شرکت','شعبه','مشتری','عملیات'],branches.map(b=>`<tr><td>شرکت اصلی ارقام</td><td>${b}</td><td>${fa(store.customers.filter(x=>x.branch===b).length)}</td><td>${button('select-branch','انتخاب این شعبه',b)}</td></tr>`).join(''))}<p class="u-period">این پیش‌نمایش یک شرکت دارد. ساختار چندشرکتی و مجوزهای واقعی در پروژه MVC موجود است.</p></section>`;}
  function quoteDetail(id){const q=store.quotes.find(x=>x.id===id&&visible(x));if(!q)return;openModal('جزئیات پیشنهاد',`<div class="modal-details"><h3>${escapeHtml(q.code)}</h3><p>${escapeHtml(q.customer)}</p><p>فرصت: ${escapeHtml(q.opportunity)}</p><p>مبلغ: ${amount(q.amount)} ریال · تخفیف ${fa(q.discount)}٪</p><p>خالص: ${amount(q.amount*(1-q.discount/100))} ریال</p>${statusBadge(q.status)}<div class="u-actions">${link('orders','سفارش‌های مرتبط')}${link('reports','تحلیل فروش')}</div></div>`,'بستن',closeModal);}
  function showForm(title,fields,kind,id=''){
    $('#drawerTitle').textContent=title;$('#drawerBody').innerHTML=`<form class="u-form" data-u-form="${kind}" data-id="${id}">${fields}<div class="u-actions"><button class="button button--primary" type="submit">ثبت</button>${button('close-drawer','انصراف')}</div><div class="u-form-error" role="alert"></div></form>`;
    $('#drawer').classList.add('is-open');$('#drawerBackdrop').classList.add('is-open');$('#drawer').setAttribute('aria-hidden','false');
  }
  // Large forms open in a centered popup (same as the MVC follow-up center); short forms keep the drawer.
  function showPopupForm(title,fields,kind,id=''){
    closeDrawer();$('#modal').classList.add('modal--form');$('#modalTitle').textContent=title;$('#modalFooter').innerHTML='';
    $('#modalBody').innerHTML=`<form class="u-form" data-u-form="${kind}" data-id="${id}">${fields}<div class="u-actions"><button class="button button--primary" type="submit">ثبت</button><button type="button" class="button button--ghost" data-action="close-modal">انصراف</button></div><div class="u-form-error" role="alert"></div></form>`;
    $('#modal').classList.add('is-open');$('#modalBackdrop').classList.add('is-open');$('#modal').setAttribute('aria-hidden','false');
  }
  const legacyOpenModal=openModal;openModal=function(...args){$('#modal').classList.remove('modal--form');legacyOpenModal(...args);};
  function closeForms(){closeDrawer();closeModal();}
  function download(name,text,type){const url=URL.createObjectURL(new Blob([text],{type}));const a=document.createElement('a');a.href=url;a.download=name;document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),2000);}
  function csvCell(v){let s=String(v??'');if(/^[\s]*[=+\-@]/.test(s)||/[\r\n\t]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';}
  function exportReport(format){if(!canExport())return;const all=report().facts, f=format==='csv'?all.filter(x=>x.metric===drillMetric):all;if(f.length>5000){toast('بیش از ۵۰۰۰ ردیف؛ دامنه را محدود کنید.','warning');return;}
    if(format==='csv'){const keys=['metric','id','code','name','branch','numerator','denominator','source'];download('crm-'+drillMetric+'.csv','\ufeff'+[keys,...f.map(x=>keys.map(k=>x[k]))].map(r=>r.map(csvCell).join(',')).join('\r\n'),'text/csv;charset=utf-8');}
    else download('crm-reporting-demo-v1.json',JSON.stringify({schemaVersion:'crm.reporting.demo.v1',demo:true,generatedAt:new Date().toISOString(),filter:reportFilter,facts:f},null,2),'application/json');
    store.audit.push({at:new Date().toISOString(),event:'Reporting.Export',format,rows:f.length});save();toast('خروجی نمونه آماده شد.');
  }
  function navigate(route,id='') { closeDrawer();closeModal();state.route=route;state.query='';if(route==='customer'){if(state.selectedCustomerId!==Number(id))af.resetUi();state.selectedCustomerId=Number(id);}if(route==='report-detail')drillMetric=id||'forecast';if(route==='follow-up')fu.select(id);
    const hash='#/'+route+(id?'?id='+encodeURIComponent(id):'');if(location.hash!==hash){try{history.pushState(null,'',hash);}catch{location.hash=hash;}}render();closeMobileMenu(); }
  function readRoute(){const raw=location.hash.replace(/^#\/?/,'');const [route,query='']=raw.split('?');state.route=route||'dashboard';const id=new URLSearchParams(query).get('id');if(state.route==='customer')state.selectedCustomerId=Number(id);if(state.route==='report-detail')drillMetric=id||'forecast';if(state.route==='follow-up')fu.select(id);}
  const selfService=createSelfServicePreview({store,escapeHtml,header,table,button,showForm,amount,idFor,today,save,render:()=>render(),toast,download,closeDrawer,account:()=>account,visible});
  const af=createAccountFilePreview({store,escapeHtml,header,table,button,showForm,amount,idFor,save,render:()=>render(),toast,closeDrawer,link,statusBadge,account:()=>account,visible,
    navigate:(route,id)=>navigate(route,id),selected:()=>state.selectedCustomerId,releases:window.CRM_RELEASES||[],version:window.CRM_VERSION||''});
  const lists=createListPreview({store,escapeHtml,save,toast,closeDrawer,showForm,rows,render:()=>render(),account:()=>account});
  const fu=createFollowUpPreview({store,escapeHtml,save,toast,closeDrawer:()=>closeForms(),showForm,showPopupForm:(...a)=>showPopupForm(...a),render:()=>render(),account:()=>account,navigate:(route,id)=>navigate(route,id)});
  const allowedRoute=route=>af.allowed(route)&&fu.allowed(route)&&(route==='version'||route.startsWith('follow-up')||selfService.allowed(route));
  const knownRoutes=[...af.routes,...fu.routes,'dashboard','customers','customer','customer-quality','customer-duplicates','leads','opportunities','quotes','orders','dealers','workqueue','reports','report-detail','identity','organization','portal','partner-requests','mobile'];
  render = function(){if(!signedIn)return;
    for(const key of ['opportunities','quotes'])for(const record of store[key])record.branch??=store.customers.find(c=>c.name===record.customer)?.branch||scope||'مرکزی';
    const source={};for(const k of ['customers','leads','opportunities','quotes','tasks']){source[k]=store[k];store[k]=rows(k);}
    try {
      const views={orders:ordersView,dealers:dealersView,reports:reportsView,'report-detail':drillView,organization:organizationView,...selfService.views,...af.views,...lists.views,...fu.views};
      if(!allowedRoute(state.route))$('#viewHost').innerHTML=header('این صفحه برای نقش فعلی فعال نیست','از منوی مجاز استفاده کنید.');
      else if(!knownRoutes.includes(state.route))$('#viewHost').innerHTML=header('صفحه پیدا نشد','از منوی اصلی یک صفحه را انتخاب کنید.',link('dashboard','مرکز عملیات'));
      else if(state.route==='customer'&&!store.customers.some(x=>x.id===state.selectedCustomerId))$('#viewHost').innerHTML=header('مشتری در این دامنه موجود نیست','',link('customers','فهرست مشتریان'));
      else if(views[state.route])$('#viewHost').innerHTML=views[state.route]();
      else legacyRender();
      const title=$('#viewHost h1')?.textContent||'ارقام CRM';document.title=title+' | ارقام CRM';
      $('#viewHost').insertAdjacentHTML('afterbegin',`<div class="unified-breadcrumb">${link('dashboard','مرکز عملیات')}<span> / ${escapeHtml(title)}</span></div>`);
      $$('.nav-item').forEach(x=>{const active=x.dataset.route===state.route||(state.route.startsWith('customer')&&x.dataset.route==='customers')||(state.route==='report-detail'&&x.dataset.route==='reports')||(state.route.startsWith('follow-up')&&x.dataset.route==='follow-ups');x.classList.toggle('is-active',active);if(active)x.setAttribute('aria-current','page');else x.removeAttribute('aria-current');});
      $$('.nav-item').forEach(x=>x.hidden=!allowedRoute(x.dataset.route));
      $('#globalSearch').hidden=account==='dealer.user';$('#companySelect').disabled=account==='dealer.user';
      $('#customerNavCount').textContent=fa(store.customers.length);
      if(state.route==='quotes')$$('tbody tr').forEach((tr,i)=>{const q=store.quotes[i];if(q)tr.querySelector('.row-actions')?.insertAdjacentHTML('beforeend',button('quote-detail','مشاهده',q.id));});
      if(readOnly())$$('[data-action],[data-u-action]').filter(x=>['new-customer','new-lead','new-quote','new-task','new-user','approve-quote','reject-quote','convert-lead','next-stage','toggle-task','toggle-user','new-opportunity'].includes(x.dataset.action)).forEach(x=>x.hidden=true);
      $('#viewHost').focus({preventScroll:true});
      const record=state.route==='customer'?state.selectedCustomerId:state.route==='report-detail'?drillMetric:state.route==='follow-up'?(fu.selected()||''):'';
      const hash='#/'+state.route+(record?'?id='+encodeURIComponent(record):'');try{history.replaceState(null,'',hash);}catch{/* Hash links still work when history is restricted. */}
    } catch(error){$('#viewHost').innerHTML=header('نمایش صفحه با خطا روبه‌رو شد','داده نمونه ذخیره‌شده ممکن است ناسازگار باشد.',button('reset-data','بازنشانی داده نمونه'));console.error(error);}
    finally{for(const k of Object.keys(source))store[k]=source[k];}
  };
  handleForm=function(form){const oldIds=new Set(store.leads.map(x=>x.id));legacyForm(form);for(const l of store.leads)if(!oldIds.has(l.id)){l.created=today;l.branch=scope||'مرکزی';}for(const o of store.opportunities)o.expectedClose??=today;save();};
  handleAction=function(action,target){
    if(action==='export'){navigate('reports');return;}
    if(action==='new-customer'){af.customerForm();return;}
    if(action==='convert-lead'){
      const lead=store.leads.find(x=>x.id===Number(target.dataset.id));if(!lead||lead.status==='تبدیل‌شده')return;
      if(lead.status!=='واجد شرایط'){lead.status='واجد شرایط';save();render();return;}
      openModal('تبدیل سرنخ به فرصت',`<p>برای ${escapeHtml(lead.name)} مشتری و فرصت ایجاد شود؟</p>`,'تبدیل',()=>{let c=store.customers.find(x=>x.name===lead.name);if(!c){c={id:idFor('customers'),code:'CUS-DEMO-'+idFor('customers'),name:lead.name,type:'حقوقی',segment:'رشد',city:'',owner:lead.owner,branch:lead.branch||scope||'مرکزی',phone:'',email:'',nationalId:'',status:'در بررسی',quality:20,balance:0,credit:1000000000,initials:lead.name.slice(0,2),color:'teal'};store.customers.push(c);}
        store.opportunities.push({id:idFor('opportunities'),code:'OP-DEMO-'+idFor('opportunities'),name:'همکاری با '+lead.name,customer:c.name,value:2500000000,owner:lead.owner,branch:c.branch,probability:10,stage:'شناسایی',next:'پیگیری',age:0,expectedClose:today});lead.status='تبدیل‌شده';save();navigate('opportunities');toast('مشتری و فرصت ایجاد شدند؛ سرنخ در گزارش تبدیل حفظ شد.');});return;
    }
    legacyAction(action,target);
  };
  function userAction(action,id){
    if(selfService.action(action,id))return;
    if(af.action(action,id))return;
    if(lists.action(action,id))return;
    if(fu.action(action,id))return;
    if(action==='close-drawer'){closeDrawer();return;}
    if(action==='logout'){if(selfService.pending()&&!window.confirm('صف ارسال‌نشده با خروج پاک می‌شود. ادامه می‌دهید؟'))return;selfService.reset();signedIn=false;account='';try{sessionStorage.removeItem('crm-unified-session');}catch{}$('#appShell').hidden=true;$('#loginScreen').hidden=false;$('#loginSubmit').disabled=false;return;}
    if(action==='reset-data'){openModal('بازنشانی داده نمونه','<p>تغییرات همین پیش‌نمایش پاک و داده اولیه بازگردانده شود؟</p>','بازنشانی',()=>{storageSet('argham-crm-unified-v1','');location.reload();});return;}
    if(action==='csv'||action==='bi'){exportReport(action);return;}
    if(action==='reset-report'){reportFilter={from:monthStart,to:nextMonth,branch:'',profile:'sales'};render();return;}
    if(action==='select-branch'){scope=id;$('#companySelect').value=id;reportFilter.branch='';navigate('dashboard');return;}
    if(action==='quote-detail'){quoteDetail(Number(id));return;}
    if(action==='new-order'){const eligible=rows('quotes').filter(q=>q.status==='تأییدشده'&&!store.orders.some(o=>o.quoteId===q.id));if(!eligible.length){toast('پیشنهاد تأییدشده بدون سفارش موجود نیست.','warning');return;}showForm('ایجاد درخواست سفارش',`<label>پیشنهاد تأییدشده<select name="quoteId">${eligible.map(q=>`<option value="${q.id}">${escapeHtml(q.code+' · '+q.customer)}</option>`).join('')}</select></label><p>مبلغ خالص از همان پیشنهاد خوانده می‌شود.</p>`,'order');return;}
    if(action.startsWith('order-')){const o=store.orders.find(x=>x.id===Number(id)&&visible(x));if(!o)return;if(action==='order-next'){const stages=['پیش‌نویس','اعتبار تأیید شد','ارسال به ERP','تحویل‌شده','فاکتورشده','تکمیل‌شده'];o.status=stages[Math.min(stages.indexOf(o.status)+1,stages.length-1)];o.history.push(o.status);if(o.status==='ارسال به ERP')o.erp='ERP-DEMO-'+o.id;save();render();toast('مرحله نمونه سفارش ثبت شد.');}else openModal('جزئیات سفارش',`<h3>${escapeHtml(o.code)}</h3><p>${escapeHtml(o.customer)} · ${amount(o.amount)} ریال</p><p>مرجع ERP نمونه: ${escapeHtml(o.erp||'هنوز ارسال نشده')}</p><ol>${o.history.map(x=>`<li>${escapeHtml(x)}</li>`).join('')}</ol>${link('quotes','پیشنهادهای مرتبط')}`,'بستن',closeModal);return;}
    if(action==='new-dealer'||action==='dealer-edit'){const d=store.dealers.find(x=>x.id===Number(id))||{name:'',branch:scope||'مرکزی',contract:'',target:1};showForm('مشخصات و هدف نماینده',`<label>نام نماینده<input name="name" required value="${escapeHtml(d.name)}"></label><label>شعبه<select name="branch">${options(branches,d.branch)}</select></label><label>قرارداد<input name="contract" required value="${escapeHtml(d.contract)}"></label><label>هدف دوره (ریال)<input type="number" min="1" max="999999999999999" name="target" required value="${d.target}"></label><p>دوره نمونه: ${monthStart} تا ${nextMonth}. عملکرد از منبع نمونه مستقل دریافت می‌شود.</p>`,'dealer',d.id||'');return;}
    if(action==='dealer-detail'){const d=store.dealers.find(x=>x.id===Number(id)&&visible(x));if(!d)return;openModal('پرونده نماینده',`<h3>${escapeHtml(d.name)}</h3><p>${escapeHtml(d.contract)}</p><p>هدف: ${amount(d.target)} · عملکرد: ${amount(d.actual)} ریال</p><p>تحقق: ${amount(d.target?100*d.actual/d.target:null)}٪</p><p>مشتریان مرتبط:</p>${d.customers.map(cid=>{const c=store.customers.find(x=>x.id===cid&&visible(x));return c?`<p>${link('customer',c.name,c.id)}</p>`:'';}).join('')}<div class="u-actions">${link('reports','گزارش عملکرد')}</div>`,'بستن',closeModal);}
  }
  document.addEventListener('click',event=>{
    const nav=event.target.closest('[data-route]');if(nav){event.preventDefault();event.stopImmediatePropagation();navigate(nav.dataset.route,nav.dataset.record||'');return;}
    const action=event.target.closest('[data-u-action]');if(action){event.preventDefault();event.stopImmediatePropagation();userAction(action.dataset.uAction,action.dataset.id||'');}
  },true);
  function enter(user){account=user;signedIn=true;scope=user==='sales.expert'?'مرکزی':'';reportFilter.profile=user==='reporting.ceo'?'executive':user==='finance.manager'?'finance':'sales';
    $('#loginScreen').hidden=true;$('#appShell').hidden=false;$('#companySelect').value=scope;
    $('.sidebar__user strong').textContent=accounts[user];$('.sidebar__user small').textContent='حساب نمونه · '+user;
    try{sessionStorage.setItem('crm-unified-session',user);}catch{/* Session persistence is optional. */}
    readRoute();if(user==='dealer.user')state.route='portal';else if(state.route==='login'||state.route==='portal')state.route='dashboard';render();
  }
  document.addEventListener('submit',event=>{
    const form=event.target;
    if(form.id==='loginForm'){event.preventDefault();event.stopImmediatePropagation();const data=new FormData(form),user=String(data.get('username')||'').trim();if(!accounts[user]||data.get('password')!=='Demo@1405'){$('#startupMessage').textContent='نام کاربری یا رمز نمونه نادرست است. sales.manager / Demo@1405';return;}enter(user);return;}
    if(!form.dataset.uForm)return;event.preventDefault();event.stopImmediatePropagation();if(!form.reportValidity())return;
    const data=Object.fromEntries(new FormData(form));
    try{if(selfService.submit(form.dataset.uForm,data,form.dataset.id))return;if(af.submit(form.dataset.uForm,data,form.dataset.id))return;if(lists.submit(form.dataset.uForm,data,form.dataset.id))return;if(fu.submit(form.dataset.uForm,data,form.dataset.id))return;}catch(error){form.querySelector('.u-form-error').textContent=error.message;return;}
    if(form.dataset.uForm==='report'){const days=(Date.parse(data.to+'T00:00:00Z')-Date.parse(data.from+'T00:00:00Z'))/86400000;if(!Number.isFinite(days)||days<0||days>365||data.from<'2000-01-01'){$('#reportErrors').innerHTML='<p class="u-error">بازه باید از سال ۲۰۰۰ به بعد و بین ۱ تا ۳۶۶ روز باشد.</p>';return;}reportFilter=data;render();return;}
    if(form.dataset.uForm==='order'){const q=store.quotes.find(x=>x.id===Number(data.quoteId)&&visible(x)&&x.status==='تأییدشده');if(!q||store.orders.some(x=>x.quoteId===q.id))return;const id=idFor('orders');store.orders.push({id,code:'ORD-DEMO-'+id,quoteId:q.id,customer:q.customer,amount:q.amount*(1-q.discount/100),status:'پیش‌نویس',erp:'',history:['ایجاد از '+q.code]});}
    if(form.dataset.uForm==='dealer'){const target=Number(data.target);if(!Number.isFinite(target)||target<=0)return;const d=store.dealers.find(x=>x.id===Number(form.dataset.id));if(d)Object.assign(d,{name:data.name,branch:data.branch,contract:data.contract,target});else{const id=idFor('dealers');store.dealers.push({id,code:'DLR-DEMO-'+id,name:data.name,branch:data.branch,contract:data.contract,target,actual:0,status:'در بررسی',customers:[],synced:today,periodFrom:monthStart,periodTo:nextMonth});}}
    save();closeDrawer();render();toast('اطلاعات در مدل مشترک ذخیره شد.');
  },true);
  $('#companySelect').innerHTML=options(['',...branches]);$('#companySelect').setAttribute('aria-label','دامنه شعبه در شرکت نمونه');
  $('#companySelect').addEventListener('change',()=>{scope=$('#companySelect').value;reportFilter.branch='';closeDrawer();closeModal();render();});
  $('.sidebar__user button').removeAttribute('data-toast');$('.sidebar__user button').dataset.uAction='logout';$('.sidebar__user button').title='خروج';
  $('#globalSearch').placeholder='جست‌وجوی مشتری یا کد…';
  window.addEventListener('popstate',()=>{readRoute();render();});window.addEventListener('hashchange',()=>{readRoute();render();});
  window.addEventListener('beforeunload',event=>{if(selfService.pending()){event.preventDefault();event.returnValue='';}});
  if(typeof setInterval==='function')setInterval(()=>selfService.tick(),30000);
  $('#startupMessage').textContent='آماده است؛ مدیر: sales.manager، نماینده: dealer.user، رمز: Demo@1405. همه صفحات در همین فایل باز می‌شوند.';
  $('#loginSubmit').disabled=false;
  // Remove credentials left by older preview versions; they are never used for sign-in.
  if(/[?&](username|password)=/i.test(location.search))try{history.replaceState(null,'',location.pathname+location.hash);}catch{}
  try{const previous=sessionStorage.getItem('crm-unified-session');if(accounts[previous])enter(previous);}catch{}
})();
