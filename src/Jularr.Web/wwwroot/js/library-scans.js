// Keeps the library scan history current while a scan is queued or running by re-reading the
// same page and swapping in its history panel. Without JavaScript the page still works via reloads.
(() => {
    const panel = document.querySelector("[data-scan-history]");
    if (!panel) {
        return;
    }

    const intervalMs = 3000;

    const refresh = async () => {
        if (!panel.querySelector("[data-scan-active]")) {
            return;
        }

        try {
            const response = await fetch(window.location.href, {
                credentials: "same-origin",
                headers: { Accept: "text/html" }
            });

            if (response.ok) {
                const page = new DOMParser().parseFromString(await response.text(), "text/html");
                const next = page.querySelector("[data-scan-history]");
                if (next) {
                    panel.replaceChildren(...next.childNodes);
                }
            }
        } catch {
            // Keep the last known state and try again on the next tick.
        }

        setTimeout(() => void refresh(), intervalMs);
    };

    setTimeout(() => void refresh(), intervalMs);
})();
