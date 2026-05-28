const root = document.documentElement;
const appearanceStorageKey = "bocchi-theme-appearance";
const legacyAppearanceStorageKey = "bocchi-theme";
const languageStorageKey = "bocchi-theme-language";
const inlineColorFormat = "inlineColor";
const appearanceQuery = window.matchMedia?.("(prefers-color-scheme: dark)");

const readStorage = (key) => {
  try { return localStorage.getItem(key); } catch { return null; }
};

const writeStorage = (key, value) => {
  try { localStorage.setItem(key, value); } catch {}
};

const readI18nData = () => {
  const element = document.getElementById("bocchi-i18n-data");
  if (!element?.textContent) return {};
  try { return JSON.parse(element.textContent); } catch { return {}; }
};

const i18n = readI18nData();
const languages = Array.isArray(i18n.languages)
  ? i18n.languages.filter((language) => typeof language?.code === "string" && language.code)
  : [];

const findLanguageCode = (value) => {
  if (typeof value !== "string" || !value) return null;
  const normalized = value.toLowerCase();
  return languages.find((language) => language.code.toLowerCase() === normalized)?.code ?? null;
};

const primaryLanguage = findLanguageCode(i18n.primaryLanguage)
  ?? findLanguageCode(i18n.currentLanguage)
  ?? root.lang
  ?? "en-US";

const normalizeLanguage = (value) => findLanguageCode(value) ?? primaryLanguage;
const normalizeAppearance = (value) => value === "light" || value === "dark" || value === "auto" ? value : "auto";
const getEffectiveAppearance = (mode) => mode === "dark" || (mode === "auto" && appearanceQuery?.matches) ? "dark" : "light";
const getLanguage = (code) => languages.find((language) => language.code === code) ?? null;
const getLanguageDisplayName = (code) => {
  const language = getLanguage(code);
  return language?.nativeName || language?.englishName || code;
};

const readJsonAttribute = (element, attribute) => {
  const raw = element.getAttribute(attribute);
  if (!raw) return null;
  try { return JSON.parse(raw); } catch { return null; }
};

const pageLanguageHrefs = readJsonAttribute(document.body, "data-bocchi-page-hrefs");

const findByLanguage = (items, language) => {
  if (!Array.isArray(items) || typeof language !== "string") return null;
  const normalized = language.toLowerCase();
  return items.find((item) => typeof item?.language === "string" && item.language.toLowerCase() === normalized) ?? null;
};

const findMapValueByLanguage = (map, language) => {
  if (!map || typeof map !== "object" || typeof language !== "string") return null;
  const normalized = language.toLowerCase();
  const key = Object.keys(map).find((candidate) => candidate.toLowerCase() === normalized);
  return key && typeof map[key] === "string" ? map[key] : null;
};

