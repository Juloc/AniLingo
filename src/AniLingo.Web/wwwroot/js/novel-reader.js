(() => {
    const shell = document.querySelector("[data-novel-reader]");
    if (!shell) return;

    const storage = {
        view: "anilingo.novel.view",
        size: "anilingo.novel.size",
        leading: "anilingo.novel.leading",
        width: "anilingo.novel.width",
        theme: "anilingo.novel.theme"
    };

    const progressForm = shell.querySelector("[data-progress-form]");
    const bookmarkForm = shell.querySelector("[data-bookmark-form]");
    const highlightForm = shell.querySelector("[data-highlight-form]");
    const progressBar = document.querySelector("[data-reading-progress]");
    const toast = shell.querySelector("[data-reader-toast]");
    const selectionMenu = shell.querySelector("[data-selection-menu]");
    const notes = shell.querySelector("[data-reader-notes]");
    const bookmarkList = shell.querySelector("[data-bookmark-list]");
    const highlightList = shell.querySelector("[data-highlight-list]");
    const bookmarkTracks = Array.from(shell.querySelectorAll("[data-bookmark-track]"));
    const railFill = shell.querySelector("[data-reader-rail-fill]");
    const percentOutput = shell.querySelector("[data-reader-percent]");
    const bookmarkButton = shell.querySelector("[data-reader-bookmark]");
    const noteCountBadge = shell.querySelector("[data-reader-note-count]");
    const currentBookmarkCountBadge = shell.querySelector("[data-current-bookmark-count]");
    const emptyBookmarks = shell.querySelector("[data-empty-bookmarks]");
    const emptyHighlights = shell.querySelector("[data-empty-highlights]");
    const removeBookmarkEndpoint = shell.querySelector("[data-remove-bookmark-endpoint]");
    const removeHighlightEndpoint = shell.querySelector("[data-remove-highlight-endpoint]");
    const chapterDrawer = shell.querySelector("[data-chapter-drawer]");
    const chapterDrawerBackdrop = shell.querySelector("[data-chapter-drawer-backdrop]");
    const chapterFilter = shell.querySelector("[data-chapter-filter]");
    const chapterList = shell.querySelector("[data-chapter-list]");
    const chapterDataElement = shell.querySelector("[data-chapter-data]");
    const translateForm = shell.querySelector("[data-translate-form]");
    const translationSlot = shell.querySelector("[data-translation-slot]");
    let hasTranslation = shell.dataset.hasTranslation === "true";
    const chapterBookmarks = new Map();

    let restoreComplete = false;
    let chapterRowsReady = false;
    let progressTimer = null;
    let toastTimer = null;
    let pendingSelection = null;
    let lastSentKey = "";
    let lastScrollY = window.scrollY;

    const normalizeText = value =>
        (value || "").replace(/\s+/g, " ").trim();

    const clamp = (value, min, max) =>
        Math.min(max, Math.max(min, value));

    const showToast = message => {
        if (!toast) return;
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.hidden = false;
        toastTimer = setTimeout(() => {
            toast.hidden = true;
        }, 2200);
    };

    const setBadgeCount = (element, count) => {
        if (!element) return;
        element.textContent = String(count);
        element.hidden = count <= 0;
    };

    const syncAnnotationUi = () => {
        const bookmarkCount =
            bookmarkList?.querySelectorAll("[data-bookmark-card]").length || 0;
        const highlightCount =
            highlightList?.querySelectorAll("[data-highlight-card]").length || 0;
        const currentBookmarkCount = chapterBookmarks.size;

        setBadgeCount(noteCountBadge, bookmarkCount + highlightCount);
        setBadgeCount(currentBookmarkCountBadge, currentBookmarkCount);

        emptyBookmarks?.classList.toggle("is-hidden", bookmarkCount > 0);
        emptyHighlights?.classList.toggle("is-hidden", highlightCount > 0);
        bookmarkButton?.classList.toggle("has-bookmarks", currentBookmarkCount > 0);
    };

    const currentView = () => shell.dataset.view || "ja";

    const anchorLanguage = () => {
        const view = currentView();
        if (view === "de" && hasTranslation) return "de";
        return "ja";
    };

    const syncLanguageControls = () => {
        shell.querySelectorAll("[data-reader-view]").forEach(button => {
            if (button.dataset.readerView !== "ja") {
                button.disabled = !hasTranslation;
            }
        });
    };

    const applyView = view => {
        const allowed = hasTranslation ? ["ja", "de", "both"] : ["ja"];
        const next = allowed.includes(view) ? view : "ja";
        shell.dataset.view = next;
        localStorage.setItem(storage.view, next);

        shell.querySelectorAll("[data-reader-view]").forEach(button => {
            button.setAttribute(
                "aria-pressed",
                button.dataset.readerView === next ? "true" : "false");
        });
    };

    const applyNumber = (name, value, min, max, unit) => {
        const number = clamp(Number(value), min, max);
        if (!Number.isFinite(number)) return null;
        document.documentElement.style.setProperty(name, number + unit);
        return number;
    };

    const themes = ["dark", "paper", "light"];
    const themeLabels = {
        dark: "Dark",
        paper: "Paper",
        light: "Light"
    };

    const applyTheme = theme => {
        const next = themes.includes(theme) ? theme : "dark";
        shell.dataset.theme = next;
        localStorage.setItem(storage.theme, next);
        shell.querySelectorAll("[data-reader-theme-label]").forEach(label => {
            label.textContent = themeLabels[next];
        });
    };

    const initialAnchorLanguage = shell.dataset.anchorLanguage || "ja";
    const forcedAnchor = shell.dataset.forceAnchor === "true";
    const storedView = localStorage.getItem(storage.view);
    const initialView = forcedAnchor
        ? initialAnchorLanguage
        : (storedView || initialAnchorLanguage);

    syncLanguageControls();
    applyView(initialView);
    applyTheme(localStorage.getItem(storage.theme) || "dark");

    const savedSize = localStorage.getItem(storage.size);
    if (savedSize) applyNumber("--novel-reader-size", savedSize, .9, 1.7, "rem");

    const savedLeading = localStorage.getItem(storage.leading);
    if (savedLeading) applyNumber("--novel-reader-leading", savedLeading, 1.45, 2.5, "");

    const savedWidth = localStorage.getItem(storage.width);
    if (savedWidth) applyNumber("--novel-reader-width", savedWidth, 560, 1040, "px");

    const paragraphsFor = language =>
        Array.from(shell.querySelectorAll(
            `[data-reader-paragraph][data-language="${language}"]`));

    const positionPermille = () => {
        const max = document.documentElement.scrollHeight - window.innerHeight;
        if (max <= 0) return 1000;
        return clamp(Math.round(window.scrollY / max * 1000), 0, 1000);
    };

    const currentAnchor = () => {
        const language = anchorLanguage();
        const paragraphs = paragraphsFor(language);
        if (paragraphs.length === 0) {
            return {
                language,
                paragraphIndex: null,
                characterOffset: 0
            };
        }

        const targetY = window.innerHeight * .28;
        let target = paragraphs[0];

        for (const paragraph of paragraphs) {
            const rect = paragraph.getBoundingClientRect();
            if (rect.top <= targetY) target = paragraph;
            if (rect.top <= targetY && rect.bottom >= targetY) {
                target = paragraph;
                break;
            }
            if (rect.top > targetY) break;
        }

        const rect = target.getBoundingClientRect();
        const textLength = target.textContent?.length || 0;
        const fraction = rect.height <= 0
            ? 0
            : clamp((targetY - rect.top) / rect.height, 0, 1);

        return {
            language,
            paragraphIndex: Number(target.dataset.index),
            characterOffset: Math.round(textLength * fraction)
        };
    };

    const updateProgressBar = () => {
        const progress = positionPermille();
        const percent = Math.round(progress / 10);

        if (progressBar) {
            progressBar.style.width = (progress / 10) + "%";
        }
        if (railFill) {
            railFill.style.height = (progress / 10) + "%";
        }
        if (percentOutput) {
            percentOutput.textContent = percent + "%";
        }
    };

    const sendProgress = () => {
        if (!progressForm || !restoreComplete) return;

        const position = positionPermille();
        const anchor = currentAnchor();
        const key = [
            position,
            anchor.language,
            anchor.paragraphIndex ?? "",
            anchor.characterOffset
        ].join(":");

        if (key === lastSentKey) return;
        lastSentKey = key;

        const data = new FormData(progressForm);
        data.set("positionPermille", String(position));
        data.set("anchorLanguage", anchor.language);
        data.set(
            "anchorParagraphIndex",
            anchor.paragraphIndex == null ? "" : String(anchor.paragraphIndex));
        data.set("anchorOffset", String(anchor.characterOffset));

        fetch(progressForm.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" },
            keepalive: true
        }).catch(() => {});
    };

    const findResumeParagraph = () => {
        const language = initialAnchorLanguage === "de" && hasTranslation
            ? "de"
            : "ja";
        const paragraphs = paragraphsFor(language);
        const requestedIndex = Number(shell.dataset.anchorParagraph);
        const anchorText = normalizeText(shell.dataset.anchorText);

        if (Number.isInteger(requestedIndex) &&
            requestedIndex >= 0 &&
            requestedIndex < paragraphs.length) {
            const candidate = paragraphs[requestedIndex];
            if (!anchorText ||
                normalizeText(candidate.textContent).startsWith(anchorText)) {
                return candidate;
            }
        }

        if (anchorText) {
            return paragraphs.find(paragraph =>
                normalizeText(paragraph.textContent).startsWith(anchorText)) || null;
        }

        return null;
    };

    const restorePosition = () => {
        const anchorParagraph = findResumeParagraph();
        const initialOffset = Number(shell.dataset.anchorOffset || 0);

        if (anchorParagraph) {
            const length = anchorParagraph.textContent?.length || 0;
            const fraction = length <= 0 ? 0 : clamp(initialOffset / length, 0, 1);
            const rect = anchorParagraph.getBoundingClientRect();
            const top =
                window.scrollY +
                rect.top +
                rect.height * fraction -
                window.innerHeight * .28;

            window.scrollTo({ top: Math.max(0, top), behavior: "auto" });
        } else {
            const initial = Number(shell.dataset.progressPermille || 0);
            const max = document.documentElement.scrollHeight - window.innerHeight;
            if (initial > 5 && max > 0) {
                window.scrollTo({
                    top: max * initial / 1000,
                    behavior: "auto"
                });
            }
        }

        updateProgressBar();
        setTimeout(() => {
            restoreComplete = true;
            lastScrollY = window.scrollY;
        }, 50);
    };

    const textNodes = root => {
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode: node =>
                    node.textContent?.length
                        ? NodeFilter.FILTER_ACCEPT
                        : NodeFilter.FILTER_REJECT
            });

        const nodes = [];
        let node;
        while ((node = walker.nextNode())) nodes.push(node);
        return nodes;
    };

    const absoluteOffset = (root, targetNode, targetOffset) => {
        let offset = 0;
        for (const node of textNodes(root)) {
            if (node === targetNode) return offset + targetOffset;
            offset += node.textContent?.length || 0;
        }
        return offset;
    };

    const locateOffset = (root, wanted) => {
        let remaining = clamp(wanted, 0, root.textContent?.length || 0);
        const nodes = textNodes(root);

        for (const node of nodes) {
            const length = node.textContent?.length || 0;
            if (remaining <= length) {
                return { node, offset: remaining };
            }
            remaining -= length;
        }

        const last = nodes[nodes.length - 1];
        return last
            ? { node: last, offset: last.textContent?.length || 0 }
            : null;
    };

    const applyHighlight = (language, paragraphIndex, start, end, highlightId = "") => {
        const paragraph = shell.querySelector(
            `[data-reader-paragraph][data-language="${language}"][data-index="${paragraphIndex}"]`);
        if (!paragraph || end <= start) return;

        const startPoint = locateOffset(paragraph, start);
        const endPoint = locateOffset(paragraph, end);
        if (!startPoint || !endPoint) return;

        try {
            const range = document.createRange();
            range.setStart(startPoint.node, startPoint.offset);
            range.setEnd(endPoint.node, endPoint.offset);

            const mark = document.createElement("mark");
            mark.className = "novel-highlight";
            if (highlightId) {
                mark.dataset.highlightId = highlightId;
            }
            range.surroundContents(mark);
        } catch {
            // Overlapping/nested highlights can make surroundContents invalid.
            // The annotation remains saved and visible in the notes panel.
        }
    };

    const applySavedHighlights = () => {
        const saved = Array.from(shell.querySelectorAll("[data-saved-highlight]"))
            .map(element => ({
                id: element.dataset.highlightId || "",
                language: element.dataset.language || "ja",
                paragraph: Number(element.dataset.paragraph),
                start: Number(element.dataset.start),
                end: Number(element.dataset.end)
            }))
            .sort((a, b) =>
                a.language.localeCompare(b.language) ||
                a.paragraph - b.paragraph ||
                b.start - a.start);

        for (const highlight of saved) {
            applyHighlight(
                highlight.language,
                highlight.paragraph,
                highlight.start,
                highlight.end,
                highlight.id);
        }
    };

    const closestParagraph = node => {
        const element = node?.nodeType === Node.TEXT_NODE
            ? node.parentElement
            : node;
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

        const start = absoluteOffset(
            startParagraph,
            range.startContainer,
            range.startOffset);
        const end = absoluteOffset(
            startParagraph,
            range.endContainer,
            range.endOffset);

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

    const postForm = async (form, mutate = () => {}) => {
        const data = new FormData(form);
        mutate(data);
        const response = await fetch(form.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" }
        });

        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || "Request failed.");
        }

        return response.headers
            .get("content-type")
            ?.includes("application/json")
            ? response.json()
            : null;
    };

    const escapeAttributeSelector = value => value || "";

    const bookmarkFromElement = element => ({
        id: element.dataset.bookmarkId || "",
        chapterId: element.dataset.chapterId || shell.dataset.chapterId || "",
        positionPermille: Number(element.dataset.position || 0),
        language: element.dataset.language || "ja",
        paragraphIndex:
            element.dataset.paragraph === "" ||
            element.dataset.paragraph == null
                ? null
                : Number(element.dataset.paragraph),
        characterOffset: Number(element.dataset.offset || 0),
        anchorText: element.dataset.anchorText || "",
        label: ""
    });

    const jumpToBookmark = bookmark => {
        if (!bookmark) return;

        if (currentView() !== "both") {
            applyView(
                bookmark.language === "de" && hasTranslation
                    ? "de"
                    : "ja");
        }

        requestAnimationFrame(() => {
            const language =
                bookmark.language === "de" && hasTranslation
                    ? "de"
                    : "ja";
            const paragraph =
                bookmark.paragraphIndex == null
                    ? null
                    : shell.querySelector(
                        `[data-reader-paragraph][data-language="${language}"][data-index="${bookmark.paragraphIndex}"]`);

            if (paragraph) {
                const length = paragraph.textContent?.length || 0;
                const fraction =
                    length <= 0
                        ? 0
                        : clamp(bookmark.characterOffset / length, 0, 1);
                const rect = paragraph.getBoundingClientRect();
                const top =
                    window.scrollY +
                    rect.top +
                    rect.height * fraction -
                    window.innerHeight * .28;

                window.scrollTo({
                    top: Math.max(0, top),
                    behavior: "smooth"
                });
                return;
            }

            const max =
                document.documentElement.scrollHeight - window.innerHeight;
            window.scrollTo({
                top: Math.max(0, max * bookmark.positionPermille / 1000),
                behavior: "smooth"
            });
        });
    };

    const renderBookmarkRailMarker = bookmark => {
        if (!bookmark.id) return;

        for (const track of bookmarkTracks) {
            track.querySelector(
                `[data-bookmark-marker][data-bookmark-id="${escapeAttributeSelector(bookmark.id)}"]`)
                ?.remove();

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

            marker.innerHTML = "<span></span>";
            marker.title = bookmark.anchorText
                ? `Lesezeichen · ${bookmark.anchorText}`
                : `Lesezeichen · ${Math.round(bookmark.positionPermille / 10)}%`;
            marker.setAttribute("aria-label", marker.title);
            track.append(marker);
        }
    };

    const renderBookmarkCard = bookmark => {
        if (!bookmarkList || !bookmark?.id) return;

        bookmarkList
            .querySelector(
                `[data-bookmark-card][data-bookmark-id="${escapeAttributeSelector(bookmark.id)}"]`)
            ?.remove();

        const card = document.createElement("article");
        card.className = "novel-note-card";
        card.dataset.bookmarkCard = "";
        card.dataset.bookmarkId = bookmark.id;

        const link = document.createElement("a");
        link.href =
            `/Novels/Read/${encodeURIComponent(bookmark.chapterId)}?bookmark=${encodeURIComponent(bookmark.id)}`;
        if (bookmark.chapterId === shell.dataset.chapterId) {
            link.dataset.localBookmarkId = bookmark.id;
        }

        const strong = document.createElement("strong");
        strong.textContent =
            bookmark.label ||
            `Bookmark · ${Math.round(bookmark.positionPermille / 10)}%`;
        link.append(strong);

        if (bookmark.anchorText) {
            const excerpt = document.createElement("span");
            excerpt.textContent = bookmark.anchorText;
            link.append(excerpt);
        }

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "novel-note-remove";
        remove.dataset.removeBookmarkButton = "";
        remove.dataset.bookmarkId = bookmark.id;
        remove.textContent = "Remove";
        remove.setAttribute("aria-label", "Remove bookmark");

        card.append(link, remove);
        bookmarkList.prepend(card);
    };

    const renderHighlightCard = highlight => {
        if (!highlightList || !highlight?.id) return;

        highlightList
            .querySelector(
                `[data-highlight-card][data-highlight-id="${escapeAttributeSelector(highlight.id)}"]`)
            ?.remove();

        const card = document.createElement("article");
        card.className = "novel-note-card";
        card.dataset.highlightCard = "";
        card.dataset.highlightId = highlight.id;

        const link = document.createElement("a");
        link.href =
            `/Novels/Read/${encodeURIComponent(highlight.chapterId || shell.dataset.chapterId)}`;

        const strong = document.createElement("strong");
        strong.textContent = highlight.text || "Highlight";
        link.append(strong);

        if (highlight.note) {
            const note = document.createElement("span");
            note.textContent = highlight.note;
            link.append(note);
        }

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "novel-note-remove";
        remove.dataset.removeHighlightButton = "";
        remove.dataset.highlightId = highlight.id;
        remove.textContent = "Remove";
        remove.setAttribute("aria-label", "Remove highlight");

        card.append(link, remove);
        highlightList.prepend(card);
    };

    const removeHighlightMark = highlightId => {
        shell.querySelectorAll(
            `mark[data-highlight-id="${escapeAttributeSelector(highlightId)}"]`)
            .forEach(mark => {
                const parent = mark.parentNode;
                if (!parent) return;
                while (mark.firstChild) {
                    parent.insertBefore(mark.firstChild, mark);
                }
                mark.remove();
                parent.normalize();
            });
    };

    const removeBookmarkUi = bookmarkId => {
        shell.querySelectorAll("[data-bookmark-card]").forEach(card => {
            if (card.dataset.bookmarkId === bookmarkId) card.remove();
        });
        shell.querySelectorAll("[data-saved-bookmark]").forEach(element => {
            if (element.dataset.bookmarkId === bookmarkId) element.remove();
        });
        bookmarkTracks.forEach(track => {
            track.querySelectorAll("[data-bookmark-marker]").forEach(marker => {
                if (marker.dataset.bookmarkId === bookmarkId) marker.remove();
            });
        });
        chapterBookmarks.delete(bookmarkId);
        syncAnnotationUi();
    };

    const removeHighlightUi = highlightId => {
        shell.querySelectorAll("[data-highlight-card]").forEach(card => {
            if (card.dataset.highlightId === highlightId) card.remove();
        });
        shell.querySelectorAll("[data-saved-highlight]").forEach(element => {
            if (element.dataset.highlightId === highlightId) element.remove();
        });
        removeHighlightMark(highlightId);
        syncAnnotationUi();
    };

    const removeBookmark = async bookmarkId => {
        if (!removeBookmarkEndpoint || !bookmarkId) return;
        try {
            await postForm(removeBookmarkEndpoint, data => {
                data.set("bookmarkId", bookmarkId);
            });
            removeBookmarkUi(bookmarkId);
            showToast("Bookmark removed");
        } catch (error) {
            showToast(error.message || "Could not remove bookmark");
        }
    };

    const removeHighlight = async highlightId => {
        if (!removeHighlightEndpoint || !highlightId) return;
        try {
            await postForm(removeHighlightEndpoint, data => {
                data.set("highlightId", highlightId);
            });
            removeHighlightUi(highlightId);
            showToast("Highlight removed");
        } catch (error) {
            showToast(error.message || "Could not remove highlight");
        }
    };

    const saveBookmark = async () => {
        if (!bookmarkForm) return;

        const anchor = currentAnchor();
        const position = positionPermille();
        try {
            const saved = await postForm(bookmarkForm, data => {
                data.set("positionPermille", String(position));
                data.set("language", anchor.language);
                data.set(
                    "paragraphIndex",
                    anchor.paragraphIndex == null ? "" : String(anchor.paragraphIndex));
                data.set("characterOffset", String(anchor.characterOffset));
                data.set("label", "");
            });

            const bookmark = {
                ...saved,
                positionPermille:
                    saved?.positionPermille ?? position,
                language:
                    saved?.language ?? anchor.language,
                paragraphIndex:
                    saved?.paragraphIndex ?? anchor.paragraphIndex,
                characterOffset:
                    saved?.characterOffset ?? anchor.characterOffset,
                anchorText:
                    saved?.anchorText ?? "",
                label:
                    saved?.label ?? ""
            };

            renderBookmarkCard(bookmark);
            if (bookmark.chapterId === shell.dataset.chapterId) {
                chapterBookmarks.set(bookmark.id, bookmark);
                renderBookmarkRailMarker(bookmark);
            }
            syncAnnotationUi();
            showToast("Bookmark saved");
        } catch (error) {
            showToast(error.message || "Could not save bookmark");
        }
    };

    const saveHighlight = async withNote => {
        if (!highlightForm || !pendingSelection) return;

        let note = "";
        if (withNote) {
            note = window.prompt("Note for this highlight:", "") ?? "";
        }

        const selected = { ...pendingSelection };
        try {
            const saved = await postForm(highlightForm, data => {
                data.set("language", selected.language);
                data.set("paragraphIndex", String(selected.paragraphIndex));
                data.set("startOffset", String(selected.startOffset));
                data.set("endOffset", String(selected.endOffset));
                data.set("note", note);
            });

            applyHighlight(
                saved?.language || selected.language,
                saved?.paragraphIndex ?? selected.paragraphIndex,
                saved?.startOffset ?? selected.startOffset,
                saved?.endOffset ?? selected.endOffset,
                saved?.id || "");

            if (saved) {
                renderHighlightCard(saved);
                syncAnnotationUi();
            }

            window.getSelection()?.removeAllRanges();
            hideSelectionMenu();
            showToast(note ? "Highlight and note saved" : "Highlight saved");
        } catch (error) {
            showToast(error.message || "Could not save highlight");
        }
    };

    const ensureChapterRows = () => {
        if (chapterRowsReady || !chapterList || !chapterDataElement) return;

        let chapters = [];
        try {
            chapters = JSON.parse(chapterDataElement.textContent || "[]");
        } catch {
            chapters = [];
        }

        const fragment = document.createDocumentFragment();

        for (const chapter of chapters) {
            const link = document.createElement("a");
            link.className = [
                "novel-drawer-row",
                chapter.isCurrent
                    ? "current"
                    : chapter.isEarlier
                        ? "earlier"
                        : ""
            ].filter(Boolean).join(" ");
            link.href = `/Novels/Read/${encodeURIComponent(chapter.id)}`;
            link.dataset.chapterRow = "";
            link.dataset.chapterSearch =
                normalizeText(`${chapter.number} ${chapter.title}`)
                    .toLocaleLowerCase();

            const number = document.createElement("span");
            number.className = "novel-drawer-number";
            number.textContent = String(chapter.number);

            const title = document.createElement("span");
            title.className = "novel-drawer-title";
            title.lang = "ja";
            title.textContent = chapter.title;

            const lang = document.createElement("span");
            lang.className = "novel-drawer-lang";
            lang.textContent = chapter.hasTranslation ? "DE" : "";

            const current = document.createElement("span");
            if (chapter.isCurrent) {
                current.className = "novel-drawer-current-mark";
                current.setAttribute("aria-label", "Aktuelles Kapitel");
            }

            link.append(number, title, lang, current);
            fragment.append(link);
        }

        chapterList.append(fragment);
        chapterRowsReady = true;
    };

    const setChapterDrawer = open => {
        if (!chapterDrawer || !chapterDrawerBackdrop) return;

        if (open) {
            ensureChapterRows();
        }

        chapterDrawer.hidden = !open;
        chapterDrawerBackdrop.hidden = !open;
        shell.classList.toggle("chapter-drawer-open", open);
        document.documentElement.classList.toggle("novel-drawer-lock", open);

        if (open) {
            requestAnimationFrame(() => {
                chapterFilter?.focus({ preventScroll: true });
                chapterDrawer.querySelector(".novel-drawer-row.current")
                    ?.scrollIntoView({ block: "center" });
            });
        }
    };

    const filterChapters = query => {
        const needle = normalizeText(query).toLocaleLowerCase();
        shell.querySelectorAll("[data-chapter-row]").forEach(row => {
            row.hidden =
                needle.length > 0 &&
                !(row.dataset.chapterSearch || "").includes(needle);
        });
    };

    const installGermanParagraphs = paragraphs => {
        if (!Array.isArray(paragraphs) || paragraphs.length === 0) return;

        const content = shell.querySelector(".novel-reader-content");
        if (!content) return;

        paragraphs.forEach((text, index) => {
            let segment = content.querySelector(
                `[data-reader-segment="${index}"]`);

            if (!segment) {
                segment = document.createElement("section");
                segment.className = "novel-reader-segment";
                segment.dataset.readerSegment = String(index);
                content.append(segment);
            }

            let paragraph = segment.querySelector(
                '[data-reader-paragraph][data-language="de"]');

            if (!paragraph) {
                paragraph = document.createElement("p");
                paragraph.className = "novel-reader-paragraph de";
                paragraph.lang = "de";
                paragraph.dataset.readerParagraph = "";
                paragraph.dataset.language = "de";
                paragraph.dataset.index = String(index);
                segment.append(paragraph);
            }

            paragraph.textContent = text;
        });

        hasTranslation = true;
        shell.dataset.hasTranslation = "true";
        syncLanguageControls();

        const state = translationSlot?.querySelector(".novel-translation-state");
        if (state) {
            state.classList.add("ready");
            state.textContent = "Deutsch bereit";
        }

        translateForm?.remove();
    };

    const waitForTranslation = async () => {
        for (let attempt = 0; attempt < 45; attempt++) {
            await new Promise(resolve => setTimeout(resolve, 2000));

            try {
                const response = await fetch(
                    `${window.location.pathname}?handler=TranslationStatus`,
                    {
                        credentials: "same-origin",
                        cache: "no-store",
                        headers: { "X-Requested-With": "fetch" }
                    });

                if (!response.ok) continue;
                const result = await response.json();

                if (result?.status === "ready") {
                    installGermanParagraphs(result.paragraphs);
                    showToast("Deutsche Übersetzung ist bereit");
                    return;
                }
            } catch {
                // Keep the reader usable if a background status request fails.
            }
        }
    };

    const queueTranslation = async form => {
        const button = form?.querySelector("button");
        if (!form || !button) return;

        button.disabled = true;
        const previous = button.textContent;
        button.textContent = "Startet…";

        try {
            const result = await postForm(form);

            if (result?.status === "ready") {
                const statusResponse = await fetch(
                    `${window.location.pathname}?handler=TranslationStatus`,
                    {
                        credentials: "same-origin",
                        cache: "no-store",
                        headers: { "X-Requested-With": "fetch" }
                    });
                if (statusResponse.ok) {
                    const status = await statusResponse.json();
                    installGermanParagraphs(status.paragraphs);
                }
                return;
            }

            const state = translationSlot?.querySelector(".novel-translation-state");
            if (state) {
                state.textContent = "Übersetzung läuft";
            }

            button.textContent = "Läuft";
            showToast("Übersetzung gestartet");
            void waitForTranslation();
        } catch (error) {
            button.disabled = false;
            button.textContent = previous;
            showToast(error.message || "Übersetzung konnte nicht gestartet werden");
        }
    };

    document.addEventListener("click", event => {
        if (event.target.closest("[data-reader-chapters-toggle]")) {
            setChapterDrawer(true);
            return;
        }

        if (event.target.closest("[data-chapter-drawer-close]") ||
            event.target.closest("[data-chapter-drawer-backdrop]")) {
            setChapterDrawer(false);
            return;
        }

        const viewButton = event.target.closest("[data-reader-view]");
        if (viewButton) {
            applyView(viewButton.dataset.readerView);
            return;
        }

        const adjust = event.target.closest("[data-reader-adjust]");
        if (adjust) {
            const action = adjust.dataset.readerAdjust;
            if (action === "size-up" || action === "size-down") {
                const current =
                    parseFloat(getComputedStyle(document.documentElement)
                        .getPropertyValue("--novel-reader-size")) || 1.08;
                const value = applyNumber(
                    "--novel-reader-size",
                    current + (action === "size-up" ? .08 : -.08),
                    .9,
                    1.7,
                    "rem");
                if (value != null) localStorage.setItem(storage.size, value);
            } else if (action === "leading") {
                const current =
                    parseFloat(getComputedStyle(document.documentElement)
                        .getPropertyValue("--novel-reader-leading")) || 1.9;
                const value = current >= 2.35 ? 1.55 : current + .15;
                applyNumber("--novel-reader-leading", value, 1.45, 2.5, "");
                localStorage.setItem(storage.leading, value);
            } else if (action === "width") {
                const current =
                    parseFloat(getComputedStyle(document.documentElement)
                        .getPropertyValue("--novel-reader-width")) || 760;
                const value = current >= 980 ? 620 : current + 120;
                applyNumber("--novel-reader-width", value, 560, 1040, "px");
                localStorage.setItem(storage.width, value);
            }
            return;
        }

        if (event.target.closest("[data-reader-theme]")) {
            const current = shell.dataset.theme || "dark";
            const index = themes.indexOf(current);
            applyTheme(themes[(index + 1) % themes.length]);
            return;
        }

        if (event.target.closest("[data-reader-focus]")) {
            shell.classList.toggle("reader-focus");
            return;
        }

        if (event.target.closest("[data-reader-bookmark]")) {
            saveBookmark();
            return;
        }

        const bookmarkMarker = event.target.closest("[data-bookmark-marker]");
        if (bookmarkMarker) {
            jumpToBookmark(chapterBookmarks.get(bookmarkMarker.dataset.bookmarkId));
            return;
        }

        const localBookmarkLink = event.target.closest("[data-local-bookmark-id]");
        if (localBookmarkLink) {
            event.preventDefault();
            jumpToBookmark(chapterBookmarks.get(localBookmarkLink.dataset.localBookmarkId));
            return;
        }

        const removeBookmarkButton = event.target.closest("[data-remove-bookmark-button]");
        if (removeBookmarkButton) {
            removeBookmark(removeBookmarkButton.dataset.bookmarkId);
            return;
        }

        const removeHighlightButton = event.target.closest("[data-remove-highlight-button]");
        if (removeHighlightButton) {
            removeHighlight(removeHighlightButton.dataset.highlightId);
            return;
        }

        if (event.target.closest("[data-reader-notes-toggle]")) {
            if (notes) notes.hidden = false;
            shell.classList.add("notes-open");
            return;
        }

        if (event.target.closest("[data-reader-notes-close]")) {
            if (notes) notes.hidden = true;
            shell.classList.remove("notes-open");
            return;
        }

        if (event.target.closest("[data-save-highlight-note]")) {
            saveHighlight(true);
            return;
        }

        if (event.target.closest("[data-save-highlight]")) {
            saveHighlight(false);
            return;
        }

        if (selectionMenu &&
            !selectionMenu.hidden &&
            !event.target.closest("[data-selection-menu]")) {
            setTimeout(captureSelection, 0);
        }
    });

    document.addEventListener("submit", event => {
        const bookmarkRemoveForm = event.target.closest("[data-remove-bookmark-form]");
        if (bookmarkRemoveForm) {
            event.preventDefault();
            removeBookmark(
                bookmarkRemoveForm.querySelector('[name="bookmarkId"]')?.value);
            return;
        }

        const highlightRemoveForm = event.target.closest("[data-remove-highlight-form]");
        if (highlightRemoveForm) {
            event.preventDefault();
            removeHighlight(
                highlightRemoveForm.querySelector('[name="highlightId"]')?.value);
            return;
        }

        const translationForm = event.target.closest("[data-translate-form]");
        if (translationForm) {
            event.preventDefault();
            queueTranslation(translationForm);
        }
    });

    chapterFilter?.addEventListener("input", event => {
        filterChapters(event.target.value || "");
    });

    document.addEventListener("keydown", event => {
        if (event.key === "Escape") {
            if (chapterDrawer && !chapterDrawer.hidden) {
                setChapterDrawer(false);
                return;
            }
            if (notes && !notes.hidden) {
                notes.hidden = true;
                shell.classList.remove("notes-open");
            }
        }
    });

    document.addEventListener("mouseup", () => {
        setTimeout(captureSelection, 0);
    });

    document.addEventListener("touchend", () => {
        setTimeout(captureSelection, 40);
    }, { passive: true });

    window.addEventListener("scroll", () => {
        updateProgressBar();

        clearTimeout(progressTimer);
        progressTimer = setTimeout(sendProgress, 700);

        const toolbar = shell.querySelector(".novel-reader-toolbar");
        const y = window.scrollY;
        if (toolbar && !shell.classList.contains("notes-open")) {
            if (y > lastScrollY + 16 && y > 160) {
                toolbar.classList.add("toolbar-hidden");
            } else if (y < lastScrollY - 10) {
                toolbar.classList.remove("toolbar-hidden");
            }
        }
        lastScrollY = y;
    }, { passive: true });

    window.addEventListener("pagehide", sendProgress);

    shell.querySelectorAll("[data-saved-bookmark]").forEach(element => {
        const bookmark = bookmarkFromElement(element);
        if (!bookmark.id) return;
        chapterBookmarks.set(bookmark.id, bookmark);
        renderBookmarkRailMarker(bookmark);
    });
    syncAnnotationUi();
    applySavedHighlights();
    requestAnimationFrame(() => requestAnimationFrame(restorePosition));
})();
