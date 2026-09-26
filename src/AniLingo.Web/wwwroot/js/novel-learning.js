// Novel reader learning integration (issues #147 / #233): wires the shared
// language inspector (window.AniLingoLanguageInspector, see language-inspector.js
// and docs/LEARNING_V2.md) into the novel reader's own selection menu and the
// furigana reader preference. This module never binds a second selection UI:
// the inspector partial is hosted without a selectionSurface, so its built-in
// floating "Look up" button stays inert and novel-annotations.js's existing
// selection menu is the only text-selection UI.
(() => {
    const registry = window.AniLingoNovelReader = window.AniLingoNovelReader || {};

    // A sentence boundary is the nearest terminator around the selection,
    // mirroring language-inspector.js's own (private) sentenceAround helper so
    // the "explain this sentence" context is consistent wherever it is built.
    const sentenceAround = (paragraphText, start, end) => {
        const terminators = /[。！？!?.\n]/;
        let from = start;
        while (from > 0 && !terminators.test(paragraphText[from - 1])) from--;
        let to = end;
        while (to < paragraphText.length && !terminators.test(paragraphText[to])) to++;
        if (to < paragraphText.length) to++;
        return paragraphText.slice(from, to).trim().slice(0, 500);
    };

    const offsetWithin = (element, node, offset) => {
        const range = document.createRange();
        range.selectNodeContents(element);
        range.setEnd(node, offset);
        return range.toString().length;
    };

    registry.learning = reader => {
        const { shell } = reader;
        const inspector = window.AniLingoLanguageInspector;
        const lookupButton = shell.querySelector("[data-language-lookup-selection]");
        const furiganaButton = shell.querySelector("[data-reader-furigana-toggle]");
        const settingsForm = shell.querySelector("[data-reader-settings-form]");

        if (lookupButton) {
            lookupButton.hidden = !inspector?.available;
        }

        // ---- selection capture (independent of novel-annotations.js's own
        // pendingSelection, which is a private closure) --------------------

        let lastSelection = null;

        const closestParagraph = node => {
            const element = node?.nodeType === Node.TEXT_NODE ? node.parentElement : node;
            return element?.closest?.("[data-reader-paragraph]") || null;
        };

        const captureSelection = () => {
            const selection = window.getSelection();
            if (!selection || selection.rangeCount !== 1 || selection.isCollapsed) {
                return;
            }

            const range = selection.getRangeAt(0);
            const paragraph = closestParagraph(range.startContainer);
            const text = selection.toString().trim();
            if (!paragraph ||
                paragraph !== closestParagraph(range.endContainer) ||
                !shell.contains(paragraph) ||
                !text ||
                text.length > 500) {
                return;
            }

            const start = offsetWithin(paragraph, range.startContainer, range.startOffset);
            const end = offsetWithin(paragraph, range.endContainer, range.endOffset);
            lastSelection = {
                text,
                language: paragraph.dataset.language || "ja",
                paragraph: Number(paragraph.dataset.index),
                sentence: sentenceAround(paragraph.textContent || "", start, end)
            };
        };

        document.addEventListener("mouseup", () => setTimeout(captureSelection, 0));
        document.addEventListener("touchend", () => setTimeout(captureSelection, 40), { passive: true });

        if (lookupButton && inspector?.available) {
            shell.addEventListener("click", event => {
                if (!event.target.closest("[data-language-lookup-selection]")) return;
                const selected = lastSelection;
                if (!selected) return;
                void inspector.open(selected.text, {
                    language: selected.language,
                    paragraph: selected.paragraph,
                    sentence: selected.sentence
                });
            });
        }

        // ---- tap a Japanese word to inspect just that token, where the
        // platform can segment words (Intl.Segmenter) ------------------------

        const segmenter = (() => {
            try {
                return "Segmenter" in Intl ? new Intl.Segmenter("ja", { granularity: "word" }) : null;
            } catch {
                return null;
            }
        })();

        const wordAt = (text, offset) => {
            for (const segment of segmenter.segment(text)) {
                const end = segment.index + segment.segment.length;
                if (offset >= segment.index && offset < end) {
                    return segment.isWordLike ? segment.segment : null;
                }
            }
            return null;
        };

        const caretOffset = (paragraph, x, y) => {
            let node = null;
            let offset = 0;
            if (document.caretPositionFromPoint) {
                const position = document.caretPositionFromPoint(x, y);
                if (!position) return null;
                node = position.offsetNode;
                offset = position.offset;
            } else if (document.caretRangeFromPoint) {
                const range = document.caretRangeFromPoint(x, y);
                if (!range) return null;
                node = range.startContainer;
                offset = range.startOffset;
            } else {
                return null;
            }

            if (!node || !paragraph.contains(node)) return null;
            return offsetWithin(paragraph, node, offset);
        };

        if (segmenter && inspector?.available) {
            shell.addEventListener("click", event => {
                if (event.target.closest(
                    "a, button, [data-language-inspect], [data-language-lookup-selection]")) {
                    return;
                }

                const paragraph = event.target.closest?.("[data-reader-paragraph][data-language=\"ja\"]");
                if (!paragraph) return;

                const selection = window.getSelection();
                if (selection && !selection.isCollapsed) return;

                const text = paragraph.textContent || "";
                const offset = caretOffset(paragraph, event.clientX, event.clientY);
                if (offset === null) return;

                const word = wordAt(text, offset);
                if (!word) return;

                void inspector.open(word, {
                    language: paragraph.dataset.language || "ja",
                    paragraph: Number(paragraph.dataset.index),
                    sentence: sentenceAround(text, offset, offset + word.length)
                });
            });
        }

        // ---- furigana: an existing ReaderPreferences field (saved at the
        // global default scope, like other reading-experience settings),
        // applied client-side without a reload. Computed readings are fetched
        // once from the bounded ?handler=Furigana endpoint (never on the
        // reader's own GET) and wrapped onto the live paragraph DOM by
        // character offset, the same technique novel-annotations.js already
        // uses for highlights, so it survives whatever marks exist. -------

        const furiganaUrl = shell.dataset.furiganaUrl || "";

        const wrapFuriganaSegments = (paragraph, segments) => {
            let cursor = 0;
            const ranges = [];
            for (const segment of segments) {
                const start = cursor;
                cursor += (segment.text || "").length;
                if (segment.reading) ranges.push({ start, end: cursor, reading: segment.reading });
            }
            if (ranges.length === 0) return;

            const walker = document.createTreeWalker(paragraph, NodeFilter.SHOW_TEXT);
            const nodes = [];
            let offset = 0;
            while (walker.nextNode()) {
                const node = walker.currentNode;
                nodes.push({ node, start: offset, end: offset + node.data.length });
                offset += node.data.length;
            }

            for (const { node, start, end } of nodes) {
                const overlapping = ranges
                    .filter(range => range.start < end && range.end > start)
                    .sort((a, b) => b.start - a.start);
                let head = node;
                for (const range of overlapping) {
                    const from = Math.max(range.start, start) - start;
                    const to = Math.min(range.end, end) - start;
                    if (to < head.data.length) head.splitText(to);
                    const middle = from > 0 ? head.splitText(from) : head;
                    const ruby = document.createElement("ruby");
                    ruby.className = "novel-computed-furigana";
                    const rt = document.createElement("rt");
                    rt.dataset.rt = range.reading;
                    middle.replaceWith(ruby);
                    ruby.append(middle, rt);
                }
            }
        };

        const applyFurigana = async () => {
            if (!furiganaUrl) return;
            try {
                const data = await reader.getJson(furiganaUrl);
                const paragraphs = data?.paragraphs || {};
                Object.keys(paragraphs).forEach(index => {
                    const paragraph = reader.paragraphAt("ja", Number(index));
                    if (paragraph && paragraph.dataset.furiganaApplied !== "true") {
                        wrapFuriganaSegments(paragraph, paragraphs[index]);
                        paragraph.dataset.furiganaApplied = "true";
                    }
                });
            } catch {
                reader.showToast("Furigana konnte nicht geladen werden");
            }
        };

        const removeFurigana = () => {
            shell.querySelectorAll("ruby.novel-computed-furigana").forEach(ruby => {
                ruby.replaceWith(ruby.firstChild);
            });
            shell.querySelectorAll("[data-reader-paragraph][data-language=\"ja\"]").forEach(paragraph => {
                paragraph.normalize();
                delete paragraph.dataset.furiganaApplied;
            });
        };

        if (shell.dataset.furiganaEnabled === "true") {
            void applyFurigana();
        }

        if (furiganaButton && settingsForm) {
            furiganaButton.addEventListener("click", async () => {
                const next = furiganaButton.getAttribute("aria-pressed") !== "true";
                furiganaButton.disabled = true;
                try {
                    await reader.postForm(settingsForm, data => {
                        data.set("changedKey", "furiganaEnabled");
                        data.set("FuriganaEnabled", next ? "true" : "false");
                        data.set("scope", "default");
                    });
                    furiganaButton.setAttribute("aria-pressed", next ? "true" : "false");
                    shell.dataset.furiganaEnabled = next ? "true" : "false";
                    if (next) {
                        await applyFurigana();
                    } else {
                        removeFurigana();
                    }
                } catch (error) {
                    reader.showToast(error.message || "Furigana konnte nicht gespeichert werden");
                } finally {
                    furiganaButton.disabled = false;
                }
            });
        }

        return {};
    };
})();
