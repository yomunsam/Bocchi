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

    // Dashboard 外观只控制后台明暗模式；不要和前台业务 Theme 混用。
    function apply(mode) {
        const normalized = normalize(mode);
        const effective = effectiveMode(normalized);
        root.dataset.bocchiAppearance = normalized;
        root.dataset.bocchiEffectiveAppearance = effective;
        root.style.colorScheme = effective;
        syncSelect(normalized);
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
    document.addEventListener("DOMContentLoaded", () => syncSelect(root.dataset.bocchiAppearance || "auto"));
})();
