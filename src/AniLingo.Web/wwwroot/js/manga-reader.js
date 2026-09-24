(() => {
    const shell = document.querySelector("[data-manga-reader]");
    if (!shell) return;

    const pageCount = Math.max(1, Number(shell.dataset.pageCount || 1));
    const profile = shell.dataset.profile || "default";
    const settingsKey = `anilingo.reader.manga.${profile}`;

    const stage = shell.querySelector("[data-stage]");
    const frame = shell.querySelector("[data-page-frame]");
    const primary = shell.querySelector("[data-page-primary]");
    const left = shell.querySelector("[data-page-left]");
    const right = shell.querySelector("[data-page-right]");
    const continuous = shell.querySelector("[data-continuous]");
    const scrubber = shell.querySelector("[data-page-scrubber]");
    const label = shell.querySelector("[data-page-label]");
    const progressForm = shell.querySelector("[data-progress-form]");
    const bookmarkForm = shell.querySelector("[data-bookmark-form]");
    const removeBookmarkForm = shell.querySelector("[data-remove-bookmark-form]");
    const bookmarkCount = shell.querySelector("[data-bookmark-count]");
    const bookmarkButton = shell.querySelector("[data-bookmark]");
    const bookmarkTicks = shell.querySelector("[data-bookmark-ticks]");
    const toast = shell.querySelector("[data-toast]");
    const drawer = shell.querySelector("[data-chapter-drawer]");
    const backdrop = shell.querySelector("[data-drawer-backdrop]");
    const chapterList = shell.querySelector("[data-chapter-list]");
    const chapterData = shell.querySelector("[data-chapter-data]");
    const chapterSearch = shell.querySelector("[data-chapter-search]");
    const zoomLabel = shell.querySelector("[data-zoom-label]");

    const bookmarks = new Map();
    let page = clamp(Number(shell.dataset.page || 0), 0, pageCount - 1);
    let mode = "single";
    let direction = shell.dataset.direction === "ltr" ? "ltr" : "rtl";
    let fit = "height";
    let zoom = 1;
    let saveTimer = null;
    let toastTimer = null;
    let chaptersBuilt = false;
    let continuousBuilt = false;
    let continuousObserver = null;
    let touchX = null;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    const pageUrl = index =>
        `${window.location.pathname}?handler=Page&page=${index}`;

    const readSettings = () => {
        try {
            return JSON.parse(localStorage.getItem(settingsKey) || "{}");
        } catch {
            return {};
        }
    };

    const writeSettings = () => {
        localStorage.setItem(settingsKey, JSON.stringify({
            mode,
            direction,
            fit,
            zoom
        }));
    };

    const showToast = message => {
        if (!toast) return;
        clearTimeout(toastTimer);
        toast.textContent = message;
        toast.hidden = false;
        toastTimer = setTimeout(() => {
            toast.hidden = true;
        }, 1800);
    };

    const postForm = async (form, mutate) => {
        const data = new FormData(form);
        mutate?.(data);
        const response = await fetch(form.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" },
            keepalive: form === progressForm
        });

        if (!response.ok) {
            throw new Error(await response.text() || "Request failed.");
        }

        return response.headers.get("content-type")?.includes("application/json")
            ? response.json()
            : null;
    };

    const setImage = (image, index) => {
        if (!image) return;
        if (index < 0 || index >= pageCount) {
            image.hidden = true;
            image.removeAttribute("src");
            return;
        }

        image.hidden = false;
        const src = pageUrl(index);
        if (image.getAttribute("src") !== src) {
            image.src = src;
        }
        image.dataset.pageIndex = String(index);
    };

    const currentStep = () => mode === "double" ? 2 : 1;

    const updateChrome = () => {
        if (scrubber) scrubber.value = String(page);
        if (label) label.textContent = `${page + 1} / ${pageCount}`;

        shell.querySelectorAll("[data-mode]").forEach(button => {
            button.setAttribute(
                "aria-pressed",
                button.dataset.mode === mode ? "true" : "false");
        });

        const directionButton = shell.querySelector("[data-direction-toggle]");
        if (directionButton) directionButton.textContent = direction.toUpperCase();

        shell.dataset.mode = mode;
        shell.dataset.direction = direction;
        shell.dataset.fit = fit;
        shell.style.setProperty("--manga-zoom", String(zoom));
        if (zoomLabel) zoomLabel.textContent = `${Math.round(zoom * 100)}%`;

        const currentBookmark = Array.from(bookmarks.values())
            .some(item => item.pageIndex === page);
        bookmarkButton?.classList.toggle("active", currentBookmark);
    };

    const renderPaged = () => {
        if (!frame || !continuous) return;
        continuous.hidden = true;
        frame.hidden = false;

        if (mode === "single") {
            setImage(primary, page);
            if (left) left.hidden = true;
            if (right) right.hidden = true;
            return;
        }

        if (mode === "double") {
            if (primary) primary.hidden = true;
            const base = page - (page % 2);
            if (direction === "rtl") {
                setImage(right, base);
                setImage(left, base + 1);
            } else {
                setImage(left, base);
                setImage(right, base + 1);
            }
        }
    };

    const buildContinuous = () => {
        if (continuousBuilt || !continuous) return;

        const fragment = document.createDocumentFragment();
        for (let index = 0; index < pageCount; index++) {
            const wrapper = document.createElement("figure");
            wrapper.className = "manga-continuous-page";
            wrapper.dataset.continuousPage = String(index);

            const image = document.createElement("img");
            image.loading = index <= page + 2 ? "eager" : "lazy";
            image.decoding = "async";
            image.alt = `Page ${index + 1}`;
            image.src = pageUrl(index);
            image.draggable = false;
            wrapper.append(image);
            fragment.append(wrapper);
        }

        continuous.append(fragment);
        continuousBuilt = true;

        continuousObserver = new IntersectionObserver(entries => {
            const visible = entries
                .filter(entry => entry.isIntersecting)
                .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
            if (!visible) return;

            const next = Number(visible.target.dataset.continuousPage);
            if (Number.isInteger(next) && next !== page) {
                page = next;
                updateChrome();
                queueProgress();
                prefetch();
            }
        }, {
            threshold: [0.45, 0.65, 0.85]
        });

        continuous.querySelectorAll("[data-continuous-page]")
            .forEach(element => continuousObserver.observe(element));
    };

    const render = (scrollContinuous = false) => {
        updateChrome();

        if (mode === "continuous") {
            if (frame) frame.hidden = true;
            if (continuous) continuous.hidden = false;
            buildContinuous();

            if (scrollContinuous) {
                continuous?.querySelector(
                    `[data-continuous-page="${page}"]`)
                    ?.scrollIntoView({ behavior: "smooth", block: "start" });
            }
        } else {
            renderPaged();
        }

        renderBookmarkTicks();
        prefetch();
    };

    const goTo = (next, options = {}) => {
        const target = clamp(next, 0, pageCount - 1);
        if (target === page && !options.force) return;

        page = target;
        render(mode === "continuous");
        queueProgress();
    };

    const forward = () => {
        const next = page + currentStep();
        if (next < pageCount) {
            goTo(next);
            return;
        }

        const nextChapter = shell.querySelector(".manga-chapter-step:last-child:not(.disabled)");
        if (nextChapter?.getAttribute("href") &&
            nextChapter.getAttribute("href") !== "#") {
            window.location.assign(nextChapter.href);
        }
    };

    const back = () => {
        const previous = page - currentStep();
        if (previous >= 0) {
            goTo(previous);
            return;
        }

        const previousChapter = shell.querySelector(".manga-chapter-step:first-child:not(.disabled)");
        if (previousChapter?.getAttribute("href") &&
            previousChapter.getAttribute("href") !== "#") {
            window.location.assign(previousChapter.href);
        }
    };

    const queueProgress = () => {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(saveProgress, 350);
    };

    const saveProgress = () => {
        if (!progressForm) return;
        void postForm(progressForm, data => {
            data.set("pageIndex", String(page));
        }).catch(() => {});
    };

    const prefetch = () => {
        const indexes = [
            page + currentStep(),
            page + currentStep() * 2,
            page - currentStep()
        ];

        indexes
            .filter(index => index >= 0 && index < pageCount)
            .forEach(index => {
                const image = new Image();
                image.decoding = "async";
                image.src = pageUrl(index);
            });
    };

    const renderBookmarkTicks = () => {
        if (!bookmarkTicks) return;
        bookmarkTicks.replaceChildren();

        for (const bookmark of bookmarks.values()) {
            const button = document.createElement("button");
            button.type = "button";
            button.dataset.bookmarkTick = bookmark.id;
            button.style.left =
                (pageCount <= 1
                    ? 0
                    : bookmark.pageIndex / (pageCount - 1) * 100) + "%";
            button.title = bookmark.label || `Page ${bookmark.pageIndex + 1}`;
            button.setAttribute("aria-label", button.title);
            bookmarkTicks.append(button);
        }

        if (bookmarkCount) {
            bookmarkCount.textContent = String(bookmarks.size);
            bookmarkCount.hidden = bookmarks.size === 0;
        }

        bookmarkButton?.classList.toggle(
            "active",
            Array.from(bookmarks.values()).some(item => item.pageIndex === page));
    };

    const toggleBookmark = async () => {
        const existing = Array.from(bookmarks.values())
            .find(item => item.pageIndex === page);

        try {
            if (existing) {
                await postForm(removeBookmarkForm, data => {
                    data.set("bookmarkId", existing.id);
                });
                bookmarks.delete(existing.id);
                showToast("Bookmark removed");
            } else {
                const saved = await postForm(bookmarkForm, data => {
                    data.set("pageIndex", String(page));
                    data.set("label", "");
                });
                bookmarks.set(saved.id, {
                    id: saved.id,
                    pageIndex: saved.pageIndex,
                    label: saved.label || ""
                });
                showToast("Bookmark saved");
            }

            renderBookmarkTicks();
        } catch (error) {
            showToast(error.message || "Bookmark failed");
        }
    };

    const ensureChapters = () => {
        if (chaptersBuilt || !chapterList || !chapterData) return;

        let chapters = [];
        try {
            chapters = JSON.parse(chapterData.textContent || "[]");
        } catch {
            chapters = [];
        }

        const fragment = document.createDocumentFragment();
        for (const chapter of chapters) {
            const link = document.createElement("a");
            link.href = `/Manga/Read/${encodeURIComponent(chapter.id)}`;
            link.className = [
                "manga-drawer-row",
                chapter.current ? "current" : "",
                chapter.earlier ? "earlier" : ""
            ].filter(Boolean).join(" ");
            link.dataset.chapterRow = "";
            link.dataset.search =
                `${chapter.number} ${chapter.title}`.toLocaleLowerCase();

            const number = document.createElement("span");
            number.textContent = chapter.number;

            const title = document.createElement("strong");
            title.textContent = chapter.title;

            const pages = document.createElement("small");
            pages.textContent = `${chapter.pageCount}p`;

            link.append(number, title, pages);
            fragment.append(link);
        }

        chapterList.append(fragment);
        chaptersBuilt = true;
    };

    const setDrawer = open => {
        if (!drawer || !backdrop) return;
        if (open) ensureChapters();
        drawer.hidden = !open;
        backdrop.hidden = !open;
        document.documentElement.classList.toggle("manga-drawer-lock", open);

        if (open) {
            requestAnimationFrame(() => {
                drawer.querySelector(".manga-drawer-row.current")
                    ?.scrollIntoView({ block: "center" });
            });
        }
    };

    const applySettings = () => {
        const stored = readSettings();
        const sharedDefaultMode =
            ["single", "double", "continuous"].includes(shell.dataset.defaultMode)
                ? shell.dataset.defaultMode
                : "single";
        mode = ["single", "double", "continuous"].includes(stored.mode)
            ? stored.mode
            : (window.matchMedia("(max-width: 720px)").matches
                ? (sharedDefaultMode === "continuous" ? "continuous" : "single")
                : sharedDefaultMode);
        direction = stored.direction === "ltr"
            ? "ltr"
            : stored.direction === "rtl"
                ? "rtl"
                : direction;
        fit = stored.fit === "width" ? "width" : "height";
        zoom = clamp(Number(stored.zoom || 1), .6, 2.5);
    };

    shell.querySelectorAll("[data-saved-bookmark]").forEach(element => {
        bookmarks.set(element.dataset.bookmarkId, {
            id: element.dataset.bookmarkId,
            pageIndex: Number(element.dataset.page || 0),
            label: element.dataset.label || ""
        });
    });

    document.addEventListener("click", event => {
        const modeButton = event.target.closest("[data-mode]");
        if (modeButton) {
            mode = modeButton.dataset.mode;
            if (mode === "double") {
                page -= page % 2;
            }
            writeSettings();
            render(mode === "continuous");
            queueProgress();
            return;
        }

        if (event.target.closest("[data-direction-toggle]")) {
            direction = direction === "rtl" ? "ltr" : "rtl";
            writeSettings();
            render();
            return;
        }

        const fitButton = event.target.closest("[data-fit]");
        if (fitButton) {
            fit = fitButton.dataset.fit === "width" ? "width" : "height";
            writeSettings();
            render();
            return;
        }

        const zoomButton = event.target.closest("[data-zoom]");
        if (zoomButton) {
            zoom = clamp(
                zoom + (zoomButton.dataset.zoom === "+" ? .1 : -.1),
                .6,
                2.5);
            writeSettings();
            updateChrome();
            return;
        }

        if (event.target.closest("[data-bookmark]")) {
            void toggleBookmark();
            return;
        }

        const tick = event.target.closest("[data-bookmark-tick]");
        if (tick) {
            const bookmark = bookmarks.get(tick.dataset.bookmarkTick);
            if (bookmark) goTo(bookmark.pageIndex, { force: true });
            return;
        }

        if (event.target.closest("[data-chapters-toggle]")) {
            setDrawer(true);
            return;
        }

        if (event.target.closest("[data-drawer-close]") ||
            event.target.closest("[data-drawer-backdrop]")) {
            setDrawer(false);
            return;
        }

        const tap = event.target.closest("[data-tap]");
        if (tap && mode !== "continuous") {
            const isStart = tap.dataset.tap === "start";
            const next = direction === "rtl" ? isStart : !isStart;
            next ? forward() : back();
        }
    });

    scrubber?.addEventListener("input", event => {
        const next = Number(event.target.value);
        if (Number.isInteger(next)) goTo(next);
    });

    chapterSearch?.addEventListener("input", event => {
        ensureChapters();
        const query = (event.target.value || "").trim().toLocaleLowerCase();
        shell.querySelectorAll("[data-chapter-row]").forEach(row => {
            row.hidden = query.length > 0 && !(row.dataset.search || "").includes(query);
        });
    });

    document.addEventListener("keydown", event => {
        if (event.target.matches("input, textarea, select")) return;

        if (event.key === "Escape") {
            setDrawer(false);
            return;
        }

        if (event.key === "ArrowLeft") {
            event.preventDefault();
            direction === "rtl" ? forward() : back();
        } else if (event.key === "ArrowRight") {
            event.preventDefault();
            direction === "rtl" ? back() : forward();
        } else if (event.key.toLowerCase() === "b") {
            void toggleBookmark();
        }
    });

    stage?.addEventListener("touchstart", event => {
        touchX = event.changedTouches[0]?.clientX ?? null;
    }, { passive: true });

    stage?.addEventListener("touchend", event => {
        if (touchX == null || mode === "continuous") return;
        const endX = event.changedTouches[0]?.clientX ?? touchX;
        const delta = endX - touchX;
        touchX = null;

        if (Math.abs(delta) < 55) return;
        const swipeRight = delta > 0;
        const next = direction === "rtl" ? swipeRight : !swipeRight;
        next ? forward() : back();
    }, { passive: true });

    window.addEventListener("pagehide", saveProgress);

    applySettings();
    render();
    queueProgress();
})();
