// Executes the actual preview scripts and route/form logic in a minimal DOM harness.
// This is not a layout, Chromium or accessibility test; those remain separate gates.
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const html=readFileSync(resolve(root,'preview/crm-unified.html'),'utf8');
assert(!/<script[^>]+src=|<iframe/i.test(html),'Preview must be self-contained.');
assert(html.includes('id="loginSubmit"')&&html.includes('disabled'),'Login waits until scripts have initialized.');
function runtime({storageBlocked=false,hash='#/reports',corruptStore=false}={}) {
  const nodes=new Map(),listeners=[],errors=[],files=[];
  const node=selector=>{
    if(nodes.has(selector))return nodes.get(selector);
    const classes=new Set(),n={selector,innerHTML:'',textContent:'',value:'',hidden:false,disabled:true,dataset:{},
      classList:{add:(...xs)=>xs.forEach(x=>classes.add(x)),remove:(...xs)=>xs.forEach(x=>classes.delete(x)),toggle:(x,b)=>{const yes=b??!classes.has(x);yes?classes.add(x):classes.delete(x);return yes;},contains:x=>classes.has(x)},
      setAttribute(k,v){this[k]=v;},removeAttribute(k){delete this[k];},getAttribute(k){return this[k];},
      querySelector:s=>node(selector+' '+s),querySelectorAll:()=>[],
      insertAdjacentHTML(position,value){this.innerHTML=position==='afterbegin'?value+this.innerHTML:this.innerHTML+value;},
      append(){},remove(){},click(){if(this.download)files.push(this.download);},focus(){},
      addEventListener(type,fn,options){listeners.push({selector,type,fn,capture:options===true});},
      closest:()=>null,reportValidity:()=>true};nodes.set(selector,n);return n;
  };
  const storage=new Map();if(corruptStore)storage.set('argham-crm-unified-v1',corruptStore==='shape'?'"invalid saved object"':'{broken');
  const localStorage={getItem:k=>{if(storageBlocked)throw new Error('Blocked storage');return storage.get(k)||null;},setItem:(k,v)=>{if(storageBlocked)throw new Error('Blocked storage');storage.set(k,v);},removeItem:k=>storage.delete(k)};
  const location={hash,search:'',pathname:'/crm-unified.html',reload(){}};
  const document={querySelector:node,querySelectorAll:()=>[],getElementById:id=>node('#'+id),documentElement:node('html'),body:node('body'),createElement:tag=>node('new-'+tag),addEventListener:(type,fn,options)=>listeners.push({type,fn,capture:options===true})};
  const winListeners=[];
  const context={document,location,history:{pushState:(a,b,url)=>{location.hash=url;},replaceState:(a,b,url)=>{location.hash=url;}},localStorage,sessionStorage:localStorage,Intl,URLSearchParams,
    URL:{createObjectURL:()=> 'blob:sample',revokeObjectURL(){}},Blob,console:{log(){},error:x=>errors.push(String(x))},setTimeout(){},clearTimeout(){},FormData:class{constructor(form){this.data=form.data||{};}get(k){return this.data[k];}entries(){return Object.entries(this.data);} [Symbol.iterator](){return this.entries()[Symbol.iterator]();}}};
  context.window=context;context.addEventListener=(type,fn)=>winListeners.push({type,fn});
  vm.createContext(context);
  for(const script of html.matchAll(/<script>([\s\S]*?)<\/script>/g))vm.runInContext(script[1],context);
  function dispatch(type,target){let stopped=false;const event={target,preventDefault(){},stopImmediatePropagation(){stopped=true;}};
    for(const h of [...listeners.filter(x=>x.capture),...listeners.filter(x=>!x.capture)])if(!h.selector&&h.type===type&&!stopped)h.fn(event);
  }
  const form=(kind,data,id='')=>({id:kind==='login'?'loginForm':'',data,dataset:kind==='login'?{}:{uForm:kind,id},reportValidity:()=>true,querySelector:s=>node('form '+s),matches:()=>false});
  const click=dataset=>dispatch('click',{closest:s=>s==='[data-route]'&&dataset.route?{dataset}:s==='[data-u-action]'&&dataset.uAction?{dataset}:null});
  return {context,nodes,node,errors,files,location,dispatch,form,click,eval:code=>vm.runInContext(code,context)};
}
for(const storageBlocked of [false,true]){
  const app=runtime({storageBlocked,corruptStore:true});
  assert.equal(app.node('#loginSubmit').disabled,false);
  app.dispatch('submit',app.form('login',{username:'sales.manager',password:'wrong'}));
  assert.match(app.node('#startupMessage').textContent,/نادرست/);
  app.dispatch('submit',app.form('login',{username:'sales.manager',password:'Demo@1405'}));
  assert.equal(app.node('#loginScreen').hidden,true);
  assert.deepEqual(app.errors,[], 'Initial route must render without runtime errors.');
  assert.match(app.node('#viewHost').innerHTML,/data-report-metric="forecast"/,'Deep link survives login.');
  assert(!app.node('#viewHost').innerHTML.includes('data-report-metric="collection"'));
  for(const route of ['dashboard','customers','leads','opportunities','quotes','orders','dealers','workqueue','reports','identity','organization']){
    app.click({route});assert(!app.node('#viewHost').innerHTML.includes('نمایش صفحه با خطا'),'Route '+route);assert.match(app.node('#viewHost').innerHTML,/<h1>/);
  }
  app.click({route:'reports'});
  app.dispatch('submit',app.form('report',{from:'2026-09-03',to:'2026-09-01',branch:'',profile:'sales'}));
  assert.match(app.node('#reportErrors').innerHTML,/۳۶۶/);
  app.click({route:'report-detail',record:'forecast'});assert.match(app.node('#viewHost').innerHTML,/پیش‌بینی وزنی/);
  app.click({uAction:'csv'});assert(app.files.includes('crm-forecast.csv'));
  app.click({route:'dealers'});app.dispatch('submit',app.form('dealer',{name:'نماینده پایلوت جنوب',branch:'اهواز',contract:'قرارداد تست',target:'14000000000'},'81'));
  assert.equal(app.eval('store.dealers.find(x=>x.id===81).target'),14000000000);
  app.click({route:'report-detail',record:'dealerAchievement'});assert.match(app.node('#viewHost').innerHTML,/۱۴٬۰۰۰٬۰۰۰٬۰۰۰/,'Dealer target is shared with reports.');
  app.click({route:'orders'});app.eval('store.quotes[0].status="تأییدشده"');const quoteId=app.eval('store.quotes[0].id');
  app.dispatch('submit',app.form('order',{quoteId:String(quoteId)}));const count=app.eval('store.orders.length');
  app.dispatch('submit',app.form('order',{quoteId:String(quoteId)}));assert.equal(app.eval('store.orders.length'),count,'Order creation is idempotent per quote.');
  app.click({uAction:'logout'});assert.equal(app.node('#appShell').hidden,true);
  app.dispatch('submit',app.form('login',{username:'finance.manager',password:'Demo@1405'}));app.click({route:'reports'});assert.match(app.node('#viewHost').innerHTML,/data-report-metric="collection"/);
  assert.deepEqual(app.errors,[]);
}
console.log('Unified preview cumulative checks passed: login, blocked storage, corrupt JSON, previous routes, deep links, filters, shared dealer target, CSV, order creation and logout.');
const corrupt=runtime({corruptStore:'shape'});
corrupt.dispatch('submit',corrupt.form('login',{username:'sales.manager',password:'Demo@1405'}));
assert.deepEqual(corrupt.errors,[]);
const p10=runtime({hash:'#/mobile'});
const login=user=>p10.dispatch('submit',p10.form('login',{username:user,password:'Demo@1405'}));
login('dealer.user');assert.match(p10.node('#viewHost').innerHTML,/پرتال نماینده/);
p10.click({uAction:'p10-request'});assert.match(p10.node('#drawerBody').innerHTML,/data-u-form="p10-request"/);
p10.dispatch('submit',p10.form('p10-request',{kind:'Lead',subject:'سرنخ پرتال آزمون',description:'شرح مشتری'}));
const requestId=p10.eval('store.portalRequests.at(-1).id');
p10.click({uAction:'p10-invoice'});assert(p10.files.includes('dealer-invoice-demo.csv'));
p10.click({route:'mobile'});assert.match(p10.node('#viewHost').innerHTML,/فعال نیست/);
p10.click({uAction:'logout'});login('sales.manager');p10.click({route:'partner-requests'});
assert.match(p10.node('#viewHost').innerHTML,/سرنخ پرتال آزمون/);
p10.dispatch('submit',p10.form('p10-review',{version:'1',status:'Accepted',reply:'سرنخ ایجاد شد'},String(requestId)));
assert.equal(p10.eval('store.portalRequests.at(-1).status'),'Accepted');
assert.equal(p10.eval('store.leads.at(-1).source'),'DealerPortal');
p10.click({route:'leads'});assert.match(p10.node('#viewHost').innerHTML,/سرنخ پرتال آزمون/);
p10.click({route:'mobile'});assert.match(p10.node('#viewHost').innerHTML,/بازدیدهای من/);
p10.click({uAction:'p10-offline'});
p10.dispatch('submit',p10.form('p10-visit',{version:'1',status:'CheckedIn',outcome:'شروع'},'111'));
assert.equal(p10.eval('store.visits[0].status'),'Planned','Offline operation must not persist before sync.');
p10.dispatch('submit',p10.form('p10-visit',{version:'2',status:'Completed',outcome:'نتیجه بازدید'},'111'));
p10.click({uAction:'p10-offline'});p10.click({uAction:'p10-sync'});
assert.equal(p10.eval('store.visits[0].status'),'Completed');assert.equal(p10.eval('store.visits[0].version'),3);
p10.dispatch('submit',p10.form('p10-plan',{customerId:'1',planned:'2026-09-27T10:00',purpose:'آزمون تعارض'}));
const visitId=p10.eval('store.visits.at(-1).id');p10.click({uAction:'p10-offline'});
p10.dispatch('submit',p10.form('p10-visit',{version:'1',status:'CheckedIn',outcome:'شروع'},String(visitId)));
p10.eval('store.visits.at(-1).version=2');p10.click({uAction:'p10-offline'});p10.click({uAction:'p10-sync'});
assert.equal(p10.eval('store.visits.at(-1).status'),'Planned','Conflict must not overwrite server state.');
p10.click({uAction:'p10-discard'});p10.click({uAction:'logout'});login('sales.expert');p10.click({route:'mobile'});
assert(!p10.node('#viewHost').innerHTML.includes('نتیجه بازدید'),'Another owner cannot see the visit.');
assert.deepEqual(p10.errors,[]);
console.log('Priority-10 unified preview checks passed: dealer portal, shared review/lead, invoice download, mobile FIFO offline queue, conflict and owner scope.');
// v1.1–v1.8 in the preview: account file, quick actions, outcome + next action, payments, customer form, new pages and version.
const af=runtime({hash:'#/customer?id=1'});
af.dispatch('submit',af.form('login',{username:'sales.manager',password:'Demo@1405'}));
let page=af.node('#viewHost').innerHTML;
for(const text of ['ایجاد فرصت','برنامه‌ریزی تماس','برنامه‌ریزی جلسه','ایجاد وظیفه','ایجاد یادداشت','ایجاد سرنخ','فعالیت‌های معوق','رکوردهای مرتبط','تاریخچه تغییرات'])assert(page.includes(text),'Account file shows '+text);
af.click({uAction:'af-new-call',id:'1'});assert.match(af.node('#drawerBody').innerHTML,/data-u-form="af-activity"/);
const start=af.eval('(()=>{const d=new Date(Date.now()+86400000);const p=n=>String(n).padStart(2,"0");return `${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())}T10:00`;})()');
const callData={op:'op-test-1',subject:'تماس آزمون پیش‌نمایش',contact:'علی رستگار',owner:'مهدی نادری',start,duration:'15',reminder:'15',opportunityId:''};
const before=af.eval('store.activities.length');
af.dispatch('submit',af.form('af-activity',callData,'call:1'));af.dispatch('submit',af.form('af-activity',callData,'call:1'));
assert.equal(af.eval('store.activities.length'),before+1,'Double submit stores the call once.');
const callId=af.eval('store.activities.at(-1).id');
assert.match(af.node('#viewHost').innerHTML,/تماس آزمون پیش‌نمایش/);
af.dispatch('submit',af.form('af-complete',{op:'op-test-2',outcome:'',result:''},String(callId)));
assert.match(af.node('form .u-form-error').textContent,/نتیجه/,'Completing a call needs an outcome.');
af.dispatch('submit',af.form('af-complete',{op:'op-test-3',outcome:'مشتری نمونه خواست',result:'پاسخ داد',nextType:'task',nextSubject:'ارسال نمونه',nextStart:start},String(callId)));
assert.equal(af.eval(`store.activities.find(x=>x.id===${callId}).status`),'done');
assert.equal(af.eval('store.activities.at(-1).followUpOf'),callId,'Next action is planned after completion.');
af.dispatch('submit',af.form('af-payment',{op:'op-test-4',direction:'دریافت',method:'نقد',amount:'۵۰۰٬۰۰۰',currency:'IRR',date:start.slice(0,10)<=new Date().toISOString().slice(0,10)?start.slice(0,10):new Date().toISOString().slice(0,10)},'1'));
const payId=af.eval('store.payments.at(-1).id');
af.click({uAction:'af-pay-approve',id:String(payId)});assert.equal(af.eval(`store.payments.find(x=>x.id===${payId}).status`),'ثبت‌شده','The recorder cannot approve their own payment.');
af.click({uAction:'af-tab',id:'related'});page=af.node('#viewHost').innerHTML;
const payTotals=(page.match(/خالص تأییدشده[^<]*<\/span>(.*?)<\/div>/)||[])[1]||'';
assert(payTotals.includes('۱٬۲۰۰٬۰۰۰٬۰۰۰ IRR')&&!payTotals.includes('USD'),'Payment totals use approved items only, per currency.');
af.click({uAction:'af-doc-unlink',id:'1'});assert.equal(af.eval('store.documents.find(x=>x.id===1).links.length'),0);assert.equal(af.eval('store.documents.length')>=2,true,'Unlinking keeps the document.');
af.click({uAction:'af-campaign-leave',id:'1'});assert.equal(af.eval('store.campaigns.length'),2,'Leaving a campaign keeps the campaign.');
af.click({uAction:'af-new-lead',id:'1'});af.dispatch('submit',af.form('af-lead',{op:'op-test-5',name:'نیاز خط دوم',contact:'علی رستگار',source:'مشتری فعلی',owner:'مهدی نادری'},'1'));
assert.equal(af.eval('store.leads.at(-1).customerId'),1,'«ایجاد سرنخ» links the lead to the account.');
af.click({uAction:'af-tab',id:'history'});assert.match(af.node('#viewHost').innerHTML,/سرنخ برای حساب ثبت شد/);
af.click({route:'customers'});af.eval('handleAction("new-customer",{dataset:{}})');assert.match(af.node('#drawerBody').innerHTML,/data-u-form="af-customer"/);
af.click({uAction:'af-customer-kind',id:'حقیقی'});
af.dispatch('submit',af.form('af-customer',{op:'op-test-6',firstName:'زهرا',lastName:'منشی',nationalId:'0499370898',mobile:'09120001122',branch:'مرکزی'},'حقیقی'));
assert.match(af.node('form .u-form-error').textContent,/کد ملی/,'An invalid national code is rejected.');
af.dispatch('submit',af.form('af-customer',{op:'op-test-7',firstName:'زهرا',lastName:'منشی',nationalId:'۰۴۹۹۳۷۰۸۹۹',mobile:'09120001122',branch:'مرکزی',contactName:'رابط آزمون'},'حقیقی'));
assert.equal(af.eval('store.customers.at(-1).name'),'زهرا منشی');assert.equal(af.eval('store.contacts.at(-1).name'),'رابط آزمون');
for(const route of ['version','service','commissions','notifications']){af.click({route});assert(!af.node('#viewHost').innerHTML.includes('نمایش صفحه با خطا'),route);assert.match(af.node('#viewHost').innerHTML,/<h1>/);}
af.click({route:'version'});assert(af.node('#viewHost').innerHTML.includes('v'+af.eval('window.CRM_VERSION'))&&af.node('#viewHost').innerHTML.includes('نسخهٔ جاری'),'Version page lists the current release.');
assert(html.includes('class="version-pill"'),'Top bar shows the version badge.');
af.click({route:'commissions'});af.click({uAction:'cm-calc'});assert(af.eval('store.commissions.length')>0,'Commission is calculated.');
af.click({route:'service'});af.click({uAction:'sv-create'});af.dispatch('submit',af.form('sv-new',{op:'op-test-8',customerId:'1',subject:'شکایت آزمون',priority:'بالا'},''));
assert.equal(af.eval('store.serviceCases.at(-1).subject'),'شکایت آزمون');
af.click({uAction:'logout'});af.dispatch('submit',af.form('login',{username:'sales.expert',password:'Demo@1405'}));af.click({route:'customer',record:'1'});
assert(!af.node('#viewHost').innerHTML.includes('data-u-action="af-pay-approve"'),'An expert cannot approve payments.');
af.click({uAction:'logout'});af.dispatch('submit',af.form('login',{username:'dealer.user',password:'Demo@1405'}));af.click({route:'version'});assert.match(af.node('#viewHost').innerHTML,/نسخه و تغییرات/);
af.click({route:'commissions'});assert.match(af.node('#viewHost').innerHTML,/فعال نیست/);
assert.deepEqual(af.errors,[]);
console.log('v1.8 preview checks passed: account file, six quick actions, double submit, outcome + next action, payment SoD and totals, unlink without delete, lead from account, history, customer form validation, service, commissions, notifications and version page.');
// v1.9 list workspace (سرنخ‌ها و مشتریان) in the preview: tabs, tiles, filters, selection, bulk actions, layouts and import.
const lk=runtime({hash:'#/leads'});
lk.dispatch('submit',lk.form('login',{username:'sales.manager',password:'Demo@1405'}));
let lp=lk.node('#viewHost').innerHTML;
for(const text of ['مدیریت سرنخ‌های فروش','همه سرنخ‌ها','سرنخ‌های من','بدون اقدام بعدی','نیازمند اقدام','مشکوک به تکرار','list-pager','مورد در صفحه','ورود اطلاعات'])assert(lp.includes(text),'Lead list shows '+text);
assert(/class="bulk-bar" hidden/.test(lp),'The bulk bar is hidden until a row is selected.');
lk.click({uAction:'ls-view',id:'leads:overdue'});lp=lk.node('#viewHost').innerHTML;
assert(lp.includes('تجارت نوین پارس')&&!lp.includes('پایدار انرژی خاور'),'«معوق» lists only the lead past its due time.');
lk.click({uAction:'ls-view',id:'leads:all'});
lk.dispatch('change',{dataset:{lsFilter:'leads:source'},value:'نمایشگاه'});lp=lk.node('#viewHost').innerHTML;
assert(lp.includes('پایدار انرژی خاور')&&!lp.includes('تجارت نوین پارس'),'The source filter narrows the rows.');
lk.dispatch('change',{dataset:{lsFilter:'leads:source'},value:''});
lk.dispatch('change',{dataset:{lsSelect:'leads:12'},checked:true});lp=lk.node('#viewHost').innerHTML;
assert(lp.includes('<b>۱</b> مورد انتخاب شد')&&!/class="bulk-bar" hidden/.test(lp),'Selecting a row shows the bulk bar with its count.');
lk.click({uAction:'ls-bulk-next',id:'leads'});assert.match(lk.node('#drawerBody').innerHTML,/data-u-form="ls-next"/);
lk.dispatch('submit',lk.form('ls-next',{next:'ارسال پیشنهاد قیمت',at:'2000-01-01T10:00'},'12'));
assert.match(lk.node('form .u-form-error').textContent,/آینده/,'A past next-action time is rejected.');
const future=lk.eval('(()=>{const d=new Date(Date.now()+2*86400000);const p=n=>String(n).padStart(2,"0");return `${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())}T10:00`;})()');
lk.dispatch('submit',lk.form('ls-next',{next:'ارسال پیشنهاد قیمت',at:future},'12'));
assert.equal(lk.eval('store.leads.find(x=>x.id===12).next'),'ارسال پیشنهاد قیمت','The bulk next action is stored on the selected lead.');
assert(/class="bulk-bar" hidden/.test(lk.node('#viewHost').innerHTML),'The selection is cleared after the bulk action.');
lk.dispatch('change',{dataset:{lsSelect:'leads:14'},checked:true});lk.click({uAction:'ls-bulk-assign',id:'leads'});assert.match(lk.node('#drawerBody').innerHTML,/data-u-form="ls-assign"/);
lk.dispatch('submit',lk.form('ls-assign',{owner:'نرگس یوسفی',reason:'ارجاع آزمون'},'12,14'));
assert.equal(lk.eval('store.leads.filter(x=>[12,14].includes(x.id)&&x.owner==="نرگس یوسفی").length'),2,'Bulk assign moves every selected lead.');
lk.click({uAction:'ls-layout',id:'leads:cards'});assert.match(lk.node('#viewHost').innerHTML,/class="list-cards"/);
lk.click({uAction:'ls-sort',id:'leads:score'});assert.match(lk.node('#viewHost').innerHTML,/sort-link is-active|list-cards/);
const leadCount=lk.eval('store.leads.length');
lk.click({uAction:'ls-import',id:'leads'});lk.dispatch('submit',lk.form('ls-import',{rows:'شرکت واردات آزمون، مینا شریفی، نمایشگاه\n، بدون نام'},''));
assert.equal(lk.eval('store.leads.length'),leadCount+1,'The import creates the named row only.');
lk.click({route:'customers'});let cp=lk.node('#viewHost').innerHTML;
for(const text of ['مشتریان و حساب‌ها','همه حساب‌ها','حساب‌های من','بدون پیگیری','پیگیری امروز','نیازمند تکمیل','کیفیت داده','تماس پیگیری پرداخت فاکتور'])assert(cp.includes(text),'Customer list shows '+text);
lk.click({uAction:'ls-view',id:'customers:incomplete'});cp=lk.node('#viewHost').innerHTML;
assert(cp.includes('بازرگانی نخل جنوب')&&!cp.includes('صنایع غذایی سپهر'),'«نیازمند تکمیل» lists accounts with missing contact data.');
lk.dispatch('change',{dataset:{lsSelect:'customers:3'},checked:true});lk.click({uAction:'ls-bulk-task',id:'customers'});
lk.dispatch('submit',lk.form('ls-task',{subject:'پیگیری گروهی پیش‌نمایش',at:future},'3'));
assert.equal(lk.eval('store.activities.at(-1).subject'),'پیگیری گروهی پیش‌نمایش','The bulk task is planned on the selected account.');
assert.equal(lk.eval('store.activities.at(-1).customerId'),3);
lk.click({uAction:'logout'});lk.dispatch('submit',lk.form('login',{username:'reporting.ceo',password:'Demo@1405'}));lk.click({route:'leads'});
assert(!lk.node('#viewHost').innerHTML.includes('data-ls-select'),'A read-only role gets no row selection.');
assert.deepEqual(lk.errors,[]);
console.log('v1.9 preview checks passed: lead and customer list tabs, tiles, filters, selection, bulk next action/assign/task, cards, sort and import.');
