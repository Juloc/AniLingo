(() => {
    "use strict";

    // Overflow menus are <details>: they open and close natively, this only closes them when the
    // visitor clicks elsewhere, presses Escape or opens another one.
    const menus = () => document.querySelectorAll("details[data-ad-menu][open]");

    document.addEventListener("click", event => {
        for (const menu of menus()) {
            if (!menu.contains(event.target)) {
                menu.open = false;
            }
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") {
            return;
        }

        for (const menu of menus()) {
            menu.open = false;
            menu.querySelector("summary")?.focus();
        }
    });

    // Changing the sort applies it right away; without script the form keeps its own button.
    document.addEventListener("change", event => {
        const select = event.target;
        if (select instanceof HTMLSelectElement && select.form?.hasAttribute("data-ad-sort")) {
            select.form.requestSubmit();
        }
    });
})();
