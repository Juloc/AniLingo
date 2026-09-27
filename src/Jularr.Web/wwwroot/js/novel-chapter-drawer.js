// Novel chapter list ("Contents" tab of the reader frame): loads bounded chapter
// windows on demand from the reader's Chapters handler (around the current
// chapter, paging by number, server-side search). The page never embeds the
// full chapter index. EPUB volumes group the rows under a volume heading.
(() => {
    const registry = window.JularrNovelReader = window.JularrNovelReader || {};

    // Chapters that are not numbered story chapters in the source: prologue,
    // epilogue, interludes, side/short stories, extras and the afterword.
    const specialChapterPattern = new RegExp(
        "^(?:" + [
            "プロローグ", "エピローグ", "序章", "終章", "幕間", "閑話", "番外編?", "外伝",
            "あとがき", "後書き", "短編", "書き下ろし",
            "prolog(?:ue)?", "epilog(?:ue)?", "interlude", "intermission", "afterword",
            "nachwort", "zwischenspiel", "extra", "bonus", "side ?story", "short ?story",
            "character ?stor(?:y|ies)"
        ].join("|") + ")(?![a-z])",
        "i");
    const isSpecialChapter = title => specialChapterPattern.test(title || "");

    registry.chapterDrawer = reader => {
        const { shell, normalizeText, t } = reader;
        const drawer = shell.querySelector("[data-chapter-drawer]");
        const filter = shell.querySelector("[data-chapter-filter]");
        const list = shell.querySelector("[data-chapter-list]");
        const scroller = shell.querySelector("[data-chapter-list-scroll]");
        const loadBefore = shell.querySelector("[data-chapter-load-before]");
        const loadAfter = shell.querySelector("[data-chapter-load-after]");
        const status = shell.querySelector("[data-chapter-status]");
        const chaptersUrl = shell.dataset.chaptersUrl || "";
        const currentId = shell.dataset.chapterId || "";
        const currentNumber = Number(shell.dataset.chapterNumber || 0);

        if (!drawer || !list || !chaptersUrl) {
            return { open: () => {}, close: () => {} };
        }

        let query = "";
        let firstNumber = null;
        let lastNumber = null;
        let firstVolume = null;
        let lastVolume = null;
        let loadedQuery = null;
        let requestId = 0;
        let searchTimer = null;

        const setStatus = (message, retry = false) => {
            if (!status) return;
            status.replaceChildren();
            if (!message) return;
            status.append(document.createTextNode(message));
            if (retry) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "novel-drawer-more";
                button.dataset.chapterRetry = "";
                button.textContent = t("retry", "Try again");
                status.append(button);
            }
        };

        const volumeHeading = volume => {
            const heading = document.createElement("h3");
            heading.className = "novel-drawer-volume-heading";
            heading.textContent = t("volumeLabel", "Volume {volume}", { volume });
            return heading;
        };

        const createRow = chapter => {
            const link = document.createElement("a");
            const isCurrent = chapter.id === currentId;
            link.className = [
                "reader-contents-row",
                "novel-drawer-row",
                isCurrent ? "current" : chapter.number < currentNumber ? "earlier" : "",
                chapter.hasContent ? "" : "is-remote"
            ].filter(Boolean).join(" ");
            link.href = `/Novels/Read/${encodeURIComponent(chapter.id)}`;
            link.dataset.chapterRow = "";
            if (isCurrent) link.setAttribute("aria-current", "page");
            if (!chapter.hasContent) link.title = t("notDownloaded", "Not downloaded yet");

            // Two lines like a printed table of contents: "Chapter 3" and the
            // chapter's own title. Prologues, interludes, extras and afterwords
            // carry their name as the only line; the number would be noise.
            const titleText = normalizeText(chapter.title);
            const text = document.createElement("span");
            text.className = "novel-drawer-text";
            if (!isSpecialChapter(titleText)) {
                const number = document.createElement("span");
                number.className = "novel-drawer-label";
                number.textContent = t("chapterNumber", "Chapter {number}", { number: chapter.number });
                text.append(number);
            }

            const title = document.createElement("span");
            title.className = "reader-contents-title";
            title.lang = "ja";
            title.textContent = titleText;
            text.append(title);

            link.append(text);
            if (chapter.hasTranslation) {
                const language = document.createElement("span");
                language.className = "novel-drawer-lang";
                language.textContent = "DE";
                language.title = t("germanAvailable", "German translation available");
                link.append(language);
            }
            return link;
        };

        // Builds rows plus volume headings; `previousVolume` is the volume of the
        // row directly before the inserted block (null at the list start).
        const buildRows = (items, previousVolume) => {
            const fragment = document.createDocumentFragment();
            let volume = previousVolume;
            for (const item of items) {
                if (item.volumeNumber && item.volumeNumber !== volume) {
                    fragment.append(volumeHeading(item.volumeNumber));
                }
                volume = item.volumeNumber || volume;
                fragment.append(createRow(item));
            }
            return fragment;
        };

        const render = (items, mode) => {
            if (mode === "replace") {
                list.replaceChildren(buildRows(items, null));
            } else if (mode === "prepend") {
                const previousHeight = scroller?.scrollHeight || 0;
                // The first existing heading repeats when the block ends in its volume.
                const firstHeading = list.firstElementChild;
                const endsInFirstVolume = items.length > 0 &&
                    items[items.length - 1].volumeNumber === firstVolume;
                if (endsInFirstVolume && firstHeading?.classList.contains("novel-drawer-volume-heading")) {
                    firstHeading.remove();
                }
                list.prepend(buildRows(items, null));
                if (scroller) scroller.scrollTop += scroller.scrollHeight - previousHeight;
            } else {
                list.append(buildRows(items, lastVolume));
            }

            if (items.length === 0) return;
            if (mode !== "append") {
                firstNumber = items[0].number;
                firstVolume = items[0].volumeNumber || null;
            }
            if (mode !== "prepend") {
                lastNumber = items[items.length - 1].number;
                lastVolume = items[items.length - 1].volumeNumber || null;
            }
        };

        const load = async ({ mode, after = null, before = null }) => {
            const id = ++requestId;
            const button = mode === "prepend" ? loadBefore : mode === "append" ? loadAfter : null;
            if (button) button.disabled = true;
            if (mode === "replace") {
                firstNumber = null;
                lastNumber = null;
                list.setAttribute("aria-busy", "true");
                setStatus(t("loadingChapters", "Loading chapters…"));
            }

            try {
                const page = await reader.getJson(reader.withQuery(chaptersUrl, {
                    q: query,
                    after,
                    before
                }));
                if (id !== requestId) return;

                const items = page.items || [];
                render(items, mode);

                if (mode !== "append" && loadBefore) loadBefore.hidden = !page.hasBefore;
                if (mode !== "prepend" && loadAfter) loadAfter.hidden = !page.hasAfter;

                loadedQuery = query;
                setStatus(mode === "replace" && items.length === 0
                    ? (query
                        ? t("noChaptersFound", "No chapters found.")
                        : t("noChapters", "No chapters yet."))
                    : "");

                if (mode === "replace" && !query) {
                    list.querySelector("[aria-current='page']")
                        ?.scrollIntoView({ block: "center" });
                }
            } catch {
                if (id !== requestId) return;
                setStatus(t("chaptersFailed", "Chapters could not be loaded."), true);
            } finally {
                if (id === requestId) list.removeAttribute("aria-busy");
                if (button) button.disabled = false;
            }
        };

        // Initial window around the current chapter; rows are only built when
        // the Contents tab is shown for the first time.
        const ensureChapterRows = () => {
            if (loadedQuery === query) return;
            void load({ mode: "replace" });
        };

        shell.addEventListener("jularr:reader-contents", event => {
            if (event.detail?.open && event.detail.tab === "chapters") ensureChapterRows();
        });

        shell.addEventListener("click", event => {
            if (event.target.closest("[data-chapter-load-before]") && firstNumber !== null) {
                void load({ mode: "prepend", before: firstNumber });
                return;
            }

            if (event.target.closest("[data-chapter-load-after]") && lastNumber !== null) {
                void load({ mode: "append", after: lastNumber });
                return;
            }

            if (event.target.closest("[data-chapter-retry]")) {
                loadedQuery = null;
                ensureChapterRows();
            }
        });

        filter?.addEventListener("input", () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => {
                const next = normalizeText(filter.value);
                if (next === query) return;
                query = next;
                ensureChapterRows();
            }, 250);
        });

        return {
            open: () => reader.frame()?.openContents("chapters"),
            close: () => reader.frame()?.closeContents()
        };
    };
})();
