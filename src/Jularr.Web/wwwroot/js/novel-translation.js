// Novel chapter translation. The existing full AI translation and the local
// TranslateGemma text-only translation are independent cached tracks.
(() => {
    const registry = window.JularrNovelReader = window.JularrNovelReader || {};

    registry.translation = reader => {
        const { shell } = reader;
        let translationSlot = shell.querySelector("[data-translation-slot]");
        const aiStatusUrl = shell.dataset.translationStatusUrl || "";

        const handlerUrl = handler => {
            const target = new URL(aiStatusUrl || window.location.href, window.location.origin);
            target.searchParams.set("handler", handler);
            if (!target.pathname.includes(shell.dataset.chapterId || "") &&
                shell.dataset.chapterId) {
                target.searchParams.set("id", shell.dataset.chapterId);
            }
            return target.pathname + target.search;
        };

        const gemmaStatusUrl = handlerUrl("TranslateGemmaStatus");
        const gemmaPostUrl = handlerUrl("TranslateGemma");

        const ensureTranslationSlot = () => {
            if (translationSlot) return translationSlot;

            const content = shell.querySelector("[data-reader-content]");
            if (!content) return null;

            translationSlot = document.createElement("div");
            translationSlot.className = "novel-translation-slot";
            translationSlot.dataset.translationSlot = "";
            content.before(translationSlot);
            return translationSlot;
        };

        const aiStateLabel = () =>
            translationSlot?.querySelector(".novel-translation-state");

        const ensureSourcePanel = () => {
            const slot = ensureTranslationSlot();
            if (!slot) return null;

            let panel = slot.querySelector("[data-translation-source-panel]");
            if (panel) return panel;

            panel = document.createElement("div");
            panel.className = "novel-translation-source-panel";
            panel.dataset.translationSourcePanel = "";
            panel.innerHTML = `
                <div class="novel-translation-source-switch"
                     role="group"
                     aria-label="Deutsche Übersetzung">
                    <button type="button"
                            data-reader-translation-source="gemma"
                            disabled>Übersetzung</button>
                    <button type="button"
                            data-reader-translation-source="ai"
                            disabled>AI</button>
                    <button type="button"
                            data-reader-translation-source="both"
                            disabled>Beide</button>
                </div>
                <div class="novel-local-translation-status" data-local-translation-status></div>
            `;
            slot.append(panel);
            reader.applyView(reader.currentView());
            return panel;
        };

        const localStatusElement = () =>
            ensureSourcePanel()?.querySelector("[data-local-translation-status]");

        const installAiParagraphs = paragraphs => {
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

                    const source = segment.querySelector(
                        '[data-reader-paragraph][data-language="ja"]');
                    if (source?.classList.contains("is-heading")) {
                        paragraph.classList.add("is-heading");
                        paragraph.setAttribute("role", "heading");
                        if (source.getAttribute("aria-level")) {
                            paragraph.setAttribute(
                                "aria-level",
                                source.getAttribute("aria-level"));
                        }
                    }

                    segment.append(paragraph);
                }

                paragraph.textContent = text;
            });

            reader.setHasTranslation(true);

            const state = aiStateLabel();
            if (state) {
                state.classList.add("ready");
                state.textContent = "AI-Übersetzung bereit";
            }

            translationSlot?.querySelector("[data-translate-form]")?.remove();
            ensureSourcePanel();
            shell.dispatchEvent(new CustomEvent("jularr:novel-translation-ready", {
                detail: { source: "ai" }
            }));
        };

        const installGemmaParagraphs = paragraphs => {
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
                    '[data-reader-paragraph][data-language="de-gemma"]');
                if (!paragraph) {
                    paragraph = document.createElement("p");
                    paragraph.className = "novel-reader-paragraph de-gemma";
                    paragraph.lang = "de";
                    paragraph.dataset.readerParagraph = "";
                    paragraph.dataset.language = "de-gemma";
                    paragraph.dataset.index = String(index);

                    const source = segment.querySelector(
                        '[data-reader-paragraph][data-language="ja"]');
                    if (source?.classList.contains("is-heading")) {
                        paragraph.classList.add("is-heading");
                        paragraph.setAttribute("role", "heading");
                        if (source.getAttribute("aria-level")) {
                            paragraph.setAttribute(
                                "aria-level",
                                source.getAttribute("aria-level"));
                        }
                    }

                    segment.append(paragraph);
                }

                paragraph.textContent = text;
            });

            reader.setHasTranslateGemma(true);
            const status = localStatusElement();
            if (status) {
                status.replaceChildren();
                const label = document.createElement("span");
                label.className = "novel-translation-state ready";
                label.textContent = "Übersetzung bereit";
                status.append(label);
            }

            shell.dispatchEvent(new CustomEvent("jularr:novel-translation-ready", {
                detail: { source: "gemma" }
            }));
        };

        const fetchAiStatus = () => reader.getJson(aiStatusUrl);

        const fetchGemmaStatus = async () => {
            const response = await fetch(gemmaStatusUrl, {
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "fetch" }
            });

            if (response.status === 403) {
                return { status: "blocked", canGenerate: false };
            }

            if (!response.ok) {
                throw new Error((await response.text()) || "Request failed.");
            }

            return response.json();
        };

        const waitForAiTranslation = async () => {
            for (let attempt = 0; attempt < 45; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 2000));
                try {
                    const result = await fetchAiStatus();
                    if (result?.status === "ready") {
                        installAiParagraphs(result.paragraphs);
                        reader.showToast("AI-Übersetzung ist bereit");
                        return;
                    }
                } catch {
                    // Background work may outlive this reader session.
                }
            }

            const state = aiStateLabel();
            if (state) state.textContent = "AI-Übersetzung läuft im Hintergrund";
        };

        const waitForGemmaTranslation = async () => {
            for (let attempt = 0; attempt < 60; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 5000));
                try {
                    const result = await fetchGemmaStatus();
                    if (result?.status === "ready") {
                        installGemmaParagraphs(result.paragraphs);
                        reader.showToast("Übersetzung ist bereit");
                        return;
                    }
                } catch {
                    // A slow local model may continue after the page is closed.
                }
            }

            const status = localStatusElement();
            if (status) status.textContent = "Übersetzung läuft im Hintergrund";
        };

        const queueAiTranslation = async form => {
            const button = form?.querySelector("button");
            if (!form || !button || !aiStatusUrl) return;

            button.disabled = true;
            const previous = button.textContent;
            button.textContent = "Startet…";

            try {
                const result = await reader.postForm(form);

                if (result?.status === "ready") {
                    const status = await fetchAiStatus();
                    installAiParagraphs(status.paragraphs);
                    return;
                }

                const state = aiStateLabel();
                if (state) state.textContent = "AI-Übersetzung läuft";

                button.textContent = "Läuft";
                reader.showToast("AI-Übersetzung gestartet");
                void waitForAiTranslation();
            } catch (error) {
                button.disabled = false;
                button.textContent = previous;
                reader.showToast(
                    error.message || "AI-Übersetzung konnte nicht gestartet werden");
            }
        };

        const createGemmaForm = () => {
            const form = document.createElement("form");
            form.method = "post";
            form.action = gemmaPostUrl;
            form.dataset.translateGemmaForm = "";

            const token = document.querySelector(
                'input[name="__RequestVerificationToken"]');
            if (token) {
                form.append(token.cloneNode(true));
            }

            const button = document.createElement("button");
            button.type = "submit";
            button.className = "novel-text-action";
            button.textContent = "Übersetzen";
            form.append(button);
            return form;
        };

        const renderGemmaStatus = result => {
            const status = localStatusElement();
            if (!status) return;

            status.replaceChildren();

            if (result?.status === "ready") {
                installGemmaParagraphs(result.paragraphs);
                return;
            }

            const label = document.createElement("span");
            label.className = "novel-translation-state";

            if (result?.status === "unavailable") {
                label.textContent = "Lokale Übersetzung nicht eingerichtet";
                status.append(label);
                return;
            }

            if (result?.status === "blocked") {
                label.textContent = "Lokale Übersetzung nicht erstellt";
                status.append(label);
                return;
            }

            label.textContent = "Lokale Übersetzung nicht erstellt";
            status.append(label);

            if (result?.canGenerate) {
                status.append(createGemmaForm());
            }
        };

        const queueGemmaTranslation = async form => {
            const button = form?.querySelector("button");
            if (!form || !button) return;

            button.disabled = true;
            const previous = button.textContent;
            button.textContent = "Startet…";

            try {
                const result = await reader.postForm(form);
                if (result?.status === "ready") {
                    const status = await fetchGemmaStatus();
                    installGemmaParagraphs(status.paragraphs);
                    return;
                }

                const status = localStatusElement();
                if (status) {
                    status.textContent = "Übersetzung läuft";
                }

                reader.showToast("Lokale Übersetzung gestartet");
                void waitForGemmaTranslation();
            } catch (error) {
                button.disabled = false;
                button.textContent = previous;
                reader.showToast(
                    error.message || "Lokale Übersetzung konnte nicht gestartet werden");
            }
        };

        shell.addEventListener("submit", event => {
            const aiForm = event.target.closest("[data-translate-form]");
            if (aiForm) {
                event.preventDefault();
                void queueAiTranslation(aiForm);
                return;
            }

            const gemmaForm = event.target.closest("[data-translate-gemma-form]");
            if (gemmaForm) {
                event.preventDefault();
                void queueGemmaTranslation(gemmaForm);
            }
        });

        // Cached local text is loaded lazily because the Razor reader markup is
        // shared with the existing AI path. This also keeps cached translations
        // readable when generation is disabled.
        void fetchGemmaStatus()
            .then(renderGemmaStatus)
            .catch(() => {
                // Reader stays fully usable when no local translation endpoint
                // is reachable/configured.
            });

        if (reader.hasAiTranslation()) {
            ensureSourcePanel();
        }

        return {
            installGermanParagraphs: installAiParagraphs,
            installAiParagraphs,
            installGemmaParagraphs
        };
    };
})();
