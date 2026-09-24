(() => {
    const shell = document.querySelector("[data-novel-reader][data-reader-personalization]");
    if (!shell) return;

    const settingsElement = shell.querySelector("[data-reader-settings-json]");
    const settingsForm = shell.querySelector("[data-reader-settings-form]");
    const resetForm = shell.querySelector("[data-reader-reset-form]");
    const bookmarkAppearanceForm = shell.querySelector("[data-bookmark-appearance-endpoint]");
    const bookmarkForm = shell.querySelector("[data-bookmark-form]");
    const progressForm = shell.querySelector("[data-progress-form]");
    const content = shell.querySelector("[data-reader-content]");
    const pageControls = shell.querySelector("[data-reader-page-controls]");
    const pageNumber = shell.querySelector("[data-reader-page-number]");
    const pageBookmarks = shell.querySelector("[data-page-bookmarks]");
    const autoScrollButton = shell.querySelector("[data-reader-autoscroll-toggle]");
    const overrideState = shell.querySelector("[data-reader-override-state]");
    const settingsPanel = shell.querySelector("[data-reader-settings-panel]");
    const toast = shell.querySelector("[data-reader-toast]");
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    if (!settingsElement || !settingsForm || !content) return;

    let state;
    try {
        state = JSON.parse(settingsElement.textContent || "{}");
    } catch {
        return;
    }

    let pageCount = 1;
    let currentPage = 0;
    let resizeTimer = null;
    let autoScrollFrame = null;
    let autoScrollLastTime = null;
    let autoScrollRunning = false;
    let toastTimer = null;
    let saveTimer = null;

    const bookmarkState = new Map();

    const clamp = (value, min, max) =>
        Math.min(max, Math.max(min, value));

    const cssEscape = value =>
        window.CSS?.escape ? window.CSS.escape(value) : String(value).replace(/["\\]/g, "\\$&");

    const showToast = message => {
        if (!toast) return;
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.hidden = false;
        toastTimer = setTimeout(() => {
            toast.hidden = true;
        }, 1800);
    };

    const tokenFrom = form =>
        form?.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

    const setFormValue = (data, key, value) => {
        data.set(key, value == null ? "" : String(value));
    };

    const fillSettingsData = data => {
        setFormValue(data, "ReadingMode", state.readingMode);
        setFormValue(data, "PageTransition", state.pageTransition);
        setFormValue(data, "TwoPageSpread", state.twoPageSpread);
        setFormValue(data, "AutoScrollSpeed", state.autoScrollSpeed);
        setFormValue(data, "FontFamily", state.fontFamily);
        setFormValue(data, "FontSizeRem", state.fontSizeRem);
        setFormValue(data, "LineHeight", state.lineHeight);
        setFormValue(data, "ParagraphSpacingEm", state.paragraphSpacingEm);
        setFormValue(data, "TextWidthPx", state.textWidthPx);
        setFormValue(data, "TextAlignment", state.textAlignment);
        setFormValue(data, "ChapterStyle", state.chapterStyle);
        setFormValue(data, "PaperStyle", state.paperStyle);
        setFormValue(data, "GenreArtworkEnabled", state.genreArtworkEnabled);
        setFormValue(data, "GenreTheme", state.genreTheme);
        setFormValue(data, "BackgroundIntensity", state.backgroundIntensity);
        setFormValue(data, "BookmarkStyle", state.bookmarkStyle);
        setFormValue(data, "BookmarkColor", state.bookmarkColor);
    };

    const postSettings = async (scope, changedKey) => {
        const data = new FormData(settingsForm);
        fillSettingsData(data);
        setFormValue(data, "scope", scope);
        if (changedKey) setFormValue(data, "changedKey", changedKey);

        const response = await fetch(settingsForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });
        if (!response.ok) {
            throw new Error((await response.text()) || "Einstellungen konnten nicht gespeichert werden.");
        }

        const payload = await response.json();
        if (payload?.settings) {
            state = payload.settings;
            applySettings(false);
        }
    };

    const scheduleBookSave = changedKey => {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(async () => {
            try {
                await postSettings("book", changedKey);
                if (overrideState) overrideState.textContent = "Buch-Override";
            } catch (error) {
                showToast(error.message);
            }
        }, 260);
    };

    const fontStacks = {
        "system-serif": 'Georgia, "Times New Roman", "Noto Serif JP", serif',
        "system-sans": 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans JP", sans-serif',
        "literary-serif": '"Literata", Georgia, "Noto Serif JP", serif',
        "book-serif": '"Lora", Georgia, "Noto Serif JP", serif',
        "atkinson": '"Atkinson Hyperlegible", system-ui, "Noto Sans JP", sans-serif',
        "noto-serif-jp": '"Noto Serif JP", "Yu Mincho", serif',
        "noto-sans-jp": '"Noto Sans JP", system-ui, sans-serif'
    };

    const loadGoogleFont = family => {
        const normalized = (family || "").trim();
        if (!normalized || normalized.length > 80) return;
        const id = "reader-google-font";
        let link = document.getElementById(id);
        if (!link) {
            link = document.createElement("link");
            link.id = id;
            link.rel = "stylesheet";
            document.head.append(link);
        }
        const query = normalized.replace(/ /g, "+");
        link.href =
            "https://fonts.googleapis.com/css2?family=" +
            encodeURIComponent(query).replace(/%2B/g, "+") +
            ":wght@400;500;600;700&display=swap";
    };

    const fontCss = font => {
        if (font?.startsWith("google:")) {
            const family = font.slice("google:".length).trim();
            loadGoogleFont(family);
            return `"${family.replace(/"/g, "")}", "Noto Serif JP", serif`;
        }
        return fontStacks[font] || fontStacks["literary-serif"];
    };

    const ensureFontOption = font => {
        const select = settingsForm.querySelector('[data-setting-key="fontFamily"]');
        if (!select || !font) return;
        if (!Array.from(select.options).some(option => option.value === font)) {
            const option = document.createElement("option");
            option.value = font;
            option.textContent = font.startsWith("google:")
                ? font.slice("google:".length)
                : font;
            select.append(option);
        }
    };

    const formatOutput = (key, value) => {
        if (key === "autoScrollSpeed") return `${Math.round(value)} px/s`;
        if (key === "fontSizeRem") return `${Number(value).toFixed(2)} rem`;
        if (key === "lineHeight") return Number(value).toFixed(2);
        if (key === "paragraphSpacingEm") return `${Number(value).toFixed(1)} em`;
        if (key === "textWidthPx") return `${Math.round(value)} px`;
        if (key === "backgroundIntensity") return `${Math.round(Number(value) * 100)}%`;
        return String(value ?? "");
    };

    const syncControls = () => {
        ensureFontOption(state.fontFamily);
        settingsForm.querySelectorAll("[data-reader-setting]").forEach(control => {
            const key = control.dataset.settingKey;
            if (!(key in state)) return;
            if (control.type === "checkbox") {
                control.checked = Boolean(state[key]);
            } else {
                control.value = String(state[key] ?? "");
            }
        });

        settingsForm.querySelectorAll("[data-setting-output]").forEach(output => {
            const key = output.dataset.settingOutput;
            output.textContent = formatOutput(key, state[key]);
        });

        if (overrideState) {
            overrideState.textContent = state.hasBookOverride
                ? "Buch-Override"
                : "User-Standard";
        }
    };

    const captureLogicalAnchor = () => {
        const language =
            shell.dataset.view === "de" && shell.dataset.hasTranslation === "true"
                ? "de"
                : "ja";
        const paragraphs = Array.from(shell.querySelectorAll(
            `[data-reader-paragraph][data-language="${language}"]`));

        if (paragraphs.length === 0) return null;

        if (state.readingMode === "paged") {
            const contentRect = content.getBoundingClientRect();
            return paragraphs.find(paragraph => {
                const rect = paragraph.getBoundingClientRect();
                return rect.right > contentRect.left + 20 &&
                    rect.left < contentRect.right - 20;
            }) || paragraphs[0];
        }

        const target = window.innerHeight * .28;
        let selected = paragraphs[0];
        for (const paragraph of paragraphs) {
            const rect = paragraph.getBoundingClientRect();
            if (rect.top <= target) selected = paragraph;
            if (rect.top <= target && rect.bottom >= target) break;
            if (rect.top > target) break;
        }
        return selected;
    };

    const updateReadingProgress = () => {
        if (state.readingMode !== "paged") return;
        const progress =
            pageCount <= 1 ? 1000 : Math.round(currentPage / (pageCount - 1) * 1000);
        document.querySelectorAll("[data-reading-progress]").forEach(bar => {
            bar.style.width = (progress / 10) + "%";
        });
        shell.querySelectorAll("[data-reader-rail-fill]").forEach(bar => {
            bar.style.height = (progress / 10) + "%";
        });
        shell.querySelectorAll("[data-reader-percent]").forEach(output => {
            output.textContent = Math.round(progress / 10) + "%";
        });
    };

    const sendPagedProgress = () => {
        if (!progressForm || state.readingMode !== "paged") return;
        const paragraph = captureLogicalAnchor();
        const language =
            shell.dataset.view === "de" && shell.dataset.hasTranslation === "true"
                ? "de"
                : "ja";
        const progress =
            pageCount <= 1 ? 1000 : Math.round(currentPage / (pageCount - 1) * 1000);
        const data = new FormData(progressForm);
        setFormValue(data, "positionPermille", progress);
        setFormValue(data, "anchorLanguage", language);
        setFormValue(data, "anchorParagraphIndex", paragraph?.dataset.index ?? "");
        setFormValue(data, "anchorOffset", 0);

        fetch(progressForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" },
            keepalive: true
        }).catch(() => {});
    };

    const syncPageState = () => {
        if (state.readingMode !== "paged") return;
        const width = Math.max(1, content.clientWidth);
        pageCount = Math.max(1, Math.ceil(content.scrollWidth / width));
        currentPage = clamp(Math.round(content.scrollLeft / width), 0, pageCount - 1);
        if (pageNumber) pageNumber.textContent = `${currentPage + 1} / ${pageCount}`;
        shell.querySelector("[data-reader-page-prev]")?.toggleAttribute("disabled", currentPage <= 0);
        shell.querySelector("[data-reader-page-next]")?.toggleAttribute("disabled", currentPage >= pageCount - 1);
        updateReadingProgress();
        renderPageBookmarks();
    };

    const animatePage = direction => {
        if (reduceMotion.matches || state.pageTransition === "none") return;
        const animation = `reader-turn-${state.pageTransition}-${direction}`;
        content.classList.remove(
            "reader-turn-curl-next", "reader-turn-curl-prev",
            "reader-turn-slide-next", "reader-turn-slide-prev",
            "reader-turn-fade-next", "reader-turn-fade-prev");
        void content.offsetWidth;
        content.classList.add(animation);
        setTimeout(() => content.classList.remove(animation), 430);
    };

    const goToPage = (page, animate = true) => {
        if (state.readingMode !== "paged") return;
        const next = clamp(page, 0, pageCount - 1);
        const direction = next >= currentPage ? "next" : "prev";
        if (animate && next !== currentPage) animatePage(direction);
        content.scrollTo({
            left: next * content.clientWidth,
            behavior: reduceMotion.matches ? "auto" : "smooth"
        });
        currentPage = next;
        syncPageState();
        window.setTimeout(() => {
            syncPageState();
            sendPagedProgress();
        }, reduceMotion.matches ? 0 : 360);
    };

    const setupPaged = anchor => {
        stopAutoScroll();
        shell.dataset.readingMode = "paged";
        pageControls.hidden = false;
        document.body.classList.add("novel-paged-body");

        requestAnimationFrame(() => {
            syncPageState();
            if (anchor) {
                const contentRect = content.getBoundingClientRect();
                const anchorRect = anchor.getBoundingClientRect();
                const horizontalOffset =
                    content.scrollLeft + anchorRect.left - contentRect.left;
                goToPage(Math.floor(horizontalOffset / Math.max(1, content.clientWidth)), false);
            }
            syncPageState();
        });
    };

    const teardownPaged = anchor => {
        const index = anchor?.dataset.index;
        const language = anchor?.dataset.language;
        content.scrollLeft = 0;
        shell.dataset.readingMode = "continuous";
        pageControls.hidden = true;
        document.body.classList.remove("novel-paged-body");

        requestAnimationFrame(() => {
            if (index == null || !language) return;
            const target = shell.querySelector(
                `[data-reader-paragraph][data-language="${cssEscape(language)}"][data-index="${cssEscape(index)}"]`);
            if (!target) return;
            const top = window.scrollY + target.getBoundingClientRect().top - window.innerHeight * .28;
            window.scrollTo({ top: Math.max(0, top), behavior: "auto" });
        });
    };

    const applySettings = (preserveAnchor = true) => {
        const anchor = preserveAnchor ? captureLogicalAnchor() : null;
        const previousMode = shell.dataset.readingMode || state.readingMode;

        shell.dataset.readingMode = state.readingMode;
        shell.dataset.pageTransition = state.pageTransition;
        shell.dataset.twoPage = String(Boolean(state.twoPageSpread));
        shell.dataset.chapterStyle = state.chapterStyle;
        shell.dataset.paperStyle = state.paperStyle;
        shell.dataset.genreArtwork = String(Boolean(state.genreArtworkEnabled));
        shell.dataset.genreTheme = state.resolvedGenreTheme || state.genreTheme || "neutral";
        shell.dataset.textAlignment = state.textAlignment;

        document.documentElement.style.setProperty("--novel-reader-size", `${state.fontSizeRem}rem`);
        document.documentElement.style.setProperty("--novel-reader-leading", state.lineHeight);
        document.documentElement.style.setProperty("--novel-reader-width", `${state.textWidthPx}px`);
        document.documentElement.style.setProperty("--reader-paragraph-spacing", `${state.paragraphSpacingEm}em`);
        document.documentElement.style.setProperty("--reader-background-intensity", state.backgroundIntensity);
        document.documentElement.style.setProperty("--reader-font-family", fontCss(state.fontFamily));

        const bookmarkStyle = bookmarkForm?.querySelector('[name="style"]');
        const bookmarkColor = bookmarkForm?.querySelector('[name="color"]');
        if (bookmarkStyle) bookmarkStyle.value = state.bookmarkStyle;
        if (bookmarkColor) bookmarkColor.value = state.bookmarkColor;

        if (state.readingMode === "paged") {
            setupPaged(previousMode === "paged" ? null : anchor);
        } else if (previousMode === "paged") {
            teardownPaged(anchor);
        } else {
            pageControls.hidden = true;
            document.body.classList.remove("novel-paged-body");
        }

        syncControls();
        decorateBookmarks();
    };

    const stopAutoScroll = () => {
        autoScrollRunning = false;
        autoScrollLastTime = null;
        if (autoScrollFrame != null) {
            cancelAnimationFrame(autoScrollFrame);
            autoScrollFrame = null;
        }
        if (autoScrollButton) {
            autoScrollButton.setAttribute("aria-pressed", "false");
            autoScrollButton.setAttribute("aria-label", "Auto-Scroll starten");
        }
    };

    const autoScrollTick = time => {
        if (!autoScrollRunning || state.readingMode !== "continuous") {
            stopAutoScroll();
            return;
        }
        if (autoScrollLastTime == null) autoScrollLastTime = time;
        const elapsed = Math.min(80, time - autoScrollLastTime);
        autoScrollLastTime = time;
        const amount = Number(state.autoScrollSpeed || 36) * elapsed / 1000;
        window.scrollBy(0, amount);

        const max = document.documentElement.scrollHeight - window.innerHeight;
        if (window.scrollY >= max - 2) {
            stopAutoScroll();
            return;
        }
        autoScrollFrame = requestAnimationFrame(autoScrollTick);
    };

    const toggleAutoScroll = () => {
        if (autoScrollRunning) {
            stopAutoScroll();
            return;
        }
        if (state.readingMode !== "continuous") {
            showToast("Auto-Scroll ist im Scroll-Modus verfügbar.");
            return;
        }
        autoScrollRunning = true;
        if (autoScrollButton) {
            autoScrollButton.setAttribute("aria-pressed", "true");
            autoScrollButton.setAttribute("aria-label", "Auto-Scroll pausieren");
        }
        autoScrollFrame = requestAnimationFrame(autoScrollTick);
    };

    const collectBookmarks = () => {
        shell.querySelectorAll("[data-saved-bookmark]").forEach(element => {
            const id = element.dataset.bookmarkId;
            if (!id) return;
            bookmarkState.set(id, {
                id,
                positionPermille: Number(element.dataset.position || 0),
                style: element.dataset.bookmarkStyle || state.bookmarkStyle,
                color: element.dataset.bookmarkColor || state.bookmarkColor
            });
        });

        shell.querySelectorAll("[data-bookmark-card]").forEach(card => {
            const id = card.dataset.bookmarkId;
            if (!id) return;
            const existing = bookmarkState.get(id) || {
                id,
                positionPermille: 0
            };
            existing.style = card.dataset.bookmarkStyle || existing.style || state.bookmarkStyle;
            existing.color = card.dataset.bookmarkColor || existing.color || state.bookmarkColor;
            bookmarkState.set(id, existing);
        });
    };

    const decorateBookmarks = () => {
        collectBookmarks();

        shell.querySelectorAll("[data-bookmark-card]").forEach(card => {
            const item = bookmarkState.get(card.dataset.bookmarkId);
            if (!item) return;
            card.dataset.bookmarkStyle = item.style;
            card.dataset.bookmarkColor = item.color;
            card.style.setProperty("--bookmark-color", item.color);

            const styleControl = card.querySelector("[data-bookmark-style-control]");
            const colorControl = card.querySelector("[data-bookmark-color-control]");
            if (styleControl) styleControl.value = item.style;
            if (colorControl) colorControl.value = item.color;
        });

        shell.querySelectorAll("[data-bookmark-marker]").forEach(marker => {
            const item = bookmarkState.get(marker.dataset.bookmarkId);
            if (!item) return;
            marker.dataset.bookmarkStyle = item.style;
            marker.style.setProperty("--bookmark-color", item.color);
        });

        renderPageBookmarks();
    };

    const renderPageBookmarks = () => {
        if (!pageBookmarks) return;
        pageBookmarks.replaceChildren();
        if (state.readingMode !== "paged") return;

        for (const bookmark of bookmarkState.values()) {
            const targetPage =
                pageCount <= 1
                    ? 0
                    : clamp(Math.round(bookmark.positionPermille / 1000 * (pageCount - 1)), 0, pageCount - 1);
            if (targetPage !== currentPage) continue;

            const button = document.createElement("button");
            button.type = "button";
            button.className = "novel-page-bookmark";
            button.dataset.bookmarkStyle = bookmark.style || state.bookmarkStyle;
            button.style.setProperty("--bookmark-color", bookmark.color || state.bookmarkColor);
            button.dataset.pageBookmark = bookmark.id;
            button.title = "Lesezeichen";
            button.setAttribute("aria-label", "Lesezeichen auf dieser Seite");
            pageBookmarks.append(button);
        }
    };

    const saveBookmarkAppearance = async (id, style, color) => {
        if (!bookmarkAppearanceForm) return;
        const data = new FormData(bookmarkAppearanceForm);
        setFormValue(data, "bookmarkId", id);
        setFormValue(data, "style", style);
        setFormValue(data, "color", color);

        const response = await fetch(bookmarkAppearanceForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });
        if (!response.ok) throw new Error("Lesezeichen konnte nicht geändert werden.");
        const payload = await response.json();
        const bookmark = bookmarkState.get(id);
        if (bookmark) {
            bookmark.style = payload.style || style;
            bookmark.color = payload.color || color;
        }
        decorateBookmarks();
    };

    settingsForm.addEventListener("input", event => {
        const control = event.target.closest("[data-reader-setting]");
        if (!control) return;
        const key = control.dataset.settingKey;
        state[key] = control.type === "checkbox"
            ? control.checked
            : control.type === "range"
                ? Number(control.value)
                : control.value;

        if (key === "genreTheme") {
            state.resolvedGenreTheme =
                state.genreTheme === "auto"
                    ? state.resolvedGenreTheme
                    : state.genreTheme;
        }
        applySettings();
    });

    settingsForm.addEventListener("change", event => {
        const control = event.target.closest("[data-reader-setting]");
        if (!control) return;
        scheduleBookSave(control.dataset.settingKey);
    });

    settingsForm.addEventListener("submit", event => event.preventDefault());

    shell.querySelector("[data-reader-save-defaults]")?.addEventListener("click", async () => {
        try {
            await postSettings("default", null);
            showToast("User-Standard gespeichert.");
        } catch (error) {
            showToast(error.message);
        }
    });

    shell.querySelector("[data-reader-reset-book]")?.addEventListener("click", async () => {
        if (!resetForm) return;
        try {
            const response = await fetch(resetForm.action, {
                method: "POST",
                body: new FormData(resetForm),
                credentials: "same-origin",
                headers: { "X-Requested-With": "fetch" }
            });
            if (!response.ok) throw new Error("Buch-Einstellungen konnten nicht zurückgesetzt werden.");
            const payload = await response.json();
            if (payload?.settings) {
                state = payload.settings;
                applySettings();
            }
            showToast("Buch verwendet wieder den User-Standard.");
        } catch (error) {
            showToast(error.message);
        }
    });

    shell.querySelector("[data-reader-apply-google-font]")?.addEventListener("click", () => {
        const input = shell.querySelector("[data-reader-google-font]");
        const family = input?.value?.trim();
        if (!family) return;
        state.fontFamily = `google:${family}`;
        ensureFontOption(state.fontFamily);
        applySettings();
        scheduleBookSave("fontFamily");
    });

    autoScrollButton?.addEventListener("click", toggleAutoScroll);

    content.addEventListener("pointerdown", event => {
        if (autoScrollRunning && !event.target.closest("a, button, input, select, textarea")) {
            stopAutoScroll();
        }
    }, { passive: true });

    settingsPanel?.addEventListener("toggle", () => {
        if (settingsPanel.open) stopAutoScroll();
    });

    shell.querySelector("[data-reader-page-prev]")?.addEventListener("click", () =>
        goToPage(currentPage - 1));
    shell.querySelector("[data-reader-page-next]")?.addEventListener("click", () =>
        goToPage(currentPage + 1));

    content.addEventListener("scroll", () => {
        if (state.readingMode !== "paged") return;
        window.clearTimeout(content.__readerPageTimer);
        content.__readerPageTimer = window.setTimeout(() => {
            syncPageState();
            sendPagedProgress();
        }, 180);
    }, { passive: true });

    document.addEventListener("keydown", event => {
        if (state.readingMode !== "paged") return;
        if (event.target.matches("input, select, textarea, button")) return;
        if (event.key === "ArrowRight" || event.key === "PageDown") {
            event.preventDefault();
            goToPage(currentPage + 1);
        } else if (event.key === "ArrowLeft" || event.key === "PageUp") {
            event.preventDefault();
            goToPage(currentPage - 1);
        }
    });

    document.addEventListener("change", event => {
        const styleControl = event.target.closest("[data-bookmark-style-control]");
        const colorControl = event.target.closest("[data-bookmark-color-control]");
        const control = styleControl || colorControl;
        if (!control) return;
        const card = control.closest("[data-bookmark-card]");
        const id = card?.dataset.bookmarkId;
        if (!id) return;

        const item = bookmarkState.get(id) || {
            id,
            positionPermille: 0,
            style: state.bookmarkStyle,
            color: state.bookmarkColor
        };
        item.style = card.querySelector("[data-bookmark-style-control]")?.value || item.style;
        item.color = card.querySelector("[data-bookmark-color-control]")?.value || item.color;
        bookmarkState.set(id, item);
        decorateBookmarks();
        saveBookmarkAppearance(id, item.style, item.color).catch(error => showToast(error.message));
    });

    pageBookmarks?.addEventListener("click", event => {
        const button = event.target.closest("[data-page-bookmark]");
        if (!button) return;
        const item = bookmarkState.get(button.dataset.pageBookmark);
        if (!item) return;
        const target =
            pageCount <= 1 ? 0 : Math.round(item.positionPermille / 1000 * (pageCount - 1));
        goToPage(target);
    });

    window.addEventListener("resize", () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (state.readingMode === "paged") {
                const anchor = captureLogicalAnchor();
                requestAnimationFrame(() => {
                    syncPageState();
                    if (anchor) {
                        const contentRect = content.getBoundingClientRect();
                        const rect = anchor.getBoundingClientRect();
                        const horizontalOffset = content.scrollLeft + rect.left - contentRect.left;
                        goToPage(Math.floor(horizontalOffset / Math.max(1, content.clientWidth)), false);
                    }
                });
            }
        }, 160);
    });

    const bookmarkObserver = new MutationObserver(() => decorateBookmarks());
    const bookmarkList = shell.querySelector("[data-bookmark-list]");
    if (bookmarkList) bookmarkObserver.observe(bookmarkList, { childList: true, subtree: true });
    shell.querySelectorAll("[data-bookmark-track]").forEach(track =>
        bookmarkObserver.observe(track, { childList: true, subtree: true }));

    collectBookmarks();
    syncControls();
    applySettings(false);
})();