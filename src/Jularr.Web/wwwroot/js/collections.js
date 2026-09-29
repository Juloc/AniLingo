// Manual-collection work picker (#427): a debounced title search over media-core works that adds the
// chosen work to the collection by submitting the hidden add form. Progressive enhancement only — the
// page works without it, this just makes adding works pleasant.
(function () {
    "use strict";

    const manage = document.querySelector(".collections-manage");
    if (!manage) {
        return;
    }

    const searchUrl = manage.getAttribute("data-search-url");
    const input = document.getElementById("collection-work-search");
    const results = document.getElementById("collection-work-results");
    const addForm = document.getElementById("collection-add-form");
    const addWorkId = document.getElementById("collection-add-workid");
    if (!searchUrl || !input || !results || !addForm || !addWorkId) {
        return;
    }

    let timer = null;
    let controller = null;

    function clear() {
        results.textContent = "";
    }

    function render(items) {
        clear();
        if (!Array.isArray(items) || items.length === 0) {
            return;
        }

        for (const item of items) {
            const li = document.createElement("li");
            const button = document.createElement("button");
            button.type = "button";
            button.className = "collections-search-hit";
            const year = item.year ? " (" + item.year + ")" : "";
            button.textContent = item.title + year + " — " + item.mediaType;
            button.addEventListener("click", function () {
                addWorkId.value = item.id;
                addForm.submit();
            });
            li.appendChild(button);
            results.appendChild(li);
        }
    }

    async function search(query) {
        if (controller) {
            controller.abort();
        }
        controller = new AbortController();
        try {
            const url = searchUrl + (searchUrl.indexOf("?") >= 0 ? "&" : "?") + "q=" + encodeURIComponent(query);
            const response = await fetch(url, { signal: controller.signal, headers: { "Accept": "application/json" } });
            if (!response.ok) {
                return;
            }
            render(await response.json());
        } catch (error) {
            if (error && error.name !== "AbortError") {
                clear();
            }
        }
    }

    input.addEventListener("input", function () {
        const query = input.value.trim();
        if (timer) {
            clearTimeout(timer);
        }
        if (query.length < 2) {
            clear();
            return;
        }
        timer = setTimeout(function () { search(query); }, 250);
    });
})();
