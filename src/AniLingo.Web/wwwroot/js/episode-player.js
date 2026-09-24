(() => {
    const root = document.querySelector("[data-episode-player]");
    if (!root) {
        return;
    }

    const design = window.AniLingoPlayerDesign;
    if (!design) {
        return;
    }

    const preferenceKey = "anilingo.playbackMode";
    const progressUrl = root.dataset.progressUrl || "";
    const persistedResumeSeconds = Number(root.dataset.resumeSeconds);
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
    const learningKicker = root.querySelector("[data-learning-kicker]");
    const word = root.querySelector("[data-word]");
    const reading = root.querySelector("[data-reading]");
    const meaning = root.querySelector("[data-meaning]");
    const state = root.querySelector("[data-state]");
    const replay = root.querySelector("[data-replay]");
    const closeLearning = root.querySelector("[data-close-learning]");
    const error = root.querySelector("[data-player-error]");
    const timeline = root.querySelector("[data-playback-timeline]");
    const timelineCurrent = root.querySelector("[data-playback-current]");
    const timelineDuration = root.querySelector("[data-playback-duration]");
    const storageActions = root.querySelector("[data-storage-actions]");
    const storageRetry = root.querySelector("[data-storage-retry]");
    const storageWake = root.querySelector("[data-storage-wake]");

    if (!video || !stage || !placeholder || !playbackStatus ||
        !playbackSummary || !playbackBadge || !modeSelect || !overlay || !data ||
        !timeline || !timelineCurrent || !timelineDuration || !inspector ||
        !learningKicker || !word || !reading || !meaning || !state ||
        !replay || !closeLearning) {
        return;
    }

    let videoCodec = (root.dataset.videoCodec || "").toLowerCase();
    let isHevc = videoCodec === "hevc" || videoCodec === "h265";
    let durationSeconds = Number(root.dataset.durationSeconds);
    let hasKnownDuration = Number.isFinite(durationSeconds) && durationSeconds > 0;
    const capabilityProbe = document.createElement("video");
    const supportsHevc =
        capabilityProbe.canPlayType('video/mp4; codecs="hvc1"') !== "" ||
        capabilityProbe.canPlayType('video/mp4; codecs="hev1"') !== "";

    const options = {
        device: {
            availability: root.dataset.deviceAvailability || "unsupported",
            status: root.dataset.deviceStatus || "Device playback is unavailable.",
            live: root.dataset.deviceLive === "true"
        },
        server: {
            availability: root.dataset.serverAvailability || "unsupported",
            status: root.dataset.serverStatus || "Server playback is unavailable.",
            live: root.dataset.serverLive === "true"
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

    const readSceneStartSeconds = () => {
        const value = new URL(window.location.href).searchParams.get("at");
        if (value === null || !/^\d+$/.test(value)) {
            return null;
        }

        const milliseconds = Number(value);
        if (!Number.isSafeInteger(milliseconds) || milliseconds < 0) {
            return null;
        }

        return milliseconds / 1000;
    };

    let preference = readPreference();
    let runtimeDeviceFailed = false;
    let effectiveMode = "device";
    const sceneStartSeconds = readSceneStartSeconds();
    let pendingResumeTime = sceneStartSeconds !== null
        ? sceneStartSeconds
        : Number.isFinite(persistedResumeSeconds) && persistedResumeSeconds > 0
            ? persistedResumeSeconds
            : null;
    let resumeShouldPlay = false;
    let streamStartSeconds = 0;
    let timelinePreviewing = false;
    let playbackWasRequested = false;
    let storageState = root.dataset.storageState || "unknown";
    let storageRetryStartedAt = null;
    let storageRetryAttempt = 0;
    let storageRetryTimer = null;
    let storageRecoveryActive = false;
    modeSelect.value = preference;

    const clampToDuration = (seconds) => {
        const safe = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
        if (!hasKnownDuration) {
            return safe;
        }

        return Math.min(safe, Math.max(0, durationSeconds - 0.05));
    };

    const formatTime = (seconds) => {
        if (!Number.isFinite(seconds) || seconds < 0) {
            return "--:--";
        }

        const totalSeconds = Math.floor(seconds);
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const remainder = totalSeconds % 60;

        if (hours > 0) {
            return `${hours}:${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`;
        }

        return `${minutes}:${String(remainder).padStart(2, "0")}`;
    };

    const absoluteCurrentTime = () => {
        const localTime = Number.isFinite(video.currentTime) ? video.currentTime : 0;
        const absolute = options[effectiveMode]?.live
            ? streamStartSeconds + localTime
            : localTime;
        return clampToDuration(absolute);
    };

    const updateTimeline = () => {
        if (!hasKnownDuration) {
            timeline.disabled = true;
            timelineDuration.textContent = "--:--";
            return;
        }

        timeline.disabled = false;
        timeline.max = String(durationSeconds);
        timelineDuration.textContent = formatTime(durationSeconds);

        if (!timelinePreviewing) {
            const current = absoluteCurrentTime();
            timeline.value = String(current);
            timelineCurrent.textContent = formatTime(current);
        }
    };

    let lastProgressSentAt = Date.now();
    let lastProgressPositionMs = -1;

    const persistProgress = (completed = false, force = false) => {
        if (!progressUrl) {
            return;
        }

        const positionMs = Math.max(0, Math.round(absoluteCurrentTime() * 1000));
        const durationMs = hasKnownDuration
            ? Math.max(0, Math.round(durationSeconds * 1000))
            : null;
        const now = Date.now();

        if (!force && now - lastProgressSentAt < 15000) {
            return;
        }

        if (!force && positionMs === lastProgressPositionMs) {
            return;
        }

        lastProgressSentAt = now;
        lastProgressPositionMs = positionMs;

        void fetch(progressUrl, {
            method: "PUT",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                positionMs,
                durationMs,
                completed
            }),
            keepalive: force
        }).catch(() => {});
    };

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

    const buildMediaUrl = (mode, startSeconds = 0) => {
        const url = new URL(root.dataset.mediaUrl || window.location.href, window.location.origin);
        url.searchParams.set("mode", mode);

        if (options[mode]?.live && startSeconds > 0) {
            url.searchParams.set("start", String(startSeconds));
        } else {
            url.searchParams.delete("start");
        }

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
            modeHint.textContent = "Server mode streams H.264 from ffmpeg immediately when conversion is required.";
            return;
        }

        if (isHevc) {
            modeHint.textContent = supportsHevc
                ? "Auto detected HEVC support: keep HEVC and let this device decode it."
                : "Auto did not detect HEVC support: use the server H.264 fallback.";
            return;
        }

        modeHint.textContent = "Auto prefers direct play or live remux and only uses live server transcoding when required.";
    };

    const hideVideo = () => {
        video.pause();
        video.hidden = true;
        placeholder.hidden = false;
        stage.classList.add("player-placeholder");
    };

    const loadSource = (mode, requestedStart = 0) => {
        const live = options[mode]?.live === true;
        streamStartSeconds = live ? clampToDuration(requestedStart) : 0;
        const sourceKey = live
            ? `${mode}:${streamStartSeconds.toFixed(3)}`
            : mode;

        if (video.dataset.playbackSource === sourceKey) {
            return false;
        }

        video.dataset.playbackMode = mode;
        video.dataset.playbackSource = sourceKey;
        video.src = buildMediaUrl(mode, streamStartSeconds);
        video.load();
        return true;
    };

    const showVideo = (mode) => {
        placeholder.hidden = true;
        video.hidden = false;
        stage.classList.remove("player-placeholder");

        const live = options[mode]?.live === true;
        const requestedStart = pendingResumeTime !== null && Number.isFinite(pendingResumeTime)
            ? clampToDuration(pendingResumeTime)
            : 0;

        if (live) {
            pendingResumeTime = null;
        }

        const changed = loadSource(mode, live ? requestedStart : 0);
        if (!changed && !live && pendingResumeTime !== null && video.readyState >= 1) {
            video.currentTime = requestedStart;
            pendingResumeTime = null;

            if (resumeShouldPlay) {
                resumeShouldPlay = false;
                void video.play().catch(() => {});
            }
        }

        updateTimeline();
    };

    const storageIsAvailable = () => storageState === "available";

    const stopStorageRetry = () => {
        if (storageRetryTimer !== null) {
            window.clearTimeout(storageRetryTimer);
            storageRetryTimer = null;
        }

        storageRecoveryActive = false;
        storageRetryStartedAt = null;
        storageRetryAttempt = 0;
    };

    const showStorageState = (availability, exhausted = false) => {
        storageState = availability?.state || storageState || "unknown";
        const retryable = availability?.retryable !== false &&
            storageState !== "file_missing" &&
            storageState !== "source_unreachable";

        if (storageIsAvailable()) {
            if (storageActions) {
                storageActions.hidden = true;
            }
            return;
        }

        storageRecoveryActive = true;
        hideVideo();
        playbackBadge.classList.remove("status-ok", "status-warning", "status-error");

        if (storageState === "file_missing") {
            playbackStatus.textContent = "This media file is missing.";
            playbackSummary.textContent = "Media file missing";
            playbackBadge.classList.add("status-error");
            playbackBadge.textContent = "Missing";
        } else if (storageState === "source_unreachable") {
            playbackStatus.textContent = "Media storage cannot currently be read.";
            playbackSummary.textContent = "Storage unreachable";
            playbackBadge.classList.add("status-error");
            playbackBadge.textContent = "Storage";
        } else if (storageState === "source_starting") {
            playbackStatus.textContent = "Starting NAS… waiting for media storage.";
            playbackSummary.textContent = "Storage starting";
            playbackBadge.classList.add("status-warning");
            playbackBadge.textContent = "Starting";
        } else if (exhausted) {
            playbackStatus.textContent = "Media storage is still offline.";
            playbackSummary.textContent = "Storage offline";
            playbackBadge.classList.add("status-error");
            playbackBadge.textContent = "Offline";
        } else {
            playbackStatus.textContent = "Waiting for media storage… retrying automatically.";
            playbackSummary.textContent = "Storage unavailable";
            playbackBadge.classList.add("status-warning");
            playbackBadge.textContent = "Waiting";
        }

        if (storageActions) {
            storageActions.hidden = false;
        }

        if (storageRetry) {
            storageRetry.hidden = !exhausted && retryable;
        }

        if (storageWake) {
            const wakeableState = storageState === "source_offline" ||
                storageState === "source_starting" ||
                storageState === "unknown";
            storageWake.hidden = !root.dataset.storageWakeUrl || !wakeableState;
        }
    };

    const readStorageAvailability = async () => {
        const url = root.dataset.storageAvailabilityUrl;
        if (!url) {
            return null;
        }

        try {
            const probeUrl = new URL(url, window.location.origin);
            probeUrl.searchParams.set("fresh", "true");
            const response = await fetch(probeUrl, {
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                return null;
            }

            return await response.json();
        } catch {
            return null;
        }
    };

    const refreshPlayerBootstrap = async () => {
        const url = root.dataset.playerBootstrapUrl;
        if (!url) {
            return false;
        }

        try {
            const response = await fetch(url, {
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });
            if (!response.ok) {
                return false;
            }

            const bootstrap = await response.json();
            if (!bootstrap?.media) {
                return false;
            }

            options.device = {
                availability: bootstrap.media.device?.availability || "unsupported",
                status: bootstrap.media.device?.message || "Device playback is unavailable.",
                live: bootstrap.media.device?.usesLiveStream === true
            };
            options.server = {
                availability: bootstrap.media.server?.availability || "unsupported",
                status: bootstrap.media.server?.message || "Server playback is unavailable.",
                live: bootstrap.media.server?.usesLiveStream === true
            };

            videoCodec = (bootstrap.media.videoCodec || "").toLowerCase();
            isHevc = videoCodec === "hevc" || videoCodec === "h265";
            durationSeconds = Number(bootstrap.media.durationMs) / 1000;
            hasKnownDuration = Number.isFinite(durationSeconds) && durationSeconds > 0;

            if (bootstrap.media.availability) {
                storageState = bootstrap.media.availability.state || "unknown";
                root.dataset.storageWakeUrl = bootstrap.media.availability.wakeUrl || "";
            }

            runtimeDeviceFailed = false;
            video.removeAttribute("src");
            delete video.dataset.playbackSource;
            video.load();
            return storageIsAvailable();
        } catch {
            return false;
        }
    };

    const scheduleStorageRetry = (delayMs) => {
        if (!storageRecoveryActive) {
            return;
        }

        if (storageRetryTimer !== null) {
            window.clearTimeout(storageRetryTimer);
        }

        storageRetryTimer = window.setTimeout(() => {
            void pollStorageAvailability();
        }, Math.max(0, delayMs));
    };

    const pollStorageAvailability = async () => {
        if (!storageRecoveryActive) {
            return;
        }

        const startedAt = storageRetryStartedAt ?? Date.now();
        storageRetryStartedAt = startedAt;

        if (Date.now() - startedAt >= 60000) {
            storageRetryTimer = null;
            showStorageState({ state: storageState, retryable: true }, true);
            return;
        }

        const availability = await readStorageAvailability();
        if (availability?.state === "available") {
            storageState = "available";
            const refreshed = await refreshPlayerBootstrap();
            if (refreshed) {
                stopStorageRetry();
                if (storageActions) {
                    storageActions.hidden = true;
                }
                applyPlayback();
                return;
            }
        }

        if (availability) {
            showStorageState(availability, false);
            if (availability.retryable === false) {
                storageRetryTimer = null;
                showStorageState(availability, true);
                return;
            }
        }

        const delays = [0, 1000, 2000, 4000, 5000];
        const delay = delays[Math.min(storageRetryAttempt, delays.length - 1)];
        storageRetryAttempt += 1;
        scheduleStorageRetry(delay);
    };

    const startStorageRetry = (preservePlaybackIntent = false) => {
        if (preservePlaybackIntent) {
            pendingResumeTime = absoluteCurrentTime();
            resumeShouldPlay = playbackWasRequested;
        }

        if (storageRecoveryActive) {
            return;
        }

        storageRecoveryActive = true;
        storageRetryStartedAt = Date.now();
        storageRetryAttempt = 1;
        showStorageState({ state: storageState, retryable: true }, false);
        scheduleStorageRetry(0);
    };

    const applyPlayback = () => {
        if (!storageIsAvailable()) {
            startStorageRetry(false);
            return;
        }

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

    };

    modeSelect.addEventListener("change", () => {
        const resumeAt = absoluteCurrentTime();
        const shouldResume = !video.paused && !video.ended;

        preference = modeSelect.value;
        runtimeDeviceFailed = false;
        pendingResumeTime = resumeAt;
        resumeShouldPlay = shouldResume;
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
    let learningResumeOnClose = false;

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

    const openLearning = (cue, token = null, selectedElement = null) => {
        if (inspector.hidden) {
            learningResumeOnClose = !video.paused && !video.ended;
        }

        video.pause();
        selectedCueStartMs = cue.startMs;

        if (token) {
            learningKicker.textContent = "Word";
            word.textContent = token.canonical || token.surface;
            reading.textContent = token.reading || "";
            meaning.textContent = token.meaning || "No local meaning available yet.";
            state.textContent = token.state || "New";
            state.hidden = false;
        } else {
            learningKicker.textContent = "Sentence";
            word.textContent = design.cueText(cue);
            reading.textContent = "";
            meaning.textContent = "Tap a highlighted word in the subtitle to inspect its reading and meaning.";
            state.textContent = "";
            state.hidden = true;
        }

        overlay.querySelectorAll('[aria-pressed="true"]').forEach(element =>
            element.removeAttribute("aria-pressed"));
        if (selectedElement instanceof HTMLElement) {
            selectedElement.setAttribute("aria-pressed", "true");
        }

        inspector.hidden = false;
        closeLearning.focus();
    };

    const closeLearningSheet = (resume = true) => {
        if (inspector.hidden) {
            return;
        }

        inspector.hidden = true;
        overlay.querySelectorAll('[aria-pressed="true"]').forEach(element =>
            element.removeAttribute("aria-pressed"));

        const shouldResume = resume && learningResumeOnClose;
        learningResumeOnClose = false;
        if (shouldResume) {
            void video.play().catch(() => {});
        }
    };

    const renderCue = (index) => {
        design.renderCue(root, overlay, index < 0 ? null : cues[index]);
    };

    root.addEventListener(design.actionEvent, event => {
        const detail = event.detail || {};
        switch (detail.action) {
            case design.actions.openWord:
                openLearning(detail.cue, detail.token, detail.element);
                break;
            case design.actions.learnCurrentCue:
                openLearning(detail.cue);
                break;
            case design.actions.repeatCurrentCue:
                closeLearningSheet(false);
                seekToAbsolute(Math.max(0, selectedCueStartMs / 1000), true);
                break;
            case design.actions.closeOverlay:
                closeLearningSheet(true);
                break;
        }
    });

    replay.addEventListener("click", () =>
        design.dispatch(root, design.actions.repeatCurrentCue));
    closeLearning.addEventListener("click", () =>
        design.dispatch(root, design.actions.closeOverlay));

    root.addEventListener("keydown", event => {
        if (event.key === "Escape" && !inspector.hidden) {
            event.preventDefault();
            design.dispatch(root, design.actions.closeOverlay);
        }
    });

    const sync = () => {
        const index = findCueIndex(Math.floor(absoluteCurrentTime() * 1000));
        if (index === activeIndex) {
            return;
        }

        activeIndex = index;
        renderCue(index);
    };

    const seekToAbsolute = (requestedSeconds, shouldPlay = !video.paused && !video.ended) => {
        const target = clampToDuration(requestedSeconds);

        if (options[effectiveMode]?.live) {
            pendingResumeTime = null;
            resumeShouldPlay = shouldPlay;
            loadSource(effectiveMode, target);
        } else if (video.readyState >= 1) {
            video.currentTime = target;
            if (shouldPlay) {
                void video.play().catch(() => {});
            }
        } else {
            pendingResumeTime = target;
            resumeShouldPlay = shouldPlay;
        }

        timelinePreviewing = false;
        updateTimeline();
        sync();
    };

    timeline.addEventListener("input", () => {
        if (!hasKnownDuration) {
            return;
        }

        timelinePreviewing = true;
        timelineCurrent.textContent = formatTime(Number(timeline.value));
    });

    timeline.addEventListener("change", () => {
        seekToAbsolute(Number(timeline.value));
    });

    video.addEventListener("timeupdate", () => {
        updateTimeline();
        sync();
        persistProgress();
    });
    video.addEventListener("seeked", () => {
        updateTimeline();
        sync();
        persistProgress(false, true);
    });
    video.addEventListener("loadedmetadata", () => {
        if (!options[effectiveMode]?.live &&
            pendingResumeTime !== null &&
            Number.isFinite(pendingResumeTime)) {
            const target = clampToDuration(pendingResumeTime);
            video.currentTime = Number.isFinite(video.duration) && video.duration >= 0
                ? Math.min(target, video.duration)
                : target;
            pendingResumeTime = null;
        }

        updateTimeline();
        sync();

        if (resumeShouldPlay) {
            resumeShouldPlay = false;
            void video.play().catch(() => {});
        }
    });
    video.addEventListener("play", () => {
        playbackWasRequested = true;
    });

    video.addEventListener("pause", () => {
        if (!video.ended) {
            persistProgress(false, true);
        }

        if (!storageRecoveryActive && !video.ended) {
            playbackWasRequested = false;
        }
    });

    video.addEventListener("ended", () => {
        persistProgress(true, true);
        playbackWasRequested = false;
    });

    video.addEventListener("error", async () => {
        const availability = await readStorageAvailability();
        if (availability && availability.state !== "available") {
            storageState = availability.state || "unknown";
            pendingResumeTime = absoluteCurrentTime();
            resumeShouldPlay = playbackWasRequested;

            if (availability.retryable === false) {
                storageRecoveryActive = true;
                showStorageState(availability, true);
            } else {
                storageRecoveryActive = false;
                startStorageRetry(false);
            }
            return;
        }

        if (preference === "auto" && effectiveMode === "device") {
            pendingResumeTime = absoluteCurrentTime();
            resumeShouldPlay = playbackWasRequested;
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

    storageRetry?.addEventListener("click", () => {
        playbackWasRequested = true;
        resumeShouldPlay = true;
        stopStorageRetry();
        storageRecoveryActive = true;
        storageRetryStartedAt = Date.now();
        storageRetryAttempt = 1;
        showStorageState({ state: storageState, retryable: true }, false);
        scheduleStorageRetry(0);
    });

    storageWake?.addEventListener("click", async () => {
        playbackWasRequested = true;
        resumeShouldPlay = true;
        const wakeUrl = root.dataset.storageWakeUrl;
        if (!wakeUrl) {
            return;
        }

        storageWake.disabled = true;
        try {
            const response = await fetch(wakeUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });

            if (!response.ok) {
                if (error) {
                    error.hidden = false;
                    error.textContent = "Wake-on-LAN could not be sent.";
                }
                return;
            }

            const availability = await response.json();
            storageState = availability.state || "source_starting";
            stopStorageRetry();
            storageRecoveryActive = true;
            storageRetryStartedAt = Date.now();
            storageRetryAttempt = 1;
            showStorageState(
                {
                    state: storageState,
                    retryable: availability.retryable !== false
                },
                false);
            scheduleStorageRetry(0);
        } catch {
            if (error) {
                error.hidden = false;
                error.textContent = "Wake-on-LAN request failed.";
            }
        } finally {
            storageWake.disabled = false;
        }
    });

    window.addEventListener("pagehide", () => {
        if (absoluteCurrentTime() > 0) {
            persistProgress(false, true);
        }
    });

    updateTimeline();
    applyPlayback();
    sync();
})();
