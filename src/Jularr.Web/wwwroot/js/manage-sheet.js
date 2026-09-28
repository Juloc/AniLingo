// Owner-only "Manage" sheet (#519, part of epic #510): opens the page's manage-sheet dialog
// from its trigger button, and auto-opens a sheet that already has something to show (e.g. a
// subtitle notice after a post-back, or a link from another page such as "#anime-manage").
// Guards every showModal() call against a dialog that is already open: a page can be reached
// through more than one script (this file plus the episode player's own dialog wiring), and
// re-opening an already-open <dialog> throws.
(() => {
    const open = dialog => {
        if (dialog instanceof HTMLDialogElement && !dialog.open) {
            dialog.showModal();
        }
    };

    for (const opener of document.querySelectorAll("[data-manage-sheet-open]")) {
        opener.addEventListener("click", () => {
            open(document.getElementById(opener.dataset.manageSheetOpen));
        });
    }

    for (const dialog of document.querySelectorAll("dialog.manage-sheet-dialog[data-open-on-load='true']")) {
        open(dialog);
    }

    // A link from another page's owner sources, e.g. "/Library/Anime/{id}#anime-manage".
    if (location.hash.length > 1) {
        open(document.getElementById(location.hash.slice(1)));
    }
})();
