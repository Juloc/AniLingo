// Novel chapter drawer: loads bounded chapter windows on demand from the
// reader's Chapters handler (around the current chapter, paging by number,
// server-side search). The page never embeds the full chapter index.
(() => {
    const registry = window.AniLingoNovelReader = window.AniLingoNovelReader || {};

    registry.chapterDrawer = reader => {
        const { shell, normalizeText } = reader;
        const drawer = shell.querySelector("[data-chapter-drawer]");
        const backdrop = shell.querySelector("[data-chapter-drawer-backdrop]");
        const filter = shell.querySelector("[data-chapter-filter]");
        const list = shell.querySelector("[data-chapter-list]");
        const scroller = shell.querySelector("[data-chapter-list-scroll]");
        const loadBefore = shell.querySelector("[data-chapter-load-before]");
        const loadAfter = shell.querySelector("[data-chapter-load-after]");
        const status = shell.querySelector("[data-chapter-status]");
        const toggle = shell.querySelector("[data-reader-chapters-toggle]");
        const chaptersUrl = shell.dataset.chaptersUrl || "";
        const currentId = shell.dataset.chapterId || "";
        const currentNumber = Number(shell.dataset.chapterNumber || 0);

        if (!drawer || !backdrop || !list || !chaptersUrl) {
            return { open: () => {}, close: () => {} };
        }

        let opener = null;
        let query = "";
        let firstNumber = null;
        let lastNumber = null;
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
                button.textContent = "Erneut versuchen";
                status.append(button);
            }
        };

        const createRow = chapter => {
            const link = document.createElement("a");
            const isCurrent = chapter.id === currentId;
            link.className = [
                "novel-drawer-row",
                isCurrent ? "current" : chapter.number < currentNumber ? "earlier" : "",
                chapter.hasContent ? "" : "is-remote"
            ].filter(Boolean).join(" ");
            link.href = `/Novels/Read/${encodeURIComponent(chapter.id)}`;
            link.dataset.chapterRow = "";
            if (isCurrent) link.setAttribute("aria-current", "page");
            if (!chapter.hasContent) link.title = "Noch nicht heruntergeladen";

            const number = document.createElement("span");
            number.className = "novel-drawer-number";
            number.textContent = String(chapter.number);

            const title = document.createElement("span");
            title.className = "novel-drawer-title";
            title.lang = "ja";
            title.textContent = normalizeText(chapter.title);
            if (chapter.volumeNumber) {
                const volume = document.createElement("span");
                volume.className = "novel-drawer-volume";
                volume.lang = "de";
                volume.textContent = `Bd. ${chapter.volumeNumber}`;
                title.prepend(volume);
            }

            const language = document.createElement("span");
            language.className = "novel-drawer-lang";
            language.textContent = chapter.hasTranslation ? "DE" : "";

            const mark = document.createElement("span");
            if (isCurrent) {
                mark.className = "novel-drawer-current-mark";
                mark.setAttribute("aria-label", "Aktuelles Kapitel");
            }

            link.append(number, title, language, mark);
            return link;
        };

        const render = (items, mode) => {
            const fragment = document.createDocumentFragment();
            items.forEach(item => fragment.append(createRow(item)));

            if (mode === "replace") {
                list.replaceChildren(fragment);
            } else if (mode === "prepend") {
                const previousHeight = scroller?.scrollHeight || 0;
                list.prepend(fragment);
                if (scroller) scroller.scrollTop += scroller.scrollHeight - previousHeight;
            } else {
                list.append(fragment);
            }

            if (items.length === 0) return;
            if (mode !== "append") firstNumber = items[0].number;
            if (mode !== "prepend") lastNumber = items[items.length - 1].number;
        };

        const load = async ({ mode, after = null, before = null }) => {
            const id = ++requestId;
            const button = mode === "prepend" ? loadBefore : mode === "append" ? loadAfter : null;
            if (button) button.disabled = true;
            if (mode === "replace") {
                firstNumber = null;
                lastNumber = null;
                list.setAttribute("aria-busy", "true");
                setStatus("Kapitel werden geladen …");
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
                    ? (query ? "Keine Kapitel gefunden." : "Keine Kapitel vorhanden.")
                    : "");

                if (mode === "replace" && !query) {
                    list.querySelector(".novel-drawer-row.current")
                        ?.scrollIntoView({ block: "center" });
                }
            } catch {
                if (id !== requestId) return;
                setStatus("Kapitel konnten nicht geladen werden.", true);
            } finally {
                if (id === requestId) list.removeAttribute("aria-busy");
                if (button) button.disabled = false;
            }
        };

        // Initial window around the current chapter; rows are only built when
        // the drawer is opened for the first time.
        const ensureChapterRows = () => {
            if (loadedQuery === query) return;
            void load({ mode: "replace" });
        };

        const setOpen = (open, restoreFocus = true) => {
            if (open === !drawer.hidden) return;

            drawer.hidden = !open;
            backdrop.hidden = !open;
            shell.classList.toggle("chapter-drawer-open", open);
            document.documentElement.classList.toggle("novel-drawer-lock", open);
            toggle?.setAttribute("aria-expanded", open ? "true" : "false");

            if (open) {
                opener = document.activeElement;
                reader.announcePanel("chapters");
                ensureChapterRows();
                // Avoid opening the on-screen keyboard on touch layouts.
                if (window.matchMedia("(min-width: 821px)").matches && filter) {
                    filter.focus({ preventScroll: true });
                } else {
                    drawer.focus({ preventScroll: true });
                }
                list.querySelector(".novel-drawer-row.current")
                    ?.scrollIntoView({ block: "center" });
            } else if (restoreFocus && opener instanceof HTMLElement) {
                opener.focus({ preventScroll: true });
                opener = null;
            }
        };

        shell.addEventListener("click", event => {
            if (event.target.closest("[data-reader-chapters-toggle]")) {
                setOpen(drawer.hidden);
                return;
            }

            if (event.target.closest("[data-chapter-drawer-close]") ||
                event.target.closest("[data-chapter-drawer-backdrop]")) {
                setOpen(false);
                return;
            }

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

        document.addEventListener("keydown", event => {
            if (drawer.hidden) return;
            if (event.key === "Escape") {
                event.preventDefault();
                setOpen(false);
                return;
            }
            reader.trapFocus(drawer, event);
        });

        reader.onOtherPanelOpened("chapters", setOpen.bind(null, false));

        return {
            open: () => setOpen(true),
            close: () => setOpen(false)
        };
    };
})();
