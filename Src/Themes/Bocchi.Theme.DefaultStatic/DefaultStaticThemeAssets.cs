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
        .card h3{margin:0 0 12px;font-size:22px}
        .card p{margin:0;color:var(--text-muted)}
        .tags{margin-top:16px;color:var(--text-faint);font-family:var(--font-mono);font-size:12px}
        .tags span::before{content:"· "}
        .note{max-width:var(--prose);padding:24px 0;border-bottom:1px solid var(--line)}
        .note time{font-family:var(--font-mono);font-size:12px;color:var(--text-faint)}
        .note__body{margin-top:8px;color:var(--text)}
        .article-header{padding:72px 0 48px}
        .article-header h1{font-size:48px;line-height:1.12;margin:0}
        .article-meta{margin-top:16px;color:var(--text-muted);font-family:var(--font-mono);font-size:13px}
        .prose-body{font-size:17px;line-height:1.78}
        .prose-body h2,.prose-body h3{margin:40px 0 12px;line-height:1.25}
        .prose-body p,.prose-body ul,.prose-body ol,.prose-body blockquote{margin:0 0 22px}
        .prose-body a{text-decoration:underline;text-decoration-color:var(--accent);text-underline-offset:3px}
        .empty{border:1px solid var(--line);background:var(--surface-soft);padding:24px;color:var(--text-muted)}
        .footer{border-top:1px solid var(--line);padding:32px;color:var(--text-muted)}
        .footer__inner{max-width:var(--container);margin:0 auto;display:flex;justify-content:space-between;gap:16px;flex-wrap:wrap}
        @media(max-width:768px){.topbar__inner{padding:0 20px}.nav{display:none}.mobile-toggle{display:inline-flex}.container,.content,.prose{padding:0 20px}.hero{min-height:auto;padding:64px 0}.hero.container,.section.content{padding-left:20px;padding-right:20px}.hero h1{font-size:48px;line-height:1.02}.section{padding-top:56px;padding-bottom:56px}.section-head{align-items:start;flex-direction:column}.list-row{grid-template-columns:1fr;gap:6px;padding:18px 0}.list-row__meta{text-align:left}.grid{grid-template-columns:1fr}.article-header h1{font-size:36px}}
        """;

    /// <summary>默认 Theme 的原生渐进增强脚本。</summary>
    public const string Js = """
        const root = document.documentElement;
        const storedTheme = localStorage.getItem("bocchi-theme");
        if (storedTheme === "dark" || storedTheme === "light") root.dataset.theme = storedTheme;
        document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
          button.addEventListener("click", () => {
            const next = root.dataset.theme === "dark" ? "light" : "dark";
            root.dataset.theme = next;
            localStorage.setItem("bocchi-theme", next);
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
        customElements.define("bocchi-time", class extends HTMLElement {
          connectedCallback() {
            const value = this.getAttribute("datetime");
            const authorZone = this.getAttribute("author-time-zone");
            if (!value || !authorZone) return;
            const visitorZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
            if (!visitorZone || visitorZone === authorZone) return;
            const date = new Date(value);
            const visitor = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short", timeZone: visitorZone }).format(date);
            this.title = `${this.textContent?.trim()} / ${visitor} (${visitorZone})`;
          }
        });
        """;
}
