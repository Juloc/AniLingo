// Books page: client-side tabs, filters, sort and view over the server-rendered shelf, plus the
// "Add book" dialog (catalog search → add or request through the access policy). The Offline tab
// is applied by offline-library-ui.js through the shared data-library-filter-option buttons.
(() => {
    const library = document.querySelector("[data-books-library]");
    const grid = library?.querySelector("[data-books-grid]");
    const tiles = grid ? Array.from(grid.querySelectorAll(".book-tile")) : [];
    const textFilter = document.querySelector("[data-books-filter-text]");
    const genreFilter = library?.querySelector("[data-books-genre]");
    const sortSelect = library?.querySelector("[data-books-sort]");
    const noMatches = library?.querySelector("[data-books-no-matches]");
    const panels = library ? Array.from(library.querySelectorAll("[data-books-panel]")) : [];
    const tabs = library ? Array.from(library.querySelectorAll("[data-books-tab]")) : [];
    const viewKey = "jularr.books.view";
    let tab = "all";

    const normalize = (value) => (value || "").toLocaleLowerCase();

    const apply = () => {
        const text = normalize(textFilter?.value.trim());
        const genre = genreFilter?.value || "";
        let visible = 0;
        for (const tile of tiles) {
            const progress = Number(tile.dataset.progress || 0);
            const matchesTab = tab === "reading" ? progress > 0 && progress < 990
                : tab === "finished" ? progress >= 990
                : true;
            const matchesText = !text
                || normalize(tile.dataset.title).includes(text)
                || normalize(tile.dataset.author).includes(text)
                || normalize(tile.dataset.genres).includes(text);
            const matchesGenre = !genre || (tile.dataset.genres || "").split("|").includes(genre);
            const show = matchesTab && matchesText && matchesGenre;
            tile.classList.toggle("is-filter-hidden", !show);
            if (show && !tile.classList.contains("is-offline-hidden")) visible++;
        }
        if (noMatches) noMatches.hidden = tiles.length === 0 || visible > 0;
    };

    const sort = () => {
        if (!grid || !sortSelect) return;
        const key = sortSelect.value;
        const by = {
            recent: (a, b) => (b.dataset.lastRead || "").localeCompare(a.dataset.lastRead || ""),
            title: (a, b) => a.dataset.title.localeCompare(b.dataset.title),
            author: (a, b) => (a.dataset.author || "￿").localeCompare(b.dataset.author || "￿"),
            progress: (a, b) => Number(b.dataset.progress) - Number(a.dataset.progress)
        }[key];
        if (!by) return;
        for (const tile of [...tiles].sort(by)) grid.append(tile);
    };

    for (const button of tabs) {
        button.addEventListener("click", () => {
            tab = button.dataset.booksTab;
            for (const other of tabs) {
                const active = other === button;
                other.classList.toggle("active", active);
                other.setAttribute("aria-selected", String(active));
            }
            for (const panel of panels) {
                panel.hidden = panel.dataset.booksPanel !== (tab === "requests" ? "requests" : "library");
            }
            // The offline filter re-evaluates on its own click handler; re-apply after it ran.
            window.setTimeout(apply, 0);
        });
    }
    textFilter?.addEventListener("input", apply);
    genreFilter?.addEventListener("change", apply);
    sortSelect?.addEventListener("change", sort);

    const setView = (view) => {
        if (!grid) return;
        grid.dataset.view = view;
        for (const button of library.querySelectorAll("[data-books-view]")) {
            const active = button.dataset.booksView === view;
            button.classList.toggle("active", active);
            button.setAttribute("aria-pressed", String(active));
        }
        try { localStorage.setItem(viewKey, view); } catch { /* storage unavailable */ }
    };
    for (const button of library?.querySelectorAll("[data-books-view]") || []) {
        button.addEventListener("click", () => setView(button.dataset.booksView));
    }
    try {
        const stored = localStorage.getItem(viewKey);
        if (stored === "list" || stored === "grid") setView(stored);
    } catch { /* storage unavailable */ }

    // Close any open ⋮ menu when clicking elsewhere.
    document.addEventListener("click", event => {
        for (const menu of document.querySelectorAll(".book-tile-menu[open]")) {
            if (!menu.contains(event.target)) menu.open = false;
        }
    });

    sort();
    apply();
})();

