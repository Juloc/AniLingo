// Novel reader annotations: highlight rendering (overlap-safe), text selection,
// bookmarks, the notes panel and on-demand notes from other chapters.
// Bookmark appearance and paged-mode bookmarks remain in reader-personalization.js.
(() => {
    const registry = window.AniLingoNovelReader = window.AniLingoNovelReader || {};

    // Splits a paragraph into segments at every highlight boundary so multiple
    // and overlapping highlights render as flat, non-nested <mark> runs.
    const buildHighlightSegments = (textLength, highlights) => {
        const ranges = highlights
            .map(item => ({
                id: item.id,
                start: Math.max(0, Math.min(textLength, item.start)),
                end: Math.max(0, Math.min(textLength, item.end))
            }))
            .filter(item => item.end > item.start);
        if (ranges.length === 0) return [];

        const boundaries = Array.from(new Set(
            [0, textLength].concat(ranges.flatMap(item => [item.start, item.end]))))
            .sort((a, b) => a - b);

        const segments = [];
        for (let index = 0; index < boundaries.length - 1; index++) {
            const start = boundaries[index];
            const end = boundaries[index + 1];
            if (end <= start) continue;
            const ids = ranges
                .filter(item => item.start <= start && item.end >= end)
                .map(item => item.id);
            const previous = segments[segments.length - 1];
            if (previous && previous.ids.join(" ") === ids.join(" ")) {
                previous.end = end;
            } else {
                segments.push({ start, end, ids });
            }
        }
        return segments;
    };

    registry.buildHighlightSegments = buildHighlightSegments;

    registry.annotations = reader => {
        const { shell, clamp } = reader;
        const bookmarkForm = shell.querySelector("[data-bookmark-form]");
        const highlightForm = shell.querySelector("[data-highlight-form]");
        const removeBookmarkEndpoint = shell.querySelector("[data-remove-bookmark-endpoint]");
        const removeHighlightEndpoint = shell.querySelector("[data-remove-highlight-endpoint]");
        const selectionMenu = shell.querySelector("[data-selection-menu]");
        const notes = shell.querySelector("[data-reader-notes]");
        const notesToggle = shell.querySelector("[data-reader-notes-toggle]");
        const bookmarkList = shell.querySelector("[data-bookmark-list]");
        const highlightList = shell.querySelector("[data-highlight-list]");
        const otherNotesStatus = shell.querySelector("[data-other-notes-status]");
        const bookmarkTracks = Array.from(shell.querySelectorAll("[data-bookmark-track]"));
        const bookmarkButton = shell.querySelector("[data-reader-bookmark]");
        const noteCountBadge = shell.querySelector("[data-reader-note-count]");
        const currentBookmarkCountBadge = shell.querySelector("[data-current-bookmark-count]");
        const emptyBookmarks = shell.querySelector("[data-empty-bookmarks]");
        const emptyHighlights = shell.querySelector("[data-empty-highlights]");
        const workNotesUrl = shell.dataset.workNotesUrl || "";
        const chapterId = shell.dataset.chapterId || "";

        const chapterBookmarks = new Map();
        const highlights = new Map();
        const otherNotes = {
            bookmarks: { offset: 0, loading: false, loaded: false, hasMore: false },
            highlights: { offset: 0, loading: false, loaded: false, hasMore: false }
        };

        let pendingSelection = null;
        let notesOpener = null;

        // ---- counts --------------------------------------------------------

        const setBadgeCount = (element, count) => {
            if (!element) return;
            element.textContent = String(count);
            element.hidden = count <= 0;
        };

        const syncCounts = () => {
            const bookmarkCount =
                bookmarkList?.querySelectorAll("[data-bookmark-card]").length || 0;
            const highlightCount =
                highlightList?.querySelectorAll("[data-highlight-card]").length || 0;

            setBadgeCount(noteCountBadge, bookmarkCount + highlightCount);
            setBadgeCount(currentBookmarkCountBadge, chapterBookmarks.size);
            emptyBookmarks?.classList.toggle("is-hidden", bookmarkCount > 0);
            emptyHighlights?.classList.toggle("is-hidden", highlightCount > 0);
            bookmarkButton?.classList.toggle("has-bookmarks", chapterBookmarks.size > 0);
        };

        // ---- highlights ----------------------------------------------------

        // Paragraphs may carry inline markup (EPUB emphasis and ruby, whose
        // readings are CSS-drawn so textContent stays the plain paragraph).
        // Highlights wrap text nodes of a pristine copy of that markup.
        const pristineParagraphs = new WeakMap();

        const pristineCopy = paragraph => {
            const saved = pristineParagraphs.get(paragraph);
            if (saved && saved.textContent === paragraph.textContent) {
                return saved.cloneNode(true);
            }
            // First render, or the text was replaced (e.g. a new translation).
            const copy = paragraph.cloneNode(true);
            copy.querySelectorAll("mark.novel-highlight").forEach(mark => mark.replaceWith(...mark.childNodes));
            copy.normalize();
            pristineParagraphs.set(paragraph, copy);
            return copy.cloneNode(true);
        };

        const wrapSegments = (root, segments) => {
            const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
            const nodes = [];
            let offset = 0;
            while (walker.nextNode()) {
                const node = walker.currentNode;
                nodes.push({ node, start: offset, end: offset + node.data.length });
                offset += node.data.length;
            }

            for (const { node, start, end } of nodes) {
                const overlapping = segments
                    .filter(segment => segment.ids.length > 0 && segment.start < end && segment.end > start)
                    .sort((a, b) => b.start - a.start);
                let head = node;
                for (const segment of overlapping) {
                    const from = Math.max(segment.start, start) - start;
                    const to = Math.min(segment.end, end) - start;
                    if (to < head.data.length) head.splitText(to);
                    const middle = from > 0 ? head.splitText(from) : head;
                    const mark = document.createElement("mark");
                    mark.className = "novel-highlight";
                    mark.dataset.highlightIds = segment.ids.join(" ");
                    mark.dataset.highlightDepth = String(Math.min(segment.ids.length, 3));
                    middle.parentNode.insertBefore(mark, middle);
                    mark.append(middle);
                }
            }
        };

        const renderParagraph = paragraph => {
            if (!paragraph) return;
            const content = pristineCopy(paragraph);
            const text = content.textContent || "";
            const language = paragraph.dataset.language || "ja";
            const index = Number(paragraph.dataset.index);
            const items = Array.from(highlights.values())
                .filter(item => item.language === language && item.paragraph === index);
            const segments = buildHighlightSegments(text.length, items);

            if (segments.length === 0) {
                if (paragraph.querySelector("mark")) paragraph.replaceChildren(...content.childNodes);
                return;
            }

            wrapSegments(content, segments);
            paragraph.replaceChildren(...content.childNodes);
        };

        const renderHighlightsFor = (language, index) =>
            renderParagraph(reader.paragraphAt(language, index));

        const renderAllHighlights = () => {
            const touched = new Set(
                Array.from(highlights.values()).map(item => `${item.language}:${item.paragraph}`));
            shell.querySelectorAll("[data-reader-paragraph]").forEach(paragraph => {
                const key = `${paragraph.dataset.language}:${paragraph.dataset.index}`;
                if (touched.has(key) || paragraph.querySelector("mark")) {
                    renderParagraph(paragraph);
                }
            });
        };

        const rememberHighlight = item => {
            if (!item?.id) return null;
            const highlight = {
                id: item.id,
                language: item.language || "ja",
                paragraph: Number(item.paragraphIndex ?? item.paragraph),
                start: Number(item.startOffset ?? item.start),
                end: Number(item.endOffset ?? item.end)
            };
            highlights.set(highlight.id, highlight);
            return highlight;
        };

        // ---- selection -----------------------------------------------------

        // Character offset of a DOM boundary within the paragraph's plain text,
        // independent of how highlight marks split the text nodes.
        const absoluteOffset = (root, targetNode, targetOffset) => {
            const range = document.createRange();
            range.selectNodeContents(root);
            range.setEnd(targetNode, targetOffset);
            return range.toString().length;
        };

        const closestParagraph = node => {
            const element = node?.nodeType === Node.TEXT_NODE ? node.parentElement : node;
            return element?.closest?.("[data-reader-paragraph]") || null;
        };

        const hideSelectionMenu = () => {
            if (selectionMenu) selectionMenu.hidden = true;
            pendingSelection = null;
        };

        const captureSelection = () => {
            const selection = window.getSelection();
            if (!selection || selection.rangeCount !== 1 || selection.isCollapsed) {
                hideSelectionMenu();
                return;
            }

            const range = selection.getRangeAt(0);
            const startParagraph = closestParagraph(range.startContainer);
            const endParagraph = closestParagraph(range.endContainer);

            if (!startParagraph ||
                startParagraph !== endParagraph ||
                !shell.contains(startParagraph)) {
                hideSelectionMenu();
                return;
            }

            const start = absoluteOffset(startParagraph, range.startContainer, range.startOffset);
            const end = absoluteOffset(startParagraph, range.endContainer, range.endOffset);
            if (end <= start) {
                hideSelectionMenu();
                return;
            }

            pendingSelection = {
                language: startParagraph.dataset.language || "ja",
                paragraphIndex: Number(startParagraph.dataset.index),
                startOffset: start,
                endOffset: end
            };

            if (selectionMenu) {
                const rect = range.getBoundingClientRect();
                selectionMenu.style.left =
                    clamp(rect.left + rect.width / 2, 90, window.innerWidth - 90) + "px";
                selectionMenu.style.top =
                    clamp(rect.top - 54, 12, window.innerHeight - 70) + "px";
                selectionMenu.hidden = false;
            }
        };

        // ---- bookmarks -----------------------------------------------------

        const bookmarkFromElement = element => ({
            id: element.dataset.bookmarkId || "",
            chapterId: element.dataset.chapterId || chapterId,
            positionPermille: Number(element.dataset.position || 0),
            language: element.dataset.language || "ja",
            paragraphIndex:
                element.dataset.paragraph === "" || element.dataset.paragraph == null
                    ? null
                    : Number(element.dataset.paragraph),
            characterOffset: Number(element.dataset.offset || 0),
            anchorText: element.dataset.anchorText || "",
            label: ""
        });

        const renderBookmarkMarker = bookmark => {
            if (!bookmark.id) return;
            for (const track of bookmarkTracks) {
                track.querySelectorAll("[data-bookmark-marker]").forEach(marker => {
                    if (marker.dataset.bookmarkId === bookmark.id) marker.remove();
                });

                const marker = document.createElement("button");
                marker.type = "button";
                marker.className = "novel-bookmark-marker";
                marker.dataset.bookmarkMarker = "";
                marker.dataset.bookmarkId = bookmark.id;

                const position = clamp(bookmark.positionPermille / 10, 1.5, 98.5);
                if (track.dataset.orientation === "horizontal") {
                    marker.style.left = position + "%";
                } else {
                    marker.style.top = position + "%";
                }

                marker.append(document.createElement("span"));
                marker.title = bookmark.anchorText
                    ? `Lesezeichen · ${bookmark.anchorText}`
                    : `Lesezeichen · ${Math.round(bookmark.positionPermille / 10)}%`;
                marker.setAttribute("aria-label", marker.title);
                track.append(marker);
            }
        };

        const createNoteCard = ({ href, heading, title, detail, removeAttribute, removeLabel, id }) => {
            const card = document.createElement("article");
            card.className = "novel-note-card";

            const link = document.createElement("a");
            link.href = href;
            if (heading) {
                const chapter = document.createElement("small");
                chapter.className = "novel-note-chapter";
                chapter.textContent = heading;
                link.append(chapter);
            }
            const strong = document.createElement("strong");
            strong.textContent = title;
            link.append(strong);
            if (detail) {
                const excerpt = document.createElement("span");
                excerpt.textContent = detail;
                link.append(excerpt);
            }

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "novel-note-remove";
            remove.setAttribute(removeAttribute, "");
            remove.dataset.noteId = id;
            remove.textContent = "Entfernen";
            remove.setAttribute("aria-label", removeLabel);

            card.append(link, remove);
            return { card, link, remove };
        };

        const renderBookmarkCard = bookmark => {
            if (!bookmarkList || !bookmark?.id) return;
            bookmarkList.querySelectorAll("[data-bookmark-card]").forEach(card => {
                if (card.dataset.bookmarkId === bookmark.id) card.remove();
            });

            const { card, link, remove } = createNoteCard({
                href: `/Novels/Read/${encodeURIComponent(bookmark.chapterId)}?bookmark=${encodeURIComponent(bookmark.id)}`,
                title: bookmark.label || `Lesezeichen · ${Math.round(bookmark.positionPermille / 10)}%`,
                detail: bookmark.anchorText,
                removeAttribute: "data-remove-bookmark-button",
                removeLabel: "Lesezeichen entfernen",
                id: bookmark.id
            });
            card.dataset.bookmarkCard = "";
            card.dataset.bookmarkId = bookmark.id;
            card.dataset.bookmarkChapterId = bookmark.chapterId;
            if (bookmark.style) card.dataset.bookmarkStyle = bookmark.style;
            if (bookmark.color) card.dataset.bookmarkColor = bookmark.color;
            link.dataset.localBookmarkId = bookmark.id;
            remove.dataset.bookmarkId = bookmark.id;
            bookmarkList.prepend(card);
        };

        const renderHighlightCard = highlight => {
            if (!highlightList || !highlight?.id) return;
            highlightList.querySelectorAll("[data-highlight-card]").forEach(card => {
                if (card.dataset.highlightId === highlight.id) card.remove();
            });

            const { card, link, remove } = createNoteCard({
                href: `/Novels/Read/${encodeURIComponent(highlight.chapterId || chapterId)}?highlight=${encodeURIComponent(highlight.id)}`,
                title: highlight.text || "Markierung",
                detail: highlight.note,
                removeAttribute: "data-remove-highlight-button",
                removeLabel: "Markierung entfernen",
                id: highlight.id
            });
            card.dataset.highlightCard = "";
            card.dataset.highlightId = highlight.id;
            link.dataset.localHighlightId = highlight.id;
            remove.dataset.highlightId = highlight.id;
            highlightList.prepend(card);
        };

        const removeBookmarkUi = bookmarkId => {
            shell.querySelectorAll("[data-bookmark-card], [data-saved-bookmark]").forEach(element => {
                if (element.dataset.bookmarkId === bookmarkId) element.remove();
            });
            bookmarkTracks.forEach(track => {
                track.querySelectorAll("[data-bookmark-marker]").forEach(marker => {
                    if (marker.dataset.bookmarkId === bookmarkId) marker.remove();
                });
            });
            chapterBookmarks.delete(bookmarkId);
            syncCounts();
        };

        const removeHighlightUi = highlightId => {
            shell.querySelectorAll("[data-highlight-card], [data-saved-highlight]").forEach(element => {
                if (element.dataset.highlightId === highlightId) element.remove();
            });
            const highlight = highlights.get(highlightId);
            highlights.delete(highlightId);
            if (highlight) renderHighlightsFor(highlight.language, highlight.paragraph);
            syncCounts();
        };

        const removeBookmark = async bookmarkId => {
            if (!removeBookmarkEndpoint || !bookmarkId) return false;
            try {
                await reader.postForm(removeBookmarkEndpoint, data => {
                    data.set("bookmarkId", bookmarkId);
                });
                removeBookmarkUi(bookmarkId);
                reader.showToast("Lesezeichen entfernt");
                return true;
            } catch (error) {
                reader.showToast(error.message || "Lesezeichen konnte nicht entfernt werden");
                return false;
            }
        };

        const removeHighlight = async highlightId => {
            if (!removeHighlightEndpoint || !highlightId) return false;
            try {
                await reader.postForm(removeHighlightEndpoint, data => {
                    data.set("highlightId", highlightId);
                });
                removeHighlightUi(highlightId);
                reader.showToast("Markierung entfernt");
                return true;
            } catch (error) {
                reader.showToast(error.message || "Markierung konnte nicht entfernt werden");
                return false;
            }
        };

        const saveBookmark = async () => {
            if (!bookmarkForm) return;

            const anchor = reader.position.currentAnchor();
            const position = reader.position.positionPermille();
            try {
                const saved = await reader.postForm(bookmarkForm, data => {
                    data.set("positionPermille", String(position));
                    data.set("language", anchor.language);
                    data.set(
                        "paragraphIndex",
                        anchor.paragraphIndex == null ? "" : String(anchor.paragraphIndex));
                    data.set("characterOffset", String(anchor.characterOffset));
                    data.set("label", "");
                });

                if (!saved?.id) throw new Error("Lesezeichen konnte nicht gespeichert werden");

                const bookmark = {
                    ...saved,
                    chapterId: saved.chapterId || chapterId,
                    anchorText: saved.anchorText || "",
                    label: saved.label || ""
                };

                renderBookmarkCard(bookmark);
                if (bookmark.chapterId === chapterId) {
                    chapterBookmarks.set(bookmark.id, bookmark);
                    renderBookmarkMarker(bookmark);
                }
                syncCounts();
                reader.showToast("Lesezeichen gespeichert");
            } catch (error) {
                reader.showToast(error.message || "Lesezeichen konnte nicht gespeichert werden");
            }
        };

        const saveHighlight = async withNote => {
            if (!highlightForm || !pendingSelection) return;

            let note = "";
            if (withNote) {
                const answer = window.prompt("Notiz zu dieser Markierung:", "");
                if (answer === null) return;
                note = answer;
            }

            const selected = { ...pendingSelection };
            try {
                const saved = await reader.postForm(highlightForm, data => {
                    data.set("language", selected.language);
                    data.set("paragraphIndex", String(selected.paragraphIndex));
                    data.set("startOffset", String(selected.startOffset));
                    data.set("endOffset", String(selected.endOffset));
                    data.set("note", note);
                });

                const highlight = rememberHighlight(saved);
                if (!highlight) throw new Error("Markierung konnte nicht gespeichert werden");

                renderHighlightsFor(highlight.language, highlight.paragraph);
                renderHighlightCard(saved);
                syncCounts();

                window.getSelection()?.removeAllRanges();
                hideSelectionMenu();
                reader.showToast(note ? "Markierung und Notiz gespeichert" : "Markierung gespeichert");
            } catch (error) {
                // Keep the selection so the reader can retry after a failed save.
                reader.showToast(error.message || "Markierung konnte nicht gespeichert werden");
            }
        };

        // ---- notes panel ---------------------------------------------------

        const setNotesOpen = (open, restoreFocus = true) => {
            if (!notes) return;
            if (open === !notes.hidden) return;

            notes.hidden = !open;
            shell.classList.toggle("notes-open", open);
            notesToggle?.setAttribute("aria-expanded", open ? "true" : "false");

            if (open) {
                notesOpener = document.activeElement;
                reader.announcePanel("notes");
                notes.focus({ preventScroll: true });
            } else if (restoreFocus && notesOpener instanceof HTMLElement) {
                notesOpener.focus({ preventScroll: true });
                notesOpener = null;
            }
        };

        const otherNoteCard = (kind, note) => {
            const isBookmark = kind === "bookmarks";
            const { card } = createNoteCard({
                href: isBookmark
                    ? `/Novels/Read/${encodeURIComponent(note.chapterId)}?bookmark=${encodeURIComponent(note.id)}`
                    : `/Novels/Read/${encodeURIComponent(note.chapterId)}?highlight=${encodeURIComponent(note.id)}`,
                heading: `Kapitel ${note.chapterNumber}`,
                title: note.title || (isBookmark
                    ? `Lesezeichen · ${Math.round((note.positionPermille || 0) / 10)}%`
                    : "Markierung"),
                detail: note.detail,
                removeAttribute: "data-remove-other-note",
                removeLabel: isBookmark ? "Lesezeichen entfernen" : "Markierung entfernen",
                id: note.id
            });
            card.classList.add("novel-other-note");
            card.dataset.otherNoteCard = "";
            card.dataset.noteKind = kind;
            card.dataset.noteId = note.id;
            card.querySelector("[data-remove-other-note]").dataset.noteKind = kind;
            if (isBookmark && note.color) card.style.setProperty("--bookmark-color", note.color);
            return card;
        };

        const loadOtherNotes = async kind => {
            const state = otherNotes[kind];
            const list = shell.querySelector(`[data-other-note-list="${kind}"]`);
            const more = shell.querySelector(`[data-other-notes-more="${kind}"]`);
            if (!state || !list || state.loading || !workNotesUrl) return;

            state.loading = true;
            if (more) more.disabled = true;
            if (otherNotesStatus) otherNotesStatus.textContent = "Notizen werden geladen …";

            try {
                const page = await reader.getJson(
                    reader.withQuery(workNotesUrl, { kind, offset: state.offset }));
                const fragment = document.createDocumentFragment();
                for (const note of page.items || []) {
                    fragment.append(otherNoteCard(kind, note));
                }
                list.append(fragment);
                state.offset = page.nextOffset ?? state.offset;
                state.loaded = true;
                state.hasMore = Boolean(page.hasMore);
                if (more) more.hidden = !state.hasMore;
                if (otherNotesStatus) otherNotesStatus.textContent = "";
            } catch {
                if (otherNotesStatus) {
                    otherNotesStatus.textContent = "Notizen konnten nicht geladen werden.";
                }
            } finally {
                state.loading = false;
                if (more) more.disabled = false;
            }
        };

        const toggleOtherNotes = kind => {
            const toggle = shell.querySelector(`[data-other-notes-toggle="${kind}"]`);
            const list = shell.querySelector(`[data-other-note-list="${kind}"]`);
            const more = shell.querySelector(`[data-other-notes-more="${kind}"]`);
            if (!toggle || !list) return;

            const expand = toggle.getAttribute("aria-expanded") !== "true";
            toggle.setAttribute("aria-expanded", expand ? "true" : "false");
            list.hidden = !expand;
            if (!expand) {
                if (more) more.hidden = true;
                return;
            }

            if (!otherNotes[kind].loaded) {
                void loadOtherNotes(kind);
            } else if (more) {
                more.hidden = !otherNotes[kind].hasMore;
            }
        };

        const removeOtherNote = async button => {
            const kind = button.dataset.noteKind;
            const id = button.dataset.noteId;
            const removed = kind === "bookmarks"
                ? await removeBookmark(id)
                : await removeHighlight(id);
            if (!removed) return;

            button.closest("[data-other-note-card]")?.remove();
            const count = shell.querySelector(`[data-other-notes-count="${kind}"]`);
            if (count) {
                count.textContent = String(Math.max(0, Number(count.textContent || 0) - 1));
            }
            otherNotes[kind].offset = Math.max(0, otherNotes[kind].offset - 1);
        };

        // ---- events --------------------------------------------------------

        shell.addEventListener("click", event => {
            if (event.target.closest("[data-reader-bookmark]")) {
                void saveBookmark();
                return;
            }

            const localBookmark = event.target.closest("[data-local-bookmark-id]");
            if (localBookmark && chapterBookmarks.has(localBookmark.dataset.localBookmarkId)) {
                event.preventDefault();
                reader.position.scrollTo(chapterBookmarks.get(localBookmark.dataset.localBookmarkId));
                return;
            }

            const localHighlight = event.target.closest("[data-local-highlight-id]");
            if (localHighlight && highlights.has(localHighlight.dataset.localHighlightId)) {
                event.preventDefault();
                const item = highlights.get(localHighlight.dataset.localHighlightId);
                reader.position.scrollTo({
                    language: item.language,
                    paragraphIndex: item.paragraph,
                    characterOffset: item.start
                });
                return;
            }

            const removeBookmarkButton = event.target.closest("[data-remove-bookmark-button]");
            if (removeBookmarkButton) {
                void removeBookmark(removeBookmarkButton.dataset.bookmarkId);
                return;
            }

            const removeHighlightButton = event.target.closest("[data-remove-highlight-button]");
            if (removeHighlightButton) {
                void removeHighlight(removeHighlightButton.dataset.highlightId);
                return;
            }

            const removeOther = event.target.closest("[data-remove-other-note]");
            if (removeOther) {
                void removeOtherNote(removeOther);
                return;
            }

            const otherToggle = event.target.closest("[data-other-notes-toggle]");
            if (otherToggle) {
                toggleOtherNotes(otherToggle.dataset.otherNotesToggle);
                return;
            }

            const otherMore = event.target.closest("[data-other-notes-more]");
            if (otherMore) {
                void loadOtherNotes(otherMore.dataset.otherNotesMore);
                return;
            }

            if (event.target.closest("[data-reader-notes-toggle]")) {
                setNotesOpen(notes?.hidden !== false);
                return;
            }

            if (event.target.closest("[data-reader-notes-close]")) {
                setNotesOpen(false);
                return;
            }

            if (event.target.closest("[data-save-highlight-note]")) {
                void saveHighlight(true);
                return;
            }

            if (event.target.closest("[data-save-highlight]")) {
                void saveHighlight(false);
            }
        });

        // Marker clicks and selection-menu dismissal are handled page-wide.
        document.addEventListener("click", event => {
            const marker = event.target.closest("[data-bookmark-marker]");
            if (marker) {
                reader.position.scrollTo(chapterBookmarks.get(marker.dataset.bookmarkId));
                return;
            }

            if (selectionMenu &&
                !selectionMenu.hidden &&
                !event.target.closest("[data-selection-menu]")) {
                setTimeout(captureSelection, 0);
            }
        });

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && notes && !notes.hidden) {
                setNotesOpen(false);
            }
        });

        document.addEventListener("mouseup", () => setTimeout(captureSelection, 0));
        document.addEventListener("touchend", () => setTimeout(captureSelection, 40), { passive: true });

        shell.addEventListener("anilingo:novel-translation-ready", renderAllHighlights);
        reader.onOtherPanelOpened("notes", setNotesOpen.bind(null, false));

        // ---- initial render ------------------------------------------------

        shell.querySelectorAll("[data-saved-bookmark]").forEach(element => {
            const bookmark = bookmarkFromElement(element);
            if (!bookmark.id) return;
            chapterBookmarks.set(bookmark.id, bookmark);
            renderBookmarkMarker(bookmark);
        });

        shell.querySelectorAll("[data-saved-highlight]").forEach(element => {
            rememberHighlight({
                id: element.dataset.highlightId,
                language: element.dataset.language,
                paragraph: element.dataset.paragraph,
                start: element.dataset.start,
                end: element.dataset.end
            });
        });

        renderAllHighlights();
        syncCounts();

        return {
            renderAllHighlights
        };
    };
})();
