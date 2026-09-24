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
    const backgroundSelect = shell.querySelector("[data-reader-background-select]");
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
    let saveQueue = Promise.resolve();
    let backgroundCatalog = [];
    let backgroundSuggestedId = null;
    let backgroundCatalogGenre = null;

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

    const fillSettingsData = (data, source = state) => {
        setFormValue(data, "ReadingMode", source.readingMode);
        setFormValue(data, "PageTransition", source.pageTransition);
        setFormValue(data, "TwoPageSpread", source.twoPageSpread);
        setFormValue(data, "AutoScrollSpeed", source.autoScrollSpeed);
        setFormValue(data, "FontFamily", source.fontFamily);
        setFormValue(data, "FontSizeRem", source.fontSizeRem);
        setFormValue(data, "LineHeight", source.lineHeight);
        setFormValue(data, "ParagraphSpacingEm", source.paragraphSpacingEm);
        setFormValue(data, "TextWidthPx", source.textWidthPx);
        setFormValue(data, "TextAlignment", source.textAlignment);
        setFormValue(data, "ChapterStyle", source.chapterStyle);
        setFormValue(data, "PaperStyle", source.paperStyle);
        setFormValue(data, "GenreArtworkEnabled", source.genreArtworkEnabled);
        setFormValue(data, "GenreTheme", source.genreTheme);
        setFormValue(data, "BackgroundAssetId", source.backgroundAssetId || "auto");
        setFormValue(data, "BackgroundIntensity", source.backgroundIntensity);
        setFormValue(data, "BookmarkStyle", source.bookmarkStyle);
        setFormValue(data, "BookmarkColor", source.bookmarkColor);
    };

    const postSettings = async (
        scope,
        changedKey,
        source = state,
        applyResponse = true) => {
        const data = new FormData(settingsForm);
        fillSettingsData(data, source);
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
        if (payload?.settings && applyResponse) {
            state = payload.settings;
            applySettings();
        }
        return payload?.settings || null;
    };

    const scheduleBookSave = changedKey => {
        const snapshot = { ...state };
        saveQueue = saveQueue
            .then(async () => {
                const saved = await postSettings(
                    "book",
                    changedKey,
                    snapshot,
                    false);

                state.hasBookOverride = true;
                if (saved && state[changedKey] === snapshot[changedKey]) {
                    state[changedKey] = saved[changedKey];
                    if (changedKey === "genreTheme") {
                        state.resolvedGenreTheme = saved.resolvedGenreTheme;
                    }
                    applySettings();
                    if (changedKey === "genreTheme") {
                        loadBackgroundCatalog(true);
                    }
                }
                if (overrideState) overrideState.textContent = "Buch-Override";
            })
            .catch(error => {
                showToast(error.message);
            });
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

    const bundledGoogleFonts = {
        "literary-serif": "Literata",
        "book-serif": "Lora",
        "atkinson": "Atkinson Hyperlegible",
        "noto-serif-jp": "Noto Serif JP",
        "noto-sans-jp": "Noto Sans JP"
    };

    const fontCss = font => {
        if (font?.startsWith("google:")) {
            const family = font.slice("google:".length).trim();
            loadGoogleFont(family);
            return `"${family.replace(/"/g, "")}", "Noto Serif JP", serif`;
        }
        const remoteFamily = bundledGoogleFonts[font];
        if (remoteFamily) loadGoogleFont(remoteFamily);
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

    const initialAnchorElement = () => {
        const language = shell.dataset.anchorLanguage || "ja";
        const index = shell.dataset.anchorParagraph;
        if (index == null || index === "") return null;
        return shell.querySelector(
            `[data-reader-paragraph][data-language="${cssEscape(language)}"][data-index="${cssEscape(index)}"]`);
    };

    const captureLogicalAnchor = () => {
        const language =
            shell.dataset.view === "de" && shell.dataset.hasTranslation === "true"
                ? "de"
                : "ja";
        const paragraphs = Array.from(shell.querySelectorAll(
            `[data-reader-paragraph][data-language="${language}"]`));

        if (paragraphs.length === 0) return null;

        if ((shell.dataset.readingMode || state.readingMode) === "paged") {
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

    const syncBackgroundOptions = () => {
        if (!backgroundSelect) return;

        const selected = state.backgroundAssetId || "auto";
        backgroundSelect.replaceChildren();

        const auto = document.createElement("option");
        auto.value = "auto";
        auto.textContent = "Automatisch";
        backgroundSelect.append(auto);

        const groups = new Map();
        for (const item of backgroundCatalog) {
            const genre = item.genreLabel || item.genre || "Weitere";
            let group = groups.get(genre);
            if (!group) {
                group = document.createElement("optgroup");
                group.label = genre;
                groups.set(genre, group);
                backgroundSelect.append(group);
            }

            const option = document.createElement("option");
            option.value = item.id;
            option.textContent = item.name || item.variant || item.id;
            group.append(option);
        }

        if (selected !== "auto" &&
            !backgroundCatalog.some(item => item.id === selected)) {
            const unavailable = document.createElement("option");
            unavailable.value = selected;
            unavailable.textContent = selected + " (nicht verfügbar)";
            backgroundSelect.append(unavailable);
        }

        backgroundSelect.value = selected;
    };

    const applyBackgroundImage = () => {
        if (!state.genreArtworkEnabled) {
            shell.style.removeProperty("--reader-genre-background-image");
            return;
        }

        const requested = state.backgroundAssetId || "auto";
        const id = requested === "auto" ? backgroundSuggestedId : requested;
        const item = backgroundCatalog.find(candidate => candidate.id === id);
        if (!item?.url || !item.url.startsWith("/reader-backgrounds/")) {
            shell.style.removeProperty("--reader-genre-background-image");
            return;
        }

        const safeUrl = item.url.replace(/["\\()]/g, character =>
            encodeURIComponent(character));
        shell.style.setProperty(
            "--reader-genre-background-image",
            `url("${safeUrl}")`);
    };

    const loadBackgroundCatalog = async (force = false) => {
        const genre =
            state.resolvedGenreTheme ||
            (state.genreTheme === "auto" ? "neutral" : state.genreTheme) ||
            "neutral";

        if (!force && backgroundCatalogGenre === genre && backgroundCatalog.length > 0) {
            syncBackgroundOptions();
            applyBackgroundImage();
            return;
        }

        try {
            const response = await fetch(
                "/api/reader-backgrounds?genre=" + encodeURIComponent(genre),
                {
                    credentials: "same-origin",
                    headers: { "X-Requested-With": "fetch" }
                });
            if (!response.ok) return;

            const payload = await response.json();
            backgroundCatalog = Array.isArray(payload?.items) ? payload.items : [];
            backgroundSuggestedId = payload?.suggestedId || null;
            backgroundCatalogGenre = genre;
            syncBackgroundOptions();
            applyBackgroundImage();
        } catch {
            // The CSS mood fallback remains active if the optional catalog is unavailable.
        }
    };

    const applySettings = (preserveAnchor = true) => {
        const anchor = preserveAnchor ? captureLogicalAnchor() : initialAnchorElement();
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
        applyBackgroundImage();

        const bookmarkStyle = bookmarkForm?.querySelector('[name="style"]');
        const bookmarkColor = bookmarkForm?.querySelector('[name="color"]');
        if (bookmarkStyle) bookmarkStyle.value = state.bookmarkStyle;
        if (bookmarkColor) bookmarkColor.value = state.bookmarkColor;

        if (state.readingMode === "paged") {
            setupPaged(anchor);
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
        shell.querySelectorAll("[data-bookmark-marker]").forEach(marker => {
            const id = marker.dataset.bookmarkId;
            if (!id) return;
            const rawPosition = marker.style.top || marker.style.left || "";
            const percent = Number.parseFloat(rawPosition);
            const existing = bookmarkState.get(id) || {
                id,
                style: state.bookmarkStyle,
                color: state.bookmarkColor
            };
            if (Number.isFinite(percent) && !(existing.positionPermille > 0)) {
                existing.positionPermille = Math.round(percent * 10);
            }
            bookmarkState.set(id, existing);
        });

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
            if (!id || card.dataset.bookmarkChapterId !== shell.dataset.chapterId) return;
            const existing = bookmarkState.get(id);
            if (!existing) return;
            existing.style = card.dataset.bookmarkStyle || existing.style || state.bookmarkStyle;
            existing.color = card.dataset.bookmarkColor || existing.color || state.bookmarkColor;
        });
    };

    const decorateBookmarks = () => {
        collectBookmarks();

        shell.querySelectorAll("[data-bookmark-card]").forEach(card => {
            const item = bookmarkState.get(card.dataset.bookmarkId);
            const style = item?.style || card.dataset.bookmarkStyle || state.bookmarkStyle;
            const color = item?.color || card.dataset.bookmarkColor || state.bookmarkColor;
            card.dataset.bookmarkStyle = style;
            card.dataset.bookmarkColor = color;
            card.style.setProperty("--bookmark-color", color);

            let appearance = card.querySelector(".novel-bookmark-appearance");
            if (!appearance) {
                appearance = document.createElement("div");
                appearance.className = "novel-bookmark-appearance";
                appearance.innerHTML =
                    '<select data-bookmark-style-control aria-label="Lesezeichen-Stil">' +
                    '<option value="fabric">Stoff</option>' +
                    '<option value="paper">Papier</option>' +
                    '<option value="leather">Leder</option>' +
                    '<option value="cord">Schnur</option>' +
                    '<option value="minimal">Minimal</option>' +
                    '</select>' +
                    '<input type="color" data-bookmark-color-control aria-label="Lesezeichen-Farbe" />';
                const remove = card.querySelector("[data-remove-bookmark-form], [data-remove-bookmark-button]");
                if (remove) card.insertBefore(appearance, remove);
                else card.append(appearance);
            }

            const styleControl = card.querySelector("[data-bookmark-style-control]");
            const colorControl = card.querySelector("[data-bookmark-color-control]");
            if (styleControl) styleControl.value = style;
            if (colorControl) colorControl.value = color;
        });

        const bookmarkCount =
            shell.querySelectorAll("[data-bookmark-card]").length;
        const currentCount = bookmarkState.size;
        const noteCount =
            bookmarkCount + shell.querySelectorAll("[data-highlight-card]").length;
        const currentBadge = shell.querySelector("[data-current-bookmark-count]");
        const notesBadge = shell.querySelector("[data-reader-note-count]");
        const bookmarkButton = shell.querySelector("[data-reader-bookmark]");
        if (currentBadge) {
            currentBadge.textContent = String(currentCount);
            currentBadge.hidden = currentCount <= 0;
        }
        if (notesBadge) {
            notesBadge.textContent = String(noteCount);
            notesBadge.hidden = noteCount <= 0;
        }
        bookmarkButton?.classList.toggle("has-bookmarks", currentCount > 0);

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

    const addPagedBookmarkUi = bookmark => {
        if (!bookmark?.id) return;
        const item = {
            id: bookmark.id,
            positionPermille: Number(bookmark.positionPermille || 0),
            style: bookmark.style || state.bookmarkStyle,
            color: bookmark.color || state.bookmarkColor
        };
        bookmarkState.set(item.id, item);

        const hidden = document.createElement("span");
        hidden.hidden = true;
        hidden.dataset.savedBookmark = "";
        hidden.dataset.bookmarkId = item.id;
        hidden.dataset.chapterId = bookmark.chapterId || shell.dataset.chapterId;
        hidden.dataset.position = String(item.positionPermille);
        hidden.dataset.language = bookmark.language || "ja";
        hidden.dataset.paragraph =
            bookmark.paragraphIndex == null ? "" : String(bookmark.paragraphIndex);
        hidden.dataset.offset = String(bookmark.characterOffset || 0);
        hidden.dataset.anchorText = bookmark.anchorText || "";
        hidden.dataset.bookmarkStyle = item.style;
        hidden.dataset.bookmarkColor = item.color;
        shell.append(hidden);

        const list = shell.querySelector("[data-bookmark-list]");
        if (list) {
            const card = document.createElement("article");
            card.className = "novel-note-card";
            card.dataset.bookmarkCard = "";
            card.dataset.bookmarkId = item.id;
            card.dataset.bookmarkChapterId = bookmark.chapterId || shell.dataset.chapterId;
            card.dataset.bookmarkStyle = item.style;
            card.dataset.bookmarkColor = item.color;

            const link = document.createElement("a");
            link.href =
                `/Novels/Read/${encodeURIComponent(bookmark.chapterId || shell.dataset.chapterId)}?bookmark=${encodeURIComponent(item.id)}`;
            link.dataset.localBookmarkId = item.id;

            const title = document.createElement("strong");
            title.textContent =
                bookmark.label ||
                `Lesezeichen · ${Math.round(item.positionPermille / 10)}%`;
            link.append(title);
            if (bookmark.anchorText) {
                const excerpt = document.createElement("span");
                excerpt.textContent = bookmark.anchorText;
                link.append(excerpt);
            }

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "novel-note-remove";
            remove.dataset.removeBookmarkButton = "";
            remove.dataset.bookmarkId = item.id;
            remove.textContent = "Entfernen";
            card.append(link, remove);
            list.prepend(card);
        }

        shell.querySelectorAll("[data-bookmark-track]").forEach(track => {
            const marker = document.createElement("button");
            marker.type = "button";
            marker.className = "novel-bookmark-marker";
            marker.dataset.bookmarkMarker = "";
            marker.dataset.bookmarkId = item.id;
            marker.dataset.bookmarkStyle = item.style;
            marker.style.setProperty("--bookmark-color", item.color);
            const percent = clamp(item.positionPermille / 10, 1.5, 98.5);
            if (track.dataset.orientation === "horizontal") marker.style.left = percent + "%";
            else marker.style.top = percent + "%";
            marker.innerHTML = "<span></span>";
            marker.title = bookmark.anchorText || "Lesezeichen";
            track.append(marker);
        });

        shell.querySelector("[data-empty-bookmarks]")?.classList.add("is-hidden");
        decorateBookmarks();
    };

    const savePagedBookmark = async () => {
        if (!bookmarkForm) return;
        const paragraph = captureLogicalAnchor();
        const language =
            shell.dataset.view === "de" && shell.dataset.hasTranslation === "true"
                ? "de"
                : "ja";
        const position =
            pageCount <= 1 ? 1000 : Math.round(currentPage / (pageCount - 1) * 1000);
        const data = new FormData(bookmarkForm);
        setFormValue(data, "positionPermille", position);
        setFormValue(data, "language", language);
        setFormValue(data, "paragraphIndex", paragraph?.dataset.index ?? "");
        setFormValue(data, "characterOffset", 0);
        setFormValue(data, "label", "");
        setFormValue(data, "style", state.bookmarkStyle);
        setFormValue(data, "color", state.bookmarkColor);

        const response = await fetch(bookmarkForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });
        if (!response.ok) {
            throw new Error((await response.text()) || "Lesezeichen konnte nicht gespeichert werden.");
        }

        addPagedBookmarkUi(await response.json());
        showToast("Lesezeichen gespeichert.");
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
        if (key === "genreTheme") {
            loadBackgroundCatalog(true);
        }
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

    document.addEventListener("click", event => {
        if (state.readingMode !== "paged") return;

        const bookmarkButton = event.target.closest("[data-reader-bookmark]");
        if (bookmarkButton) {
            event.preventDefault();
            event.stopImmediatePropagation();
            savePagedBookmark().catch(error => showToast(error.message));
            return;
        }

        const localBookmark = event.target.closest("[data-local-bookmark-id]");
        if (localBookmark) {
            const item = bookmarkState.get(localBookmark.dataset.localBookmarkId);
            if (!item) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            const target =
                pageCount <= 1
                    ? 0
                    : Math.round(item.positionPermille / 1000 * (pageCount - 1));
            goToPage(target);
        }
    }, true);

    document.addEventListener("change", event => {
        const styleControl = event.target.closest("[data-bookmark-style-control]");
        const colorControl = event.target.closest("[data-bookmark-color-control]");
        const control = styleControl || colorControl;
        if (!control) return;
        const card = control.closest("[data-bookmark-card]");
        const id = card?.dataset.bookmarkId;
        if (!id) return;

        const currentItem = bookmarkState.get(id);
        const style =
            card.querySelector("[data-bookmark-style-control]")?.value ||
            currentItem?.style ||
            card.dataset.bookmarkStyle ||
            state.bookmarkStyle;
        const color =
            card.querySelector("[data-bookmark-color-control]")?.value ||
            currentItem?.color ||
            card.dataset.bookmarkColor ||
            state.bookmarkColor;

        card.dataset.bookmarkStyle = style;
        card.dataset.bookmarkColor = color;
        card.style.setProperty("--bookmark-color", color);
        if (currentItem) {
            currentItem.style = style;
            currentItem.color = color;
        }
        decorateBookmarks();
        saveBookmarkAppearance(id, style, color).catch(error => showToast(error.message));
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
    loadBackgroundCatalog();
})();