// Keeps the Activity center current by re-reading the same page and swapping in its live
// region (summary and lanes). Without JavaScript the page still works via reloads and the
// cancel/retry forms post normally.
(() => {
    const live = document.querySelector("[data-activity-live]");
    if (!live) {
        return;
    }

    const activeIntervalMs = 3000;
    const idleIntervalMs = 10000;
    let timer = 0;

    const schedule = () => {
        const busy = live.querySelector('[data-status="running"], [data-status="queued"]');
        timer = window.setTimeout(() => void refresh(), busy ? activeIntervalMs : idleIntervalMs);
    };

    const refresh = async () => {
        // Do not swap the DOM under a pending click or while the tab is in the background.
        const interacting = live.contains(document.activeElement)
            && document.activeElement instanceof HTMLButtonElement;
        if (!document.hidden && !interacting) {
            try {
                const response = await fetch(window.location.href, {
                    credentials: "same-origin",
                    headers: { Accept: "text/html" }
                });

                if (response.ok) {
                    const page = new DOMParser().parseFromString(await response.text(), "text/html");
                    const next = page.querySelector("[data-activity-live]");
                    if (next) {
                        live.replaceChildren(...next.childNodes);
                    }
                }
            } catch {
                // Keep the last known state and try again on the next tick.
            }
        }

        schedule();
    };

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden) {
            window.clearTimeout(timer);
            void refresh();
        }
    });

    schedule();
})();
