(function () {
    "use strict";

    const status = {
        available: "available",
        downloadable: "downloadable",
        downloading: "downloading",
        unavailable: "unavailable",
        error: "error",
    };

    const chromeBuiltInProviderId = "chrome-built-in";
    const supportedTextLanguages = new Set(["en", "es", "ja"]);
    const defaultInputLanguages = ["en"];
    const defaultOutputLanguages = ["en"];
    const sessions = new Map();
    let nextSessionId = 1;

    function normalizeAvailability(value) {
        switch (value) {
            case status.available:
            case "readily":
                return status.available;
            case status.downloadable:
            case "after-download":
                return status.downloadable;
            case status.downloading:
                return status.downloading;
            case status.unavailable:
            case "no":
                return status.unavailable;
            default:
                return status.unavailable;
        }
    }

    function chromeBuiltInApi() {
        return globalThis.LanguageModel;
    }

    async function inspectChromeBuiltIn(request) {
        const api = chromeBuiltInApi();
        if (!api?.availability || !api?.create) {
            return {
                id: chromeBuiltInProviderId,
                displayName: "Chrome Built-in AI",
                status: status.unavailable,
                reason: "当前浏览器没有暴露 LanguageModel API。",
            };
        }

        try {
            return {
                id: chromeBuiltInProviderId,
                displayName: "Chrome Built-in AI",
                status: normalizeAvailability(await api.availability(createChromeBuiltInLanguageOptions(request))),
                reason: null,
            };
        } catch (error) {
            return {
                id: chromeBuiltInProviderId,
                displayName: "Chrome Built-in AI",
                status: status.error,
                reason: formatError(error),
            };
        }
    }

    async function getAvailability() {
        const providers = [await inspectChromeBuiltIn()];
        return {
            providers,
            hasAvailableProvider: providers.some((provider) => provider.status === status.available),
        };
    }

    async function prompt(request) {
        const text = request?.prompt;
        if (typeof text !== "string" || text.trim().length === 0) {
            throw new Error("AI prompt cannot be empty.");
        }

        const session = await createSession(request);
        try {
            return await promptSession(session.sessionId, text);
        } finally {
            destroySession(session.sessionId);
        }
    }

    async function createSession(request) {
        const providerId = request?.providerId || chromeBuiltInProviderId;
        if (providerId !== chromeBuiltInProviderId) {
            throw new Error(`Unknown AI provider: ${providerId}`);
        }

        const availability = await inspectChromeBuiltIn(request);
        if (availability.status !== status.available) {
            throw new Error(`AI provider is not available: ${availability.status}`);
        }

        const session = await createChromeBuiltInSession(request);
        const sessionId = String(nextSessionId++);
        sessions.set(sessionId, { providerId, session });
        return { sessionId, providerId };
    }

    async function promptSession(sessionId, promptText) {
        const entry = sessions.get(String(sessionId));
        if (!entry) {
            throw new Error("AI session does not exist or has already been destroyed.");
        }

        if (typeof promptText !== "string" || promptText.trim().length === 0) {
            throw new Error("AI prompt cannot be empty.");
        }

        return {
            providerId: entry.providerId,
            text: await entry.session.prompt(promptText),
        };
    }

    function destroySession(sessionId) {
        const key = String(sessionId);
        const entry = sessions.get(key);
        if (!entry) {
            return;
        }

        sessions.delete(key);
        entry.session.destroy?.();
    }

    async function createChromeBuiltInSession(request) {
        const options = {};
        Object.assign(options, createChromeBuiltInLanguageOptions(request));
        if (typeof request?.systemPrompt === "string" && request.systemPrompt.trim().length > 0) {
            options.initialPrompts = [{ role: "system", content: request.systemPrompt }];
        }

        if (Number.isFinite(request?.temperature)) {
            options.temperature = request.temperature;
        }

        if (Number.isFinite(request?.topK)) {
            options.topK = request.topK;
        }

        // Chrome 端侧模型的下载与运行都由浏览器托管，这里只做薄封装，方便以后接入其它 provider。
        return await chromeBuiltInApi().create(options);
    }

    function createChromeBuiltInLanguageOptions(request) {
        return {
            expectedInputs: [{ type: "text", languages: normalizeLanguages(request?.expectedInputLanguages, defaultInputLanguages) }],
            expectedOutputs: [{ type: "text", languages: normalizeLanguages(request?.expectedOutputLanguages, defaultOutputLanguages) }],
        };
    }

    function normalizeLanguages(value, fallback) {
        const values = Array.isArray(value) ? value : [];
        const normalized = values
            .map((language) => typeof language === "string" ? language.trim().toLowerCase() : "")
            .filter((language, index, languages) => supportedTextLanguages.has(language) && languages.indexOf(language) === index);
        return normalized.length > 0 ? normalized : fallback;
    }

    function formatError(error) {
        if (error instanceof Error && error.message) {
            return error.message;
        }

        return String(error);
    }

    globalThis.bocchiAi = {
        createSession,
        destroySession,
        getAvailability,
        prompt,
        promptSession,
    };
})();
