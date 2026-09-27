// Novel in-work search (reader frame top bar): queries the reader's Search
// handler (ReaderTextSearch over the Japanese source and current German
// translations). Hits in the current chapter jump in place; other chapters open
// at the paragraph.
(() => {
    const registry = window.JularrNovelReader = window.JularrNovelReader || {};

    registry.search = reader => {
        const { shell, t } = reader;
        const form = shell.querySelector("[data-novel-search-form]");
        const input = shell.querySelector("[data-novel-search-input]");
        const status = shell.querySelector("[data-novel-search-status]");
        const results = shell.querySelector("[data-novel-search-results]");
        const searchUrl = shell.dataset.searchUrl || "";
        if (!form || !input || !status || !results || !searchUrl) return;

        const currentChapterId = (shell.dataset.chapterId || "").toLowerCase();
        let timer = 0;
        let run = 0;

        const languageOf = hit => hit.language === "original" ? "ja" : "de";

        const chapterHeading = hit =>
            t("chapterNumber", "Chapter {number}", { number: hit.chapterNumber }) +
            (hit.chapterTitle ? " · " + hit.chapterTitle : "");

        const render = hits => {
            results.replaceChildren();
            status.hidden = hits.length > 0;
            status.textContent = hits.length ? "" : t("noMatches", "No matches.");
            for (const hit of hits) {
                const language = languageOf(hit);
                const item = document.createElement("li");
                const link = document.createElement("a");
                link.href = reader.withQuery(`/Novels/Read/${encodeURIComponent(hit.chapterId)}`, {
                    paragraph: hit.paragraphIndex,
                    lang: language
                });
                const heading = document.createElement("strong");
                heading.textContent = chapterHeading(hit);
                const snippet = document.createElement("span");
                snippet.lang = language;
                const start = Number(hit.matchStart || 0);
                const length = Number(hit.matchLength || 0);
                const text = String(hit.snippet || "");
                const mark = document.createElement("mark");
                mark.textContent = text.slice(start, start + length);
                snippet.append(text.slice(0, start), mark, text.slice(start + length));
                link.append(heading, snippet);
                link.addEventListener("click", event => {
                    if (String(hit.chapterId).toLowerCase() !== currentChapterId) return;
                    event.preventDefault();
                    reader.frame()?.closeMenus(false);
                    if (reader.currentView() !== "both" && reader.currentView() !== language) {
                        reader.applyView(language);
                    }
                    requestAnimationFrame(() => shell.dispatchEvent(
                        new CustomEvent("jularr:novel-jump-paragraph", {
                            detail: { language, index: hit.paragraphIndex }
                        })));
                });
                item.append(link);
                results.append(item);
            }
        };

        const search = async () => {
            const query = input.value.trim();
            const id = ++run;
            if (query.length < 2) {
                results.replaceChildren();
                status.hidden = false;
                status.textContent = t("searchHint", "Type at least two characters.");
                return;
            }
            status.hidden = false;
            status.textContent = t("searching", "Searching…");
            try {
                const result = await reader.getJson(reader.withQuery(searchUrl, { q: query }));
                if (id === run) render(result.hits || []);
            } catch {
                if (id === run) status.textContent = t("searchFailed", "Search failed.");
            }
        };

        input.addEventListener("input", () => {
            window.clearTimeout(timer);
            timer = window.setTimeout(search, 300);
        });
        form.addEventListener("submit", event => {
            event.preventDefault();
            window.clearTimeout(timer);
            void search();
        });
    };
})();
