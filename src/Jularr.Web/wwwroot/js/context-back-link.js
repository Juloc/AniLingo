(() => {
    "use strict";

    document.addEventListener("click", event => {
        const link = event.target.closest("a[data-context-back]");
        if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        if (!document.referrer || window.history.length < 2) {
            return;
        }

        let previous;
        try {
            previous = new URL(document.referrer);
        } catch {
            return;
        }

        if (previous.origin !== window.location.origin || previous.href === window.location.href) {
            return;
        }

        event.preventDefault();
        window.history.back();
    });
})();
