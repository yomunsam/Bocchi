(() => {
    const storageKey = "bocchi.dashboard.appearance";
    const root = document.documentElement;
    const query = window.matchMedia("(prefers-color-scheme: dark)");

    function normalize(value) {
        return value === "light" || value === "dark" ? value : "auto";
    }

    function effectiveMode(mode) {
        return mode === "auto" ? (query.matches ? "dark" : "light") : mode;
    }

    function syncSelect(mode) {
        const select = document.getElementById("bocchi-appearance-select");
        if (select instanceof HTMLSelectElement) {
            select.value = mode;
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
        syncSelect(normalized);
        syncToggle(effective);
    }

    window.bocchiAppearance = {
        set(value) {
            const normalized = normalize(value);
            localStorage.setItem(storageKey, normalized);
            apply(normalized);
        },
        apply,
    };

    apply(localStorage.getItem(storageKey) || "auto");
    query.addEventListener("change", () => apply(root.dataset.bocchiAppearance || "auto"));
    document.addEventListener("DOMContentLoaded", () => {
        syncSelect(root.dataset.bocchiAppearance || "auto");
        syncToggle(root.dataset.bocchiEffectiveAppearance || effectiveMode(root.dataset.bocchiAppearance || "auto"));
        for (const button of document.querySelectorAll("[data-bocchi-appearance-toggle]")) {
            button.addEventListener("click", () => {
                const effective = root.dataset.bocchiEffectiveAppearance || effectiveMode(root.dataset.bocchiAppearance || "auto");
                window.bocchiAppearance.set(effective === "dark" ? "light" : "dark");
            });
        }
    });

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
