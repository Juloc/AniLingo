// EPUB file pickers on the novel pages: the native input stays in the form (validation,
// keyboard, screen readers) but is drawn as a normal button with the chosen file names,
// so the browser's own "Choose files / No file chosen" text in its UI language never shows.
(() => {
    document.querySelectorAll("[data-novel-file-picker]").forEach(picker => {
        const input = picker.querySelector("input[type=file]");
        const names = picker.querySelector("[data-novel-file-names]");
        if (!input || !names) return;

        const update = () => {
            const files = Array.from(input.files || []);
            names.textContent = files.length === 0
                ? names.dataset.empty || ""
                : files.length === 1
                    ? files[0].name
                    : (names.dataset.several || "{count}").replace("{count}", String(files.length));
            names.title = files.map(file => file.name).join("\n");
        };

        input.addEventListener("change", update);
        input.form?.addEventListener("reset", () => setTimeout(update));
        update();
    });
})();
