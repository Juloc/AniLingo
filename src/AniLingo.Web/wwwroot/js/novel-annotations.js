// Novel reader annotations: highlight rendering (overlap-safe), text selection,
// bookmarks, the notes panel and on-demand notes from other chapters.
// Bookmark appearance and paged-mode bookmarks remain in reader-personalization.js.
(() => {
    const registry = window.AniLingoNovelReader = window.AniLingoNovelReader || {};

    // Desktop shortcuts for this module. Kept in one place and exposed on the
    // registry so a single source of truth exists to check against reader-shell.js
    // (Escape), reader-personalization.js (ArrowRight/ArrowLeft/PageUp/PageDown in
    // paged mode) and reader-tts.js (Space/Arrow/PageUp/PageDown scroll tracking).
    const READER_SHORTCUTS = {
        bookmark: "b",
        notes: "n",
        previousBookmark: "[",
        nextBookmark: "]"
    };
    registry.shortcutKeys = READER_SHORTCUTS;

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
        const { shell, clamp, normalizeText } = reader;
        const highlightForm = shell.querySelector("[data-highlight-form]");
        const removeHighlightEndpoint = shell.querySelector("[data-remove-highlight-endpoint]");
        const bookmarkLabelEndpoint = shell.querySelector("[data-bookmark-label-endpoint]");
        const highlightNoteEndpoint = shell.querySelector("[data-highlight-note-endpoint]");
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
        const searchNotesUrl = shell.dataset.searchNotesUrl || "";
        const adjacentBookmarkUrl = shell.dataset.adjacentBookmarkUrl || "";
        const chapterId = shell.dataset.chapterId || "";
        const workId = shell.dataset.workId || "";
        // Bookmark add/remove/rename always go through the offline-first sync
        // queue (#221 part 2), online and offline alike — see
        // offline-library-repository.js and docs/OFFLINE_LIBRARY.md. Highlights
        // are not part of the offline sync contract and keep using their
        // existing endpoints below unchanged.
        const repository = workId && window.AniLingoOfflineLibraryRepository
            ? window.AniLingoOfflineLibraryRepository.forWork(workId)
            : null;

        // Heading shown on every bookmark/highlight note card for the current
        // chapter, matching the label the server attaches to other chapters'
        // notes (chapter number/title and volume, when the work has volumes).
        const currentChapterHeading = () => {
            const volumeNumber = shell.dataset.volumeNumber;
            const chapterNumber = shell.dataset.chapterNumber || "";
            const chapterTitle = shell.dataset.chapterTitle || "";
            const volumePrefix = volumeNumber ? `Band ${volumeNumber} · ` : "";
            return `${volumePrefix}Kapitel ${chapterNumber} · ${chapterTitle}`;
        };

        const bookmarkCardSelector = id =>
            `[data-note-kind="bookmarks"][data-note-id="${id}"]`;
        const highlightCardSelector = id =>
            `[data-note-kind="highlights"][data-note-id="${id}"]`;

        const setCardDetailText = (card, text) => {
            const link = card.querySelector("a");
            if (!link) return;
            let span = link.querySelector("span");
            if (!text) {
                span?.remove();
                return;
            }
            if (!span) {
                span = document.createElement("span");
                link.append(span);
            }
            span.textContent = text;
        };

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

        const createNoteCard = ({
            href, heading, title, detail,
            removeAttribute, removeLabel,
            editAttribute, editLabel,
            id
        }) => {
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

            const actions = document.createElement("div");
            actions.className = "novel-note-actions";

            let edit = null;
            if (editAttribute) {
                edit = document.createElement("button");
                edit.type = "button";
                edit.className = "novel-note-edit";
                edit.setAttribute(editAttribute, "");
                edit.dataset.noteId = id;
                edit.textContent = "Bearbeiten";
                edit.setAttribute("aria-label", editLabel || "Bearbeiten");
                actions.append(edit);
            }

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "novel-note-remove";
            remove.setAttribute(removeAttribute, "");
            remove.dataset.noteId = id;
            remove.textContent = "Entfernen";
            remove.setAttribute("aria-label", removeLabel);
            actions.append(remove);

            card.append(link, actions);
            return { card, link, remove, edit };
        };

        const renderBookmarkCard = bookmark => {
            if (!bookmarkList || !bookmark?.id) return;
            bookmarkList.querySelectorAll("[data-bookmark-card]").forEach(card => {
                if (card.dataset.bookmarkId === bookmark.id) card.remove();
            });

            const { card, link, remove, edit } = createNoteCard({
                href: `/Novels/Read/${encodeURIComponent(bookmark.chapterId)}?bookmark=${encodeURIComponent(bookmark.id)}`,
                heading: bookmark.chapterId === chapterId ? currentChapterHeading() : undefined,
                title: bookmark.label || `Lesezeichen · ${Math.round(bookmark.positionPermille / 10)}%`,
                detail: bookmark.anchorText,
                removeAttribute: "data-remove-bookmark-button",
                removeLabel: "Lesezeichen entfernen",
                editAttribute: "data-edit-bookmark-button",
                editLabel: "Lesezeichen umbenennen",
                id: bookmark.id
            });
            card.dataset.bookmarkCard = "";
            card.dataset.bookmarkId = bookmark.id;
            card.dataset.bookmarkChapterId = bookmark.chapterId;
            card.dataset.noteKind = "bookmarks";
            card.dataset.noteId = bookmark.id;
            card.dataset.bookmarkLabel = bookmark.label || "";
            card.dataset.positionPermille = String(bookmark.positionPermille || 0);
            if (bookmark.style) card.dataset.bookmarkStyle = bookmark.style;
            if (bookmark.color) card.dataset.bookmarkColor = bookmark.color;
            link.dataset.localBookmarkId = bookmark.id;
            remove.dataset.bookmarkId = bookmark.id;
            if (edit) edit.dataset.bookmarkId = bookmark.id;
            bookmarkList.prepend(card);
        };

        const renderHighlightCard = highlight => {
            if (!highlightList || !highlight?.id) return;
            highlightList.querySelectorAll("[data-highlight-card]").forEach(card => {
                if (card.dataset.highlightId === highlight.id) card.remove();
            });

            const { card, link, remove, edit } = createNoteCard({
                href: `/Novels/Read/${encodeURIComponent(highlight.chapterId || chapterId)}?highlight=${encodeURIComponent(highlight.id)}`,
                heading: currentChapterHeading(),
                title: highlight.text || "Markierung",
                detail: highlight.note,
                removeAttribute: "data-remove-highlight-button",
                removeLabel: "Markierung entfernen",
                editAttribute: "data-edit-highlight-button",
                editLabel: "Notiz bearbeiten",
                id: highlight.id
            });
            card.dataset.highlightCard = "";
            card.dataset.highlightId = highlight.id;
            card.dataset.noteKind = "highlights";
            card.dataset.noteId = highlight.id;
            card.dataset.highlightNote = highlight.note || "";
            link.dataset.localHighlightId = highlight.id;
            remove.dataset.highlightId = highlight.id;
            if (edit) edit.dataset.highlightId = highlight.id;
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
            if (!repository || !bookmarkId) return false;
            try {
                await repository.queueBookmarkRemove(bookmarkId, { chapterId });
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
            if (!repository) return;

            const anchor = reader.position.currentAnchor();
            const position = reader.position.positionPermille();
            const paragraph = anchor.paragraphIndex == null
                ? null
                : reader.paragraphAt(anchor.language, anchor.paragraphIndex);
            try {
                const engine = window.AniLingoOfflineLibraryRepository;
                const saved = await repository.queueBookmarkUpsert({
                    chapterId,
                    positionPermille: position,
                    language: anchor.language,
                    paragraphIndex: anchor.paragraphIndex,
                    characterOffset: anchor.characterOffset,
                    anchorText: engine.createAnchorText(paragraph?.textContent),
                    label: ""
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

        // Shared card builder for notes belonging to another chapter, used both
        // for the "other chapters" toggle lists and for search results (which
        // reuse the same removal/edit wiring via [data-other-note-card]).
        const remoteNoteCard = (kind, note, variant) => {
            const isBookmark = kind === "bookmarks";
            const volumePrefix = note.volumeNumber ? `Band ${note.volumeNumber} · ` : "";
            const heading = `${volumePrefix}Kapitel ${note.chapterNumber}${note.chapterTitle ? ` · ${note.chapterTitle}` : ""}`;
            const { card, remove, edit } = createNoteCard({
                href: isBookmark
                    ? `/Novels/Read/${encodeURIComponent(note.chapterId)}?bookmark=${encodeURIComponent(note.id)}`
                    : `/Novels/Read/${encodeURIComponent(note.chapterId)}?highlight=${encodeURIComponent(note.id)}`,
                heading,
                title: note.title || (isBookmark
                    ? `Lesezeichen · ${Math.round((note.positionPermille || 0) / 10)}%`
                    : "Markierung"),
                detail: note.detail,
                removeAttribute: "data-remove-other-note",
                removeLabel: isBookmark ? "Lesezeichen entfernen" : "Markierung entfernen",
                editAttribute: "data-edit-other-note",
                editLabel: isBookmark ? "Lesezeichen umbenennen" : "Notiz bearbeiten",
                id: note.id
            });
            card.classList.add("novel-other-note");
            card.dataset.otherNoteCard = "";
            card.dataset.noteKind = kind;
            card.dataset.noteId = note.id;
            if (isBookmark) {
                card.dataset.bookmarkLabel = note.title || "";
                card.dataset.positionPermille = String(note.positionPermille || 0);
            } else {
                card.dataset.highlightNote = note.detail || "";
            }
            remove.dataset.noteKind = kind;
            if (edit) edit.dataset.noteKind = kind;
            if (isBookmark && note.color) card.style.setProperty("--bookmark-color", note.color);
            if (variant === "search") card.dataset.searchCard = "";
            return card;
        };

        const otherNoteCard = (kind, note) => remoteNoteCard(kind, note, "other");
        const searchNoteCard = (kind, note) => remoteNoteCard(kind, note, "search");

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

            const card = button.closest("[data-other-note-card]");
            const isSearchCard = card?.dataset.searchCard !== undefined;
            card?.remove();

            // Search results share the removal wiring but page independently,
            // so only the "other chapters" toggle list's offset bookkeeping
            // needs to shift back by one.
            if (!isSearchCard) {
                const count = shell.querySelector(`[data-other-notes-count="${kind}"]`);
                if (count) {
                    count.textContent = String(Math.max(0, Number(count.textContent || 0) - 1));
                }
                otherNotes[kind].offset = Math.max(0, otherNotes[kind].offset - 1);
            }
        };

        // ---- edit (rename bookmark / edit highlight note) ------------------

        const editBookmarkLabel = async button => {
            const bookmarkId = button.dataset.bookmarkId || button.dataset.noteId;
            const card = button.closest("article");
            if (!bookmarkId || !card) return;

            const currentLabel = card.dataset.bookmarkLabel || "";
            const answer = window.prompt("Name des Lesezeichens:", currentLabel);
            if (answer === null) return;
            const nextLabel = answer.trim();
            if (nextLabel === currentLabel) return;

            // A bookmark of the currently open chapter has every field
            // available locally, so its rename can go through the
            // offline-first queue like add/remove. A bookmark from another
            // chapter (renamed from the "other chapters"/search list) only
            // has its label/position cached in the DOM here; renaming that
            // one keeps using the existing endpoint's targeted field update
            // (it already requires network to have loaded that list at all).
            const known = chapterBookmarks.get(bookmarkId);

            try {
                let saved;
                if (known && repository) {
                    saved = await repository.queueBookmarkUpsert({
                        bookmarkId,
                        chapterId: known.chapterId || chapterId,
                        positionPermille: known.positionPermille,
                        language: known.language,
                        paragraphIndex: known.paragraphIndex,
                        characterOffset: known.characterOffset,
                        anchorText: known.anchorText,
                        style: known.style,
                        color: known.color,
                        label: nextLabel
                    });
                } else if (bookmarkLabelEndpoint) {
                    saved = await reader.postForm(bookmarkLabelEndpoint, data => {
                        data.set("bookmarkId", bookmarkId);
                        data.set("label", nextLabel);
                    });
                } else {
                    return;
                }

                const label = saved?.label ?? nextLabel;
                const position = Number(card.dataset.positionPermille || 0);
                const displayTitle = label || `Lesezeichen · ${Math.round(position / 10)}%`;

                shell.querySelectorAll(bookmarkCardSelector(bookmarkId)).forEach(match => {
                    match.dataset.bookmarkLabel = label;
                    const strong = match.querySelector("strong");
                    if (strong) strong.textContent = displayTitle;
                });

                if (known) {
                    known.label = label;
                    chapterBookmarks.set(bookmarkId, known);
                    renderBookmarkMarker(known);
                }

                reader.showToast("Lesezeichen umbenannt");
            } catch (error) {
                reader.showToast(error.message || "Lesezeichen konnte nicht umbenannt werden");
            }
        };

        const editHighlightNote = async button => {
            if (!highlightNoteEndpoint) return;
            const highlightId = button.dataset.highlightId || button.dataset.noteId;
            const card = button.closest("article");
            if (!highlightId || !card) return;

            const currentNote = card.dataset.highlightNote || "";
            const answer = window.prompt("Notiz zu dieser Markierung:", currentNote);
            if (answer === null) return;
            const nextNote = answer.trim();
            if (nextNote === currentNote) return;

            try {
                const saved = await reader.postForm(highlightNoteEndpoint, data => {
                    data.set("highlightId", highlightId);
                    data.set("note", nextNote);
                });
                const note = saved?.note ?? nextNote;

                shell.querySelectorAll(highlightCardSelector(highlightId)).forEach(match => {
                    match.dataset.highlightNote = note;
                    setCardDetailText(match, note);
                });

                reader.showToast("Notiz gespeichert");
            } catch (error) {
                reader.showToast(error.message || "Notiz konnte nicht gespeichert werden");
            }
        };

        // ---- bookmark selection & previous/next bookmark navigation --------

        const saveBookmarkAtSelection = async () => {
            if (!repository || !pendingSelection) return;
            const selected = { ...pendingSelection };

            try {
                const engine = window.AniLingoOfflineLibraryRepository;
                const paragraph = reader.paragraphAt(selected.language, selected.paragraphIndex);
                const saved = await repository.queueBookmarkUpsert({
                    chapterId,
                    positionPermille: reader.position.positionPermille(),
                    language: selected.language,
                    paragraphIndex: selected.paragraphIndex,
                    characterOffset: selected.startOffset,
                    anchorText: engine.createAnchorText(paragraph?.textContent),
                    label: ""
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

                window.getSelection()?.removeAllRanges();
                hideSelectionMenu();
                reader.showToast("Auswahl als Lesezeichen gespeichert");
            } catch (error) {
                reader.showToast(error.message || "Lesezeichen konnte nicht gespeichert werden");
            }
        };

        const goToAdjacentBookmark = async forward => {
            if (!adjacentBookmarkUrl) return;

            try {
                const result = await reader.getJson(reader.withQuery(adjacentBookmarkUrl, {
                    forward,
                    positionPermille: reader.position.positionPermille()
                }));

                if (!result?.found) {
                    reader.showToast("Keine Lesezeichen vorhanden");
                    return;
                }

                if (result.chapterId === chapterId) {
                    const local = chapterBookmarks.get(result.id);
                    if (local) {
                        reader.position.scrollTo(local);
                        reader.showToast(
                            local.anchorText || local.label ||
                            `Lesezeichen · ${Math.round(local.positionPermille / 10)}%`);
                    } else {
                        reader.position.scrollTo({
                            language: reader.anchorLanguage(),
                            paragraphIndex: null,
                            characterOffset: 0,
                            positionPermille: result.positionPermille
                        });
                        reader.showToast(result.label || "Lesezeichen");
                    }
                } else {
                    window.location.href =
                        `/Novels/Read/${encodeURIComponent(result.chapterId)}?bookmark=${encodeURIComponent(result.id)}`;
                }
            } catch {
                reader.showToast("Lesezeichen konnte nicht geladen werden");
            }
        };

        // ---- tabs (Current chapter / All chapters / Bookmarks / Highlights) --

        const notesTabs = Array.from(shell.querySelectorAll("[data-notes-tab]"));
        const notesSections = {
            bookmarks: shell.querySelector('[data-notes-section="bookmarks"]'),
            highlights: shell.querySelector('[data-notes-section="highlights"]')
        };
        let activeTab = "current";

        const applyTab = tab => {
            activeTab = tab;
            notesTabs.forEach(button => {
                button.setAttribute("aria-selected", button.dataset.notesTab === tab ? "true" : "false");
            });

            const showBookmarks = tab === "current" || tab === "all" || tab === "bookmarks";
            const showHighlights = tab === "current" || tab === "all" || tab === "highlights";
            if (notesSections.bookmarks) notesSections.bookmarks.hidden = !showBookmarks;
            if (notesSections.highlights) notesSections.highlights.hidden = !showHighlights;

            const showOtherChapters = tab !== "current";
            const visibleKinds = [
                ...(showBookmarks ? ["bookmarks"] : []),
                ...(showHighlights ? ["highlights"] : [])
            ];

            visibleKinds.forEach(kind => {
                const container = shell.querySelector(`[data-other-notes="${kind}"]`);
                if (!container) return;
                container.hidden = !showOtherChapters;
                if (!showOtherChapters) return;

                const toggle = shell.querySelector(`[data-other-notes-toggle="${kind}"]`);
                const list = shell.querySelector(`[data-other-note-list="${kind}"]`);
                if (!toggle || !list) return;

                if (toggle.getAttribute("aria-expanded") !== "true") {
                    toggle.setAttribute("aria-expanded", "true");
                    list.hidden = false;
                }
                if (!otherNotes[kind].loaded) {
                    void loadOtherNotes(kind);
                } else {
                    const more = shell.querySelector(`[data-other-notes-more="${kind}"]`);
                    if (more) more.hidden = !otherNotes[kind].hasMore;
                }
            });

            if (searchInput && normalizeText(searchInput.value)) {
                void runSearch();
            }
        };

        // ---- search (server-side, bounded, paged, profile-isolated) --------

        const searchInput = shell.querySelector("[data-notes-search]");
        const searchSection = shell.querySelector("[data-notes-search-section]");
        const searchList = shell.querySelector("[data-search-list]");
        const searchMoreButton = shell.querySelector("[data-search-more]");
        const searchStatus = shell.querySelector("[data-search-status]");

        let searchTimer = null;
        let searchRequestId = 0;
        const searchState = { query: "", kinds: [], offsets: {}, hasMore: {} };

        const searchKindsForTab = () => {
            if (activeTab === "bookmarks") return ["bookmarks"];
            if (activeTab === "highlights") return ["highlights"];
            return ["bookmarks", "highlights"];
        };

        const setSearchActive = active => {
            if (searchSection) searchSection.hidden = !active;
            if (!active) return;

            // While searching, the normal current/other-chapter blocks make way
            // for the results list; clearing the query restores them (applyTab
            // decides which of the two sections stay visible for the tab).
            if (notesSections.bookmarks) notesSections.bookmarks.hidden = true;
            if (notesSections.highlights) notesSections.highlights.hidden = true;
        };

        const runSearch = async () => {
            const query = normalizeText(searchInput?.value);
            if (!query) {
                setSearchActive(false);
                applyTab(activeTab);
                return;
            }

            if (!searchNotesUrl) return;
            setSearchActive(true);

            const requestId = ++searchRequestId;
            const kinds = searchKindsForTab();
            searchState.query = query;
            searchState.kinds = kinds;
            searchState.offsets = Object.fromEntries(kinds.map(kind => [kind, 0]));
            searchState.hasMore = Object.fromEntries(kinds.map(kind => [kind, false]));

            if (searchList) searchList.replaceChildren();
            if (searchMoreButton) searchMoreButton.hidden = true;
            if (searchStatus) searchStatus.textContent = "Suche läuft …";

            try {
                const pages = await Promise.all(kinds.map(kind =>
                    reader.getJson(reader.withQuery(searchNotesUrl, { kind, q: query, offset: 0 }))
                        .then(page => ({ kind, page }))));
                if (requestId !== searchRequestId) return;

                let total = 0;
                for (const { kind, page } of pages) {
                    for (const note of page.items || []) {
                        searchList?.append(searchNoteCard(kind, note));
                        total++;
                    }
                    searchState.offsets[kind] = page.nextOffset ?? 0;
                    searchState.hasMore[kind] = Boolean(page.hasMore);
                }

                if (searchMoreButton) {
                    searchMoreButton.hidden = !kinds.some(kind => searchState.hasMore[kind]);
                }
                if (searchStatus) {
                    searchStatus.textContent = total === 0 ? "Keine Treffer." : "";
                }
            } catch {
                if (requestId !== searchRequestId) return;
                if (searchStatus) searchStatus.textContent = "Suche fehlgeschlagen.";
            }
        };

        const loadMoreSearchResults = async () => {
            const kinds = searchState.kinds.filter(kind => searchState.hasMore[kind]);
            if (kinds.length === 0 || !searchState.query || !searchNotesUrl) return;

            if (searchMoreButton) searchMoreButton.disabled = true;
            try {
                const pages = await Promise.all(kinds.map(kind =>
                    reader.getJson(reader.withQuery(searchNotesUrl, {
                        kind,
                        q: searchState.query,
                        offset: searchState.offsets[kind]
                    })).then(page => ({ kind, page }))));

                for (const { kind, page } of pages) {
                    for (const note of page.items || []) {
                        searchList?.append(searchNoteCard(kind, note));
                    }
                    searchState.offsets[kind] = page.nextOffset ?? searchState.offsets[kind];
                    searchState.hasMore[kind] = Boolean(page.hasMore);
                }

                if (searchMoreButton) {
                    searchMoreButton.hidden = !searchState.kinds.some(kind => searchState.hasMore[kind]);
                }
            } finally {
                if (searchMoreButton) searchMoreButton.disabled = false;
            }
        };

        searchInput?.addEventListener("input", () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => { void runSearch(); }, 250);
        });

        // ---- events --------------------------------------------------------

        shell.addEventListener("click", event => {
            if (event.target.closest("[data-reader-bookmark]")) {
                void saveBookmark();
                return;
            }

            const localBookmark = event.target.closest("[data-local-bookmark-id]");
            if (localBookmark && chapterBookmarks.has(localBookmark.dataset.localBookmarkId)) {
                event.preventDefault();
                const target = chapterBookmarks.get(localBookmark.dataset.localBookmarkId);
                reader.position.scrollTo(target);
                if (target.anchorText) reader.showToast(target.anchorText);
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

            const editBookmarkButton = event.target.closest("[data-edit-bookmark-button]");
            if (editBookmarkButton) {
                void editBookmarkLabel(editBookmarkButton);
                return;
            }

            const editHighlightButton = event.target.closest("[data-edit-highlight-button]");
            if (editHighlightButton) {
                void editHighlightNote(editHighlightButton);
                return;
            }

            const editOther = event.target.closest("[data-edit-other-note]");
            if (editOther) {
                if (editOther.dataset.noteKind === "bookmarks") {
                    void editBookmarkLabel(editOther);
                } else {
                    void editHighlightNote(editOther);
                }
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

            const searchMore = event.target.closest("[data-search-more]");
            if (searchMore) {
                void loadMoreSearchResults();
                return;
            }

            const notesTab = event.target.closest("[data-notes-tab]");
            if (notesTab) {
                applyTab(notesTab.dataset.notesTab);
                return;
            }

            const bookmarkNav = event.target.closest("[data-bookmark-nav]");
            if (bookmarkNav) {
                void goToAdjacentBookmark(bookmarkNav.dataset.bookmarkNav === "next");
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
                return;
            }

            if (event.target.closest("[data-save-bookmark-selection]")) {
                void saveBookmarkAtSelection();
            }
        });

        // Marker clicks and selection-menu dismissal are handled page-wide.
        document.addEventListener("click", event => {
            const marker = event.target.closest("[data-bookmark-marker]");
            if (marker) {
                const target = chapterBookmarks.get(marker.dataset.bookmarkId);
                reader.position.scrollTo(target);
                if (target?.anchorText) reader.showToast(target.anchorText);
                return;
            }

            if (event.target.closest("[data-jump-context-dismiss]")) {
                event.target.closest("[data-jump-context]")?.remove();
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
                return;
            }

            if (event.ctrlKey || event.metaKey || event.altKey) return;
            if (event.target?.closest?.('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) {
                return;
            }

            const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
            switch (key) {
                case registry.shortcutKeys.bookmark:
                    event.preventDefault();
                    void saveBookmark();
                    break;
                case registry.shortcutKeys.notes:
                    event.preventDefault();
                    setNotesOpen(notes?.hidden !== false);
                    break;
                case registry.shortcutKeys.previousBookmark:
                    event.preventDefault();
                    void goToAdjacentBookmark(false);
                    break;
                case registry.shortcutKeys.nextBookmark:
                    event.preventDefault();
                    void goToAdjacentBookmark(true);
                    break;
                default:
                    break;
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
        applyTab("current");

        // The forced-jump context banner (server-rendered when the reader opens
        // straight onto a bookmark/highlight from elsewhere) fades on its own so
        // it does not linger over the text.
        const jumpContext = shell.querySelector("[data-jump-context]");
        if (jumpContext) {
            setTimeout(() => jumpContext.remove(), 6000);
        }

        return {
            renderAllHighlights
        };
    };
})();
