const root = document.documentElement;
const appearanceStorageKey = "bocchi-theme-appearance";
const legacyAppearanceStorageKey = "bocchi-theme";
const languageStorageKey = "bocchi-theme-language";
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

const applyLanguage = (language, persist = true) => {
  currentLanguage = normalizeLanguage(language);
  root.lang = currentLanguage;
  root.setAttribute("data-bocchi-language", currentLanguage);
  if (persist) writeStorage(languageStorageKey, currentLanguage);

  document.querySelectorAll("[data-bocchi-i18n]").forEach((element) => {
    const key = element.getAttribute("data-bocchi-i18n");
    const value = key ? resolveText(key) : null;
    if (value !== null) element.textContent = value;
  });

  document.querySelectorAll("[data-mobile-toggle]").forEach((button) => {
    button.setAttribute("aria-label", resolveText("theme.defaultStatic.openMenu") ?? "Open menu");
  });

  syncLanguageControls();
  syncAppearanceControls();
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
    applyLanguage(languageOption.getAttribute("data-bocchi-language-option"));
    closeOwnMenu(languageOption);
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

applyLanguage(currentLanguage, false);
applyAppearance(currentAppearance, false);

if (!customElements.get("bocchi-time")) {
  customElements.define("bocchi-time", class extends HTMLElement {
    connectedCallback() {
      if (this.dataset.ready === "true") return;
      this.dataset.ready = "true";
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
  });
}
