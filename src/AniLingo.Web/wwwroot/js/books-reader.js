(() => {
    const root = document.querySelector("[data-book-reader]");
    if (!root) return;

    const original = root.querySelector("[data-book-original]");
    const translated = root.querySelector("[data-book-translated]");
    const content = root.querySelector("[data-book-reader-content]");
    const progressForm = root.querySelector("[data-book-progress-form]");
    const bookmarkForm = root.querySelector("[data-book-bookmark-form]");
    const translateForm = root.querySelector("[data-book-translate-form]");
    const settingsForm = root.querySelector("[data-book-settings-form]");
    const resetForm = root.querySelector("[data-book-reset-form]");
    const settingsJson = root.querySelector("[data-book-settings-json]");
    const progressBar = root.querySelector("[data-book-progress-bar]");
    const toast = root.querySelector("[data-book-toast]");
    const pageControls = root.querySelector("[data-book-page-controls]");
    const pageNumber = root.querySelector("[data-book-page-number]");
    const pagePrev = root.querySelector("[data-book-page-prev]");
    const pageNext = root.querySelector("[data-book-page-next]");
    const autoScrollButton = root.querySelector("[data-book-autoscroll]");
    const overrideState = root.querySelector("[data-book-override-state]");
    const targetLanguage = root.dataset.targetLanguage || "id";
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    let view = root.dataset.view || "original";
    let saveTimer = 0;
    let pollTimer = 0;
    let settingsSave = Promise.resolve();
    let autoScrollFrame = 0;
    let autoScrollLast = 0;
    let autoScrollRunning = false;
    let currentPage = 0;
    let pageCount = 1;
    let restoringPage = false;

    let settings = {
        readingMode: "continuous",
        pageTransition: "curl",
        twoPageSpread: true,
        autoScrollSpeed: 36,
        fontFamily: "literary-serif",
        fontSizeRem: 1.06,
        lineHeight: 1.9,
        paragraphSpacingEm: 0.85,
        textWidthPx: 760,
        textAlignment: "start",
        chapterStyle: "light-novel",
        paperStyle: "midnight",
        genreArtworkEnabled: true,
        genreTheme: "auto",
        backgroundAssetId: "auto",
        backgroundIntensity: 0.055,
        backgroundMotionMode: "auto",
        themeEffectStrength: 1,
        themeBrightness: 1,
        themeContrast: 1,
        themeSaturation: 1,
        themeBlurPx: 0,
        themeVignetteStrength: 1,
        themeGrainStrength: 1,
        themeTextBackdropStrength: 1,
        themeParallaxStrength: 1,
        themeTintStrength: 1,
        bookmarkStyle: "fabric",
        bookmarkColor: "#b04455",
        hasBookOverride: false
    };

    if (settingsJson) {
        try {
            settings = Object.assign(settings, JSON.parse(settingsJson.textContent || "{}"));
        } catch {
        }
    }

    const fontStacks = {
        "system-serif": 'Georgia, "Times New Roman", serif',
        "system-sans": 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
        "literary-serif": '"Literata", Georgia, "Times New Roman", serif',
        "book-serif": '"Lora", Georgia, "Times New Roman", serif',
        "atkinson": '"Atkinson Hyperlegible", system-ui, sans-serif',
        "noto-serif-jp": '"Noto Serif JP", "Yu Mincho", serif',
        "noto-sans-jp": '"Noto Sans JP", system-ui, sans-serif'
    };

    function activeColumn() {
        if (view === "translated" && translated && !translated.hidden) return translated;
        if (view === "original" && original && !original.hidden) return original;
        return null;
    }

    function setView(next) {
        if (next === "translated" && root.dataset.hasTranslation !== "true") return;
        if (next === "both" && root.dataset.hasTranslation !== "true") return;

        stopAutoScroll();
        view = next;
        root.dataset.view = next;

        if (original) original.hidden = next === "translated";
        if (translated) translated.hidden = next === "original";

        root.querySelectorAll("[data-book-view]").forEach((button) => {
            button.classList.toggle("active", button.dataset.bookView === next);
        });

        requestAnimationFrame(() => configurePaging(true));
    }

    function scrollPermille() {
        if (settings.readingMode === "paged" && view !== "both") {
            return pageCount <= 1
                ? 0
                : Math.round(currentPage / (pageCount - 1) * 1000);
        }

        const max = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
        return Math.max(0, Math.min(1000, Math.round(window.scrollY / max * 1000)));
    }

    function updateProgress() {
        const value = scrollPermille();
        if (progressBar) progressBar.style.width = String(value / 10) + "%";

        for (const form of [progressForm, bookmarkForm]) {
            if (!form) continue;
            const field = form.querySelector('[name="positionPermille"]');
            if (field) field.value = String(value);
        }

        return value;
    }

    async function postForm(form) {
        const response = await fetch(form.action, {
            method: "POST",
            body: new FormData(form),
            headers: { "X-Requested-With": "fetch" },
            credentials: "same-origin"
        });

        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || ("Request failed (" + response.status + ")"));
        }

        const type = response.headers.get("content-type") || "";
        return type.includes("application/json") ? response.json() : null;
    }

    function queueProgressSave() {
        updateProgress();
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(async () => {
            if (!progressForm) return;
            try {
                await postForm(progressForm);
            } catch {
            }
        }, 900);
    }

    function showToast(message) {
        if (!toast) return;
        toast.textContent = message;
        toast.hidden = false;
        window.clearTimeout(showToast.timer);
        showToast.timer = window.setTimeout(() => {
            toast.hidden = true;
        }, 2200);
    }

    function renderTranslation(paragraphs) {
        if (!translated) return;
        translated.replaceChildren();

        for (const value of paragraphs || []) {
            const paragraph = document.createElement("p");
            paragraph.textContent = value;
            translated.append(paragraph);
        }

        root.dataset.hasTranslation = "true";
        root.querySelectorAll('[data-book-view="translated"], [data-book-view="both"]')
            .forEach((button) => {
                button.disabled = false;
            });

        const slot = root.querySelector("[data-translation-slot]");
        if (slot) slot.remove();

        setView("translated");
    }

    async function pollTranslation() {
        window.clearTimeout(pollTimer);

        try {
            const url = new URL(window.location.href);
            url.searchParams.set("handler", "TranslationStatus");
            url.searchParams.set("lang", targetLanguage);

            const response = await fetch(url, {
                credentials: "same-origin",
                headers: { "X-Requested-With": "fetch" }
            });
            if (!response.ok) return;

            const result = await response.json();
            if (result.status === "ready") {
                renderTranslation(result.paragraphs);
                showToast("Translation ready.");
                return;
            }
        } catch {
        }

        pollTimer = window.setTimeout(pollTranslation, 2500);
    }

    function formatSetting(key, value) {
        if (key === "autoScrollSpeed") return Math.round(Number(value)) + " px/s";
        if (key === "fontSizeRem") return Number(value).toFixed(2) + " rem";
        if (key === "lineHeight") return Number(value).toFixed(2);
        if (key === "paragraphSpacingEm") return Number(value).toFixed(1) + " em";
        if (key === "textWidthPx") return Math.round(Number(value)) + " px";
        if (key === "backgroundIntensity") return Math.round(Number(value) * 100) + "%";
        if (key === "themeBlurPx") return Number(value).toFixed(1) + " px";
        if ([
            "themeEffectStrength",
            "themeBrightness",
            "themeContrast",
            "themeSaturation",
            "themeVignetteStrength",
            "themeGrainStrength",
            "themeTextBackdropStrength",
            "themeParallaxStrength",
            "themeTintStrength"
        ].includes(key)) {
            return Math.round(Number(value) * 100) + "%";
        }
        return String(value == null ? "" : value);
    }

    function syncSettingControls() {
        if (!settingsForm) return;

        settingsForm.querySelectorAll("[data-book-setting]").forEach((control) => {
            const key = control.dataset.bookSetting;
            if (!(key in settings)) return;

            if (control.type === "checkbox") {
                control.checked = Boolean(settings[key]);
            } else {
                control.value = String(settings[key]);
            }
        });

        settingsForm.querySelectorAll("[data-book-setting-output]").forEach((output) => {
            const key = output.dataset.bookSettingOutput;
            output.textContent = formatSetting(key, settings[key]);
        });

        if (overrideState) {
            overrideState.textContent = settings.hasBookOverride
                ? "Customized for this book"
                : settings.hasGenreOverride
                    ? "Genre default"
                    : settings.hasTypeOverride
                        ? "Type default"
                        : "My default";
        }
    }

    function setSettingsFormState(data) {
        const names = {
            readingMode: "ReadingMode",
            pageTransition: "PageTransition",
            twoPageSpread: "TwoPageSpread",
            autoScrollSpeed: "AutoScrollSpeed",
            fontFamily: "FontFamily",
            fontSizeRem: "FontSizeRem",
            lineHeight: "LineHeight",
            paragraphSpacingEm: "ParagraphSpacingEm",
            textWidthPx: "TextWidthPx",
            textAlignment: "TextAlignment",
            chapterStyle: "ChapterStyle",
            paperStyle: "PaperStyle",
            genreArtworkEnabled: "GenreArtworkEnabled",
            genreTheme: "GenreTheme",
            backgroundAssetId: "BackgroundAssetId",
            backgroundIntensity: "BackgroundIntensity",
            backgroundMotionMode: "BackgroundMotionMode",
            themeEffectStrength: "ThemeEffectStrength",
            themeBrightness: "ThemeBrightness",
            themeContrast: "ThemeContrast",
            themeSaturation: "ThemeSaturation",
            themeBlurPx: "ThemeBlurPx",
            themeVignetteStrength: "ThemeVignetteStrength",
            themeGrainStrength: "ThemeGrainStrength",
            themeTextBackdropStrength: "ThemeTextBackdropStrength",
            themeParallaxStrength: "ThemeParallaxStrength",
            themeTintStrength: "ThemeTintStrength",
            bookmarkStyle: "BookmarkStyle",
            bookmarkColor: "BookmarkColor"
        };

        for (const [key, name] of Object.entries(names)) {
            data.set(name, String(settings[key]));
        }
    }

    async function saveSettings(scope, changedKey) {
        if (!settingsForm) return null;

        const data = new FormData(settingsForm);
        setSettingsFormState(data);
        data.set("scope", scope);
        data.set("changedKey", changedKey || "");

        const response = await fetch(settingsForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });

        if (!response.ok) {
            throw new Error((await response.text()) || "Reader settings could not be saved.");
        }

        const result = await response.json();
        if (result && result.settings) {
            settings = Object.assign(settings, result.settings);
            applySettings(false);
        }

        return result;
    }

    function activePreferenceTarget() {
        return settingsForm?.querySelector('[name="scope"]')?.value || "work";
    }

    function scheduleSettingSave(changedKey) {
        const scope = activePreferenceTarget();
        if (scope === "work" || scope === "book") {
            settings.hasBookOverride = true;
        }
        syncSettingControls();

        settingsSave = settingsSave
            .then(() => saveSettings(scope, changedKey))
            .catch((error) => showToast(error.message));
    }

    function applySettings(keepPage) {
        root.dataset.readingMode = settings.readingMode;
        root.dataset.paperStyle = settings.paperStyle;
        root.dataset.chapterStyle = settings.chapterStyle;
        root.dataset.pageTransition = settings.pageTransition;
        root.dataset.twoPageSpread = settings.twoPageSpread ? "true" : "false";

        root.style.setProperty("--book-reader-font-size", Number(settings.fontSizeRem) + "rem");
        root.style.setProperty("--book-reader-line-height", String(settings.lineHeight));
        root.style.setProperty("--book-reader-paragraph-spacing", Number(settings.paragraphSpacingEm) + "em");
        root.style.setProperty("--book-reader-text-width", Math.round(Number(settings.textWidthPx)) + "px");
        root.style.setProperty("--book-reader-font", fontStacks[settings.fontFamily] || fontStacks["literary-serif"]);
        root.style.setProperty("--book-bookmark-color", settings.bookmarkColor || "#b04455");

        if (content) {
            content.style.textAlign = settings.textAlignment === "justify" ? "justify" : "start";
        }

        syncSettingControls();

        root.dispatchEvent(new CustomEvent("anilingo:reader-settings", {
            detail: { settings }
        }));

        if (settings.readingMode !== "continuous") {
            stopAutoScroll();
        }

        requestAnimationFrame(() => configurePaging(Boolean(keepPage)));
    }

    function configurePaging(keepPage) {
        if (!pageControls) return;

        const paged = settings.readingMode === "paged" && view !== "both";
        pageControls.hidden = !paged;

        if (!paged) {
            currentPage = 0;
            pageCount = 1;
            updateProgress();
            return;
        }

        const column = activeColumn();
        if (!column) {
            pageControls.hidden = true;
            return;
        }

        const oldProgress = keepPage ? scrollPermille() : Number(root.dataset.progress || "0");
        const width = Math.max(1, column.clientWidth);
        pageCount = Math.max(1, Math.ceil(column.scrollWidth / width));

        const targetPage = pageCount <= 1
            ? 0
            : Math.max(0, Math.min(pageCount - 1, Math.round(oldProgress / 1000 * (pageCount - 1))));

        restoringPage = true;
        column.scrollLeft = targetPage * width;
        currentPage = targetPage;
        updatePageNumber();
        updateProgress();

        requestAnimationFrame(() => {
            restoringPage = false;
        });
    }

    function updatePageNumber() {
        if (pageNumber) pageNumber.textContent = String(currentPage + 1) + " / " + String(pageCount);
        if (pagePrev) pagePrev.disabled = currentPage <= 0;
        if (pageNext) pageNext.disabled = currentPage >= pageCount - 1;
    }

    function turnPage(direction) {
        const column = activeColumn();
        if (!column || settings.readingMode !== "paged") return;

        const width = Math.max(1, column.clientWidth);
        const target = Math.max(0, Math.min(pageCount - 1, currentPage + direction));
        if (target === currentPage) return;

        if (!reduceMotion.matches && settings.pageTransition === "curl") {
            root.classList.add(direction > 0 ? "book-turn-next" : "book-turn-prev");
            window.setTimeout(() => {
                root.classList.remove("book-turn-next", "book-turn-prev");
            }, 260);
        }

        column.scrollTo({
            left: target * width,
            behavior: reduceMotion.matches || settings.pageTransition === "none" ? "auto" : "smooth"
        });

        currentPage = target;
        updatePageNumber();
        queueProgressSave();
    }

    function stopAutoScroll() {
        if (autoScrollFrame) cancelAnimationFrame(autoScrollFrame);
        autoScrollFrame = 0;
        autoScrollLast = 0;
        autoScrollRunning = false;
        if (autoScrollButton) {
            autoScrollButton.setAttribute("aria-pressed", "false");
            autoScrollButton.textContent = "▶";
        }
    }

    function autoScrollTick(time) {
        if (!autoScrollRunning) return;
        if (!autoScrollLast) autoScrollLast = time;

        const delta = Math.min(60, time - autoScrollLast);
        autoScrollLast = time;
        window.scrollBy(0, Number(settings.autoScrollSpeed) * delta / 1000);
        updateProgress();

        if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2) {
            stopAutoScroll();
            return;
        }

        autoScrollFrame = requestAnimationFrame(autoScrollTick);
    }

    function toggleAutoScroll() {
        if (settings.readingMode !== "continuous") {
            showToast("Auto-scroll is available in Continuous mode.");
            return;
        }

        if (autoScrollRunning) {
            stopAutoScroll();
            return;
        }

        autoScrollRunning = true;
        if (autoScrollButton) {
            autoScrollButton.setAttribute("aria-pressed", "true");
            autoScrollButton.textContent = "Ⅱ";
        }
        autoScrollFrame = requestAnimationFrame(autoScrollTick);
    }

    root.querySelectorAll("[data-book-view]").forEach((button) => {
        button.addEventListener("click", () => setView(button.dataset.bookView));
    });

    if (translateForm) {
        translateForm.addEventListener("submit", async (event) => {
            event.preventDefault();
            const button = translateForm.querySelector("button");
            if (button) button.disabled = true;

            const state = root.querySelector("[data-translation-state]");
            if (state) state.textContent = "Translation queued…";

            try {
                const result = await postForm(translateForm);
                if (result && result.status === "ready") {
                    await pollTranslation();
                    return;
                }
                showToast("Translation queued.");
                pollTranslation();
            } catch (error) {
                if (button) button.disabled = false;
                if (state) state.textContent = error.message || "Translation failed.";
            }
        });
    }

    const bookmarkButton = root.querySelector("[data-bookmark-button]");
    if (bookmarkButton && bookmarkForm) {
        bookmarkButton.addEventListener("click", async () => {
            updateProgress();
            try {
                await postForm(bookmarkForm);
                const count = bookmarkButton.querySelector("[data-bookmark-count]");
                if (count) count.textContent = String((Number(count.textContent) || 0) + 1);
                bookmarkButton.classList.add("active");
                bookmarkButton.style.setProperty("--bookmark-color", settings.bookmarkColor || "#b04455");
                showToast("Bookmark added at " + Math.round(scrollPermille() / 10) + "%.");
            } catch (error) {
                showToast(error.message || "Could not add bookmark.");
            }
        });
    }

    if (settingsForm) {
        settingsForm.querySelectorAll("[data-book-setting]").forEach((control) => {
            const eventName = control.type === "range" ? "input" : "change";
            control.addEventListener(eventName, () => {
                const key = control.dataset.bookSetting;
                let value = control.type === "checkbox" ? control.checked : control.value;

                if ([
                    "autoScrollSpeed",
                    "fontSizeRem",
                    "lineHeight",
                    "paragraphSpacingEm",
                    "textWidthPx",
                    "backgroundIntensity",
                    "themeEffectStrength",
                    "themeBrightness",
                    "themeContrast",
                    "themeSaturation",
                    "themeBlurPx",
                    "themeVignetteStrength",
                    "themeGrainStrength",
                    "themeTextBackdropStrength",
                    "themeParallaxStrength",
                    "themeTintStrength"
                ].includes(key)) {
                    value = Number(value);
                }

                settings[key] = value;
                applySettings(true);

                window.clearTimeout(control._bookSaveTimer);
                control._bookSaveTimer = window.setTimeout(() => {
                    scheduleSettingSave(key);
                }, control.type === "range" ? 300 : 0);
            });
        });

        const saveDefaults = root.querySelector("[data-book-save-defaults]");
        if (saveDefaults) {
            saveDefaults.addEventListener("click", async () => {
                try {
                    await saveSettings("default", "");
                    showToast("Saved as your reader defaults.");
                } catch (error) {
                    showToast(error.message);
                }
            });
        }
    }

    const resetButton = root.querySelector("[data-book-reset-settings]");
    if (resetButton && resetForm) {
        resetButton.addEventListener("click", async () => {
            try {
                const result = await postForm(resetForm);
                if (result && result.settings) {
                    settings = Object.assign(settings, result.settings);
                    applySettings(false);
                    showToast("Book reader reset to your defaults.");
                }
            } catch (error) {
                showToast(error.message || "Could not reset reader settings.");
            }
        });
    }

    if (autoScrollButton) {
        autoScrollButton.addEventListener("click", toggleAutoScroll);
    }

    if (pagePrev) pagePrev.addEventListener("click", () => turnPage(-1));
    if (pageNext) pageNext.addEventListener("click", () => turnPage(1));

    root.addEventListener("anilingo:reader-settings-response", (event) => {
        if (!event.detail?.settings) return;
        settings = Object.assign(settings, event.detail.settings);
        applySettings(false);
    });

    root.addEventListener("anilingo:reader-page-edge", (event) => {
        const direction = Number(event.detail?.direction || 0);
        if (!direction || settings.readingMode !== "paged") return;
        turnPage(direction);
    });

    for (const column of [original, translated]) {
        if (!column) continue;
        column.addEventListener("scroll", () => {
            if (restoringPage || settings.readingMode !== "paged" || column !== activeColumn()) return;
            const width = Math.max(1, column.clientWidth);
            currentPage = Math.max(0, Math.min(pageCount - 1, Math.round(column.scrollLeft / width)));
            updatePageNumber();
            queueProgressSave();
        }, { passive: true });
    }

    window.addEventListener("scroll", () => {
        if (settings.readingMode === "continuous") queueProgressSave();
    }, { passive: true });

    window.addEventListener("resize", () => {
        window.clearTimeout(window._bookReaderResize);
        window._bookReaderResize = window.setTimeout(() => configurePaging(true), 150);
    });

    document.addEventListener("selectionchange", () => {
        if (autoScrollRunning && !window.getSelection()?.isCollapsed) stopAutoScroll();
    });

    setView(view);
    applySettings(false);

    const initial = Number(root.dataset.progress || "0");
    if (initial > 0 && settings.readingMode === "continuous") {
        root.dispatchEvent(new CustomEvent("anilingo:reader-restoring", {
            detail: { active: true }
        }));
        requestAnimationFrame(() => {
            const max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
            window.scrollTo({ top: max * initial / 1000, behavior: "auto" });
            requestAnimationFrame(() => {
                root.classList.remove("reader-chrome-hidden");
                root.dispatchEvent(new CustomEvent("anilingo:reader-restoring", {
                    detail: { active: false }
                }));
            });
        });
    } else {
        root.dispatchEvent(new CustomEvent("anilingo:reader-restoring", {
            detail: { active: false }
        }));
    }

    if (root.dataset.hasTranslation !== "true" && translateForm) {
        pollTimer = window.setTimeout(pollTranslation, 2500);
    }
})();


