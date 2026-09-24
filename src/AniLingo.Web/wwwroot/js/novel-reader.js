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
    const hasTranslation = shell.dataset.hasTranslation === "true";

    let restoreComplete = false;
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

    const currentView = () => shell.dataset.view || "ja";

    const anchorLanguage = () => {
        const view = currentView();
        if (view === "de" && hasTranslation) return "de";
        return "ja";
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
        if (progressBar) {
            progressBar.style.width = (positionPermille() / 10) + "%";
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

    const applyHighlight = (language, paragraphIndex, start, end) => {
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
            range.surroundContents(mark);
        } catch {
            // Overlapping/nested highlights can make surroundContents invalid.
            // The annotation remains saved and visible in the notes panel.
        }
    };

    const applySavedHighlights = () => {
        const saved = Array.from(shell.querySelectorAll("[data-saved-highlight]"))
            .map(element => ({
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
                highlight.end);
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

    const postForm = async (form, mutate) => {
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

    const saveBookmark = async () => {
        if (!bookmarkForm) return;

        const anchor = currentAnchor();
        try {
            await postForm(bookmarkForm, data => {
                data.set("positionPermille", String(positionPermille()));
                data.set("language", anchor.language);
                data.set(
                    "paragraphIndex",
                    anchor.paragraphIndex == null ? "" : String(anchor.paragraphIndex));
                data.set("characterOffset", String(anchor.characterOffset));
                data.set("label", "");
            });
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
                saved?.endOffset ?? selected.endOffset);

            window.getSelection()?.removeAllRanges();
            hideSelectionMenu();
            showToast(note ? "Highlight and note saved" : "Highlight saved");
        } catch (error) {
            showToast(error.message || "Could not save highlight");
        }
    };

    document.addEventListener("click", event => {
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

    applySavedHighlights();
    requestAnimationFrame(() => requestAnimationFrame(restorePosition));
})();
