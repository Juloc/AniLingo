// Novel reader position: paragraph anchors, resume/jump scrolling, the thin
// progress indicators and debounced progress saves for continuous reading.
// Paged-mode progress is sent by reader-personalization.js.
(() => {
    const registry = window.AniLingoNovelReader = window.AniLingoNovelReader || {};

    registry.position = reader => {
        const { shell, clamp, normalizeText } = reader;
        const progressForm = shell.querySelector("[data-progress-form]");
        const progressBar = document.querySelector("[data-reading-progress]");
        const railFill = shell.querySelector("[data-reader-rail-fill]");
        const percentOutput = shell.querySelector("[data-reader-percent]");

        let restoreComplete = false;
        let progressTimer = null;
        let lastSentKey = "";

        const anchorLine = () => window.innerHeight * .28;

        const positionPermille = () => {
            const max = document.documentElement.scrollHeight - window.innerHeight;
            if (max <= 0) return 1000;
            return clamp(Math.round(window.scrollY / max * 1000), 0, 1000);
        };

        const currentAnchor = () => {
            const language = reader.anchorLanguage();
            const paragraphs = reader.paragraphsFor(language);
            if (paragraphs.length === 0) {
                return { language, paragraphIndex: null, characterOffset: 0 };
            }

            const targetY = anchorLine();
            let target = paragraphs[0];

            for (const paragraph of paragraphs) {
                const rect = paragraph.getBoundingClientRect();
                if (rect.top <= targetY) target = paragraph;
                if (rect.top <= targetY && rect.bottom >= targetY) break;
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
            if (progressBar) progressBar.style.width = (progress / 10) + "%";
            if (railFill) railFill.style.height = (progress / 10) + "%";
            if (percentOutput) percentOutput.textContent = Math.round(progress / 10) + "%";
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
            }).catch(() => {
                // A failed save is retried with the next scroll pause.
                lastSentKey = "";
            });
        };

        const scrollToParagraph = (paragraph, characterOffset, behavior) => {
            const length = paragraph.textContent?.length || 0;
            const fraction = length <= 0 ? 0 : clamp(characterOffset / length, 0, 1);
            const rect = paragraph.getBoundingClientRect();
            const top = window.scrollY + rect.top + rect.height * fraction - anchorLine();
            window.scrollTo({ top: Math.max(0, top), behavior });
        };

        const scrollToPermille = (permille, behavior) => {
            const max = document.documentElement.scrollHeight - window.innerHeight;
            if (max <= 0) return;
            window.scrollTo({ top: Math.max(0, max * permille / 1000), behavior });
        };

        // The saved paragraph index is trusted only while its text still starts
        // with the stored anchor text; otherwise the anchor text is searched.
        const findResumeParagraph = () => {
            const language = shell.dataset.anchorLanguage === "de" && reader.hasTranslation()
                ? "de"
                : "ja";
            const paragraphs = reader.paragraphsFor(language);
            const requestedIndex = shell.dataset.anchorParagraph === ""
                ? NaN
                : Number(shell.dataset.anchorParagraph);
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

        const restore = () => {
            const anchorParagraph = findResumeParagraph();
            if (anchorParagraph) {
                scrollToParagraph(
                    anchorParagraph,
                    Number(shell.dataset.anchorOffset || 0),
                    "auto");
            } else {
                const initial = Number(shell.dataset.progressPermille || 0);
                if (initial > 5) scrollToPermille(initial, "auto");
            }
            updateProgressBar();
        };

        // Jump to a bookmark/highlight anchor of the current chapter.
        const scrollTo = target => {
            if (!target) return;
            const language = target.language === "de" && reader.hasTranslation()
                ? "de"
                : "ja";

            if (reader.currentView() !== "both") {
                reader.applyView(language);
            }

            requestAnimationFrame(() => {
                const paragraph = target.paragraphIndex == null
                    ? null
                    : reader.paragraphAt(language, target.paragraphIndex);

                if (paragraph) {
                    scrollToParagraph(paragraph, target.characterOffset || 0, "smooth");
                } else {
                    scrollToPermille(target.positionPermille || 0, "smooth");
                }
            });
        };

        window.addEventListener("scroll", () => {
            updateProgressBar();
            clearTimeout(progressTimer);
            progressTimer = setTimeout(sendProgress, 700);
        }, { passive: true });

        window.addEventListener("pagehide", sendProgress);

        return {
            positionPermille,
            currentAnchor,
            restore,
            scrollTo,
            markRestored: () => {
                restoreComplete = true;
            }
        };
    };
})();
