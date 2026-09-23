(() => {
    const shell = document.querySelector("[data-novel-reader]");
    if (!shell) return;

    const workKey = shell.dataset.workId || "default";
    const viewKey = "anilingo.novel.view";
    const sizeKey = "anilingo.novel.size";
    const leadingKey = "anilingo.novel.leading";
    const widthKey = "anilingo.novel.width";

    const applyView = view => {
        const hasTranslation = shell.dataset.hasTranslation === "true";
        const allowed = hasTranslation ? ["ja", "de", "both"] : ["ja"];
        const next = allowed.includes(view) ? view : "ja";
        shell.dataset.view = next;
        localStorage.setItem(viewKey, next);

        document.querySelectorAll("[data-reader-view]").forEach(button => {
            button.setAttribute("aria-pressed", button.dataset.readerView === next ? "true" : "false");
        });
    };

    const applyNumber = (name, value, min, max, unit) => {
        const number = Math.min(max, Math.max(min, Number(value)));
        if (!Number.isFinite(number)) return;
        document.documentElement.style.setProperty(name, number + unit);
        return number;
    };

    applyView(localStorage.getItem(viewKey) || shell.dataset.view || "ja");

    const savedSize = localStorage.getItem(sizeKey);
    if (savedSize) applyNumber("--novel-reader-size", savedSize, .85, 1.65, "rem");

    const savedLeading = localStorage.getItem(leadingKey);
    if (savedLeading) applyNumber("--novel-reader-leading", savedLeading, 1.45, 2.5, "");

    const savedWidth = localStorage.getItem(widthKey);
    if (savedWidth) applyNumber("--novel-reader-width", savedWidth, 560, 1050, "px");

    document.addEventListener("click", event => {
        const viewButton = event.target.closest("[data-reader-view]");
        if (viewButton) {
            applyView(viewButton.dataset.readerView);
            return;
        }

        const adjust = event.target.closest("[data-reader-adjust]");
        if (!adjust) return;

        const action = adjust.dataset.readerAdjust;
        if (action === "size-up" || action === "size-down") {
            const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--novel-reader-size")) || 1.08;
            const value = applyNumber("--novel-reader-size", current + (action === "size-up" ? .08 : -.08), .85, 1.65, "rem");
            if (value) localStorage.setItem(sizeKey, value);
        } else if (action === "leading") {
            const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--novel-reader-leading")) || 2;
            const value = current >= 2.35 ? 1.6 : current + .15;
            applyNumber("--novel-reader-leading", value, 1.45, 2.5, "");
            localStorage.setItem(leadingKey, value);
        } else if (action === "width") {
            const current = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--novel-reader-width")) || 780;
            const value = current >= 980 ? 620 : current + 120;
            applyNumber("--novel-reader-width", value, 560, 1050, "px");
            localStorage.setItem(widthKey, value);
        }
    });

    const form = document.querySelector("[data-progress-form]");
    let timer = null;
    let lastSent = -1;

    const positionPermille = () => {
        const max = document.documentElement.scrollHeight - window.innerHeight;
        if (max <= 0) return 1000;
        return Math.max(0, Math.min(1000, Math.round(window.scrollY / max * 1000)));
    };

    const sendProgress = () => {
        if (!form) return;
        const value = positionPermille();
        if (Math.abs(value - lastSent) < 5) return;
        lastSent = value;

        const data = new FormData(form);
        data.set("positionPermille", String(value));

        fetch(form.action, {
            method: "POST",
            body: data,
            credentials: "same-origin",
            headers: { "X-Requested-With": "fetch" },
            keepalive: true
        }).catch(() => {});
    };

    window.addEventListener("scroll", () => {
        clearTimeout(timer);
        timer = setTimeout(sendProgress, 1200);
    }, { passive: true });

    window.addEventListener("pagehide", sendProgress);

    const initial = Number(shell.dataset.progressPermille || 0);
    if (initial > 10 && window.scrollY === 0) {
        requestAnimationFrame(() => {
            const max = document.documentElement.scrollHeight - window.innerHeight;
            if (max > 0) window.scrollTo({ top: max * initial / 1000, behavior: "instant" });
        });
    }
})();
