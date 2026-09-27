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
    const genreChip = root.querySelector("[data-discover-genre-chip]");
    const genreLabel = root.querySelector("[data-discover-genre-label]");
    const genreClear = root.querySelector("[data-discover-genre-clear]");
    const importDetails = root.querySelector("[data-discover-import]");
    const importTitle = root.querySelector("[data-import-title]");
    const importProvider = root.querySelector("[data-import-provider]");
    const importExternalId = root.querySelector("[data-import-external-id]");
    const importUrl = root.querySelector("[data-import-url]");
    const importClear = root.querySelector("[data-import-clear]");
    const token = root.querySelector("input[name='__RequestVerificationToken']")?.value || "";
    // "add", "request" or "" per AniList category, from the owner's access rules.
    const addActions = {
        "anime": root.dataset.addAnime || "",
        "manga": root.dataset.addManga || "",
        "light-novel": root.dataset.addLightNovel || ""
    };
    const statusText = status =>
        root.dataset[`status${status.charAt(0).toUpperCase()}${status.slice(1)}`] || status;

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
        const genre = (params.get("genre") || "").trim();
        if (requestedMode !== "search") browseMode = requestedMode;

        return {
            query,
            category,
            mode: query ? "search" : requestedMode,
            genre
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

        if (genreChip) {
            genreChip.hidden = !state.genre;
            if (state.genre) genreLabel.textContent = state.genre;
        }
    }

    function updateUrl(push) {
        const params = new URLSearchParams();
        if (state.query.trim()) params.set("q", state.query.trim());
        if (state.category !== "all") params.set("category", state.category);
        const urlMode = state.mode === "search" ? browseMode : state.mode;
        if (urlMode !== "trending") params.set("mode", urlMode);
        if (state.genre) params.set("genre", state.genre);

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
        if (state.genre) params.set("genre", state.genre);

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
            local.textContent = root.dataset.textInLibrary || "In library";
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

        // One primary action (open, add or request, import), then one follow control, then
        // the provider page as a small link.
        if (item.isLocal && item.localUrl) {
            actions.append(createLink(item.localUrl, root.dataset.textOpenLocal || "Open", true));
        } else if (item.category === "book") {
            actions.append(createLink(item.detailsUrl, "Book details", true));
        } else if (
            item.category === "manga" &&
            item.detailsUrl?.startsWith("/Discover/MangaImport")) {
            actions.append(createLink(item.detailsUrl, "Add manga", true));
        }

        if (!item.isLocal && addActions[item.category]) {
            renderAddAction(item, actions);
        }

        if (item.canImportSource) {
            const source = document.createElement("button");
            source.type = "button";
            source.className = "primary";
            source.textContent = "Add source";
            source.addEventListener("click", () => openImport(item));
            actions.append(source);
        }

        renderFollowControl(item, actions);

        const providerUrl = aniListUrl(item);
        if (providerUrl) {
            const link = createLink(providerUrl, "AniList", false);
            link.classList.add("discover-provider-link");
            actions.append(link);
        }

        copy.append(actions);
        card.append(cover, copy);
        return card;
    }

    function aniListUrl(item) {
        if (item.provider !== "anilist" || !/^\d+$/.test(String(item.externalId || ""))) return null;
        const kind = item.category === "anime" ? "anime" : "manga";
        return `https://anilist.co/${kind}/${item.externalId}`;
    }

    async function postForm(url, fields) {
        const body = new FormData();
        Object.entries(fields).forEach(([name, value]) => {
            if (value !== null && value !== undefined && value !== "") body.set(name, String(value));
        });
        body.set("__RequestVerificationToken", token);
        const response = await fetch(url, {
            method: "POST",
            body,
            credentials: "same-origin",
            headers: { Accept: "application/json" }
        });
        if (!response.ok) throw new Error(String(response.status));
        return response.json();
    }

    // Follow toggle with a small menu for the franchise: one compact secondary control.
    function renderFollowControl(item, actions) {
        if (!root.dataset.watchlistUrl) return;

        const group = document.createElement("span");
        group.className = "discover-follow";

        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "discover-follow-toggle";
        const sync = () => {
            toggle.setAttribute("aria-pressed", String(item.isFollowed === true));
            toggle.textContent = item.isFollowed ? root.dataset.textFollowed : root.dataset.textFollow;
            toggle.title = item.isFollowed ? root.dataset.textUnfollow : root.dataset.textFollow;
        };
        sync();

        toggle.addEventListener("click", async () => {
            const follow = !item.isFollowed;
            toggle.disabled = true;
            try {
                const payload = await postForm(root.dataset.watchlistUrl, {
                    category: item.category,
                    provider: item.provider,
                    externalId: item.externalId,
                    title: item.title,
                    nativeTitle: item.nativeTitle,
                    coverImageUrl: item.coverImageUrl,
                    format: item.format,
                    status: item.status,
                    year: item.year,
                    follow
                });
                item.isFollowed = payload.followed === true;
                sync();
            } catch {
                toggle.title = root.dataset.textAddFailed || toggle.title;
            } finally {
                toggle.disabled = false;
            }
        });
        group.append(toggle);

        if (["anime", "manga", "light-novel"].includes(item.category) &&
            item.provider === "anilist" &&
            root.dataset.franchiseUrl) {
            group.append(...createFranchiseMenu(item));
        }

        actions.append(group);
    }

    function createFranchiseMenu(item) {
        const more = document.createElement("button");
        more.type = "button";
        more.className = "discover-follow-more";
        more.setAttribute("aria-haspopup", "menu");
        more.setAttribute("aria-expanded", "false");
        more.setAttribute("aria-label", root.dataset.textFollowOptions || "");
        more.title = root.dataset.textFollowOptions || "";
        more.textContent = "▾";

        const menu = document.createElement("div");
        menu.className = "discover-follow-menu";
        menu.setAttribute("role", "menu");
        menu.hidden = true;

        const followedLink = franchiseId => {
            const link = document.createElement("a");
            link.setAttribute("role", "menuitem");
            link.href = `/Franchises/${franchiseId}`;
            link.textContent = root.dataset.textFranchiseFollowed;
            return link;
        };

        if (item.followedFranchiseId) {
            menu.append(followedLink(item.followedFranchiseId));
        } else {
            const follow = document.createElement("button");
            follow.type = "button";
            follow.setAttribute("role", "menuitem");
            follow.textContent = root.dataset.textFollowFranchise;
            follow.addEventListener("click", async () => {
                follow.disabled = true;
                try {
                    const payload = await postForm(root.dataset.franchiseUrl, {
                        category: item.category,
                        provider: item.provider,
                        externalId: item.externalId
                    });
                    item.followedFranchiseId = payload.franchiseId;
                    menu.replaceChildren(followedLink(payload.franchiseId));
                    menu.firstElementChild.focus();
                } catch {
                    follow.title = root.dataset.textAddFailed || "";
                    follow.disabled = false;
                }
            });
            menu.append(follow);
        }

        const close = () => {
            menu.hidden = true;
            more.setAttribute("aria-expanded", "false");
            document.removeEventListener("click", onOutside, true);
        };
        const onOutside = event => {
            if (!menu.contains(event.target) && event.target !== more) close();
        };
        more.addEventListener("click", () => {
            if (!menu.hidden) {
                close();
                return;
            }
            menu.hidden = false;
            more.setAttribute("aria-expanded", "true");
            document.addEventListener("click", onOutside, true);
            menu.querySelector("a, button")?.focus();
        });
        menu.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                close();
                more.focus();
            }
        });

        return [more, menu];
    }

    function renderAddAction(item, actions) {
        const slot = document.createElement("span");
        slot.className = "discover-add";
        actions.append(slot);

        const showStatus = (status, resultUrl, message) => {
            const pill = document.createElement("span");
            pill.className = `status-pill request-status-${status}`;
            pill.textContent = statusText(status);
            if (message) pill.title = message;
            slot.replaceChildren(pill);
            if (resultUrl) slot.append(createLink(resultUrl, root.dataset.textOpen || "", false));
            // A failed add explains what to fix (for example a missing library root).
            if (status === "failed" && message) {
                const note = document.createElement("small");
                note.className = "discover-add-note";
                note.textContent = message;
                slot.append(note);
            }
        };

        if (item.requestStatus) {
            showStatus(item.requestStatus, null, null);
            return;
        }

        const action = addActions[item.category];
        const button = document.createElement("button");
        button.type = "button";
        button.className = "primary";
        button.textContent = action === "add" ? root.dataset.textAdd : root.dataset.textRequest;
        button.addEventListener("click", async () => {
            button.disabled = true;
            const body = new FormData();
            body.set("category", item.category);
            body.set("externalId", item.externalId);
            body.set("title", item.title);
            const subtitle = [item.format ? formatLabel(item.format) : null, item.year].filter(Boolean).join(" · ");
            if (subtitle) body.set("subtitle", subtitle);
            if (item.coverImageUrl) body.set("coverImageUrl", item.coverImageUrl);
            body.set("__RequestVerificationToken", token);
            try {
                const response = await fetch(root.dataset.addUrl, {
                    method: "POST",
                    body,
                    credentials: "same-origin",
                    headers: { Accept: "application/json" }
                });
                if (!response.ok) throw new Error(String(response.status));
                const payload = await response.json();
                item.requestStatus = payload.status;
                showStatus(payload.status, payload.resultUrl, payload.message);
            } catch {
                button.disabled = false;
                button.title = root.dataset.textAddFailed || "";
                button.textContent = root.dataset.textAddFailed || button.textContent;
            }
        });
        slot.append(button);
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

    genreClear?.addEventListener("click", () => {
        state.genre = "";
        syncControls();
        load(true);
    });

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
