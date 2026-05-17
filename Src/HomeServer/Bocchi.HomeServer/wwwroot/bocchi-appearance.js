(() => {
    const storageKey = "bocchi.dashboard.appearance";
    const root = document.documentElement;
    const query = window.matchMedia("(prefers-color-scheme: dark)");
    let controlsInitialized = false;
    let syncQueued = false;

    function normalize(value) {
        return value === "light" || value === "dark" ? value : "auto";
    }

    function effectiveMode(mode) {
        return mode === "auto" ? (query.matches ? "dark" : "light") : mode;
    }

    function readStoredMode() {
        try {
            return localStorage.getItem(storageKey) || "auto";
        } catch {
            return "auto";
        }
    }

    function writeStoredMode(mode) {
        try {
            localStorage.setItem(storageKey, mode);
        } catch {
            // 浏览器禁用 localStorage 时仍保持本次页面内外观切换可用。
        }
    }

    function syncAppearanceControl(mode) {
        for (const control of document.querySelectorAll("[data-bocchi-appearance-control]")) {
            if (control instanceof HTMLElement) {
                control.dataset.bocchiAppearanceMode = mode;
            }
        }

        for (const option of document.querySelectorAll("[data-bocchi-appearance-option]")) {
            if (!(option instanceof HTMLButtonElement)) {
                continue;
            }

            const active = option.dataset.bocchiAppearanceOption === mode;
            option.setAttribute("aria-current", active ? "true" : "false");
        }
    }

    function syncToggle(effective) {
        for (const button of document.querySelectorAll("[data-bocchi-appearance-toggle]")) {
            if (!(button instanceof HTMLButtonElement)) {
                continue;
            }

            const label = effective === "dark"
                ? button.dataset.labelLight
                : button.dataset.labelDark;
            button.dataset.mode = effective;
            if (label) {
                button.setAttribute("aria-label", label);
                button.setAttribute("title", label);
            }
        }
    }

    // Dashboard 外观只控制后台明暗模式；不要和前台业务 Theme 混用。
    function apply(mode) {
        const normalized = normalize(mode);
        const effective = effectiveMode(normalized);
        root.dataset.bocchiAppearance = normalized;
        root.dataset.bocchiEffectiveAppearance = effective;
        root.style.colorScheme = effective;
        syncAppearanceControl(normalized);
        syncToggle(effective);
    }

    window.bocchiAppearance = {
        set(value) {
            const normalized = normalize(value);
            writeStoredMode(normalized);
            apply(normalized);
        },
        apply,
    };

    function closeMenuFrom(element) {
        const menu = element.closest("details");
        if (menu instanceof HTMLDetailsElement) {
            menu.open = false;
        }
    }

    function closeOpenMenusExcept(target) {
        for (const menu of document.querySelectorAll(".bocchi-menu-control[open]")) {
            if (menu instanceof HTMLDetailsElement && (!target || !menu.contains(target))) {
                menu.open = false;
            }
        }

        for (const menu of document.querySelectorAll(".bocchi-row-delete[open]")) {
            if (menu instanceof HTMLDetailsElement && (!target || !menu.contains(target))) {
                menu.open = false;
            }
        }
    }

    function syncSoon() {
        if (syncQueued) {
            return;
        }

        syncQueued = true;
        queueMicrotask(() => {
            syncQueued = false;
            apply(root.dataset.bocchiAppearance || "auto");
        });
    }

    function setupAppearanceControls() {
        if (controlsInitialized) {
            syncSoon();
            return;
        }

        controlsInitialized = true;
        document.addEventListener("click", event => {
            const target = event.target instanceof Element ? event.target : null;
            if (!target) {
                return;
            }

            const option = target.closest("[data-bocchi-appearance-option]");
            if (option instanceof HTMLElement) {
                event.preventDefault();
                window.bocchiAppearance.set(option.dataset.bocchiAppearanceOption || "auto");
                closeMenuFrom(option);
                return;
            }

            const toggle = target.closest("[data-bocchi-appearance-toggle]");
            if (toggle instanceof HTMLButtonElement) {
                event.preventDefault();
                const effective = root.dataset.bocchiEffectiveAppearance || effectiveMode(root.dataset.bocchiAppearance || "auto");
                window.bocchiAppearance.set(effective === "dark" ? "light" : "dark");
                return;
            }

            const closeDelete = target.closest("[data-bocchi-close-delete]");
            if (closeDelete instanceof HTMLElement) {
                event.preventDefault();
                closeMenuFrom(closeDelete);
                return;
            }

            closeOpenMenusExcept(target);
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeOpenMenusExcept(null);
            }
        });

        new MutationObserver(syncSoon).observe(document.body, { childList: true, subtree: true });
        syncSoon();
    }

    apply(readStoredMode());
    query.addEventListener("change", () => apply(root.dataset.bocchiAppearance || "auto"));
    document.addEventListener("DOMContentLoaded", setupAppearanceControls);
    document.addEventListener("enhancedload", syncSoon);
    if (document.readyState !== "loading") {
        setupAppearanceControls();
    }

    function setupPasswordControls() {
        const password = document.querySelector("[data-bocchi-password-source]");
        const confirm = document.querySelector("[data-bocchi-password-confirm]");
        const meter = document.querySelector("[data-bocchi-password-meter]");

        for (const button of document.querySelectorAll("[data-bocchi-password-toggle]")) {
            if (!(button instanceof HTMLButtonElement)) {
                continue;
            }

            button.addEventListener("click", () => {
                const target = document.getElementById(button.dataset.bocchiPasswordToggle || "");
                if (!(target instanceof HTMLInputElement)) {
                    return;
                }

                target.type = target.type === "password" ? "text" : "password";
            });
        }

        if (!(password instanceof HTMLInputElement)) {
            return;
        }

        const rules = {
            length: document.querySelector("[data-bocchi-password-rule='length']"),
            letter: document.querySelector("[data-bocchi-password-rule='letter']"),
            numberOrSymbol: document.querySelector("[data-bocchi-password-rule='numberOrSymbol']"),
            match: document.querySelector("[data-bocchi-password-rule='match']"),
        };

        function setRule(name, ok) {
            const item = rules[name];
            if (item instanceof HTMLElement) {
                item.dataset.ok = ok ? "true" : "false";
            }
        }

        function update() {
            const value = password.value;
            const confirmValue = confirm instanceof HTMLInputElement ? confirm.value : "";
            const checks = {
                length: value.length >= 8,
                letter: /[A-Za-z]/.test(value),
                numberOrSymbol: /[0-9\W_]/.test(value),
                match: value.length > 0 && value === confirmValue,
            };
            for (const [name, ok] of Object.entries(checks)) {
                setRule(name, ok);
            }

            const score = Object.values(checks).filter(Boolean).length;
            const strength = score >= 4 ? "strong" : score >= 2 ? "medium" : "weak";
            if (meter instanceof HTMLElement) {
                meter.dataset.strength = strength;
                const label = meter.querySelector("[data-bocchi-password-strength-label]");
                if (label instanceof HTMLElement) {
                    label.textContent = meter.dataset[`label${strength[0].toUpperCase()}${strength.slice(1)}`] || strength;
                }
            }
        }

        password.addEventListener("input", update);
        if (confirm instanceof HTMLInputElement) {
            confirm.addEventListener("input", update);
        }
        update();
    }

    document.addEventListener("DOMContentLoaded", setupPasswordControls);
})();