(() => {
    const dialog = document.querySelector("[data-books-add]");
    if (!dialog) return;

    for (const opener of document.querySelectorAll("[data-open-dialog='books-add']")) {
        opener.addEventListener("click", () => {
            dialog.showModal();
            dialog.querySelector("[data-books-add-query]")?.focus();
        });
    }

    const form = dialog.querySelector("[data-books-add-search]");
    const query = dialog.querySelector("[data-books-add-query]");
    const state = dialog.querySelector("[data-books-add-state]");
    const results = dialog.querySelector("[data-books-add-results]");
    const token = dialog.querySelector("input[name='__RequestVerificationToken']")?.value || "";
    const text = (key) => dialog.dataset[key] || "";
    const statusText = (status) => dialog.dataset[`status${status.charAt(0).toUpperCase()}${status.slice(1)}`] || status;
    let controller = null;

    const element = (tag, className, content) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (content !== undefined) node.textContent = content;
        return node;
    };

    const renderAction = (item, slot) => {
        slot.replaceChildren();
        if (item.libraryWorkId) {
            const link = element("a", "button", text("textInLibrary"));
            link.href = `/Books/Library/${item.libraryWorkId}`;
            slot.append(link);
            return;
        }
        if (item.requestStatus) {
            slot.append(element("span", `status-pill request-status-${item.requestStatus}`, statusText(item.requestStatus)));
            if (item.resultUrl) {
                const link = element("a", "button", text("textOpen"));
                link.href = item.resultUrl;
                slot.append(link);
            }
            return;
        }
        const button = element("button", "button button-primary", dialog.dataset.createsRequest === "true" ? text("textRequest") : text("textAdd"));
        button.type = "button";
        button.addEventListener("click", async () => {
            button.disabled = true;
            // Adding looks for a free edition and then searches the indexers; show that it is working.
            const busy = element("span", "status-pill request-status-searching", statusText("searching"));
            slot.prepend(busy);
            const body = new FormData();
            body.set("catalogId", item.id);
            body.set("title", item.title);
            if (item.author) body.set("author", item.author);
            if (item.coverImageUrl) body.set("coverImageUrl", item.coverImageUrl);
            body.set("__RequestVerificationToken", token);
            try {
                const response = await fetch(dialog.dataset.addUrl, {
                    method: "POST",
                    body,
                    credentials: "same-origin",
                    headers: { Accept: "application/json" }
                });
                if (!response.ok) throw new Error(String(response.status));
                const payload = await response.json();
                item.requestStatus = payload.status;
                item.resultUrl = payload.resultUrl;
                renderAction(item, slot);
                if (payload.message) slot.append(element("small", "books-add-note", payload.message));
            } catch {
                busy.remove();
                button.disabled = false;
                state.textContent = text("textFailed");
            }
        });
        slot.append(button);
    };

    const render = (items) => {
        results.replaceChildren();
        for (const item of items) {
            const row = element("li", "books-add-result");
            const cover = element("div", "books-add-cover");
            if (item.coverImageUrl) {
                const image = document.createElement("img");
                image.src = item.coverImageUrl;
                image.alt = "";
                image.loading = "lazy";
                image.referrerPolicy = "no-referrer";
                cover.append(image);
            }
            const copy = element("div", "books-add-copy");
            copy.append(element("strong", null, item.title));
            const meta = [item.author, item.firstPublishYear, item.sourceName].filter(Boolean).join(" · ");
            if (meta) copy.append(element("span", null, meta));
            if (item.freeEdition) copy.append(element("small", "books-add-free", text("textFree")));
            const slot = element("div", "books-add-action");
            renderAction(item, slot);
            row.append(cover, copy, slot);
            results.append(row);
        }
    };

    form?.addEventListener("submit", async event => {
        event.preventDefault();
        const value = query.value.trim();
        if (value.length < 2) return;
        controller?.abort();
        controller = new AbortController();
        state.textContent = text("textSearching");
        results.replaceChildren();
        try {
            const url = new URL(dialog.dataset.searchUrl, window.location.origin);
            url.searchParams.set("q", value);
            const response = await fetch(url, { credentials: "same-origin", signal: controller.signal, headers: { Accept: "application/json" } });
            const payload = await response.json();
            state.textContent = payload.error || (payload.results.length === 0 ? text("textNoResults") : "");
            render(payload.results);
        } catch (error) {
            if (error.name !== "AbortError") state.textContent = text("textFailed");
        }
    });

    // Deep links like /Books?q=dune open the dialog with the query.
    const initial = new URLSearchParams(window.location.search).get("q");
    if (initial && query) {
        dialog.showModal();
        query.value = initial;
        form.requestSubmit();
    }
})();