const toRelativeSiteHref = (url) => {
  if (typeof url !== "string" || !url.startsWith("/") || url.startsWith("//")) return url;

  const match = /^([^?#]*)(.*)$/.exec(url);
  const targetPath = match?.[1] || "/";
  const suffix = match?.[2] || "";
  const currentPath = window.location?.pathname || "/";
  const currentDirectory = currentPath.endsWith("/") ? currentPath : currentPath.replace(/[^/]*$/, "");
  const fromParts = currentDirectory.split("/").filter(Boolean);
  const toParts = targetPath.split("/").filter(Boolean);
  let common = 0;
  while (common < fromParts.length && common < toParts.length && fromParts[common] === toParts[common]) common += 1;

  let relative = "../".repeat(Math.max(0, fromParts.length - common)) + toParts.slice(common).join("/");
  if (targetPath.endsWith("/") && relative && !relative.endsWith("/")) relative += "/";
  if (!relative) relative = "./";
  return relative + suffix;
};

const normalizeInlineColor = (value) => {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  if (/^#[0-9a-f]{3}([0-9a-f]{3})?$/i.test(trimmed)) return trimmed;
  if (trimmed.toLowerCase() === "accent") return "var(--accent)";
  return null;
};

const readOpeningColorTag = (token) => {
  const body = token.slice(1, -1).trim();
  const separator = body.indexOf("=");
  if (separator <= 0) return null;
  const name = body.slice(0, separator).trim();
  if (name.toLowerCase() !== "color") return null;
  return normalizeInlineColor(body.slice(separator + 1));
};

const isClosingColorTag = (token) => {
  const body = token.slice(1, -1).trim();
  return body.startsWith("/") && body.slice(1).trim().toLowerCase() === "color";
};

const appendInlineColorText = (rootElement, value) => {
  rootElement.textContent = "";
  const stack = [rootElement];
  let index = 0;
  while (index < value.length) {
    const open = value.indexOf("[", index);
    if (open < 0) {
      stack[stack.length - 1].append(document.createTextNode(value.slice(index)));
      break;
    }

    if (open > index) stack[stack.length - 1].append(document.createTextNode(value.slice(index, open)));
    const close = value.indexOf("]", open + 1);
    if (close < 0) {
      stack[stack.length - 1].append(document.createTextNode(value.slice(open)));
      break;
    }

    const token = value.slice(open, close + 1);
    const cssColor = readOpeningColorTag(token);
    if (cssColor) {
      const span = document.createElement("span");
      span.style.color = cssColor;
      stack[stack.length - 1].append(span);
      stack.push(span);
    } else if (isClosingColorTag(token) && stack.length > 1) {
      stack.pop();
    } else {
      stack[stack.length - 1].append(document.createTextNode(token));
    }

    index = close + 1;
  }
};

const applyElementText = (element, value) => {
  if (element.getAttribute("data-bocchi-i18n-format") === inlineColorFormat) {
    appendInlineColorText(element, value);
    return;
  }

  element.textContent = value;
};

const resolveText = (key, language = currentLanguage) => {
  const values = i18n.text?.[key];
  if (!values || typeof values !== "object") return null;

  const fallbacks = [language, primaryLanguage, i18n.currentLanguage, "en-US", languages[0]?.code].filter(Boolean);
  for (const fallback of fallbacks) {
    const value = values[fallback];
    if (typeof value === "string") return value;
  }

  for (const value of Object.values(values)) {
    if (typeof value === "string") return value;
  }

  return null;
};

let currentLanguage = normalizeLanguage(readStorage(languageStorageKey) ?? i18n.currentLanguage ?? root.lang);
let currentAppearance = normalizeAppearance(readStorage(appearanceStorageKey) ?? readStorage(legacyAppearanceStorageKey));

const syncLanguageControls = () => {
  const label = resolveText("theme.defaultStatic.languageLabel") ?? "Language";
  document.querySelectorAll("[data-bocchi-language-control]").forEach((control) => {
    const summary = control.querySelector("summary");
    summary?.setAttribute("aria-label", label);
    summary?.setAttribute("title", label);
    control.querySelector(".theme-menu__menu")?.setAttribute("aria-label", label);
  });

  document.querySelectorAll("[data-bocchi-current-language],[data-bocchi-language-summary]").forEach((element) => {
    element.textContent = getLanguageDisplayName(currentLanguage);
  });

  document.querySelectorAll("[data-bocchi-language-option]").forEach((option) => {
    option.setAttribute("aria-current", String(option.getAttribute("data-bocchi-language-option") === currentLanguage));
  });
};

const syncAppearanceControls = () => {
  const label = resolveText("theme.defaultStatic.appearanceLabel") ?? "Appearance";
  document.querySelectorAll("[data-bocchi-appearance-control]").forEach((control) => {
    control.setAttribute("data-bocchi-appearance-mode", currentAppearance);
    const summary = control.querySelector("summary");
    summary?.setAttribute("aria-label", label);
    summary?.setAttribute("title", label);
    control.querySelector(".theme-menu__menu")?.setAttribute("aria-label", label);
  });

  document.querySelectorAll("[data-bocchi-appearance-option]").forEach((option) => {
    option.setAttribute("aria-current", String(option.getAttribute("data-bocchi-appearance-option") === currentAppearance));
  });
};

const syncNavigationLinks = () => {
  document.querySelectorAll("[data-bocchi-nav-hrefs]").forEach((link) => {
    const languageHrefs = readJsonAttribute(link, "data-bocchi-nav-hrefs");
    const href = findMapValueByLanguage(languageHrefs, currentLanguage)
      ?? findMapValueByLanguage(languageHrefs, primaryLanguage);
    if (href) link.setAttribute("href", toRelativeSiteHref(href));
  });
};

const updateContentText = (rootElement, selector, value) => {
  if (typeof value !== "string") return;
  rootElement.querySelectorAll(selector).forEach((element) => {
    element.textContent = value;
  });
};

const updateContentAttribute = (rootElement, selector, attribute, value) => {
  if (typeof value !== "string") return;
  rootElement.querySelectorAll(selector).forEach((element) => {
    element.setAttribute(attribute, value);
  });
};

const syncContentVariants = () => {
  document.querySelectorAll("[data-bocchi-content-variants]").forEach((element) => {
    const variants = readJsonAttribute(element, "data-bocchi-content-variants");
    const variant = findByLanguage(variants, currentLanguage)
      ?? findByLanguage(variants, primaryLanguage)
      ?? (Array.isArray(variants) ? variants[0] : null);
    if (!variant || typeof variant !== "object") return;

    const link = element.matches("a[href]") ? element : element.querySelector("a[href]");
    if (link && typeof variant.url === "string") link.setAttribute("href", toRelativeSiteHref(variant.url));
    updateContentText(element, "[data-bocchi-content-title]", variant.title);
    updateContentAttribute(element, "[data-bocchi-content-link]", "aria-label", variant.title);
    updateContentText(element, "[data-bocchi-content-summary]", variant.summary);
    updateContentText(element, "[data-bocchi-content-meta]", variant.meta);
    updateContentText(element, "[data-bocchi-content-year-month]", variant.yearMonth);
  });
};

const syncArticleTimes = () => {
  document.querySelectorAll("bocchi-time[data-bocchi-article-time]").forEach((element) => {
    if (typeof element.renderArticleTime === "function") element.renderArticleTime();
  });
};

const currentPageLanguageHref = (language) => findMapValueByLanguage(pageLanguageHrefs, language);

const hasPageLanguageContext = () => pageLanguageHrefs && typeof pageLanguageHrefs === "object" && Object.keys(pageLanguageHrefs).length > 0;

const resolveRequestedLanguage = (language) => {
  const requestedLanguage = normalizeLanguage(language);
  if (!hasPageLanguageContext() || currentPageLanguageHref(requestedLanguage)) {
    return {
      requestedLanguage,
      targetLanguage: requestedLanguage,
      fallbackToPrimary: false,
    };
  }

  return {
    requestedLanguage,
    targetLanguage: normalizeLanguage(primaryLanguage),
    fallbackToPrimary: true,
  };
};

const navigateToLanguagePage = (language, replace = false) => {
  const href = currentPageLanguageHref(language);
  if (!href) return false;

  const relativeHref = toRelativeSiteHref(href);
  const targetUrl = new URL(relativeHref, window.location.href);
  if (targetUrl.pathname === window.location.pathname && targetUrl.search === window.location.search) return false;

  if (replace) {
    window.location.replace(relativeHref);
  } else {
    window.location.assign(relativeHref);
  }
  return true;
};

const dispatchLanguageChange = (language, reason, requestedLanguage = language, fallbackToPrimary = false) => {
  const href = currentPageLanguageHref(language);
  return window.dispatchEvent(new CustomEvent("bocchi:languagechange", {
    cancelable: true,
    detail: {
      from: currentLanguage,
      to: language,
      requested: requestedLanguage,
      reason,
      fallbackToPrimary,
      hasPageVariant: Boolean(href),
      pageHref: href ?? null,
    },
  }));
};

const requestLanguage = (language, { persist = true, navigate = true, replace = false, reason = "user" } = {}) => {
  const { requestedLanguage, targetLanguage, fallbackToPrimary } = resolveRequestedLanguage(language);
  if (!dispatchLanguageChange(targetLanguage, reason, requestedLanguage, fallbackToPrimary)) return false;
  if (persist) writeStorage(languageStorageKey, targetLanguage);
  if (navigate && navigateToLanguagePage(targetLanguage, replace)) return true;
  applyLanguage(targetLanguage, false);
  return true;
};

const applyLanguage = (language, persist = true) => {
  currentLanguage = normalizeLanguage(language);
  root.lang = currentLanguage;
  root.setAttribute("data-bocchi-language", currentLanguage);
  if (persist) writeStorage(languageStorageKey, currentLanguage);

  document.querySelectorAll("[data-bocchi-i18n]").forEach((element) => {
    const key = element.getAttribute("data-bocchi-i18n");
    const value = key ? resolveText(key) : null;
    if (value !== null) applyElementText(element, value);
  });

  document.querySelectorAll("[data-mobile-toggle]").forEach((button) => {
    button.setAttribute("aria-label", resolveText("theme.defaultStatic.openMenu") ?? "Open menu");
  });

  syncLanguageControls();
  syncAppearanceControls();
  syncNavigationLinks();
  syncContentVariants();
  syncArticleTimes();
};

const applyAppearance = (mode, persist = true) => {
  currentAppearance = normalizeAppearance(mode);
  const effectiveAppearance = getEffectiveAppearance(currentAppearance);
  root.dataset.theme = effectiveAppearance;
  root.style.colorScheme = effectiveAppearance;
  if (persist) writeStorage(appearanceStorageKey, currentAppearance);
  syncAppearanceControls();
};

const closeOpenMenus = (except = null) => {
  document.querySelectorAll("details.theme-menu[open]").forEach((menu) => {
    if (menu !== except) menu.open = false;
  });
};

const closeOwnMenu = (element) => {
  const menu = element.closest("details.theme-menu");
  if (menu instanceof HTMLDetailsElement) menu.open = false;
};

document.querySelectorAll("[data-mobile-toggle]").forEach((button) => {
  button.addEventListener("click", () => {
    const nav = document.querySelector("[data-mobile-nav]");
    if (!nav) return;
    const open = nav.classList.toggle("is-open");
    button.setAttribute("aria-expanded", String(open));
  });
});

document.addEventListener("toggle", (event) => {
  const menu = event.target;
  if (!(menu instanceof HTMLDetailsElement) || !menu.matches("details.theme-menu") || !menu.open) return;
  closeOpenMenus(menu);
}, true);

document.addEventListener("click", (event) => {
  const target = event.target instanceof Element ? event.target : null;
  if (!target) return;

  const languageOption = target.closest("[data-bocchi-language-option]");
  if (languageOption instanceof HTMLElement) {
    event.preventDefault();
    requestLanguage(languageOption.getAttribute("data-bocchi-language-option"));
    closeOwnMenu(languageOption);
    return;
  }

  const contentLanguageLink = target.closest("a[data-bocchi-language-link]");
  if (contentLanguageLink instanceof HTMLAnchorElement) {
    const nextLanguage = normalizeLanguage(contentLanguageLink.getAttribute("data-bocchi-language-link"));
    if (!dispatchLanguageChange(nextLanguage, "content-link")) {
      event.preventDefault();
      return;
    }

    writeStorage(languageStorageKey, nextLanguage);
    return;
  }

  const appearanceOption = target.closest("[data-bocchi-appearance-option]");
  if (appearanceOption instanceof HTMLElement) {
    event.preventDefault();
    applyAppearance(appearanceOption.getAttribute("data-bocchi-appearance-option"));
    closeOwnMenu(appearanceOption);
    return;
  }

  if (!target.closest("details.theme-menu")) closeOpenMenus();
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") closeOpenMenus();
});

const syncAutoAppearance = () => {
  if (currentAppearance === "auto") applyAppearance("auto", false);
};
if (appearanceQuery?.addEventListener) {
  appearanceQuery.addEventListener("change", syncAutoAppearance);
} else {
  appearanceQuery?.addListener?.(syncAutoAppearance);
}

const initialPageLanguage = normalizeLanguage(i18n.currentLanguage ?? root.lang);
const initialNavigationStarted = currentLanguage !== initialPageLanguage
  && requestLanguage(currentLanguage, { persist: false, replace: true, reason: "restore" });
if (!initialNavigationStarted) applyLanguage(currentLanguage, false);
applyAppearance(currentAppearance, false);

if (!customElements.get("bocchi-time")) {
  customElements.define("bocchi-time", class extends HTMLElement {
    connectedCallback() {
      if (this.dataset.ready === "true") return;
      this.dataset.ready = "true";
      if (this.hasAttribute("data-bocchi-article-time")) {
        this.initializeArticleTime();
        return;
      }

      this.initializeLegacyTime();
    }

    disconnectedCallback() {
      if (this.articleTimeDocumentClick) {
        document.removeEventListener("click", this.articleTimeDocumentClick);
      }
    }

    initializeLegacyTime() {
      const value = this.getAttribute("datetime");
      const authorZone = this.getAttribute("author-time-zone");
      const date = value ? new Date(value) : null;
      if (!date || Number.isNaN(date.getTime()) || !authorZone) return;

      const authorText = this.querySelector("time")?.textContent?.trim() || this.textContent?.trim() || value;
      const authorLabel = `${authorText} (${authorZone})`;
      this.setAttribute("aria-label", authorLabel);

      const visitorZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (!visitorZone || visitorZone === authorZone) return;

      const visitorText = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short", timeZone: visitorZone }).format(date);
      const detail = `Author: ${authorLabel} / Local: ${visitorText} (${visitorZone})`;
      this.title = detail;
      this.setAttribute("aria-label", detail);

      const badge = document.createElement("span");
      badge.className = "bocchi-time__zone";
      badge.textContent = visitorZone.replace(/_/g, " ");
      badge.setAttribute("aria-hidden", "true");
      this.append(badge);
    }

    initializeArticleTime() {
      this.articleTimePayload = readJsonAttribute(this, "data-bocchi-time");
      if (!this.articleTimePayload?.written) return;

      this.articleTimeLabel = this.querySelector("[data-bocchi-time-label]");
      this.articleTimeValue = this.querySelector("[data-bocchi-time-value]");
      this.articleTimeOffset = this.querySelector("[data-bocchi-time-offset]");
      this.articleTimeActiveKind = this.articleTimePayload.activeKind || "written";
      this.articleTimePinned = false;
      this.articleTimePopover = document.createElement("span");
      this.articleTimePopover.className = "bocchi-time__popover";
      this.articleTimePopover.setAttribute("role", "tooltip");
      this.append(this.articleTimePopover);
      this.renderArticleTime();

      this.addEventListener("mouseenter", () => this.setArticleTimeOpen(true));
      this.addEventListener("mouseleave", () => {
        if (!this.articleTimePinned) this.setArticleTimeOpen(false);
      });
      this.addEventListener("focus", () => this.setArticleTimeOpen(true));
      this.addEventListener("blur", () => {
        if (!this.articleTimePinned) this.setArticleTimeOpen(false);
      });
      this.addEventListener("click", (event) => {
        event.stopPropagation();
        this.toggleArticleTime();
        this.articleTimePinned = true;
        this.setArticleTimeOpen(true);
      });
      this.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          this.toggleArticleTime();
          this.articleTimePinned = true;
          this.setArticleTimeOpen(true);
        } else if (event.key === "Escape") {
          this.articleTimePinned = false;
          this.setArticleTimeOpen(false);
        }
      });
      this.articleTimeDocumentClick = (event) => {
        const target = event.target instanceof Node ? event.target : null;
        if (!target || !this.contains(target)) {
          this.articleTimePinned = false;
          this.setArticleTimeOpen(false);
        }
      };
      document.addEventListener("click", this.articleTimeDocumentClick);
    }

    toggleArticleTime() {
      if (!this.articleTimePayload?.canToggle) return;
      this.articleTimeActiveKind = this.articleTimeActiveKind === "updated" ? "written" : "updated";
      this.renderArticleTime();
    }

    renderArticleTime() {
      const entry = this.getArticleTimeEntry(this.articleTimeActiveKind);
      if (!entry) return;

      const label = resolveText(entry.labelKey) || entry.label || "";
      this.dataset.bocchiTimeKind = entry.kind;
      this.articleTimeLabel.textContent = label;
      this.articleTimeLabel.setAttribute("data-bocchi-i18n", entry.labelKey);
      this.articleTimeValue.textContent = entry.display;
      this.articleTimeValue.setAttribute("datetime", entry.iso);
      const timeZoneLabel = entry.timeZoneLabel || entry.offsetLabel || "";
      this.articleTimeOffset.textContent = timeZoneLabel;
      this.setAttribute("aria-label", `${label} ${entry.display} ${timeZoneLabel}`.trim());
      this.renderArticleTimePopover(entry, label);
    }

    getArticleTimeEntry(kind) {
      const entry = this.articleTimePayload?.[kind];
      if (entry && typeof entry === "object") return entry;
      return this.articleTimePayload?.written ?? null;
    }

    renderArticleTimePopover(entry, label) {
      if (!this.articleTimePopover) return;
      this.articleTimePopover.textContent = "";
      this.articleTimePopover.append(
        this.createArticleTimePopoverRow(
          resolveText("content.time.authorTimeZone") || "",
          entry.authorTimeZone || this.articleTimePayload.authorTimeZone || ""));

      const reader = this.createReaderTime(entry);
      if (reader) {
        this.articleTimePopover.append(
          this.createArticleTimePopoverRow(
            `${label} ${resolveText("content.time.readerTime") || ""}`.trim(),
            reader));
      }
    }

    createArticleTimePopoverRow(label, value) {
      const row = document.createElement("span");
      row.className = "bocchi-time__popover-row";
      const labelElement = document.createElement("span");
      labelElement.className = "bocchi-time__popover-label";
      labelElement.textContent = label;
      const valueElement = document.createElement("strong");
      valueElement.className = "bocchi-time__popover-value";
      valueElement.textContent = value;
      row.append(labelElement, valueElement);
      return row;
    }

    createReaderTime(entry) {
      const authorZone = entry.authorTimeZone || this.articleTimePayload.authorTimeZone;
      const readerZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
      if (!authorZone || !readerZone || authorZone === readerZone) return null;

      const date = new Date(entry.iso);
      if (Number.isNaN(date.getTime())) return null;

      const authorParts = this.getTimeParts(date, authorZone);
      const readerParts = this.getTimeParts(date, readerZone);
      if (!authorParts || !readerParts) return null;

      if (authorParts.year !== readerParts.year) {
        return `${readerParts.year}-${readerParts.month}-${readerParts.day} ${readerParts.hour}:${readerParts.minute}`;
      }

      if (authorParts.month !== readerParts.month || authorParts.day !== readerParts.day) {
        return `${readerParts.month}-${readerParts.day} ${readerParts.hour}:${readerParts.minute}`;
      }

      return `${readerParts.hour}:${readerParts.minute}`;
    }

    getTimeParts(date, timeZone) {
      try {
        const parts = new Intl.DateTimeFormat(undefined, {
          timeZone,
          year: "numeric",
          month: "2-digit",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
          hourCycle: "h23"
        }).formatToParts(date);
        const value = (type) => parts.find((part) => part.type === type)?.value;
        return {
          year: value("year"),
          month: value("month"),
          day: value("day"),
          hour: value("hour"),
          minute: value("minute")
        };
      } catch {
        return null;
      }
    }

    setArticleTimeOpen(open) {
      this.toggleAttribute("data-bocchi-time-open", open);
      this.setAttribute("aria-expanded", String(open));
    }
  });
}
