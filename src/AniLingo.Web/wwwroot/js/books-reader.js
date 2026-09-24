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
                ? "Book override"
                : "My defaults";
        }
    }

    function setSettingsFormState(data) {
        const names = {
            readingMode: "Settings.ReadingMode",
            pageTransition: "Settings.PageTransition",
            twoPageSpread: "Settings.TwoPageSpread",
            autoScrollSpeed: "Settings.AutoScrollSpeed",
            fontFamily: "Settings.FontFamily",
            fontSizeRem: "Settings.FontSizeRem",
            lineHeight: "Settings.LineHeight",
            paragraphSpacingEm: "Settings.ParagraphSpacingEm",
            textWidthPx: "Settings.TextWidthPx",
            textAlignment: "Settings.TextAlignment",
            chapterStyle: "Settings.ChapterStyle",
            paperStyle: "Settings.PaperStyle",
            genreArtworkEnabled: "Settings.GenreArtworkEnabled",
            genreTheme: "Settings.GenreTheme",
            backgroundAssetId: "Settings.BackgroundAssetId",
            backgroundIntensity: "Settings.BackgroundIntensity",
            bookmarkStyle: "Settings.BookmarkStyle",
            bookmarkColor: "Settings.BookmarkColor"
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

    function scheduleSettingSave(changedKey) {
        settings.hasBookOverride = true;
        syncSettingControls();

        settingsSave = settingsSave
            .then(() => saveSettings("book", changedKey))
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

                if (["autoScrollSpeed", "fontSizeRem", "lineHeight", "paragraphSpacingEm", "textWidthPx"].includes(key)) {
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
        requestAnimationFrame(() => {
            const max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
            window.scrollTo({ top: max * initial / 1000, behavior: "auto" });
        });
    }

    if (root.dataset.hasTranslation !== "true" && translateForm) {
        pollTimer = window.setTimeout(pollTranslation, 2500);
    }
})();
