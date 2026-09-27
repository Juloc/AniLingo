// Novel reader bootstrap. Feature modules (novel-position.js, novel-annotations.js,
// novel-chapter-drawer.js, novel-translation.js, novel-learning.js) register
// factories on window.JularrNovelReader; this file owns the shared reader
// context, language/translation view state and the restore lifecycle.
(() => {
    const shell = document.querySelector("[data-novel-reader]");
    if (!shell) return;

    const modules = window.JularrNovelReader || {};
    const profileId = document.body?.dataset.profileId || "unknown";
    const storagePrefix = `anilingo.profile.${profileId}.novel`;
    const storage = {
        view: `${storagePrefix}.view`,
        translationSource: `${storagePrefix}.translationSource`
    };

    const toast = shell.querySelector("[data-reader-toast]");
    let toastTimer = null;
    let hasAiTranslation = shell.dataset.hasTranslation === "true";
    let hasTranslateGemma = false;
    let preferredTranslationSource = localStorage.getItem(storage.translationSource) || "";

    const focusableSelector =
        "a[href], button:not([disabled]), input:not([disabled]), " +
        "select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex=\"-1\"])";

    const effectiveTranslationSource = () => {
        if (preferredTranslationSource === "both" &&
            hasAiTranslation &&
            hasTranslateGemma) {
            return "both";
        }

        if (preferredTranslationSource === "gemma" && hasTranslateGemma) {
            return "gemma";
        }

        if (preferredTranslationSource === "ai" && hasAiTranslation) {
            return "ai";
        }

        if (hasAiTranslation) return "ai";
        if (hasTranslateGemma) return "gemma";
        return "none";
    };

    const hasAnyTranslation = () => hasAiTranslation || hasTranslateGemma;

    const syncLanguageControls = () => {
        const anyTranslation = hasAnyTranslation();
        shell.querySelectorAll("[data-reader-view]").forEach(button => {
            if (button.dataset.readerView !== "ja") {
                button.disabled = !anyTranslation;
            }
        });

        const source = effectiveTranslationSource();
        shell.dataset.translationSource = source;

        shell.querySelectorAll("[data-reader-translation-source]").forEach(button => {
            const value = button.dataset.readerTranslationSource;
            button.disabled =
                (value === "ai" && !hasAiTranslation) ||
                (value === "gemma" && !hasTranslateGemma) ||
                (value === "both" && !(hasAiTranslation && hasTranslateGemma));
            button.setAttribute("aria-pressed", value === source ? "true" : "false");
        });
    };

    const reader = {
        shell,

        clamp: (value, min, max) => Math.min(max, Math.max(min, value)),

        normalizeText: value => (value || "").replace(/\s+/g, " ").trim(),

        showToast: message => {
            if (!toast) return;
            clearTimeout(toastTimer);
            toast.textContent = message;
            toast.hidden = false;
            toastTimer = setTimeout(() => {
                toast.hidden = true;
            }, 2200);
        },

        postForm: async (form, mutate = () => {}) => {
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
        },

        getJson: async url => {
            const response = await fetch(url, {
                credentials: "same-origin",
                cache: "no-store",
                headers: { "X-Requested-With": "fetch" }
            });
            if (!response.ok) {
                throw new Error((await response.text()) || "Request failed.");
            }
            return response.json();
        },

        withQuery: (url, parameters) => {
            const target = new URL(url, window.location.origin);
            for (const [key, value] of Object.entries(parameters)) {
                if (value === null || value === undefined || value === "") continue;
                target.searchParams.set(key, String(value));
            }
            return target.pathname + target.search;
        },

        // Backward-compatible name: "translation" means the existing full AI
        // translation. TranslateGemma is a separate text-only track.
        hasTranslation: () => hasAiTranslation,
        hasAiTranslation: () => hasAiTranslation,
        hasTranslateGemma: () => hasTranslateGemma,
        hasAnyTranslation,
        translationSource: effectiveTranslationSource,

        currentView: () => shell.dataset.view || "ja",

        anchorLanguage: () => {
            if (reader.currentView() !== "de") return "ja";

            const source = effectiveTranslationSource();
            if ((source === "gemma" || source === "both") && hasTranslateGemma) {
                return "de-gemma";
            }

            return hasAiTranslation ? "de" : "ja";
        },

        paragraphsFor: language =>
            Array.from(shell.querySelectorAll(
                `[data-reader-paragraph][data-language="${language}"]`)),

        paragraphAt: (language, index) =>
            shell.querySelector(
                `[data-reader-paragraph][data-language="${language}"][data-index="${Number(index)}"]`),

        announcePanel: name => {
            shell.dispatchEvent(new CustomEvent("jularr:novel-panel-open", {
                detail: { name }
            }));
        },

        onOtherPanelOpened: (name, close) => {
            shell.addEventListener("jularr:novel-panel-open", event => {
                if (event.detail?.name !== name) close(false);
            });
        },

        trapFocus: (container, event) => {
            if (event.key !== "Tab") return;
            const items = Array.from(container.querySelectorAll(focusableSelector))
                .filter(element => !element.closest("[hidden]") && element.offsetParent !== null);
            if (items.length === 0) {
                event.preventDefault();
                container.focus();
                return;
            }

            const first = items[0];
            const last = items[items.length - 1];
            if (event.shiftKey &&
                (document.activeElement === first || document.activeElement === container)) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        },

        setHasTranslation: value => {
            hasAiTranslation = Boolean(value);
            shell.dataset.hasTranslation = hasAiTranslation ? "true" : "false";
            syncLanguageControls();
            reader.applyView(reader.currentView());
        },

        setHasTranslateGemma: value => {
            hasTranslateGemma = Boolean(value);
            shell.dataset.hasTranslateGemma = hasTranslateGemma ? "true" : "false";
            syncLanguageControls();
            reader.applyView(reader.currentView());
        },

        applyTranslationSource: source => {
            if (!["ai", "gemma", "both"].includes(source)) return;
            preferredTranslationSource = source;
            localStorage.setItem(storage.translationSource, source);
            syncLanguageControls();
            reader.applyView(reader.currentView());
            shell.dispatchEvent(new CustomEvent("jularr:novel-translation-source-changed", {
                detail: { source: effectiveTranslationSource() }
            }));
        },

        applyView: view => {
            const allowed = hasAnyTranslation() ? ["ja", "de", "both"] : ["ja"];
            const next = allowed.includes(view) ? view : "ja";
            shell.dataset.view = next;
            shell.dataset.translationSource = effectiveTranslationSource();
            localStorage.setItem(storage.view, next);

            shell.querySelectorAll("[data-reader-view]").forEach(button => {
                button.setAttribute(
                    "aria-pressed",
                    button.dataset.readerView === next ? "true" : "false");
            });

            syncLanguageControls();
            shell.dispatchEvent(new CustomEvent("jularr:novel-view-changed", {
                detail: {
                    view: next,
                    translationSource: effectiveTranslationSource()
                }
            }));
        }
    };

    const initialAnchorLanguage = shell.dataset.anchorLanguage || "ja";
    const forcedAnchor = shell.dataset.forceAnchor === "true";
    const storedView = localStorage.getItem(storage.view);

    if (forcedAnchor && initialAnchorLanguage === "de-gemma") {
        preferredTranslationSource = "gemma";
    } else if (forcedAnchor && initialAnchorLanguage === "de") {
        preferredTranslationSource = "ai";
    }

    const anchorView =
        initialAnchorLanguage === "de" || initialAnchorLanguage === "de-gemma"
            ? "de"
            : "ja";

    syncLanguageControls();
    reader.applyView(forcedAnchor
        ? anchorView
        : (storedView || anchorView));

    reader.position = modules.position(reader);
    reader.annotations = modules.annotations(reader);
    modules.chapterDrawer(reader);
    modules.translation(reader);
    modules.learning?.(reader);

    if (shell.dataset.workId && window.JularrOfflineLibraryRepository) {
        window.JularrOfflineLibraryRepository.initializeOfflineChapterNavigation({
            shell,
            workId: shell.dataset.workId,
            linkSelector: ".novel-reader-footer a[href^=\"/Novels/Read/\"]",
            contentSelector: "[data-reader-content]",
            renderer: "novel"
        });
        shell.addEventListener("jularr:offline-chapter-missing", () => {
            reader.showToast("Dieses Kapitel wurde nicht für den Offline-Zugriff heruntergeladen.");
        });
    }

    shell.addEventListener("click", event => {
        const viewButton = event.target.closest("[data-reader-view]");
        if (viewButton) {
            reader.applyView(viewButton.dataset.readerView);
            return;
        }

        const sourceButton = event.target.closest("[data-reader-translation-source]");
        if (sourceButton && !sourceButton.disabled) {
            reader.applyTranslationSource(sourceButton.dataset.readerTranslationSource);
        }
    });

    const restore = () => {
        shell.dispatchEvent(new CustomEvent("jularr:reader-restoring", {
            detail: { active: true }
        }));
        reader.position.restore();
        setTimeout(() => {
            reader.position.markRestored();
            shell.classList.remove("reader-chrome-hidden");
            shell.dispatchEvent(new CustomEvent("jularr:reader-restoring", {
                detail: { active: false }
            }));
        }, 50);
    };

    requestAnimationFrame(() => requestAnimationFrame(restore));
})();
