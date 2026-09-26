// Novel reader bootstrap. Feature modules (novel-position.js, novel-annotations.js,
// novel-chapter-drawer.js, novel-translation.js, novel-learning.js) register
// factories on window.AniLingoNovelReader; this file owns the shared reader
// context, the language view state and the restore lifecycle, then starts
// every module once. Chrome visibility, settings and paged mode stay in
// reader-shell.js and reader-personalization.js.
(() => {
    const shell = document.querySelector("[data-novel-reader]");
    if (!shell) return;

    const modules = window.AniLingoNovelReader || {};
    const profileId = document.body?.dataset.profileId || "unknown";
    const storagePrefix = `anilingo.profile.${profileId}.novel`;
    const storage = {
        view: `${storagePrefix}.view`
    };

    const toast = shell.querySelector("[data-reader-toast]");
    let toastTimer = null;
    let hasTranslation = shell.dataset.hasTranslation === "true";

    const focusableSelector =
        "a[href], button:not([disabled]), input:not([disabled]), " +
        "select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex=\"-1\"])";

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

        hasTranslation: () => hasTranslation,

        currentView: () => shell.dataset.view || "ja",

        anchorLanguage: () =>
            reader.currentView() === "de" && hasTranslation ? "de" : "ja",

        paragraphsFor: language =>
            Array.from(shell.querySelectorAll(
                `[data-reader-paragraph][data-language="${language}"]`)),

        paragraphAt: (language, index) =>
            shell.querySelector(
                `[data-reader-paragraph][data-language="${language}"][data-index="${Number(index)}"]`),

        // Side panels (chapter drawer, notes) are mutually exclusive.
        announcePanel: name => {
            shell.dispatchEvent(new CustomEvent("anilingo:novel-panel-open", {
                detail: { name }
            }));
        },

        onOtherPanelOpened: (name, close) => {
            shell.addEventListener("anilingo:novel-panel-open", event => {
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
            hasTranslation = Boolean(value);
            shell.dataset.hasTranslation = hasTranslation ? "true" : "false";
            syncLanguageControls();
        },

        applyView: view => {
            const allowed = hasTranslation ? ["ja", "de", "both"] : ["ja"];
            const next = allowed.includes(view) ? view : "ja";
            shell.dataset.view = next;
            localStorage.setItem(storage.view, next);

            shell.querySelectorAll("[data-reader-view]").forEach(button => {
                button.setAttribute(
                    "aria-pressed",
                    button.dataset.readerView === next ? "true" : "false");
            });
        }
    };

    const syncLanguageControls = () => {
        shell.querySelectorAll("[data-reader-view]").forEach(button => {
            if (button.dataset.readerView !== "ja") {
                button.disabled = !hasTranslation;
            }
        });
    };

    const initialAnchorLanguage = shell.dataset.anchorLanguage || "ja";
    const forcedAnchor = shell.dataset.forceAnchor === "true";
    const storedView = localStorage.getItem(storage.view);

    syncLanguageControls();
    reader.applyView(forcedAnchor
        ? initialAnchorLanguage
        : (storedView || initialAnchorLanguage));

    reader.position = modules.position(reader);
    reader.annotations = modules.annotations(reader);
    modules.chapterDrawer(reader);
    modules.translation(reader);
    modules.learning?.(reader);

    // #221 part 2: while offline, following the previous/next chapter footer
    // link to a downloaded chapter renders it locally instead of a failing
    // full-page navigation; to an undownloaded chapter shows a clear notice.
    // Online, this never engages and the existing full-page navigation is
    // unchanged. See offline-library-repository.js.
    if (shell.dataset.workId && window.AniLingoOfflineLibraryRepository) {
        window.AniLingoOfflineLibraryRepository.initializeOfflineChapterNavigation({
            shell,
            workId: shell.dataset.workId,
            linkSelector: ".novel-reader-footer a[href^=\"/Novels/Read/\"]",
            contentSelector: "[data-reader-content]",
            renderer: "novel"
        });
        shell.addEventListener("anilingo:offline-chapter-missing", () => {
            reader.showToast("Dieses Kapitel wurde nicht für den Offline-Zugriff heruntergeladen.");
        });
    }

    shell.addEventListener("click", event => {
        const viewButton = event.target.closest("[data-reader-view]");
        if (viewButton) {
            reader.applyView(viewButton.dataset.readerView);
        }
    });

    // Resume/jump restoration must not count as a user scroll for the shared
    // chrome (reader-shell.js listens for anilingo:reader-restoring).
    const restore = () => {
        shell.dispatchEvent(new CustomEvent("anilingo:reader-restoring", {
            detail: { active: true }
        }));
        reader.position.restore();
        setTimeout(() => {
            reader.position.markRestored();
            shell.classList.remove("reader-chrome-hidden");
            shell.dispatchEvent(new CustomEvent("anilingo:reader-restoring", {
                detail: { active: false }
            }));
        }, 50);
    };

    requestAnimationFrame(() => requestAnimationFrame(restore));
})();
