(() => {
  "use strict";

  const root = document.documentElement;
  const drawer = document.getElementById("drawer");
  const drawerBody = document.getElementById("drawerBody");
  const drawerBackdrop = document.getElementById("drawerBackdrop");
  const sidebar = document.getElementById("sidebar");
  const mobileOverlay = document.getElementById("mobileOverlay");
  const token = document.querySelector('meta[name="csrf-token"]')?.content;
  const unsafeMethods = new Set(["post", "put", "patch", "delete"]);
  const changeEvents = [
    "customerChanged", "leadChanged", "opportunityChanged",
    "quoteChanged", "orderChanged", "dealerChanged", "workItemChanged", "userChanged", "organizationChanged", "duplicateChanged", "mergeChanged", "serviceChanged"
  ];

  function openDrawer() {
    drawer?.classList.add("is-open");
    drawerBackdrop?.classList.add("is-open");
    drawer?.setAttribute("aria-hidden", "false");
    drawer?.querySelector("input, select, textarea")?.focus();
  }

  function closeDrawer() {
    drawer?.classList.remove("is-open");
    drawerBackdrop?.classList.remove("is-open");
    drawer?.setAttribute("aria-hidden", "true");
    if (drawerBody) drawerBody.innerHTML = "";
  }

  function closeSidebar() {
    sidebar?.classList.remove("is-open");
    mobileOverlay?.classList.remove("is-open");
  }

  function showToast(message, warning = false) {
    const region = document.getElementById("toastRegion");
    if (!region || !message) return;
    const item = document.createElement("div");
    item.className = "toast" + (warning ? " toast--warning" : "");
    item.innerHTML = '<span><svg><use href="#' + (warning ? "i-clock" : "i-check") + '"></use></svg></span><p></p><button type="button" aria-label="بستن"><svg><use href="#i-close"></use></svg></button>';
    item.querySelector("p").textContent = message;
    item.querySelector("button").addEventListener("click", () => item.remove());
    region.appendChild(item);
    requestAnimationFrame(() => item.classList.add("is-visible"));
    window.setTimeout(() => {
      item.classList.remove("is-visible");
      window.setTimeout(() => item.remove(), 250);
    }, 3500);
  }

  const savedTheme = localStorage.getItem("crm-theme");
  if (savedTheme === "dark") root.dataset.theme = "dark";
  document.getElementById("themeToggle")?.addEventListener("click", () => {
    root.dataset.theme = root.dataset.theme === "dark" ? "light" : "dark";
    localStorage.setItem("crm-theme", root.dataset.theme);
  });

  document.getElementById("menuButton")?.addEventListener("click", () => {
    sidebar?.classList.add("is-open");
    mobileOverlay?.classList.add("is-open");
  });
  document.getElementById("sidebarClose")?.addEventListener("click", closeSidebar);
  mobileOverlay?.addEventListener("click", closeSidebar);
  document.getElementById("drawerClose")?.addEventListener("click", closeDrawer);
  drawerBackdrop?.addEventListener("click", closeDrawer);
  document.addEventListener("click", event => {
    if (event.target.closest("[data-drawer-close]")) closeDrawer();
    // Repeating form rows (e.g. customer contact persons) are removed client-side; the server binds what remains.
    const remove = event.target.closest("[data-remove-row]");
    if (remove) remove.closest("[data-row]")?.remove();
  });
  // Shows the chosen file name next to a visually hidden file input (e.g. the customer logo picker).
  document.addEventListener("change", event => {
    const input = event.target.closest?.("input[type=file][data-file-label]");
    const label = input && document.getElementById(input.dataset.fileLabel);
    if (label) label.textContent = input.files?.[0]?.name ?? "انتخاب تصویر…";
  });
  // Capture phase: runs before htmx's own submit handler, so a cancelled confirm/prompt really stops the request.
  document.addEventListener("submit", event => {
    const form = event.target;
    const message = form.dataset.confirm;
    if (message && !window.confirm(message)) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }
    const question = form.dataset.prompt;
    if (question) {
      const answer = window.prompt(question);
      if (answer === null || !answer.trim()) {
        event.preventDefault();
        event.stopPropagation();
        if (answer !== null) showToast(question + " الزامی است.", true);
        return;
      }
      let input = form.querySelector('input[name="reason"]');
      if (!input) {
        input = document.createElement("input");
        input.type = "hidden";
        input.name = "reason";
        form.appendChild(input);
      }
      input.value = answer.trim();
    }
  }, true);
  // Bubble phase (as before): forms whose own script stops the event keep managing their buttons themselves.
  document.addEventListener("submit", event => {
    const button = event.target.querySelector('button[type="submit"]');
    if (button) {
      button.classList.add("is-loading");
      button.setAttribute("aria-disabled", "true");
    }
  });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      closeDrawer();
      closeSidebar();
    }
  });

  document.body.addEventListener("htmx:configRequest", event => {
    if (token && unsafeMethods.has(event.detail.verb.toLowerCase())) {
      event.detail.headers.RequestVerificationToken = token;
    }
  });

  document.body.addEventListener("htmx:beforeSwap", event => {
    if (event.detail.xhr.status === 422 || (event.detail.xhr.status === 409 && event.detail.xhr.getResponseHeader("HX-Retarget") === "#selfServiceErrors")) {
      event.detail.shouldSwap = true;
      event.detail.isError = false;
    }
  });

  document.body.addEventListener("htmx:afterSwap", event => {
    if (event.detail.target?.id === "drawerBody") {
      // The drawer title follows the form that was loaded (e.g. «برنامه‌ریزی تماس») instead of a generic caption.
      const heading = drawerBody?.querySelector("[data-drawer-title], .form-section h3");
      const title = document.getElementById("drawerTitle");
      if (heading && title) title.textContent = heading.textContent.trim();
      openDrawer();
    }
  });

  document.body.addEventListener("htmx:afterRequest", event => {
    const button = event.detail.elt?.querySelector?.('button[type="submit"]');
    button?.classList.remove("is-loading");
    button?.removeAttribute("aria-disabled");
  });

  document.body.addEventListener("htmx:responseError", event => {
    if (event.detail.xhr.status === 403) showToast("برای این عملیات دسترسی کافی ندارید.", true);
    else showToast("در انجام درخواست خطایی رخ داد.", true);
  });

  document.body.addEventListener("rateLimited", event => showToast(event.detail?.message || "تعداد درخواست‌ها بیش از حد مجاز است.", true));

  changeEvents.forEach(name => {
    document.body.addEventListener(name, event => {
      closeDrawer();
      showToast(event.detail?.message || "تغییر با موفقیت ثبت شد.");
    });
  });

  // ───────────── List workspaces: row selection, bulk action bar, row menus ─────────────
  function syncBulk(scope) {
    const items = [...scope.querySelectorAll("[data-bulk-item]")];
    const checked = items.filter(item => item.checked);
    const bar = scope.querySelector("[data-bulk-bar]");
    if (bar) bar.hidden = checked.length === 0;
    const count = scope.querySelector("[data-bulk-count]");
    if (count) count.textContent = checked.length;
    const all = scope.querySelector("[data-bulk-all]");
    if (all) {
      all.checked = items.length > 0 && checked.length === items.length;
      all.indeterminate = checked.length > 0 && checked.length < items.length;
    }
    items.forEach(item => item.closest("tr, .list-card")?.classList.toggle("is-selected", item.checked));
  }
  document.addEventListener("change", event => {
    const input = event.target;
    const scope = input.closest?.("[data-bulk]");
    if (!scope || !input.matches("[data-bulk-item], [data-bulk-all]")) return;
    if (input.matches("[data-bulk-all]")) scope.querySelectorAll("[data-bulk-item]").forEach(item => { item.checked = input.checked; });
    syncBulk(scope);
  });
  document.addEventListener("click", event => {
    const clear = event.target.closest("[data-bulk-clear]");
    const scope = clear?.closest("[data-bulk]");
    if (scope) {
      scope.querySelectorAll("[data-bulk-item], [data-bulk-all]").forEach(item => { item.checked = false; });
      syncBulk(scope);
    }
    // One row menu at a time; any click outside (or on a menu item) closes it.
    document.querySelectorAll("details.row-menu[open]").forEach(menu => {
      if (!menu.contains(event.target) || event.target.closest(".row-menu__list a, .row-menu__list button")) menu.open = false;
    });
  });
  // Row menus are placed against the viewport, so a horizontally scrolling table does not clip them;
  // they follow their row when the page or the table scrolls.
  function placeMenu(menu) {
    const list = menu.querySelector(".row-menu__list");
    const anchor = menu.querySelector("summary")?.getBoundingClientRect();
    if (!list || !anchor) return;
    const left = Math.min(Math.max(8, anchor.left), window.innerWidth - list.offsetWidth - 8);
    const below = anchor.bottom + 4;
    list.style.left = left + "px";
    list.style.top = (below + list.offsetHeight > window.innerHeight - 8 ? Math.max(8, anchor.top - list.offsetHeight - 4) : below) + "px";
  }
  document.addEventListener("toggle", event => {
    if (event.target.matches?.("details.row-menu") && event.target.open) placeMenu(event.target);
  }, true);
  ["scroll", "resize"].forEach(name => window.addEventListener(name, () => document.querySelectorAll("details.row-menu[open]").forEach(placeMenu), true));
  // Activity drawers opened from a list (outside the account page) close and report like on the account page.
  if (!document.querySelector("[data-account-id]")) {
    document.body.addEventListener("accountChanged", event => { closeDrawer(); showToast(event.detail?.message || "ثبت شد."); });
    document.body.addEventListener("accountError", event => showToast(event.detail?.message || "انجام نشد.", true));
  }
  ["leadsRefreshed", "customersRefreshed"].forEach(name =>
    document.body.addEventListener(name, event => showToast(event.detail?.message, true)));

  // ───────────── Account file (پرونده حساب): tabs, lazy sections, in-place refresh ─────────────
  const accountRoot = document.querySelector("[data-account-id]");
  // Non-bubbling: a nested panel's refresh must not also reload the panel that contains it.
  const refresh = el => el.dispatchEvent(new CustomEvent("refresh", { bubbles: false }));

  function loadLazy(scope) {
    scope.querySelectorAll("[data-lazy]:not([data-loaded])").forEach(el => {
      const details = el.closest("details");
      if (details && !details.open) return;
      if (el.closest("[hidden]")) return;
      el.dataset.loaded = "1";
      refresh(el);
    });
  }

  function activateTab(container, name) {
    if (!container) return;
    container.querySelectorAll("[data-tab]").forEach(tab => {
      if (tab.closest("[data-tabs]") !== container) return;
      const on = tab.dataset.tab === name;
      tab.classList.toggle("is-active", on);
      tab.setAttribute("aria-selected", on ? "true" : "false");
    });
    container.querySelectorAll("[data-tab-panel]").forEach(panel => {
      if (panel.closest("[data-tabs]") !== container) return;
      panel.hidden = panel.dataset.tabPanel !== name;
      if (!panel.hidden) loadLazy(panel);
    });
  }

  function openSection(key) {
    const details = document.getElementById("sec-" + key);
    if (!details) return;
    details.open = true;
    loadLazy(details);
    details.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  function goTo(search) {
    const params = new URLSearchParams(search);
    const tab = params.get("tab");
    // The activity panel lives in the «فعالیت‌ها و تعاملات» tab; a state/contact link opens the tab with that filter.
    if ((tab === "activities" && params.get("state")) || params.get("contact")) {
      const panel = document.getElementById("activityPanel");
      if (panel) panel.dataset.loaded = "1";
      activateTab(document.querySelector('[data-tabs="main"]'), "activities");
      if (panel && window.htmx) {
        const query = new URLSearchParams();
        if (params.get("state")) query.set("state", params.get("state"));
        if (params.get("contact")) query.set("contact", params.get("contact"));
        window.htmx.ajax("GET", panel.getAttribute("hx-get").split("?")[0] + "?" + query, { target: panel, swap: "innerHTML" });
      }
      panel?.scrollIntoView({ behavior: "smooth" });
      return;
    }
    if (tab) activateTab(document.querySelector('[data-tabs="main"]'), tab);
    if (params.get("open")) openSection(params.get("open"));
  }

  if (accountRoot) {
    document.addEventListener("click", event => {
      const tab = event.target.closest("[data-tab]");
      if (tab) activateTab(tab.closest("[data-tabs]"), tab.dataset.tab);
      if (event.target.closest("summary [data-no-toggle]")) event.preventDefault();
      const link = event.target.closest("a[data-goto]");
      if (link) {
        event.preventDefault();
        goTo(new URL(link.href, location.href).search);
      }
      const toggle = event.target.closest("[data-sections-toggle]");
      if (toggle) {
        document.querySelectorAll("details[data-lazy-section]").forEach(d => {
          if (d.closest("[hidden]") || d.style.display === "none") return;
          d.open = toggle.dataset.sectionsToggle === "open";
        });
      }
    });
    document.addEventListener("toggle", event => {
      const details = event.target;
      if (details.matches?.("details[data-lazy-section], details[data-lazy-details]") && details.open) loadLazy(details);
    }, true);
    document.addEventListener("input", event => {
      const search = event.target.closest("[data-section-search]");
      if (!search) return;
      const term = search.value.trim();
      document.querySelectorAll("[data-section-group]").forEach(group => {
        let visible = 0;
        group.querySelectorAll("details[data-lazy-section]").forEach(d => {
          const show = !term || d.dataset.title.includes(term);
          d.style.display = show ? "" : "none";
          if (show) visible++;
        });
        group.style.display = visible ? "" : "none";
      });
    });
    // Thousands separators while typing amounts (the server strips them).
    document.addEventListener("input", event => {
      const input = event.target.closest?.("input[data-money=\"true\"]");
      if (!input) return;
      const digits = input.value.replace(/[^\d۰-۹٠-٩.]/g, "").replace(/[۰-۹]/g, d => "۰۱۲۳۴۵۶۷۸۹".indexOf(d)).replace(/[٠-٩]/g, d => "٠١٢٣٤٥٦٧٨٩".indexOf(d));
      const [whole, fraction] = digits.split(".");
      input.value = (whole || "").replace(/\B(?=(\d{3})+(?!\d))/g, ",") + (fraction !== undefined ? "." + fraction : "");
    });
    document.body.addEventListener("htmx:afterSwap", event => {
      const target = event.detail.target;
      if (target?.hasAttribute?.("data-account-refresh")) target.dataset.loaded = "1";
    });
    // New content gets its htmx listeners at settle time, so lazy loads inside it are started only after settling.
    document.body.addEventListener("htmx:afterSettle", event => {
      const target = event.detail.target;
      if (target?.id === "sectionNav") {
        const open = (target.dataset.reopen || "").split(",").filter(Boolean);
        open.forEach(key => { const d = document.getElementById("sec-" + key); if (d) d.open = true; });
        loadLazy(target);
        const total = [...target.querySelectorAll(".acc-section__count")].reduce((sum, el) => sum + (parseInt(el.textContent, 10) || 0), 0);
        const badge = document.querySelector(".tabbar__count");
        if (badge) badge.textContent = total;
        const search = document.querySelector("[data-section-search]");
        if (search?.value) search.dispatchEvent(new Event("input", { bubbles: true }));
      }
    });
    document.body.addEventListener("htmx:responseError", event => {
      const target = event.detail.target;
      if (!target?.hasAttribute?.("data-account-refresh")) return;
      const denied = event.detail.xhr.status === 403;
      target.innerHTML = denied
        ? '<p class="state-denied">برای مشاهدهٔ این بخش مجوز ندارید.</p>'
        : '<p class="state-error">بارگذاری انجام نشد. <button type="button" class="link-button">تلاش دوباره</button></p>';
      target.querySelector("button")?.addEventListener("click", () => refresh(target));
    });
    document.body.addEventListener("accountChanged", event => {
      closeDrawer();
      showToast(event.detail?.message || "ثبت شد.");
      const nav = document.getElementById("sectionNav");
      if (nav) nav.dataset.reopen = [...nav.querySelectorAll("details[data-lazy-section][open]")].map(d => d.dataset.section).join(",");
      document.querySelectorAll("[data-account-refresh]").forEach(el => {
        if (el !== nav && el.closest("#sectionNav")) return;
        if (el.id === "activityPanel" && document.getElementById("activityFilter")) {
          window.htmx?.trigger(document.getElementById("activityFilter"), "submit");
          return;
        }
        if (el.dataset.loaded || !el.hasAttribute("data-lazy")) refresh(el);
      });
    });
    document.body.addEventListener("accountError", event => showToast(event.detail?.message || "انجام نشد.", true));
    if (location.search) goTo(location.search);
  }
})();