(() => {
    const root = document.querySelector("[data-book-reader]");
    if (!root || root.dataset.navigationAnnotationsReady === "true") return;
    root.dataset.navigationAnnotationsReady = "true";

    const drawer = root.querySelector("[data-book-drawer]");
    const openDrawerButton = root.querySelector("[data-book-drawer-open]");
    const closeDrawerButton = root.querySelector("[data-book-drawer-close]");
    const chapterList = root.querySelector("[data-book-chapter-list]");
    const bookmarkList = root.querySelector("[data-book-bookmark-list]");
    const highlightList = root.querySelector("[data-book-highlight-list]");
    const chapterSearch = root.querySelector("[data-book-chapter-search]");
    const selectionToolbar = root.querySelector("[data-book-selection-toolbar]");
    const highlightButton = root.querySelector("[data-book-highlight-button]");
    const highlightForm = root.querySelector("[data-book-highlight-form]");
    const bookmarkForm = root.querySelector("[data-book-bookmark-form]");
    const bookmarkButton = root.querySelector("[data-bookmark-button]");
    const highlightsJson = root.querySelector("[data-book-highlights-json]");
    const original = root.querySelector("[data-book-original]");
    const translated = root.querySelector("[data-book-translated]");
    const targetLanguage = root.dataset.targetLanguage || "id";
    const currentChapterId = (root.dataset.chapterId || "").toLowerCase();

    let currentHighlights = [];
    let allAnnotations = null;
    let chaptersLoaded = false;
    let annotationLoading = null;
    let chapterTimer = 0;
    let selectionState = null;

    if (highlightsJson) {
        try {
            currentHighlights = JSON.parse(highlightsJson.textContent || "[]") || [];
        } catch {
            currentHighlights = [];
        }
    }

    function prop(value, name) {
        if (!value) return undefined;
        return value[name] ?? value[name.charAt(0).toUpperCase() + name.slice(1)];
    }

    function antiforgeryToken() {
        return root.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    }

    function handlerUrl(handler, extra) {
        const url = new URL(window.location.href);
        url.searchParams.delete("pos");
        url.searchParams.delete("view");
        url.searchParams.set("handler", handler);
        url.searchParams.set("lang", targetLanguage);

        for (const [key, value] of Object.entries(extra || {})) {
            if (value == null || value === "") {
                url.searchParams.delete(key);
            } else {
                url.searchParams.set(key, String(value));
            }
        }

        return url;
    }

    async function getJson(handler, extra) {
        const response = await fetch(handlerUrl(handler, extra), {
            method: "GET",
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });

        if (!response.ok) {
            throw new Error((await response.text()) || "Could not load reader data.");
        }

        return response.json();
    }

    async function postHandler(handler, values) {
        const data = new FormData();
        const token = antiforgeryToken();
        if (token) data.set("__RequestVerificationToken", token);
        data.set("lang", targetLanguage);

        for (const [key, value] of Object.entries(values || {})) {
            if (value != null) data.set(key, String(value));
        }

        const response = await fetch(handlerUrl(handler), {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });

        if (!response.ok) {
            throw new Error((await response.text()) || "Reader action failed.");
        }

        const type = response.headers.get("content-type") || "";
        return type.includes("application/json")
            ? response.json()
            : null;
    }

    function showToast(message) {
        const toast = root.querySelector("[data-book-toast]");
        if (!toast) return;
        toast.textContent = message;
        toast.hidden = false;
        window.clearTimeout(showToast.timer);
        showToast.timer = window.setTimeout(() => {
            toast.hidden = true;
        }, 2200);
    }

    function readerUrl(chapterId, positionPermille, requestedView) {
        const url = new URL("/Books/Read/" + chapterId, window.location.origin);
        url.searchParams.set("lang", targetLanguage);

        if (Number.isFinite(Number(positionPermille))) {
            url.searchParams.set(
                "pos",
                String(Math.max(0, Math.min(1000, Math.round(Number(positionPermille)))))
            );
        }

        const view = requestedView || root.dataset.view;
        if (view === "original" || view === "translated" || view === "both") {
            url.searchParams.set("view", view);
        }

        return url.toString();
    }

    function elementButton(label, className) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = label;
        if (className) button.className = className;
        return button;
    }

    function emptyMessage(text) {
        const value = document.createElement("span");
        value.className = "books-muted";
        value.textContent = text;
        return value;
    }

    async function loadChapters(query) {
        if (!chapterList) return;

        chapterList.replaceChildren(emptyMessage("Loading chapters…"));

        try {
            const result = await getJson("Chapters", { q: query || "" });
            const chapters = prop(result, "chapters") || [];

            chapterList.replaceChildren();

            if (chapters.length === 0) {
                chapterList.append(emptyMessage("No chapters found."));
                chaptersLoaded = true;
                return;
            }

            for (const chapter of chapters) {
                const id = String(prop(chapter, "id") || "");
                const number = prop(chapter, "number");
                const title = prop(chapter, "title") || ("Chapter " + number);

                const link = document.createElement("a");
                link.className = "book-drawer-row";
                if (id.toLowerCase() === currentChapterId) {
                    link.classList.add("current");
                    link.setAttribute("aria-current", "page");
                }
                link.href = readerUrl(id, null, root.dataset.view);

                const index = document.createElement("span");
                index.className = "book-drawer-row-index";
                index.textContent = String(number);

                const name = document.createElement("strong");
                name.textContent = title;

                link.append(index, name);
                chapterList.append(link);
            }

            chaptersLoaded = true;
        } catch (error) {
            chapterList.replaceChildren(
                emptyMessage(error.message || "Could not load chapters.")
            );
        }
    }

    async function ensureAnnotations() {
        if (allAnnotations) return allAnnotations;
        if (annotationLoading) return annotationLoading;

        annotationLoading = getJson("Annotations")
            .then((value) => {
                allAnnotations = value || {};
                renderBookmarks();
                renderHighlightsList();
                const totalBookmarks = (prop(allAnnotations, "bookmarks") || []).length;
                const count = bookmarkButton?.querySelector("[data-bookmark-count]");
                if (count) count.textContent = String(totalBookmarks);
                return allAnnotations;
            })
            .finally(() => {
                annotationLoading = null;
            });

        return annotationLoading;
    }

    function renderBookmarks() {
        if (!bookmarkList || !allAnnotations) return;

        const bookmarks = prop(allAnnotations, "bookmarks") || [];
        bookmarkList.replaceChildren();

        if (bookmarks.length === 0) {
            bookmarkList.append(emptyMessage("No bookmarks yet."));
            return;
        }

        for (const bookmark of bookmarks) {
            const row = document.createElement("div");
            row.className = "book-drawer-annotation-row";

            const jump = document.createElement("a");
            jump.className = "book-drawer-annotation-main";
            const language = prop(bookmark, "language");
            jump.href = readerUrl(
                prop(bookmark, "chapterId"),
                prop(bookmark, "positionPermille"),
                language === "original" ? "original" : "translated"
            );

            const title = document.createElement("strong");
            title.textContent =
                "Chapter " + prop(bookmark, "chapterNumber") + " · "
                + (prop(bookmark, "chapterTitle") || "");

            const meta = document.createElement("span");
            meta.textContent =
                Math.round(Number(prop(bookmark, "positionPermille") || 0) / 10)
                + "% · "
                + (language === "original"
                    ? "Original"
                    : language || targetLanguage);

            jump.append(title, meta);

            const remove = elementButton("Delete", "book-drawer-delete");
            remove.addEventListener("click", async () => {
                remove.disabled = true;
                try {
                    await postHandler("RemoveBookmark", {
                        bookmarkId: prop(bookmark, "id")
                    });

                    const items = prop(allAnnotations, "bookmarks") || [];
                    allAnnotations.bookmarks = items.filter(
                        (item) => prop(item, "id") !== prop(bookmark, "id")
                    );
                    renderBookmarks();

                    const count = bookmarkButton?.querySelector("[data-bookmark-count]");
                    if (count) {
                        count.textContent = String(allAnnotations.bookmarks.length);
                    }
                    showToast("Bookmark removed.");
                } catch (error) {
                    remove.disabled = false;
                    showToast(error.message || "Could not remove bookmark.");
                }
            });

            row.append(jump, remove);
            bookmarkList.append(row);
        }
    }

    function renderHighlightsList() {
        if (!highlightList || !allAnnotations) return;

        const highlights = prop(allAnnotations, "highlights") || [];
        highlightList.replaceChildren();

        if (highlights.length === 0) {
            highlightList.append(emptyMessage("No highlights yet."));
            return;
        }

        for (const highlight of highlights) {
            const row = document.createElement("div");
            row.className = "book-drawer-annotation-row";

            const jump = document.createElement("a");
            jump.className = "book-drawer-annotation-main";
            const language = prop(highlight, "language");
            const paragraphIndex = Number(prop(highlight, "paragraphIndex") || 0);
            jump.href = readerUrl(
                prop(highlight, "chapterId"),
                Math.max(0, Math.min(1000, paragraphIndex * 8)),
                language === "original" ? "original" : "translated"
            );

            const title = document.createElement("strong");
            title.textContent =
                "Chapter " + prop(highlight, "chapterNumber") + " · "
                + (prop(highlight, "chapterTitle") || "");

            const quote = document.createElement("span");
            quote.className = "book-drawer-highlight-quote";
            quote.textContent = "“" + (prop(highlight, "text") || "") + "”";

            jump.append(title, quote);

            const remove = elementButton("Delete", "book-drawer-delete");
            remove.addEventListener("click", async () => {
                remove.disabled = true;
                try {
                    const id = prop(highlight, "id");
                    await postHandler("RemoveHighlight", {
                        highlightId: id
                    });

                    const items = prop(allAnnotations, "highlights") || [];
                    allAnnotations.highlights = items.filter(
                        (item) => prop(item, "id") !== id
                    );
                    currentHighlights = currentHighlights.filter(
                        (item) => prop(item, "id") !== id
                    );
                    renderHighlightsList();
                    applyHighlights();
                    showToast("Highlight removed.");
                } catch (error) {
                    remove.disabled = false;
                    showToast(error.message || "Could not remove highlight.");
                }
            });

            row.append(jump, remove);
            highlightList.append(row);
        }
    }

    function setDrawerTab(name) {
        root.querySelectorAll("[data-book-drawer-tab]").forEach((button) => {
            button.classList.toggle(
                "active",
                button.dataset.bookDrawerTab === name
            );
        });

        root.querySelectorAll("[data-book-drawer-panel]").forEach((panel) => {
            panel.hidden = panel.dataset.bookDrawerPanel !== name;
        });

        if (name === "chapters") {
            if (!chaptersLoaded) loadChapters("");
        } else {
            ensureAnnotations().catch((error) => {
                const target = name === "bookmarks" ? bookmarkList : highlightList;
                if (target) {
                    target.replaceChildren(
                        emptyMessage(error.message || "Could not load annotations.")
                    );
                }
            });
        }
    }

    function openDrawer(tab) {
        if (!drawer) return;

        setDrawerTab(tab || "chapters");

        if (typeof drawer.showModal === "function") {
            if (!drawer.open) drawer.showModal();
        } else {
            drawer.setAttribute("open", "");
        }
    }

    function closeDrawer() {
        if (!drawer) return;
        if (typeof drawer.close === "function" && drawer.open) {
            drawer.close();
        } else {
            drawer.removeAttribute("open");
        }
    }

    function paragraphForNode(node) {
        const element = node?.nodeType === Node.ELEMENT_NODE
            ? node
            : node?.parentElement;
        return element?.closest?.("p[data-book-paragraph]") || null;
    }

    function offsetWithin(paragraph, node, offset) {
        const range = document.createRange();
        range.selectNodeContents(paragraph);
        range.setEnd(node, offset);
        return range.toString().length;
    }

    function hideSelectionToolbar() {
        selectionState = null;
        if (selectionToolbar) selectionToolbar.hidden = true;
    }

    function inspectSelection() {
        if (!selectionToolbar) return;

        const selection = window.getSelection();
        if (!selection || selection.isCollapsed || selection.rangeCount === 0) {
            hideSelectionToolbar();
            return;
        }

        const range = selection.getRangeAt(0);
        const startParagraph = paragraphForNode(range.startContainer);
        const endParagraph = paragraphForNode(range.endContainer);

        if (!startParagraph || startParagraph !== endParagraph) {
            hideSelectionToolbar();
            return;
        }

        const column = startParagraph.closest("[data-book-language]");
        if (!column || !root.contains(column)) {
            hideSelectionToolbar();
            return;
        }

        const start = offsetWithin(
            startParagraph,
            range.startContainer,
            range.startOffset
        );
        const end = offsetWithin(
            startParagraph,
            range.endContainer,
            range.endOffset
        );

        if (end <= start) {
            hideSelectionToolbar();
            return;
        }

        selectionState = {
            language: column.dataset.bookLanguage || "original",
            paragraphIndex: Number(startParagraph.dataset.bookParagraph || "0"),
            startOffset: start,
            endOffset: end
        };

        const rect = range.getBoundingClientRect();
        const width = selectionToolbar.offsetWidth || 96;
        const left = Math.max(
            8,
            Math.min(
                window.innerWidth - width - 8,
                rect.left + rect.width / 2 - width / 2
            )
        );
        const top = Math.max(8, rect.top - 44);

        selectionToolbar.style.left = left + "px";
        selectionToolbar.style.top = top + "px";
        selectionToolbar.hidden = false;
    }

    function rangesFor(language, paragraphIndex) {
        return currentHighlights
            .filter((item) =>
                String(prop(item, "language") || "") === language
                && Number(prop(item, "paragraphIndex")) === paragraphIndex
            )
            .map((item) => ({
                id: prop(item, "id"),
                start: Number(prop(item, "startOffset") || 0),
                end: Number(prop(item, "endOffset") || 0),
                note: prop(item, "note")
            }))
            .sort((a, b) => a.start - b.start);
    }

    function renderParagraphHighlights(paragraph, language, paragraphIndex) {
        const text = paragraph.textContent || "";
        const ranges = rangesFor(language, paragraphIndex);

        if (ranges.length === 0) {
            if (paragraph.querySelector("mark[data-book-highlight]")) {
                paragraph.textContent = text;
            }
            return;
        }

        const fragment = document.createDocumentFragment();
        let cursor = 0;

        for (const item of ranges) {
            const start = Math.max(cursor, Math.min(text.length, item.start));
            const end = Math.max(start, Math.min(text.length, item.end));
            if (end <= start) continue;

            if (start > cursor) {
                fragment.append(document.createTextNode(text.slice(cursor, start)));
            }

            const mark = document.createElement("mark");
            mark.dataset.bookHighlight = String(item.id || "");
            mark.textContent = text.slice(start, end);
            if (item.note) mark.title = item.note;
            fragment.append(mark);
            cursor = end;
        }

        if (cursor < text.length) {
            fragment.append(document.createTextNode(text.slice(cursor)));
        }

        paragraph.replaceChildren(fragment);
    }

    function ensureParagraphMetadata(column) {
        if (!column) return;
        column.querySelectorAll("p").forEach((paragraph, index) => {
            paragraph.dataset.bookParagraph = String(index);
        });
    }

    function applyHighlights() {
        ensureParagraphMetadata(original);
        ensureParagraphMetadata(translated);

        for (const column of [original, translated]) {
            if (!column) continue;
            const language = column.dataset.bookLanguage || "original";
            column.querySelectorAll("p[data-book-paragraph]").forEach((paragraph) => {
                renderParagraphHighlights(
                    paragraph,
                    language,
                    Number(paragraph.dataset.bookParagraph || "0")
                );
            });
        }
    }

    async function createHighlight() {
        if (!selectionState || !highlightForm) return;

        const data = new FormData(highlightForm);
        data.set("anchorLanguage", selectionState.language);
        data.set("paragraphIndex", String(selectionState.paragraphIndex));
        data.set("startOffset", String(selectionState.startOffset));
        data.set("endOffset", String(selectionState.endOffset));
        data.set("note", "");

        const response = await fetch(highlightForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });

        if (!response.ok) {
            throw new Error((await response.text()) || "Could not create highlight.");
        }

        const item = await response.json();
        currentHighlights.push(item);

        if (allAnnotations) {
            const existing = prop(allAnnotations, "highlights") || [];
            allAnnotations.highlights = existing.concat([item]);
            renderHighlightsList();
        }

        window.getSelection()?.removeAllRanges();
        hideSelectionToolbar();
        applyHighlights();
        showToast("Highlight saved.");
    }

    if (openDrawerButton) {
        openDrawerButton.addEventListener("click", () => openDrawer("chapters"));
    }

    if (closeDrawerButton) {
        closeDrawerButton.addEventListener("click", closeDrawer);
    }

    if (drawer) {
        drawer.addEventListener("click", (event) => {
            if (event.target === drawer) closeDrawer();
        });
    }

    root.querySelectorAll("[data-book-drawer-tab]").forEach((button) => {
        button.addEventListener("click", () => {
            setDrawerTab(button.dataset.bookDrawerTab || "chapters");
        });
    });

    if (chapterSearch) {
        chapterSearch.addEventListener("input", () => {
            window.clearTimeout(chapterTimer);
            chapterTimer = window.setTimeout(() => {
                loadChapters(chapterSearch.value || "");
            }, 180);
        });
    }

    if (bookmarkButton && bookmarkForm) {
        bookmarkButton.addEventListener("click", () => {
            const anchor = bookmarkForm.querySelector('[name="anchorLanguage"]');
            if (anchor) {
                anchor.value = root.dataset.view === "original"
                    ? "original"
                    : targetLanguage;
            }
        }, true);
    }

    document.addEventListener("selectionchange", () => {
        window.clearTimeout(inspectSelection.timer);
        inspectSelection.timer = window.setTimeout(inspectSelection, 40);
    });

    if (highlightButton) {
        highlightButton.addEventListener("click", async () => {
            highlightButton.disabled = true;
            try {
                await createHighlight();
            } catch (error) {
                showToast(error.message || "Could not create highlight.");
            } finally {
                highlightButton.disabled = false;
            }
        });
    }

    document.addEventListener("keydown", (event) => {
        if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }

        const target = event.target;
        if (target instanceof HTMLElement
            && (target.matches("input, textarea, select")
                || target.isContentEditable)) {
            return;
        }

        if (event.key.toLowerCase() === "b" && bookmarkButton) {
            event.preventDefault();
            bookmarkButton.click();
        }

        if (event.key.toLowerCase() === "n" && drawer) {
            event.preventDefault();
            if (drawer.open) {
                closeDrawer();
            } else {
                openDrawer("chapters");
            }
        }
    });

    const translatedObserver = translated
        ? new MutationObserver(() => {
            ensureParagraphMetadata(translated);
            applyHighlights();
        })
        : null;

    if (translated && translatedObserver) {
        translatedObserver.observe(translated, {
            childList: true,
            subtree: false
        });
    }

    ensureParagraphMetadata(original);
    ensureParagraphMetadata(translated);
    applyHighlights();
})();
