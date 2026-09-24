(() => {
    const root = document.querySelector("[data-book-reader]");
    if (!root) return;

    const original = root.querySelector("[data-book-original]");
    const translated = root.querySelector("[data-book-translated]");
    const progressForm = root.querySelector("[data-book-progress-form]");
    const bookmarkForm = root.querySelector("[data-book-bookmark-form]");
    const translateForm = root.querySelector("[data-book-translate-form]");
    const progressBar = root.querySelector("[data-book-progress-bar]");
    const toast = root.querySelector("[data-book-toast]");
    const targetLanguage = root.dataset.targetLanguage || "id";
    let view = root.dataset.view || "original";
    let saveTimer = 0;
    let pollTimer = 0;

    function setView(next) {
        if (next === "translated" && root.dataset.hasTranslation !== "true") return;
        if (next === "both" && root.dataset.hasTranslation !== "true") return;

        view = next;
        root.dataset.view = next;

        if (original) original.hidden = next === "translated";
        if (translated) translated.hidden = next === "original";

        root.querySelectorAll("[data-book-view]").forEach((button) => {
            button.classList.toggle("active", button.dataset.bookView === next);
        });
    }

    function currentPermille() {
        const max = Math.max(1, document.documentElement.scrollHeight - window.innerHeight);
        return Math.max(0, Math.min(1000, Math.round((window.scrollY / max) * 1000)));
    }

    function updateProgress() {
        const value = currentPermille();
        if (progressBar) progressBar.style.width = String(value / 10) + "%";

        if (progressForm) {
            const field = progressForm.querySelector('[name="positionPermille"]');
            if (field) field.value = String(value);
        }
        if (bookmarkForm) {
            const field = bookmarkForm.querySelector('[name="positionPermille"]');
            if (field) field.value = String(value);
        }
        return value;
    }

    async function postForm(form) {
        const response = await fetch(form.action, {
            method: "POST",
            body: new FormData(form),
            headers: { "X-Requested-With": "fetch" },
            credentials: "same-origin"
        });

        if (!response.ok) {
            const message = await response.text();
            throw new Error(message || ("Request failed (" + response.status + ")"));
        }

        const type = response.headers.get("content-type") || "";
        return type.includes("application/json") ? response.json() : null;
    }

    function queueProgressSave() {
        updateProgress();
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(async () => {
            if (!progressForm) return;
            try {
                await postForm(progressForm);
            } catch {
            }
        }, 900);
    }

    function showToast(message) {
        if (!toast) return;
        toast.textContent = message;
        toast.hidden = false;
        window.clearTimeout(showToast.timer);
        showToast.timer = window.setTimeout(() => {
            toast.hidden = true;
        }, 2400);
    }

    function renderTranslation(paragraphs) {
        if (!translated) return;
        translated.replaceChildren();

        for (const value of paragraphs || []) {
            const paragraph = document.createElement("p");
            paragraph.textContent = value;
            translated.append(paragraph);
        }

        root.dataset.hasTranslation = "true";
        root.querySelectorAll('[data-book-view="translated"], [data-book-view="both"]')
            .forEach((button) => {
                button.disabled = false;
            });

        const slot = root.querySelector("[data-translation-slot]");
        if (slot) slot.remove();
        setView("translated");
    }

    async function pollTranslation() {
        window.clearTimeout(pollTimer);

        try {
            const url = new URL(window.location.href);
            url.searchParams.set("handler", "TranslationStatus");
            url.searchParams.set("lang", targetLanguage);

            const response = await fetch(url, {
                credentials: "same-origin",
                headers: { "X-Requested-With": "fetch" }
            });
            if (!response.ok) return;

            const result = await response.json();
            if (result.status === "ready") {
                renderTranslation(result.paragraphs);
                showToast("Translation ready.");
                return;
            }
        } catch {
        }

        pollTimer = window.setTimeout(pollTranslation, 2500);
    }

    root.querySelectorAll("[data-book-view]").forEach((button) => {
        button.addEventListener("click", () => setView(button.dataset.bookView));
    });

    if (translateForm) {
        translateForm.addEventListener("submit", async (event) => {
            event.preventDefault();
            const button = translateForm.querySelector("button");
            if (button) button.disabled = true;

            const state = root.querySelector("[data-translation-state]");
            if (state) state.textContent = "Translation queued…";

            try {
                const result = await postForm(translateForm);
                if (result && result.status === "ready") {
                    await pollTranslation();
                    return;
                }
                showToast("Translation queued.");
                pollTranslation();
            } catch (error) {
                if (button) button.disabled = false;
                if (state) state.textContent = error.message || "Translation failed.";
            }
        });
    }

    const bookmarkButton = root.querySelector("[data-bookmark-button]");
    if (bookmarkButton && bookmarkForm) {
        bookmarkButton.addEventListener("click", async () => {
            updateProgress();
            try {
                await postForm(bookmarkForm);
                const count = bookmarkButton.querySelector("[data-bookmark-count]");
                if (count) count.textContent = String((Number(count.textContent) || 0) + 1);
                bookmarkButton.classList.add("active");
                showToast("Bookmark added at " + Math.round(currentPermille() / 10) + "%.");
            } catch (error) {
                showToast(error.message || "Could not add bookmark.");
            }
        });
    }

    window.addEventListener("scroll", queueProgressSave, { passive: true });

    setView(view);

    const initial = Number(root.dataset.progress || "0");
    if (initial > 0) {
        requestAnimationFrame(() => {
            const max = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
            window.scrollTo({ top: max * (initial / 1000), behavior: "auto" });
        });
    }

    if (root.dataset.hasTranslation !== "true" && translateForm) {
        pollTimer = window.setTimeout(pollTranslation, 2500);
    }
})();
