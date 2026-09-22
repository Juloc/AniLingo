(() => {
    const root = document.querySelector("[data-episode-player]");
    if (!root) {
        return;
    }

    const preferenceKey = "anilingo.playbackMode";
    const video = root.querySelector("[data-playback-video]");
    const stage = root.querySelector("[data-video-stage]");
    const placeholder = root.querySelector("[data-playback-placeholder]");
    const playbackStatus = root.querySelector("[data-playback-status]");
    const playbackSummary = root.querySelector("[data-playback-summary]");
    const playbackBadge = root.querySelector("[data-playback-badge]");
    const modeSelect = root.querySelector("[data-playback-mode]");
    const modeHint = root.querySelector("[data-playback-mode-hint]");
    const working = root.querySelector("[data-playback-working]");
    const deviceForm = root.querySelector('[data-prepare-form="device"]');
    const serverForm = root.querySelector('[data-prepare-form="server"]');

    const overlay = root.querySelector("[data-subtitle-overlay]");
    const data = root.querySelector("[data-cue-data]");
    const inspector = root.querySelector("[data-word-inspector]");
    const word = root.querySelector("[data-word]");
    const reading = root.querySelector("[data-reading]");
    const meaning = root.querySelector("[data-meaning]");
    const state = root.querySelector("[data-state]");
    const replay = root.querySelector("[data-replay]");
    const error = root.querySelector("[data-player-error]");

    if (!video || !stage || !placeholder || !playbackStatus ||
        !playbackSummary || !playbackBadge || !modeSelect || !overlay || !data) {
        return;
    }

    const videoCodec = (root.dataset.videoCodec || "").toLowerCase();
    const isHevc = videoCodec === "hevc" || videoCodec === "h265";
    const capabilityProbe = document.createElement("video");
    const supportsHevc =
        capabilityProbe.canPlayType('video/mp4; codecs="hvc1"') !== "" ||
        capabilityProbe.canPlayType('video/mp4; codecs="hev1"') !== "";

    const options = {
        device: {
            availability: root.dataset.deviceAvailability || "unsupported",
            status: root.dataset.deviceStatus || "Device playback is unavailable."
        },
        server: {
            availability: root.dataset.serverAvailability || "unsupported",
            status: root.dataset.serverStatus || "Server playback is unavailable."
        }
    };

    const readPreference = () => {
        try {
            const value = window.localStorage.getItem(preferenceKey);
            return value === "device" || value === "server" ? value : "auto";
        } catch {
            return "auto";
        }
    };

    const storePreference = (value) => {
        try {
            window.localStorage.setItem(preferenceKey, value);
        } catch {
        }
    };

    let preference = readPreference();
    let runtimeDeviceFailed = false;
    let effectiveMode = "device";
    let refreshScheduled = false;
    modeSelect.value = preference;

    const deviceAllowed = () =>
        options.device.availability !== "unsupported" &&
        (!isHevc || supportsHevc) &&
        !runtimeDeviceFailed;

    const chooseMode = () => {
        if (preference === "server") {
            return "server";
        }

        if (preference === "device") {
            return "device";
        }

        return deviceAllowed() ? "device" : "server";
    };

    const buildMediaUrl = (mode) => {
        const url = new URL(root.dataset.mediaUrl || window.location.href, window.location.origin);
        url.searchParams.set("mode", mode);
        return url.toString();
    };

    const setBadge = (availability, mode) => {
        playbackBadge.classList.remove("status-ok", "status-warning", "status-error");

        if (availability === "ready") {
            playbackBadge.classList.add("status-ok");
            playbackBadge.textContent = mode === "device" ? "Device" : "Server";
        } else if (availability === "preparing" || availability === "canprepare") {
            playbackBadge.classList.add("status-warning");
            playbackBadge.textContent = availability === "preparing" ? "Working…" : "Preparation needed";
        } else {
            playbackBadge.classList.add("status-error");
            playbackBadge.textContent = availability === "failed" ? "Failed" : "Unavailable";
        }
    };

    const setModeHint = () => {
        if (preference === "device") {
            modeHint.textContent = isHevc && !supportsHevc
                ? "This browser does not report HEVC MP4 support. Device-only will not use server transcoding."
                : "Device only never video-transcodes on the server.";
            return;
        }

        if (preference === "server") {
            modeHint.textContent = "Server mode uses an H.264 compatibility stream when the source needs conversion.";
            return;
        }

        if (isHevc) {
            modeHint.textContent = supportsHevc
                ? "Auto detected HEVC support: keep HEVC and let this device decode it."
                : "Auto did not detect HEVC support: use the server H.264 fallback.";
            return;
        }

        modeHint.textContent = "Auto prefers direct play or remux and only uses server video transcoding when required.";
    };

    const hideVideo = () => {
        video.pause();
        video.hidden = true;
        placeholder.hidden = false;
        stage.classList.add("player-placeholder");
    };

    const showVideo = (mode) => {
        placeholder.hidden = true;
        video.hidden = false;
        stage.classList.remove("player-placeholder");

        if (video.dataset.playbackMode !== mode) {
            video.dataset.playbackMode = mode;
            video.src = buildMediaUrl(mode);
            video.load();
        }
    };

    const scheduleRefresh = () => {
        if (refreshScheduled) {
            return;
        }

        refreshScheduled = true;
        window.setTimeout(() => window.location.reload(), 2500);
    };

    const applyPlayback = () => {
        effectiveMode = chooseMode();
        let option = options[effectiveMode];

        if (effectiveMode === "device" && isHevc && !supportsHevc) {
            option = {
                availability: "unsupported",
                status: "This browser does not report HEVC MP4 support."
            };
        }

        setModeHint();
        playbackStatus.textContent = option.status;
        playbackSummary.textContent = option.status;
        setBadge(option.availability, effectiveMode);

        if (deviceForm) {
            deviceForm.hidden =
                effectiveMode !== "device" ||
                !["canprepare", "failed"].includes(option.availability);
        }

        if (serverForm) {
            serverForm.hidden =
                effectiveMode !== "server" ||
                !["canprepare", "failed"].includes(option.availability);
        }

        if (working) {
            working.hidden = option.availability !== "preparing";
        }

        if (option.availability === "ready") {
            if (error) {
                error.hidden = true;
            }
            showVideo(effectiveMode);
            return;
        }

        hideVideo();

        if (option.availability === "preparing") {
            scheduleRefresh();
        }
    };

    modeSelect.addEventListener("change", () => {
        preference = modeSelect.value;
        runtimeDeviceFailed = false;
        storePreference(preference);

        if (error) {
            error.hidden = true;
        }

        applyPlayback();
    });

    let cues = [];
    try {
        cues = JSON.parse(data.textContent || "[]");
    } catch {
        if (error) {
            error.hidden = false;
            error.textContent = "Subtitle data could not be loaded.";
        }
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
        if (preference === "auto" && effectiveMode === "device") {
            runtimeDeviceFailed = true;
            if (error) {
                error.hidden = false;
                error.textContent = "Device playback failed. Switching to the server fallback.";
            }
            applyPlayback();
            return;
        }

        if (error) {
            error.hidden = false;
            error.textContent = "The selected playback stream could not be played by this browser.";
        }
    });

    replay?.addEventListener("click", () => {
        video.currentTime = Math.max(0, selectedCueStartMs / 1000 - 0.5);
        void video.play();
    });

    applyPlayback();
    sync();
})();
