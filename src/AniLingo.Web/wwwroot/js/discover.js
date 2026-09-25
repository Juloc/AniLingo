(() => {
    const root = document.querySelector("[data-discover]");
    if (!root) return;

    const searchInput = root.querySelector("[data-discover-search]");
    const results = root.querySelector("[data-discover-results]");
    const empty = root.querySelector("[data-discover-empty]");
    const title = root.querySelector("[data-discover-title]");
    const count = root.querySelector("[data-discover-count]");
    const warning = root.querySelector("[data-discover-warning]");
    const modeButtons = [...root.querySelectorAll("[data-discover-mode]")];
    const categoryButtons = [...root.querySelectorAll("[data-discover-category]")];
    const importDetails = root.querySelector("[data-discover-import]");
    const importTitle = root.querySelector("[data-import-title]");
    const importProvider = root.querySelector("[data-import-provider]");
    const importExternalId = root.querySelector("[data-import-external-id]");
    const importUrl = root.querySelector("[data-import-url]");
    const importClear = root.querySelector("[data-import-clear]");

    let abortController = null;
    let debounceTimer = null;
    let requestVersion = 0;
    let browseMode = "trending";
    let state = readState();

    function readState() {
        const params = new URLSearchParams(window.location.search);
        const query = (params.get("q") || "").trim();
        const category = normalizeCategory(params.get("category"));
        const requestedMode = normalizeMode(params.get("mode"));
        if (requestedMode !== "search") browseMode = requestedMode;

        return {
            query,
            category,
            mode: query ? "search" : requestedMode
        };
    }

    function normalizeCategory(value) {
        return ["all", "anime", "light-novel", "manga", "book"].includes(value)
            ? value
            : "all";
    }

    function normalizeMode(value) {
        return ["trending", "top", "my-list", "search"].includes(value)
            ? value
            : "trending";
    }

    function categoryLabel(value) {
        return {
            "all": "All",
            "anime": "Anime",
            "light-novel": "Light novel",
            "manga": "Manga",
            "book": "Book"
        }[value] || "All";
    }

    function modeLabel(value) {
        return {
            "trending": "Trending",
            "top": "Top",
            "my-list": "My AniList",
            "search": "Search"
        }[value] || "Trending";
    }

    function syncControls(syncSearchValue = true) {
        if (syncSearchValue) searchInput.value = state.query;

        modeButtons.forEach(button => {
            const active = button.dataset.discoverMode ===
                (state.mode === "search" ? browseMode : state.mode);
            button.classList.toggle("active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
        });

        categoryButtons.forEach(button => {
            const active = button.dataset.discoverCategory === state.category;
            button.classList.toggle("active", active);
            button.setAttribute("aria-pressed", active ? "true" : "false");
        });
    }

    function updateUrl(push) {
        const params = new URLSearchParams();
        if (state.query.trim()) params.set("q", state.query.trim());
        if (state.category !== "all") params.set("category", state.category);
        const urlMode = state.mode === "search" ? browseMode : state.mode;
        if (urlMode !== "trending") params.set("mode", urlMode);

        const query = params.toString();
        const url = query
            ? `${window.location.pathname}?${query}`
            : window.location.pathname;

        window.history[push ? "pushState" : "replaceState"]({}, "", url);
    }

    function scheduleSearch() {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => load(false), 250);
    }

    async function load(pushHistory, fromPopState = false) {
        clearTimeout(debounceTimer);

        if (!fromPopState) updateUrl(pushHistory);

        abortController?.abort();
        abortController = new AbortController();
        const version = ++requestVersion;
        const sources = providerSources();

        results.setAttribute("aria-busy", "true");
        count.textContent = state.query.trim() ? "Searching…" : "Loading…";
        warning.hidden = true;

        const payloads = [];
        const failures = [];

        const tasks = sources.map(async source => {
            try {
                const payload = await loadSource(source, abortController.signal);
                if (version !== requestVersion) return;

                payloads.push(payload);
                renderCombined(
                    payloads,
                    failures,
                    Math.max(0, sources.length - payloads.length - failures.length));
            } catch (error) {
                if (error?.name === "AbortError" || version !== requestVersion) {
                    return;
                }

                failures.push(error?.message || "A discovery provider is unavailable.");
                renderCombined(
                    payloads,
                    failures,
                    Math.max(0, sources.length - payloads.length - failures.length));
            }
        });

        await Promise.allSettled(tasks);

        if (version === requestVersion) {
            results.setAttribute("aria-busy", "false");
            renderCombined(payloads, failures, 0);
        }
    }

    function providerSources() {
        if (state.mode === "my-list") return ["anilist"];
        if (state.category === "book") return ["books"];
        if (state.category !== "all") return ["anilist"];
        return ["anilist", "books"];
    }

    async function loadSource(source, signal) {
        const params = new URLSearchParams({
            handler: "Results",
            category: state.category,
            mode: state.mode,
            source
        });
        if (state.query.trim()) params.set("q", state.query.trim());

        const response = await fetch(
            `${window.location.pathname}?${params}`,
            {
                signal,
                cache: "no-store",
                headers: { "X-Requested-With": "fetch" }
            });

        if (!response.ok) {
            throw new Error(`Discovery returned HTTP ${response.status}.`);
        }

        return await response.json();
    }

    function renderCombined(payloads, failures, pendingCount) {
        const first = payloads[0] || {
            query: state.query.trim(),
            category: state.category,
            mode: state.mode,
            aniListConnected: false,
            items: [],
            warnings: []
        };

        const itemMap = new Map();
        const warnings = [...failures];
        let aniListConnected = false;

        payloads.forEach(payload => {
            aniListConnected ||= payload.aniListConnected === true;
            (payload.warnings || []).forEach(message => warnings.push(message));
            (payload.items || []).forEach(item => {
                if (!itemMap.has(item.id)) itemMap.set(item.id, item);
            });
        });

        render({
            ...first,
            query: first.query || state.query.trim(),
            category: state.category,
            mode: state.mode,
            aniListConnected,
            items: [...itemMap.values()],
            warnings: [...new Set(warnings)]
        }, pendingCount);
    }

    function render(payload, pendingCount = 0) {
        results.replaceChildren();

        const items = Array.isArray(payload.items) ? payload.items : [];
        const warnings = Array.isArray(payload.warnings) ? payload.warnings : [];

        title.textContent = state.query.trim()
            ? `Results for “${payload.query || state.query.trim()}”`
            : `${modeLabel(payload.mode)} · ${categoryLabel(payload.category)}`;

        count.textContent = pendingCount > 0
            ? `${items.length} shown · loading more…`
            : `${items.length} shown`;

        if (warnings.length) {
            warning.textContent = warnings.join(" ");
            warning.hidden = false;
        } else {
            warning.hidden = true;
        }

        empty.hidden = items.length !== 0 || pendingCount > 0;
        if (items.length === 0 && pendingCount === 0) {
            const strong = empty.querySelector("strong");
            const detail = empty.querySelector("span");

            if (payload.mode === "my-list" && payload.category === "book") {
                strong.textContent = "Books are not an AniList list type";
                detail.textContent = "Use Trending, Top or search to browse books.";
            } else if (payload.mode === "my-list" && !payload.aniListConnected) {
                strong.textContent = "AniList is not connected";
                detail.textContent = "Connect your account in Settings → AniList.";
            } else if (warnings.length && payloadsEmpty(payload)) {
                strong.textContent = "Search unavailable";
                detail.textContent = "Try again in a moment.";
            } else {
                strong.textContent = "No results";
                detail.textContent = "Try another title, category or browse mode.";
            }
            return;
        }

        const fragment = document.createDocumentFragment();
        items.forEach(item => fragment.append(createCard(item)));
        results.append(fragment);
    }

    function payloadsEmpty(payload) {
        return !Array.isArray(payload.items) || payload.items.length === 0;
    }

    function createCard(item) {
        const card = document.createElement("article");
        card.className = "discover-card";

        const cover = document.createElement("div");
        cover.className = "discover-cover";

        if (item.coverImageUrl) {
            const image = document.createElement("img");
            image.src = item.coverImageUrl;
            image.alt = "";
            image.loading = "lazy";
            image.decoding = "async";
            image.referrerPolicy = "no-referrer";
            cover.append(image);
        } else {
            const placeholder = document.createElement("span");
            placeholder.className = "discover-cover-placeholder";
            placeholder.textContent = item.category === "anime"
                ? "A"
                : item.category === "manga"
                    ? "漫"
                    : "文";
            cover.append(placeholder);
        }

        const copy = document.createElement("div");
        copy.className = "discover-card-copy";

        const kicker = document.createElement("div");
        kicker.className = "discover-card-kicker";

        const category = document.createElement("span");
        category.textContent = categoryLabel(item.category);
        kicker.append(category);

        if (item.isLocal) {
            const local = document.createElement("span");
            local.className = "discover-local";
            local.textContent = "In AniLingo";
            kicker.append(local);
        }

        const cardTitle = document.createElement("div");
        cardTitle.className = "discover-card-title";
        cardTitle.textContent = item.title || "Untitled";

        copy.append(kicker, cardTitle);

        if (item.nativeTitle && item.nativeTitle !== item.title) {
            const native = document.createElement("div");
            native.className = "discover-native-title";
            native.textContent = item.nativeTitle;
            copy.append(native);
        }

        const metaValues = [
            item.format ? formatLabel(item.format) : null,
            item.year || null,
            item.listStatus ? listStatusLabel(item.listStatus) : null
        ].filter(Boolean);

        if (metaValues.length) {
            const meta = document.createElement("div");
            meta.className = "discover-card-meta";
            metaValues.forEach(value => {
                const part = document.createElement("span");
                part.textContent = String(value);
                meta.append(part);
            });
            copy.append(meta);
        }

        if (Number.isFinite(item.progress)) {
            const progress = createProgress(item);
            if (progress) copy.append(progress);
        }

        const actions = document.createElement("div");
        actions.className = "discover-card-actions";

        if (item.isLocal && item.localUrl) {
            actions.append(createLink(item.localUrl, "Open", true));
        } else if (item.category === "book") {
            actions.append(createLink(item.detailsUrl, "Book details", true));
        } else if (
            item.category === "manga" &&
            item.detailsUrl?.startsWith("/Discover/MangaImport")) {
            actions.append(createLink(item.detailsUrl, "Add manga", true));
        } else {
            actions.append(createLink(item.detailsUrl, "AniList", false));
        }

        if (item.canImportSource) {
            const source = document.createElement("button");
            source.type = "button";
            source.className = "primary";
            source.textContent = "Add source";
            source.addEventListener("click", () => openImport(item));
            actions.append(source);
        }

        copy.append(actions);
        card.append(cover, copy);
        return card;
    }

    function createProgress(item) {
        const progressValue = Number(item.progress);
        if (!Number.isFinite(progressValue)) return null;

        const holder = document.createElement("div");
        holder.className = "discover-progress";

        const copy = document.createElement("div");
        copy.className = "discover-progress-copy";

        const label = document.createElement("span");
        label.textContent = item.category === "anime"
            ? "Episodes"
            : "Chapters";

        const value = document.createElement("span");
        value.textContent = item.totalProgress
            ? `${progressValue} / ${item.totalProgress}`
            : String(progressValue);

        copy.append(label, value);
        holder.append(copy);

        if (item.totalProgress && item.totalProgress > 0) {
            const track = document.createElement("div");
            track.className = "discover-progress-track";
            const fill = document.createElement("span");
            const percent = Math.min(
                100,
                Math.max(0, progressValue / item.totalProgress * 100));
            fill.style.width = `${percent}%`;
            track.append(fill);
            holder.append(track);
        }

        return holder;
    }

    function createLink(url, label, primary) {
        const link = document.createElement("a");
        link.href = url;
        link.textContent = label;
        if (primary) link.className = "primary";

        if (/^https?:\/\//i.test(url)) {
            link.target = "_blank";
            link.rel = "noopener noreferrer";
        }

        return link;
    }

    function formatLabel(value) {
        return String(value)
            .replaceAll("_", " ")
            .toLowerCase()
            .replace(/\b\w/g, char => char.toUpperCase());
    }

    function listStatusLabel(value) {
        const label = formatLabel(value);
        return label === "Current"
            ? "In progress"
            : label;
    }

    function openImport(item) {
        if (!importDetails || !importUrl) return;

        importDetails.open = true;
        importProvider.value = item.provider || "";
        importExternalId.value = item.externalId || "";
        importTitle.textContent = `Source for ${item.title}`;
        importDetails.scrollIntoView({ behavior: "smooth", block: "center" });
        setTimeout(() => importUrl.focus(), 250);
    }

    function clearImportContext() {
        if (!importProvider) return;
        importProvider.value = "";
        importExternalId.value = "";
        importTitle.textContent = "Source URL";
        importUrl?.focus();
    }

    function showError(message) {
        results.replaceChildren();
        empty.hidden = false;
        empty.querySelector("strong").textContent = "Search unavailable";
        empty.querySelector("span").textContent = message;
        warning.hidden = true;
        count.textContent = "Unavailable";
    }

    searchInput.addEventListener("input", () => {
        state.query = searchInput.value;
        state.mode = state.query.trim() ? "search" : browseMode;
        syncControls(false);
        scheduleSearch();
    });

    modeButtons.forEach(button => {
        button.addEventListener("click", () => {
            browseMode = button.dataset.discoverMode;
            state.query = "";
            searchInput.value = "";
            state.mode = browseMode;
            syncControls();
            load(true);
        });
    });

    categoryButtons.forEach(button => {
        button.addEventListener("click", () => {
            state.category = button.dataset.discoverCategory;
            state.mode = state.query ? "search" : browseMode;
            syncControls();
            load(true);
        });
    });

    importClear?.addEventListener("click", clearImportContext);

    document.addEventListener("keydown", event => {
        if (event.key !== "/" ||
            event.ctrlKey ||
            event.metaKey ||
            event.altKey ||
            /input|textarea|select/i.test(document.activeElement?.tagName || "")) {
            return;
        }

        event.preventDefault();
        searchInput.focus();
    });

    window.addEventListener("popstate", () => {
        state = readState();
        if (state.mode !== "search") browseMode = state.mode;
        syncControls();
        load(false, true);
    });

    if (state.mode !== "search") browseMode = state.mode;
    syncControls();
    load(false);
})();
