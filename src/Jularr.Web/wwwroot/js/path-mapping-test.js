// Path test of the remote path mapping panel (#604). It only asks the server: the page handler applies
// AnimeImportSettingsState.TranslatePath, the same translation the importers use, and checks the
// result in the container. Nothing is mapped here. All server text is set with textContent.
(() => {
    const root = document.querySelector("[data-path-test]");
    if (!root) {
        return;
    }

    const form = document.getElementById(root.dataset.form);
    const sample = root.querySelector("[data-path-test-sample]");
    const run = root.querySelector("[data-path-test-run]");
    const result = root.querySelector("[data-path-test-result]");
    const note = root.querySelector("[data-path-test-note]");
    const labels = JSON.parse(root.querySelector("[data-path-test-config]").textContent).labels;
    if (!form || !sample || !run || !result || !note) {
        return;
    }

    const text = key => labels[key] ?? key;

    const line = (label, value, state) => {
        const group = document.createElement("div");
        const term = document.createElement("dt");
        term.textContent = label;
        const detail = document.createElement("dd");
        detail.textContent = value;
        if (state) {
            detail.dataset.state = state;
        }
        group.append(term, detail);
        return group;
    };

    // What is at the mapped path, as words and a state for the dot beside them.
    const outcome = check => {
        if (check.problem === "outsideStorage" || check.problem === "unavailable") {
            return { text: text(`storage.problem.${check.problem}`), state: "warn" };
        }
        if (check.kind === "file") {
            return { text: text("settings.acquisition.pathTest.file"), state: "ok" };
        }
        if (check.kind === "directory") {
            return { text: text("settings.acquisition.pathTest.folder"), state: "ok" };
        }
        if (check.kind === "missing" || check.problem === "notFound") {
            return { text: text("settings.acquisition.pathTest.missing"), state: "error" };
        }
        return {
            text: check.problem === "none" ? text("settings.acquisition.pathTest.missing") : text(`storage.problem.${check.problem}`),
            state: "error"
        };
    };

    const showFailure = () => {
        result.replaceChildren(line(text("settings.acquisition.pathTest.result"), text("settings.acquisition.pathTest.failed"), "error"));
        result.hidden = false;
        note.hidden = true;
    };

    const test = async () => {
        const path = sample.value.trim();
        if (path === "") {
            result.hidden = true;
            note.hidden = true;
            return;
        }

        const url = new URL(root.dataset.endpoint, window.location.href);
        url.searchParams.set("kind", form.elements.kind.value);
        url.searchParams.set("samplePath", path);
        // A mapping still being typed is applied as adding it would apply it.
        url.searchParams.set("remotePrefix", form.elements.remotePrefix.value.trim());
        url.searchParams.set("localPrefix", form.elements.localPrefix.value.trim());

        run.disabled = true;
        try {
            const response = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } });
            if (!response.ok) {
                showFailure();
                return;
            }

            const preview = await response.json();
            const rows = [
                line(text("settings.acquisition.pathTest.reported"), preview.reported),
                line(text("settings.acquisition.pathTest.mapped"), preview.mapped)
            ];
            if (preview.check) {
                const found = outcome(preview.check);
                rows.push(line(text("settings.acquisition.pathTest.result"), found.text, found.state));
            }
            result.replaceChildren(...rows);
            result.hidden = false;
            note.textContent = preview.changed ? "" : text("settings.acquisition.pathTest.unchanged");
            note.hidden = preview.changed;
        } catch {
            showFailure();
        } finally {
            run.disabled = false;
        }
    };

    run.addEventListener("click", () => void test());
    sample.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            void test();
        }
    });
})();
