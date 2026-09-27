// Novel chapter translation. The existing full AI translation and the local
// TranslateGemma text-only translation are independent cached tracks. The
// local track's controls exist only when the server has TranslateGemma set up
// or the chapter already has cached local text (data-translate-gemma-available).
(() => {
    const registry = window.JularrNovelReader = window.JularrNovelReader || {};

    const loadText = shell => {
        let bundle = {};
        try {
            bundle = JSON.parse(
                shell.querySelector("[data-novel-translation-text]")?.textContent || "{}") || {};
        } catch {
            bundle = {};
        }
        return (key, fallback) => bundle["novels.translation." + key] || fallback;
    };

    registry.translation = reader => {
        const { shell } = reader;
        const t = loadText(shell);
        let translationSlot = shell.querySelector("[data-translation-slot]");
        const aiStatusUrl = shell.dataset.translationStatusUrl || "";
        const gemmaAvailable = shell.dataset.translateGemmaAvailable === "true";

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

        const sourceButton = (source, label) => {
            const button = document.createElement("button");
            button.type = "button";
            button.dataset.readerTranslationSource = source;
            button.disabled = true;
            button.textContent = label;
            return button;
        };

        const ensureSourcePanel = () => {
            if (!gemmaAvailable) return null;

            const slot = ensureTranslationSlot();
            if (!slot) return null;

            let panel = slot.querySelector("[data-translation-source-panel]");
            if (panel) return panel;

            panel = document.createElement("div");
            panel.className = "novel-translation-source-panel";
            panel.dataset.translationSourcePanel = "";

            const sourceSwitch = document.createElement("div");
            sourceSwitch.className = "novel-translation-source-switch";
            sourceSwitch.setAttribute("role", "group");
            sourceSwitch.setAttribute("aria-label", t("sourceSwitchAria", "German translation source"));
            sourceSwitch.append(
                sourceButton("gemma", t("localSource", "Local")),
                sourceButton("ai", t("aiSource", "AI")),
                sourceButton("both", t("bothSources", "Both")));

            const status = document.createElement("div");
            status.className = "novel-local-translation-status";
            status.dataset.localTranslationStatus = "";

            panel.append(sourceSwitch, status);
            slot.append(panel);
            reader.applyView(reader.currentView());
            return panel;
        };

        const removeSourcePanel = () => {
            translationSlot?.querySelector("[data-translation-source-panel]")?.remove();
        };

        const localStatusElement = () =>
            ensureSourcePanel()?.querySelector("[data-local-translation-status]");

        const installParagraphs = (paragraphs, language) => {
            const content = shell.querySelector("[data-reader-content]");
            if (!content) return false;

            paragraphs.forEach((text, index) => {
                let segment = content.querySelector(`[data-reader-segment="${index}"]`);
                if (!segment) {
                    segment = document.createElement("section");
                    segment.className = "novel-reader-segment";
                    segment.dataset.readerSegment = String(index);
                    content.append(segment);
                }

                let paragraph = segment.querySelector(
                    `[data-reader-paragraph][data-language="${language}"]`);
                if (!paragraph) {
                    paragraph = document.createElement("p");
                    paragraph.className = `novel-reader-paragraph ${language}`;
                    paragraph.lang = "de";
                    paragraph.dataset.readerParagraph = "";
                    paragraph.dataset.language = language;
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

            return true;
        };

        const installAiParagraphs = paragraphs => {
            if (!Array.isArray(paragraphs) || paragraphs.length === 0) return;
            if (!installParagraphs(paragraphs, "de")) return;

            reader.setHasTranslation(true);

            const state = aiStateLabel();
            if (state) {
                state.classList.add("ready");
                state.textContent = t("aiReady", "AI translation ready");
            }

            translationSlot?.querySelector("[data-translate-form]")?.remove();
            ensureSourcePanel();
            shell.dispatchEvent(new CustomEvent("jularr:novel-translation-ready", {
                detail: { source: "ai" }
            }));
        };

        const installGemmaParagraphs = paragraphs => {
            if (!Array.isArray(paragraphs) || paragraphs.length === 0) return;
            if (!installParagraphs(paragraphs, "de-gemma")) return;

            reader.setHasTranslateGemma(true);
            const status = localStatusElement();
            if (status) {
                status.replaceChildren();
                const label = document.createElement("span");
                label.className = "novel-translation-state ready";
                label.textContent = t("localReady", "Local translation ready");
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
                throw new Error((await response.text()) || t("requestFailed", "Request failed."));
            }

            return response.json();
        };

        const waitForTranslation = async () => {
            for (let attempt = 0; attempt < 45; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 2000));
                try {
                    const result = await fetchAiStatus();
                    if (result?.status === "ready") {
                        installAiParagraphs(result.paragraphs);
                        reader.showToast(t("aiReady", "AI translation ready"));
                        return;
                    }
                } catch {
                    // Background work may outlive this reader session.
                }
            }

            const state = aiStateLabel();
            if (state) {
                state.textContent = t("aiRunningInBackground", "AI translation continues in the background");
            }
        };

        const waitForGemmaTranslation = async () => {
            for (let attempt = 0; attempt < 60; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 5000));
                try {
                    const result = await fetchGemmaStatus();
                    if (result?.status === "ready") {
                        installGemmaParagraphs(result.paragraphs);
                        reader.showToast(t("localReady", "Local translation ready"));
                        return;
                    }
                } catch {
                    // A slow local model may continue after the page is closed.
                }
            }

            const status = localStatusElement();
            if (status) {
                status.textContent = t("localRunningInBackground", "Local translation continues in the background");
            }
        };

        const queueAiTranslation = async form => {
            const button = form?.querySelector("button");
            if (!form || !button || !aiStatusUrl) return;

            button.disabled = true;
            const previous = button.textContent;
            button.textContent = t("starting", "Starting…");

            try {
                const result = await reader.postForm(form);

                if (result?.status === "ready") {
                    const status = await fetchAiStatus();
                    installAiParagraphs(status.paragraphs);
                    return;
                }

                const state = aiStateLabel();
                if (state) state.textContent = t("aiRunning", "AI translation running");

                button.textContent = t("running", "Running");
                reader.showToast(t("aiStarted", "AI translation started"));
                void waitForTranslation();
            } catch (error) {
                button.disabled = false;
                button.textContent = previous;
                reader.showToast(
                    error.message || t("aiStartFailed", "AI translation could not be started"));
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
            button.textContent = t("localTranslateButton", "Translate locally");
            form.append(button);
            return form;
        };

        const renderGemmaStatus = result => {
            if (result?.status === "ready") {
                installGemmaParagraphs(result.paragraphs);
                return;
            }

            // Not configured and nothing cached: the local track leaves no trace.
            if (result?.status === "unavailable") {
                removeSourcePanel();
                return;
            }

            const status = localStatusElement();
            if (!status) return;

            status.replaceChildren();
            const label = document.createElement("span");
            label.className = "novel-translation-state";
            label.textContent = t("localNotCreated", "No local translation yet");
            status.append(label);

            if (result?.status !== "blocked" && result?.canGenerate) {
                status.append(createGemmaForm());
            }
        };

        const queueGemmaTranslation = async form => {
            const button = form?.querySelector("button");
            if (!form || !button) return;

            button.disabled = true;
            const previous = button.textContent;
            button.textContent = t("starting", "Starting…");

            try {
                const result = await reader.postForm(form);
                if (result?.status === "ready") {
                    const status = await fetchGemmaStatus();
                    installGemmaParagraphs(status.paragraphs);
                    return;
                }

                const status = localStatusElement();
                if (status) {
                    status.textContent = t("localRunning", "Local translation running");
                }

                reader.showToast(t("localStarted", "Local translation started"));
                void waitForGemmaTranslation();
            } catch (error) {
                button.disabled = false;
                button.textContent = previous;
                reader.showToast(
                    error.message || t("localStartFailed", "Local translation could not be started"));
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
        if (gemmaAvailable) {
            void fetchGemmaStatus()
                .then(renderGemmaStatus)
                .catch(() => {
                    // Reader stays fully usable when the local endpoint is unreachable.
                });
        }

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
