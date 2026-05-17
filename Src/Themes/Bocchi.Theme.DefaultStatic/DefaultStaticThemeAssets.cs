namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme 的内置前端资产。</summary>
internal static class DefaultStaticThemeAssets
{
    /// <summary>默认 Theme 的 SVG favicon；用于避免浏览器回退请求 /favicon.ico 产生噪声。</summary>
    public const string FaviconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <rect width="64" height="64" rx="14" fill="#101012"/>
          <path d="M16 44V15h15c7 0 12 4 12 10 0 4-2 7-6 8 5 1 9 5 9 10 0 7-5 11-14 11H16Zm10-18h5c3 0 5-1 5-4s-2-4-5-4h-5v8Zm0 20h6c4 0 6-2 6-5s-2-5-6-5h-6v10Z" fill="#FAFAF7"/>
          <circle cx="48" cy="48" r="5" fill="#E85D3A"/>
        </svg>
        """;

    /// <summary>克制现代的默认 CSS。保持无外部依赖，确保离线构建也能得到可读页面。</summary>
    public const string Css = """
        *,*::before,*::after{box-sizing:border-box}
        html{color-scheme:light}
        html[data-theme="dark"]{color-scheme:dark}
        body{margin:0;background:var(--bg);color:var(--text);font-family:var(--font-body);font-size:15px;line-height:1.65;letter-spacing:0;text-rendering:optimizeLegibility}
        :root{--bg:#FAFAF7;--surface:#FFFFFF;--surface-soft:#F1F1EC;--text:#101012;--text-muted:#52524F;--text-faint:#8A8A84;--line:#E5E5E0;--accent:#E85D3A;--accent-soft:#FFE9E0;--font-display:"Instrument Serif","Noto Serif SC",ui-serif,Georgia,serif;--font-body:"Inter Tight","Inter","Noto Sans SC",ui-sans-serif,system-ui,sans-serif;--font-mono:"JetBrains Mono",ui-monospace,SFMono-Regular,Menlo,monospace;--container:1280px;--content:1080px;--prose:680px}
        html[data-theme="dark"]{--bg:#0A0A0B;--surface:#141416;--surface-soft:#1A1A1C;--text:#EDEDEA;--text-muted:#A8A8A4;--text-faint:#6A6A66;--line:#26262A;--accent:#FF7A55;--accent-soft:#3A1B11}
        a{color:inherit;text-decoration:none}
        a:hover{color:var(--accent)}
        img{max-width:100%;height:auto;display:block}
        code,pre{font-family:var(--font-mono)}
        pre{overflow:auto;border:1px solid var(--line);padding:16px;background:var(--surface-soft)}
        :focus-visible{outline:2px solid var(--accent);outline-offset:3px}
        .topbar{position:sticky;top:0;z-index:20;height:56px;background:var(--bg);border-bottom:1px solid var(--line)}
        .topbar__inner{max-width:var(--container);height:100%;margin:0 auto;padding:0 32px;display:flex;align-items:center;justify-content:space-between;gap:24px}
        .wordmark{font-weight:700;text-transform:uppercase;letter-spacing:.16em;font-size:14px}
        .wordmark::after{content:".";color:var(--accent)}
        .nav{display:flex;align-items:center;gap:24px;color:var(--text-muted);font-size:14px}
        .nav a[aria-current="page"]{color:var(--text);border-bottom:1px solid var(--accent)}
        .toolbar{display:flex;align-items:center;gap:8px}
        .theme-menu{position:relative;display:inline-flex;color:var(--text);font-size:13px}
        .theme-menu summary{height:32px;min-width:32px;border:1px solid var(--line);background:transparent;color:var(--text);display:inline-flex;align-items:center;justify-content:center;gap:6px;border-radius:8px;padding:0 8px;cursor:pointer;list-style:none}
        .theme-menu summary::-webkit-details-marker{display:none}
        .theme-menu summary:hover,.theme-menu[open] summary{background:var(--surface-soft)}
        .theme-menu__icon{font-weight:700;font-size:14px;line-height:1}
        .theme-menu__current{font-family:var(--font-mono);font-size:11px;color:var(--text-muted);line-height:1}
        .theme-menu__chevron{font-size:12px;line-height:1;color:var(--text-faint);transition:transform 140ms ease}
        .theme-menu[open] .theme-menu__chevron{transform:rotate(180deg)}
        .theme-menu__appearance-icon{display:none;font-size:16px;line-height:1}
        .theme-menu:not([data-bocchi-appearance-mode]) .theme-menu__appearance-icon--auto,.theme-menu[data-bocchi-appearance-mode="auto"] .theme-menu__appearance-icon--auto,.theme-menu[data-bocchi-appearance-mode="light"] .theme-menu__appearance-icon--light,.theme-menu[data-bocchi-appearance-mode="dark"] .theme-menu__appearance-icon--dark{display:inline-block}
        .theme-menu__menu{position:absolute;top:calc(100% + 8px);right:0;z-index:40;display:grid;gap:4px;min-width:148px;border:1px solid var(--line);border-radius:8px;background:var(--surface);box-shadow:0 18px 38px rgba(16,16,18,.14);padding:6px}
        html[data-theme="dark"] .theme-menu__menu{box-shadow:0 18px 38px rgba(0,0,0,.35)}
        .theme-menu__option{appearance:none;border:0;background:transparent;color:var(--text);font:inherit;display:flex;align-items:center;justify-content:space-between;gap:14px;width:100%;border-radius:6px;padding:8px 10px;text-align:left;cursor:pointer;white-space:nowrap}
        .theme-menu__option:hover{background:var(--surface-soft)}
        .theme-menu__option[aria-current="true"]{background:var(--surface-soft);color:var(--accent)}
        .theme-menu__option small{font-family:var(--font-mono);font-size:11px;color:var(--text-faint)}
        .theme-menu__option[aria-current="true"] small{color:var(--accent)}
        .icon-button{width:32px;height:32px;border:1px solid var(--line);background:transparent;color:var(--text);display:inline-flex;align-items:center;justify-content:center;border-radius:8px}
        .icon-button:hover{background:var(--surface-soft)}
        .mobile-toggle{display:none}
        .mobile-nav{display:none;border-bottom:1px solid var(--line);background:var(--bg);padding:16px 24px}
        .mobile-nav a{display:block;padding:12px 0;border-bottom:1px solid var(--line);font-size:24px;font-family:var(--font-display)}
        .mobile-nav.is-open{display:block}
        .container{max-width:var(--container);margin:0 auto;padding:0 32px}
        .content{max-width:var(--content);margin:0 auto;padding:0 32px}
        .prose{max-width:var(--prose);margin:0 auto;padding:0 32px}
        .section{padding:88px 0;border-top:1px solid var(--line)}
        .section:first-child{border-top:0}
        .eyebrow{font-family:var(--font-mono);font-size:12px;text-transform:uppercase;color:var(--text-faint);letter-spacing:.12em;margin:0 0 16px}
        .hero{min-height:calc(80vh - 56px);display:flex;flex-direction:column;justify-content:center;padding:72px 0 96px}
        .hero.container{padding-left:32px;padding-right:32px}
        .hero h1{font-family:var(--font-display);font-weight:400;font-style:italic;font-size:96px;line-height:.96;letter-spacing:0;margin:0;max-width:980px}
        .hero h1 em{font-style:italic;color:var(--accent)}
        .lead{max-width:640px;color:var(--text-muted);font-size:18px;line-height:1.7;margin:32px 0 0}
        .meta-row{display:flex;flex-wrap:wrap;gap:12px 20px;margin-top:32px;color:var(--text-muted);font-family:var(--font-mono);font-size:13px}
        .section-head{display:flex;align-items:end;justify-content:space-between;gap:24px;margin-bottom:24px}
        .section.content{padding-left:32px;padding-right:32px}
        .section-head h2{margin:0;font-size:32px;line-height:1.2}
        .arrow-link{border-bottom:1px solid currentColor;font-weight:600}
        .arrow-link::after{content:" ->";color:var(--accent)}
        .list{border-top:1px solid var(--line)}
        .list-row{display:grid;grid-template-columns:132px 1fr minmax(80px,160px);gap:24px;align-items:baseline;padding:22px 16px;border-bottom:1px solid var(--line)}
        .list-row:hover{background:var(--surface-soft)}
        .list-row__date{font-family:var(--font-mono);font-size:12px;color:var(--text-faint)}
        .list-row__title{font-size:18px;font-weight:600}
        .list-row__meta{font-size:13px;color:var(--text-muted);text-align:right}
        .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
        .card{border:1px solid var(--line);background:var(--surface);padding:24px;border-radius:8px}
        .card:hover{border-color:var(--text)}
        .card__cover{display:block;margin:-24px -24px 20px;border-bottom:1px solid var(--line);aspect-ratio:16/9;overflow:hidden;background:var(--surface-soft)}
        .card__cover img{width:100%;height:100%;object-fit:cover}
        .card h3{margin:0 0 12px;font-size:22px}
        .card p{margin:0;color:var(--text-muted)}
        .card__meta{margin-top:12px;color:var(--text-faint);font-family:var(--font-mono);font-size:12px}
        .tags{margin-top:16px;color:var(--text-faint);font-family:var(--font-mono);font-size:12px}
        .tags span::before{content:"· "}
        .note{max-width:var(--prose);padding:24px 0;border-bottom:1px solid var(--line)}
        bocchi-time{display:inline-flex;align-items:baseline;gap:6px}
        .note time{font-family:var(--font-mono);font-size:12px;color:var(--text-faint)}
        .bocchi-time__zone{border:1px solid var(--line);border-radius:999px;color:var(--text-muted);font-family:var(--font-mono);font-size:11px;line-height:1;padding:3px 7px}
        .note__body{margin-top:8px;color:var(--text)}
        .note__media{margin-top:16px}
        .media-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:8px}
        .media-grid img{width:100%;aspect-ratio:1/1;object-fit:cover;border:1px solid var(--line);background:var(--surface-soft)}
        .article-header{padding:72px 0 48px}
        .article-header h1{font-size:48px;line-height:1.12;margin:0}
        .article-meta{margin-top:16px;color:var(--text-muted);font-family:var(--font-mono);font-size:13px}
        .media-cover{margin-bottom:32px;aspect-ratio:16/9;overflow:hidden;border:1px solid var(--line);background:var(--surface-soft)}
        .media-cover img{width:100%;height:100%;object-fit:cover}
        .friend-avatar{width:40px;height:40px;border-radius:8px;object-fit:cover;border:1px solid var(--line);background:var(--surface-soft)}
        .prose-body{font-size:17px;line-height:1.78}
        .prose-body h2,.prose-body h3{margin:40px 0 12px;line-height:1.25}
        .prose-body p,.prose-body ul,.prose-body ol,.prose-body blockquote{margin:0 0 22px}
        .prose-body a{text-decoration:underline;text-decoration-color:var(--accent);text-underline-offset:3px}
        .empty{border:1px solid var(--line);background:var(--surface-soft);padding:24px;color:var(--text-muted)}
        .footer{border-top:1px solid var(--line);padding:32px;color:var(--text-muted)}
        .footer__inner{max-width:var(--container);margin:0 auto;display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap}
        @media(max-width:768px){.topbar__inner{padding:0 20px}.nav{display:none}.mobile-toggle{display:inline-flex}.theme-menu summary{padding:0 9px}.theme-menu__current{display:none}.theme-menu__menu{right:-8px}.container,.content,.prose{padding:0 20px}.hero{min-height:auto;padding:64px 0}.hero.container,.section.content{padding-left:20px;padding-right:20px}.hero h1{font-size:48px;line-height:1.02}.section{padding-top:56px;padding-bottom:56px}.section-head{align-items:start;flex-direction:column}.list-row{grid-template-columns:1fr;gap:6px;padding:18px 0}.list-row__meta{text-align:left}.grid{grid-template-columns:1fr}.article-header h1{font-size:36px}}
        """;

    /// <summary>默认 Theme 的原生渐进增强脚本。</summary>
    public const string Js = """
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
            element.textContent = currentLanguage;
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
        """;

    /// <summary>旧版单按钮控制区 CSS；仅用于刷新尚未修改过的物化默认 Theme。</summary>
    internal const string LegacyCss = """
        *,*::before,*::after{box-sizing:border-box}
        html{color-scheme:light}
        html[data-theme="dark"]{color-scheme:dark}
        body{margin:0;background:var(--bg);color:var(--text);font-family:var(--font-body);font-size:15px;line-height:1.65;letter-spacing:0;text-rendering:optimizeLegibility}
        :root{--bg:#FAFAF7;--surface:#FFFFFF;--surface-soft:#F1F1EC;--text:#101012;--text-muted:#52524F;--text-faint:#8A8A84;--line:#E5E5E0;--accent:#E85D3A;--accent-soft:#FFE9E0;--font-display:"Instrument Serif","Noto Serif SC",ui-serif,Georgia,serif;--font-body:"Inter Tight","Inter","Noto Sans SC",ui-sans-serif,system-ui,sans-serif;--font-mono:"JetBrains Mono",ui-monospace,SFMono-Regular,Menlo,monospace;--container:1280px;--content:1080px;--prose:680px}
        html[data-theme="dark"]{--bg:#0A0A0B;--surface:#141416;--surface-soft:#1A1A1C;--text:#EDEDEA;--text-muted:#A8A8A4;--text-faint:#6A6A66;--line:#26262A;--accent:#FF7A55;--accent-soft:#3A1B11}
        a{color:inherit;text-decoration:none}
        a:hover{color:var(--accent)}
        img{max-width:100%;height:auto;display:block}
        code,pre{font-family:var(--font-mono)}
        pre{overflow:auto;border:1px solid var(--line);padding:16px;background:var(--surface-soft)}
        :focus-visible{outline:2px solid var(--accent);outline-offset:3px}
        .topbar{position:sticky;top:0;z-index:20;height:56px;background:var(--bg);border-bottom:1px solid var(--line)}
        .topbar__inner{max-width:var(--container);height:100%;margin:0 auto;padding:0 32px;display:flex;align-items:center;justify-content:space-between;gap:24px}
        .wordmark{font-weight:700;text-transform:uppercase;letter-spacing:.16em;font-size:14px}
        .wordmark::after{content:".";color:var(--accent)}
        .nav{display:flex;align-items:center;gap:24px;color:var(--text-muted);font-size:14px}
        .nav a[aria-current="page"]{color:var(--text);border-bottom:1px solid var(--accent)}
        .toolbar{display:flex;align-items:center;gap:8px}
        .icon-button{width:32px;height:32px;border:1px solid var(--line);background:transparent;color:var(--text);display:inline-flex;align-items:center;justify-content:center;border-radius:8px}
        .icon-button:hover{background:var(--surface-soft)}
        .mobile-toggle{display:none}
        .mobile-nav{display:none;border-bottom:1px solid var(--line);background:var(--bg);padding:16px 24px}
        .mobile-nav a{display:block;padding:12px 0;border-bottom:1px solid var(--line);font-size:24px;font-family:var(--font-display)}
        .mobile-nav.is-open{display:block}
        .container{max-width:var(--container);margin:0 auto;padding:0 32px}
        .content{max-width:var(--content);margin:0 auto;padding:0 32px}
        .prose{max-width:var(--prose);margin:0 auto;padding:0 32px}
        .section{padding:88px 0;border-top:1px solid var(--line)}
        .section:first-child{border-top:0}
        .eyebrow{font-family:var(--font-mono);font-size:12px;text-transform:uppercase;color:var(--text-faint);letter-spacing:.12em;margin:0 0 16px}
        .hero{min-height:calc(80vh - 56px);display:flex;flex-direction:column;justify-content:center;padding:72px 0 96px}
        .hero.container{padding-left:32px;padding-right:32px}
        .hero h1{font-family:var(--font-display);font-weight:400;font-style:italic;font-size:96px;line-height:.96;letter-spacing:0;margin:0;max-width:980px}
        .hero h1 em{font-style:italic;color:var(--accent)}
        .lead{max-width:640px;color:var(--text-muted);font-size:18px;line-height:1.7;margin:32px 0 0}
        .meta-row{display:flex;flex-wrap:wrap;gap:12px 20px;margin-top:32px;color:var(--text-muted);font-family:var(--font-mono);font-size:13px}
        .section-head{display:flex;align-items:end;justify-content:space-between;gap:24px;margin-bottom:24px}
        .section.content{padding-left:32px;padding-right:32px}
        .section-head h2{margin:0;font-size:32px;line-height:1.2}
        .arrow-link{border-bottom:1px solid currentColor;font-weight:600}
        .arrow-link::after{content:" ->";color:var(--accent)}
        .list{border-top:1px solid var(--line)}
        .list-row{display:grid;grid-template-columns:132px 1fr minmax(80px,160px);gap:24px;align-items:baseline;padding:22px 16px;border-bottom:1px solid var(--line)}
        .list-row:hover{background:var(--surface-soft)}
        .list-row__date{font-family:var(--font-mono);font-size:12px;color:var(--text-faint)}
        .list-row__title{font-size:18px;font-weight:600}
        .list-row__meta{font-size:13px;color:var(--text-muted);text-align:right}
        .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
        .card{border:1px solid var(--line);background:var(--surface);padding:24px;border-radius:8px}
        .card:hover{border-color:var(--text)}
        .card__cover{display:block;margin:-24px -24px 20px;border-bottom:1px solid var(--line);aspect-ratio:16/9;overflow:hidden;background:var(--surface-soft)}
        .card__cover img{width:100%;height:100%;object-fit:cover}
        .card h3{margin:0 0 12px;font-size:22px}
        .card p{margin:0;color:var(--text-muted)}
        .card__meta{margin-top:12px;color:var(--text-faint);font-family:var(--font-mono);font-size:12px}
        .tags{margin-top:16px;color:var(--text-faint);font-family:var(--font-mono);font-size:12px}
        .tags span::before{content:"· "}
        .note{max-width:var(--prose);padding:24px 0;border-bottom:1px solid var(--line)}
        bocchi-time{display:inline-flex;align-items:baseline;gap:6px}
        .note time{font-family:var(--font-mono);font-size:12px;color:var(--text-faint)}
        .bocchi-time__zone{border:1px solid var(--line);border-radius:999px;color:var(--text-muted);font-family:var(--font-mono);font-size:11px;line-height:1;padding:3px 7px}
        .note__body{margin-top:8px;color:var(--text)}
        .note__media{margin-top:16px}
        .media-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:8px}
        .media-grid img{width:100%;aspect-ratio:1/1;object-fit:cover;border:1px solid var(--line);background:var(--surface-soft)}
        .article-header{padding:72px 0 48px}
        .article-header h1{font-size:48px;line-height:1.12;margin:0}
        .article-meta{margin-top:16px;color:var(--text-muted);font-family:var(--font-mono);font-size:13px}
        .media-cover{margin-bottom:32px;aspect-ratio:16/9;overflow:hidden;border:1px solid var(--line);background:var(--surface-soft)}
        .media-cover img{width:100%;height:100%;object-fit:cover}
        .friend-avatar{width:40px;height:40px;border-radius:8px;object-fit:cover;border:1px solid var(--line);background:var(--surface-soft)}
        .prose-body{font-size:17px;line-height:1.78}
        .prose-body h2,.prose-body h3{margin:40px 0 12px;line-height:1.25}
        .prose-body p,.prose-body ul,.prose-body ol,.prose-body blockquote{margin:0 0 22px}
        .prose-body a{text-decoration:underline;text-decoration-color:var(--accent);text-underline-offset:3px}
        .empty{border:1px solid var(--line);background:var(--surface-soft);padding:24px;color:var(--text-muted)}
        .footer{border-top:1px solid var(--line);padding:32px;color:var(--text-muted)}
        .footer__inner{max-width:var(--container);margin:0 auto;display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap}
        @media(max-width:768px){.topbar__inner{padding:0 20px}.nav{display:none}.mobile-toggle{display:inline-flex}.container,.content,.prose{padding:0 20px}.hero{min-height:auto;padding:64px 0}.hero.container,.section.content{padding-left:20px;padding-right:20px}.hero h1{font-size:48px;line-height:1.02}.section{padding-top:56px;padding-bottom:56px}.section-head{align-items:start;flex-direction:column}.list-row{grid-template-columns:1fr;gap:6px;padding:18px 0}.list-row__meta{text-align:left}.grid{grid-template-columns:1fr}.article-header h1{font-size:36px}}
        """;

    /// <summary>旧版单按钮控制区脚本；仅用于刷新尚未修改过的物化默认 Theme。</summary>
    internal const string LegacyJs = """
        const root = document.documentElement;
        const readTheme = () => {
          try { return localStorage.getItem("bocchi-theme"); } catch { return null; }
        };
        const writeTheme = (value) => {
          try { localStorage.setItem("bocchi-theme", value); } catch {}
        };
        const storedTheme = readTheme();
        if (storedTheme === "dark" || storedTheme === "light") root.dataset.theme = storedTheme;
        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
          button.addEventListener("click", () => {
            const next = root.dataset.theme === "dark" ? "light" : "dark";
            root.dataset.theme = next;
            writeTheme(next);
          });
        });
        document.querySelectorAll("[data-mobile-toggle]").forEach((button) => {
          button.addEventListener("click", () => {
            const nav = document.querySelector("[data-mobile-nav]");
            if (!nav) return;
            const open = nav.classList.toggle("is-open");
            button.setAttribute("aria-expanded", String(open));
          });
        });
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
        """;
}
