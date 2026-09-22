(() => {
    const root = document.querySelector("[data-episode-player]");
    if (!root) {
        return;
    }

    const video = root.querySelector("video");
    const overlay = root.querySelector("[data-subtitle-overlay]");
    const data = root.querySelector("[data-cue-data]");
    const inspector = root.querySelector("[data-word-inspector]");
    const word = root.querySelector("[data-word]");
    const reading = root.querySelector("[data-reading]");
    const meaning = root.querySelector("[data-meaning]");
    const state = root.querySelector("[data-state]");
    const replay = root.querySelector("[data-replay]");
    const error = root.querySelector("[data-player-error]");

    if (!video || !overlay || !data) {
        return;
    }

    let cues = [];
    try {
        cues = JSON.parse(data.textContent || "[]");
    } catch {
        error.hidden = false;
        error.textContent = "Subtitle data could not be loaded.";
        return;
    }

    let activeIndex = -2;
    let selectedCueStartMs = 0;

    const findCueIndex = (timeMs) => {
        let low = 0;
        let high = cues.length - 1;
        let candidate = -1;

        while (low <= high) {
            const middle = Math.floor((low + high) / 2);
            if (cues[middle].startMs <= timeMs) {
                candidate = middle;
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        if (candidate >= 0 && timeMs <= cues[candidate].endMs) {
            return candidate;
        }

        return -1;
    };

    const showWord = (token, cue) => {
        if (!inspector) {
            return;
        }

        video.pause();
        selectedCueStartMs = cue.startMs;
        word.textContent = token.canonical || token.surface;
        reading.textContent = token.reading || "";
        meaning.textContent = token.meaning || "No local meaning available.";
        state.textContent = token.state || "New";
        inspector.hidden = false;
    };

    const renderCue = (index) => {
        overlay.replaceChildren();

        if (index < 0) {
            return;
        }

        const cue = cues[index];
        for (const token of cue.tokens) {
            if (token.isVocabulary) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "subtitle-token";
                button.textContent = token.surface;
                button.title = [token.reading, token.meaning].filter(Boolean).join(" · ");
                button.addEventListener("click", () => showWord(token, cue));
                overlay.append(button);
            } else {
                const span = document.createElement("span");
                span.textContent = token.surface;
                overlay.append(span);
            }
        }
    };

    const sync = () => {
        const index = findCueIndex(Math.floor(video.currentTime * 1000));
        if (index === activeIndex) {
            return;
        }

        activeIndex = index;
        renderCue(index);
    };

    video.addEventListener("timeupdate", sync);
    video.addEventListener("seeked", sync);
    video.addEventListener("loadedmetadata", sync);
    video.addEventListener("error", () => {
        error.hidden = false;
        error.textContent = "This browser could not direct-play the media file. AniLingo does not transcode yet.";
    });

    replay?.addEventListener("click", () => {
        video.currentTime = Math.max(0, selectedCueStartMs / 1000 - 0.5);
        void video.play();
    });

    sync();
})();
