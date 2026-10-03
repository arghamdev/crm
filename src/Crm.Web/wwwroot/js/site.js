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
  document.addEventListener("submit", event => {
    const form = event.target;
    const message = form.dataset.confirm;
    if (message && !window.confirm(message)) {
      event.preventDefault();
      return;
    }
    const button = form.querySelector('button[type="submit"]');
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
    if (event.detail.target?.id === "drawerBody") openDrawer();
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
})();
