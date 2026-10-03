/* Ephemeral outbox: no localStorage, IndexedDB, service worker or background sync. */
(function (root) {
  "use strict";
  class MobileOutbox {
    constructor(context, clock = () => Date.now()) { this.context = context; this.clock = clock; this.items = []; this.blocked = false; this.busy = false; }
    expire() {
      if (this.items.some(x => this.clock() - x.created >= 8 * 60 * 60 * 1000)) {
        this.items.length = 0; this.blocked = true;
        throw new Error("صف پس از ۸ ساعت پاک شد؛ وضعیت سرور را تازه‌سازی کنید.");
      }
    }
    add(url, values, context) {
      this.expire();
      if (context !== this.context || this.blocked) throw new Error("ابتدا تعارض یا تغییر نشست را بررسی کنید.");
      if (this.items.length >= 50) throw new Error("صف پر است؛ ابتدا عملیات را ارسال کنید.");
      const key = values.OperationId;
      if (!key) throw new Error("شناسهٔ عملیات لازم است.");
      const old = this.items.find(x => x.values.OperationId === key);
      if (old) { if (JSON.stringify(old.values) !== JSON.stringify(values) || old.url !== url) throw new Error("شناسهٔ عملیات تکراری است."); return; }
      this.items.push({ url, values: { ...values }, created: this.clock() });
    }
    async sync(send, onAck = () => {}) {
      this.expire();
      if (this.busy || this.blocked) return;
      this.busy = true;
      try {
        while (this.items.length) {
          this.expire();
          const item = this.items[0];
          let result;
          try { result = await send(item); }
          catch { throw new Error("ارتباط قطع است یا پاسخ دریافت نشد؛ عملیات با همان شناسه در صف ماند."); }
          if (!result.ok) { this.blocked = true; throw new Error(result.message || "ارسال متوقف شد؛ نشست و نسخهٔ رکورد را بررسی کنید."); }
          this.items.shift(); onAck(result.ack, item);
        }
      } finally { this.busy = false; }
    }
    clear() { this.items.length = 0; this.blocked = false; }
  }
  root.CrmMobileOutbox = MobileOutbox;
  if (typeof document === "undefined") return;
  const host = document.getElementById("mobileWorkspace"); if (!host) return;
  const context = host.dataset.context, outbox = new MobileOutbox(context);
  const status = document.getElementById("mobileSyncStatus"), detail = document.getElementById("mobileSyncDetail");
  const labels = ["برنامه‌ریزی‌شده", "در حال بازدید", "انجام‌شده", "لغوشده"], names = ["Planned", "CheckedIn", "Completed", "Cancelled"];
  const uuid = () => crypto.randomUUID();
  const message = text => { detail.textContent = text; status.textContent = `صف ارسال: ${outbox.items.length} · ${navigator.onLine ? "متصل" : "آفلاین"}${outbox.blocked ? " · نیاز به بررسی" : ""}`; };
  function paint(card, state, version, pending) {
    card.dataset.status = state; card.dataset.version = String(version);
    card.querySelector("[data-visit-status]").textContent = labels[names.indexOf(state)] + (pending ? " · در صف ارسال" : "");
    const form = card.querySelector("form[data-mobile-action]"); if (!form) return;
    form.elements.ExpectedVersion.value = String(version);
    const options = form.elements.Status.options;
    for (const option of options) option.disabled = !(state === "Planned" ? ["CheckedIn", "Cancelled"].includes(option.value) : state === "CheckedIn" && option.value === "Completed");
    form.elements.Status.value = state === "Planned" ? "CheckedIn" : "Completed";
    form.querySelector('button[type="submit"]').disabled = ["Completed", "Cancelled"].includes(state) || outbox.blocked;
  }
  const cards = () => host.querySelectorAll("[data-visit]");
  cards().forEach(card => paint(card, card.dataset.status, Number(card.dataset.version), false));
  async function sync() {
    try {
      await outbox.sync(async item => {
        const body = new FormData(); Object.entries(item.values).forEach(([k,v]) => body.append(k,v));
        const controller=new AbortController(), timeout=setTimeout(()=>controller.abort(),15000);
        let response;
        try { response = await fetch(item.url, { method:"POST", body, credentials:"same-origin", redirect:"manual", signal:controller.signal, headers:{ Accept:"application/json", "HX-Request":"true" } }); }
        finally { clearTimeout(timeout); }
        if (response.headers.get("HX-Redirect") || !response.headers.get("Content-Type")?.includes("application/json")) return { ok:false, message:"نشست پایان یافته یا پاسخ معتبر نیست؛ ورود و وضعیت سرور را بررسی کنید." };
        const data = await response.json();
        return response.ok && data.visitId ? { ok:true, ack:data } : { ok:false, message:data.message || `ارسال متوقف شد (${response.status}).` };
      }, (ack, item) => {
        const card = [...cards()].find(x => x.dataset.visit === ack.visitId);
        if (card && !outbox.items.some(x => x.url === item.url)) {
          paint(card, typeof ack.visit.status === "number" ? names[ack.visit.status] : ack.visit.status, ack.visit.version, false);
          card.querySelector("[data-outcome]").textContent = ack.visit.outcome;
        }
      });
      message(outbox.items.length ? "عملیات در صف است؛ ارسال دوباره را انتخاب کنید." : `آخرین ارسال موفق: ${new Date().toLocaleString("fa-IR")}`);
    } catch (e) { message(e.message); }
  }
  host.addEventListener("submit", event => {
    const form = event.target.closest("[data-mobile-action]"); if (!form) return;
    event.preventDefault(); event.stopImmediatePropagation();
    try {
      if (!form.reportValidity()) return;
      const card = form.closest("[data-visit]"), next = form.elements.Status.value;
      if (next !== "CheckedIn" && !form.elements.Outcome.value.trim()) throw new Error("نتیجه یا دلیل را وارد کنید.");
      if (next !== "CheckedIn" || !form.elements.LocationConsent.checked) { form.elements.Latitude.value=""; form.elements.Longitude.value=""; }
      form.elements.OperationId.value=uuid(); form.elements.OccurredAtUtc.value=new Date().toISOString();
      const values = Object.fromEntries(new FormData(form).entries());
      outbox.add(form.action, values, context);
      paint(card, next, Number(values.ExpectedVersion)+1, true);
      form.elements.Latitude.value=""; form.elements.Longitude.value=""; form.elements.LocationConsent.checked=false;
      form.elements.Outcome.value=""; message("عملیات در حافظهٔ همین صفحه ثبت شد.");
      if (navigator.onLine) void sync();
    } catch(e) { message(e.message); }
  });
  host.addEventListener("click", event => {
    const button = event.target.closest("[data-location]"); if (!button) return;
    const form = button.closest("form"), line=form.querySelector("[data-location-status]");
    if (!form.elements.LocationConsent.checked || form.elements.Status.value !== "CheckedIn") { line.textContent="برای دریافت موقعیت، شروع بازدید و رضایت را انتخاب کنید."; return; }
    if (!navigator.geolocation) { line.textContent="موقعیت‌یابی در این مرورگر در دسترس نیست."; return; }
    navigator.geolocation.getCurrentPosition(position => {
      if (!form.elements.LocationConsent.checked || form.elements.Status.value !== "CheckedIn") return;
      form.elements.Latitude.value=position.coords.latitude.toFixed(6); form.elements.Longitude.value=position.coords.longitude.toFixed(6); line.textContent="موقعیت دریافت شد؛ هنگام ثبت شروع ارسال می‌شود.";
    }, () => { line.textContent="موقعیت دریافت نشد؛ می‌توانید بدون موقعیت ادامه دهید."; }, { timeout:10000, maximumAge:0, enableHighAccuracy:false });
  });
  host.addEventListener("change", e => { if (e.target.name==="LocationConsent" && !e.target.checked) { const f=e.target.form; f.elements.Latitude.value=""; f.elements.Longitude.value=""; f.querySelector("[data-location-status]").textContent="موقعیت پاک شد."; } });
  document.getElementById("mobileSync").addEventListener("click", sync);
  document.getElementById("mobileDiscard").addEventListener("click", () => { if (!outbox.items.length || confirm("عملیات ارسال‌نشده حذف و دادهٔ سرور دریافت شود؟")) { outbox.clear(); location.reload(); } });
  addEventListener("beforeunload", event => { if (outbox.items.length) { event.preventDefault(); event.returnValue=""; } });
  addEventListener("online", () => message("ارتباط برقرار است؛ برای ارسال صف دکمهٔ ارسال را بزنید."));
  addEventListener("offline", () => message("ارتباط قطع است؛ صف فقط تا بسته شدن صفحه می‌ماند."));
  setInterval(() => { try { outbox.expire(); } catch(e) { message(e.message); } }, 30000);
})(globalThis);
