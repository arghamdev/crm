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
