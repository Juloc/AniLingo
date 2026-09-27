// Novel chapter translation: queues the German AI translation through the
// page's Translate handler (an Operations job), polls TranslationStatus and
// installs the German paragraphs without reloading the reader.
(() => {
    const registry = window.JularrNovelReader = window.JularrNovelReader || {};

    registry.translation = reader => {
        const { shell, t } = reader;
        const translationSlot = shell.querySelector("[data-translation-slot]");
        const statusUrl = shell.dataset.translationStatusUrl || "";

        const stateLabel = () =>
            translationSlot?.querySelector(".novel-translation-state");

        const installGermanParagraphs = paragraphs => {
            if (!Array.isArray(paragraphs) || paragraphs.length === 0) return;

            const content = shell.querySelector("[data-reader-content]");
            if (!content) return;

            paragraphs.forEach((text, index) => {
                let segment = content.querySelector(`[data-reader-segment="${index}"]`);
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

            reader.setHasTranslation(true);

            const state = stateLabel();
            if (state) {
                state.classList.add("ready");
                state.textContent = t("germanReady", "German ready");
            }

            translationSlot?.querySelector("[data-translate-form]")?.remove();
            shell.dispatchEvent(new CustomEvent("jularr:novel-translation-ready"));
        };

        const fetchTranslationStatus = () => reader.getJson(statusUrl);

        const waitForTranslation = async () => {
            for (let attempt = 0; attempt < 45; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 2000));
                try {
                    const result = await fetchTranslationStatus();
                    if (result?.status === "ready") {
                        installGermanParagraphs(result.paragraphs);
                        reader.showToast(t("germanReady", "German ready"));
                        return;
                    }
                } catch {
                    // Keep the reader usable if a background status request fails.
                }
            }

            const state = stateLabel();
            if (state) state.textContent = t("translationStillRunning", "Translation still running – open the chapter again later");
        };

        const queueTranslation = async form => {
            const button = form?.querySelector("button");
            if (!form || !button || !statusUrl) return;

            button.disabled = true;
            const previous = button.textContent;
            button.textContent = t("translationStarting", "Starting…");

            try {
                const result = await reader.postForm(form);

                if (result?.status === "ready") {
                    const status = await fetchTranslationStatus();
                    installGermanParagraphs(status.paragraphs);
                    return;
                }

                const state = stateLabel();
                if (state) state.textContent = t("translationRunning", "Translating…");

                reader.showToast(t("translationStarted", "Translation started"));
                void waitForTranslation();
            } catch (error) {
                button.disabled = false;
                button.textContent = previous;
                reader.showToast(t("translationStartFailed", "The translation could not be started"));
            }
        };

        shell.addEventListener("submit", event => {
            const form = event.target.closest("[data-translate-form]");
            if (!form) return;
            event.preventDefault();
            void queueTranslation(form);
        });

        return { installGermanParagraphs };
    };
})();
