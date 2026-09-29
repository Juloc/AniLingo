// Shared folder browser (#604). One <dialog data-folder-browser> per page serves every path field
// (Pages/Shared/_PathField.cshtml): the Browse button opens it, and each field shows a live check of
// what the Jularr container sees at the typed path. Everything comes from /api/storage/folders, which
// lists folders only and never leaves the storage mounted into the container. All server text is put
// into the page with textContent, never as HTML.
(() => {
    const dialog = document.querySelector("[data-folder-browser]");
    const fieldElements = Array.from(document.querySelectorAll("[data-path-field='browse']"));
    if (!dialog || fieldElements.length === 0) {
        return;
    }

    const config = JSON.parse(dialog.querySelector("[data-folder-browser-config]").textContent);
    const labels = config.labels;
    const text = key => labels[key] ?? key;
    const problemText = code => text(`storage.problem.${code}`);

    const element = (tag, className, content) => {
        const node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (content !== undefined) {
            node.textContent = content;
        }
        return node;
    };

    const chip = (state, label) => {
        const item = element("li", "folder-check", label);
        item.dataset.state = state;
        return item;
    };

    const getJson = async (url, signal) => {
        const response = await fetch(url, {
            credentials: "same-origin",
            headers: { Accept: "application/json" },
            signal
        });
        let body = null;
        try {
            body = await response.json();
        } catch {
            body = null;
        }
        return { ok: response.ok, body };
    };

    // What the runtime user can do in a folder, as chips. `withExists` is off inside the browser,
    // where the folder being shown exists by definition.
    const renderAccess = (list, access, withExists) => {
        list.replaceChildren();
        if (access.unavailable) {
            list.append(chip("warn", text("storage.access.unavailable")));
            return;
        }
        if (withExists) {
            list.append(chip(access.exists ? "ok" : "error", text(access.exists ? "storage.access.exists" : "storage.access.missing")));
        }
        if (!access.exists) {
            return;
        }
        list.append(chip(access.readable ? "ok" : "error", text(access.readable ? "storage.access.readable" : "storage.access.notReadable")));
        if (access.readable) {
            // A read-only mount explains why nothing can be written; one chip says it.
            if (access.readOnlyMount) {
                list.append(chip("warn", text("storage.access.readOnly")));
            } else {
                list.append(chip(access.writable ? "ok" : "warn", text(access.writable ? "storage.access.writable" : "storage.access.notWritable")));
            }
        }
    };

    const renderCheck = (list, check) => {
        const refused = ["invalid", "notAbsolute", "traversal", "notADirectory"];
        const soft = ["outsideStorage", "unavailable"];
        if (refused.includes(check.problem)) {
            list.replaceChildren(chip("error", problemText(check.problem)));
            return;
        }
        if (soft.includes(check.problem)) {
            list.replaceChildren(chip("warn", problemText(check.problem)));
            return;
        }

        renderAccess(list, check.access, true);
        if (check.relation === "same") {
            list.append(chip("error", text("storage.pair.same")));
        } else if (check.relation === "nested") {
            list.append(chip("warn", text("storage.pair.nested")));
        }
    };

    // ---- Path fields: live check, Browse button -------------------------------------------------
    const fields = fieldElements.map(root => ({
        root,
        input: root.querySelector("input"),
        checks: root.querySelector("[data-path-checks]"),
        browse: root.querySelector("[data-path-browse]"),
        group: root.dataset.pairGroup || "",
        timer: 0,
        controller: null
    }));

    const peersOf = field => field.group === ""
        ? []
        : fields.filter(other => other !== field
            && other.group === field.group
            && other.root.closest("form") === field.root.closest("form"));

    const check = async field => {
        field.controller?.abort();
        const value = field.input.value.trim();
        if (value === "") {
            field.checks.replaceChildren();
            return;
        }

        const params = new URLSearchParams({ path: value });
        const other = peersOf(field).map(peer => peer.input.value.trim()).find(v => v !== "");
        if (other) {
            params.set("other", other);
        }

        field.controller = new AbortController();
        try {
            const { ok, body } = await getJson(`${config.endpoint}/check?${params}`, field.controller.signal);
            if (ok && body) {
                renderCheck(field.checks, body);
            } else {
                field.checks.replaceChildren();
            }
        } catch (error) {
            if (error.name !== "AbortError") {
                field.checks.replaceChildren();
            }
        }
    };

    const scheduleCheck = field => {
        clearTimeout(field.timer);
        field.timer = setTimeout(() => void check(field), 350);
    };

    for (const field of fields) {
        field.input.addEventListener("input", () => {
            scheduleCheck(field);
            peersOf(field).forEach(scheduleCheck);
        });
        field.browse?.addEventListener("click", () => void openBrowser(field));
        void check(field);
    }

    // ---- The dialog -----------------------------------------------------------------------------
    const parts = {
        crumbs: dialog.querySelector("[data-fb-crumbs]"),
        up: dialog.querySelector("[data-fb-up]"),
        newFolder: dialog.querySelector("[data-fb-new]"),
        access: dialog.querySelector("[data-fb-access]"),
        create: dialog.querySelector("[data-fb-create]"),
        name: dialog.querySelector("[data-fb-name]"),
        createSubmit: dialog.querySelector("[data-fb-create-submit]"),
        error: dialog.querySelector("[data-fb-error]"),
        list: dialog.querySelector("[data-fb-list]"),
        note: dialog.querySelector("[data-fb-note]"),
        cancel: dialog.querySelector("[data-fb-cancel]"),
        select: dialog.querySelector("[data-fb-select]")
    };

    let active = null;
    // The folder on show; null while the list of storage roots is on show.
    let current = null;
    let loadToken = 0;

    const showError = message => {
        parts.error.textContent = message;
        parts.error.hidden = false;
    };

    const clearError = () => {
        parts.error.textContent = "";
        parts.error.hidden = true;
    };

    const showNote = message => {
        parts.note.textContent = message ?? "";
        parts.note.hidden = !message;
    };

    const setBusy = busy => {
        parts.list.setAttribute("aria-busy", busy ? "true" : "false");
        dialog.classList.toggle("is-loading", busy);
    };

    const renderCrumbs = trail => {
        const items = [];
        trail.forEach((crumb, index) => {
            const last = index === trail.length - 1;
            if (index > 0) {
                items.push(element("span", "folder-crumb-separator", "›"));
            }
            if (last) {
                const label = element("span", "folder-crumb is-current", crumb.name);
                label.setAttribute("aria-current", "page");
                items.push(label);
            } else {
                const button = element("button", "folder-crumb", crumb.name);
                button.type = "button";
                button.addEventListener("click", () => void (crumb.path === null ? loadRoots() : loadFolder(crumb.path)));
                items.push(button);
            }
        });
        parts.crumbs.replaceChildren(...items);
    };

    const rootChip = access => {
        if (access.unavailable) {
            return chip("warn", text("storage.access.unavailable"));
        }
        if (!access.readable) {
            return chip("error", text("storage.access.notReadable"));
        }
        if (access.readOnlyMount) {
            return chip("warn", text("storage.access.readOnly"));
        }
        return access.writable
            ? chip("ok", text("storage.access.writable"))
            : chip("warn", text("storage.access.notWritable"));
    };

    const row = (path, name, meta, trailing) => {
        const item = element("li");
        const button = element("button", "folder-row");
        button.type = "button";
        button.append(element("span", "folder-row-name", name));
        if (meta) {
            button.append(element("span", "folder-row-meta", meta));
        }
        button.addEventListener("click", () => void loadFolder(path));
        item.append(button);
        if (trailing) {
            const marks = element("ul", "folder-checks");
            marks.append(trailing);
            item.append(marks);
        }
        return item;
    };

    const renderRoots = roots => {
        current = null;
        renderCrumbs([{ name: text("storage.folderBrowser.roots"), path: null }]);
        parts.up.disabled = true;
        parts.newFolder.hidden = true;
        parts.create.hidden = true;
        parts.select.disabled = true;
        parts.access.replaceChildren();
        if (roots === null) {
            parts.list.replaceChildren();
            showNote("");
            showError(text("storage.folderBrowser.loadFailed"));
            return;
        }
        parts.list.replaceChildren(...roots.map(root => row(root.path, root.path, root.fileSystem, rootChip(root.access))));
        showNote(roots.length === 0 ? text("storage.folderBrowser.noRoots") : "");
    };

    const renderFolder = listing => {
        current = listing;
        renderCrumbs([
            { name: text("storage.folderBrowser.roots"), path: null },
            ...listing.crumbs.map(crumb => ({ name: crumb.name, path: crumb.path }))
        ]);
        parts.up.disabled = false;
        parts.newFolder.hidden = false;
        parts.newFolder.disabled = !listing.access.writable;
        if (!listing.access.writable) {
            parts.create.hidden = true;
        }
        parts.select.disabled = false;
        renderAccess(parts.access, listing.access, false);
        parts.list.replaceChildren(...listing.folders.map(folder => row(folder.path, folder.name, "", null)));
        if (listing.truncated) {
            showNote(text("storage.folderBrowser.truncated").replaceAll("{count}", String(listing.folders.length)));
        } else {
            showNote(listing.folders.length === 0 ? text("storage.folderBrowser.empty") : "");
        }
    };

    const loadRoots = async () => {
        const token = ++loadToken;
        clearError();
        setBusy(true);
        let roots = null;
        try {
            const { ok, body } = await getJson(`${config.endpoint}/roots`);
            roots = ok && body ? body.roots : null;
        } catch {
            roots = null;
        }
        if (token !== loadToken) {
            return;
        }
        setBusy(false);
        renderRoots(roots);
    };

    // The folder above `path`, or null at the top; only used to find the deepest folder that exists
    // for a typed path. The server validates every request on its own.
    const parentOf = path => {
        const trimmed = path.replace(/[\\/]+$/, "");
        const cut = Math.max(trimmed.lastIndexOf("/"), trimmed.lastIndexOf("\\"));
        return cut > 0 ? trimmed.slice(0, cut) : null;
    };

    // `openingAt` walks up from a typed path that does not exist yet to the first folder that does.
    const loadFolder = async (path, openingAt = false) => {
        const token = ++loadToken;
        clearError();
        setBusy(true);
        let result;
        try {
            result = await getJson(`${config.endpoint}/list?path=${encodeURIComponent(path)}`);
        } catch {
            result = { ok: false, body: null };
        }
        if (token !== loadToken) {
            return;
        }
        setBusy(false);

        if (result.ok && result.body) {
            renderFolder(result.body);
            return;
        }

        const problem = result.body?.problem;
        const parent = parentOf(path);
        if (openingAt && problem === "notFound" && parent !== null) {
            await loadFolder(parent, true);
            return;
        }

        const message = problem ? problemText(problem) : text("storage.folderBrowser.loadFailed");
        if (openingAt) {
            await loadRoots();
        }
        showError(message);
    };

    const openBrowser = async field => {
        active = field;
        parts.create.hidden = true;
        clearError();
        dialog.showModal();
        const start = field.input.value.trim();
        if (start === "") {
            await loadRoots();
        } else {
            await loadFolder(start, true);
        }
    };

    const createErrorText = body => {
        switch (body?.outcome) {
            case "invalidName":
                return text(`storage.name.${body.nameProblem}`);
            case "invalidParent":
                return problemText(body.parentProblem);
            case "alreadyExists":
            case "readOnly":
            case "notWritable":
                return text(`storage.create.${body.outcome}`);
            default:
                return text("storage.create.failed");
        }
    };

    const createFolder = async () => {
        if (!current) {
            return;
        }
        clearError();
        parts.createSubmit.disabled = true;
        try {
            const response = await fetch(`${config.endpoint}/create`, {
                method: "POST",
                credentials: "same-origin",
                headers: { "Content-Type": "application/json", RequestVerificationToken: config.token },
                body: JSON.stringify({ parent: current.path, name: parts.name.value })
            });
            let body = null;
            try {
                body = await response.json();
            } catch {
                body = null;
            }
            if (response.ok && body?.outcome === "created") {
                parts.create.hidden = true;
                await loadFolder(body.path);
            } else {
                showError(createErrorText(body));
            }
        } catch {
            showError(text("storage.create.failed"));
        } finally {
            parts.createSubmit.disabled = false;
        }
    };

    parts.up.addEventListener("click", () => {
        if (current?.parent) {
            void loadFolder(current.parent);
        } else {
            void loadRoots();
        }
    });

    parts.newFolder.addEventListener("click", () => {
        parts.create.hidden = !parts.create.hidden;
        if (!parts.create.hidden) {
            parts.name.value = "";
            parts.name.focus();
        }
    });

    parts.createSubmit.addEventListener("click", () => void createFolder());
    parts.name.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            void createFolder();
        }
    });

    parts.select.addEventListener("click", () => {
        if (!current || !active) {
            return;
        }
        active.input.value = current.path;
        active.input.dispatchEvent(new Event("input", { bubbles: true }));
        dialog.close();
    });

    parts.cancel.addEventListener("click", () => dialog.close());
    dialog.addEventListener("close", () => {
        // The event arrives after close(); a browser opened again in between keeps its state.
        if (dialog.open) {
            return;
        }
        loadToken++;
        active = null;
        current = null;
    });
})();
