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
    // 配色：默认「运动服」；localStorage 里存了 maid / clip 时切换，只写 data 属性，颜色全在 tokens.css。
    const paletteKey = "bocchi.dashboard.palette";

    function normalizePalette(value) {
        return value === "maid" || value === "clip" ? value : "tracksuit";
    }

    function readStoredPalette() {
        try {
            return normalizePalette(localStorage.getItem(paletteKey));
        } catch {
            return "tracksuit";
        }
    }

    function syncPaletteControls(palette) {
        for (const input of document.querySelectorAll("[data-bocchi-palette-option]")) {
            if (input instanceof HTMLInputElement) {
                input.checked = input.value === palette;
            }
        }
    }

    function applyPalette(value) {
        const palette = normalizePalette(value);
        if (palette === "tracksuit") {
            delete root.dataset.bocchiPalette;
        } else {
            root.dataset.bocchiPalette = palette;
        }

        syncPaletteControls(palette);
    }

    function setPalette(value) {
        const palette = normalizePalette(value);
        try {
            localStorage.setItem(paletteKey, palette);
        } catch {
            // 存不下时只在本页生效。
        }

        applyPalette(palette);
    }

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
        setPalette,
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
            syncPaletteControls(normalizePalette(root.dataset.bocchiPalette));
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

        // 配色 radio：方向键和点击都会触发 change，立即换色并记住
        document.addEventListener("change", event => {
            const input = event.target;
            if (input instanceof HTMLInputElement && input.matches("[data-bocchi-palette-option]") && input.checked) {
                setPalette(input.value);
            }
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
    applyPalette(readStoredPalette());
    // 别的标签页改了配色，这里跟着换
    window.addEventListener("storage", event => {
        if (event.key === paletteKey) {
            applyPalette(readStoredPalette());
        }
    });
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

            const strengthScore = [checks.length, checks.letter, checks.numberOrSymbol]
                .filter(Boolean)
                .length;
            const strength = strengthScore >= 3 ? "strong" : strengthScore >= 2 ? "medium" : "weak";
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
