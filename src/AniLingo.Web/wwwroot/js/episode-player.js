(() => {
    const root = document.querySelector("[data-episode-player]");
    if (!root) {
        return;
    }

    const design = window.AniLingoPlayerDesign;
    if (!design) {
        return;
    }

    const profileId = document.body?.dataset.profileId || "unknown";
    // Device-local facts (decoder/network capability) live in this browser only.
    const preferenceKey = `anilingo.profile.${profileId}.playbackMode`;
    const qualityKey = `anilingo.profile.${profileId}.qualityCap`;
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

    const nextUrl = root.dataset.nextUrl || "";
    const preferencesUrl = root.dataset.playbackPreferencesUrl || "";
    let autoplayNext = root.dataset.autoplayNext === "true";
    const restartButton = root.querySelector("[data-restart]");
    const autoplayToggle = root.querySelector("[data-autoplay-toggle]");
    const postPlay = root.querySelector("[data-post-play]");
    const postPlayReplay = root.querySelector("[data-post-play-replay]");
    const postPlayCountdown = root.querySelector("[data-post-play-countdown]");
    const postPlayCancel = root.querySelector("[data-post-play-cancel]");
    const autoplayDelaySeconds = 10;

    const playbackSubtitle = root.querySelector("[data-playback-subtitle]");
    const speedSelect = root.querySelector("[data-playback-speed]");
    const audioSelect = root.querySelector("[data-audio-track]");
    const subtitleSelect = root.querySelector("[data-subtitle-track]");
    const qualitySelect = root.querySelector("[data-quality-cap]");
    const qualityHint = root.querySelector("[data-quality-hint]");
    const saveDefaults = root.querySelector("[data-save-playback-defaults]");
    const repeatLineButton = root.querySelector("[data-repeat-line]");
    const controlsData = (() => {
        try {
            return JSON.parse(root.querySelector("[data-player-controls-data]")?.textContent || "{}");
        } catch {
            return {};
        }
    })();
    const seekStepSeconds = Number(controlsData.seekStepSeconds) > 0
        ? Number(controlsData.seekStepSeconds)
        : 10;
    const variants = new Map((controlsData.variants || []).map(variant => [
        `${variant.mode}|${variant.audioTrackId || ""}|${variant.quality}`,
        variant
    ]));

    if (!video || !stage || !placeholder || !playbackStatus ||
        !playbackSummary || !playbackBadge || !modeSelect || !overlay || !data ||
        !timeline || !timelineCurrent || !timelineDuration) {
        return;
    }

    // The learning sheet is only rendered when player learning tools are
    // enabled for this scope; normal playback must work without it.
    const learningTools = Boolean(
        inspector && learningKicker && word && reading && meaning && state &&
        replay && closeLearning);

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

    const qualityCaps = ["auto", "1080p", "720p", "low"];
    const readQualityCap = () => {
        try {
            const value = window.localStorage.getItem(qualityKey);
            return qualityCaps.includes(value) ? value : "auto";
        } catch {
            return "auto";
        }
    };

    const storeQualityCap = (value) => {
        try {
            window.localStorage.setItem(qualityKey, value);
        } catch {
        }
    };

    // Session-only selections: they survive fallback and stream restarts of
    // this page but are never stored; "Save as my defaults" writes the
    // profile-scoped preference instead.
    let selectedAudioTrackId = audioSelect?.value || controlsData.initialAudioTrackId || null;
    let qualityCap = readQualityCap();
    let playbackSpeed = Number(speedSelect?.value) > 0 ? Number(speedSelect.value) : 1;
    let subtitleChoice = subtitleSelect?.value || "learning";
    if (qualitySelect) {
        qualitySelect.value = qualityCap;
    }

    const variantFor = (mode) =>
        variants.get(`${mode}|${selectedAudioTrackId || ""}|${qualityCap}`) || null;

    // Liveness of the requested stream comes from the server decision for
    // this exact (mode, audio, quality) combination.
    const streamIsLive = (mode) => {
        const variant = variantFor(mode);
        return variant ? variant.isLive === true : options[mode]?.live === true;
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
    let loadedStreamLive = false;
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
        const absolute = loadedStreamLive
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

    // Bounded checkpoints: at most one regular write per 15 seconds while
    // playing; pause, end, restart and page close flush immediately.
    const persistProgress = (completed = false, force = false) => {
        if (!progressUrl) {
            return;
        }

        const positionMs = Math.max(0, Math.round(absoluteCurrentTime() * 1000));
        const now = Date.now();

        if (!force && now - lastProgressSentAt < 15000) {
            return;
        }

        if (!force && positionMs === lastProgressPositionMs) {
            return;
        }

        sendProgress(positionMs, completed, force);
    };

    const sendProgress = (positionMs, completed, keepalive) => {
        const durationMs = hasKnownDuration
            ? Math.max(0, Math.round(durationSeconds * 1000))
            : null;

        lastProgressSentAt = Date.now();
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
            keepalive
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

        if (!deviceAllowed()) {
            return "server";
        }

        // Auto honours a remote quality cap only when the server proved the
        // device stream exceeds it; otherwise device-first direct play wins.
        const device = variantFor("device");
        const server = variantFor("server");
        const deviceExceedsCap = device?.isAvailable === true && device.satisfiesCap === false;
        const serverHonoursCap = server?.isAvailable === true && server.satisfiesCap === true &&
            options.server.availability === "ready";
        if (device?.isAvailable === false || (deviceExceedsCap && serverHonoursCap)) {
            return "server";
        }

        return "device";
    };

    // A new source resets playbackRate to defaultPlaybackRate, so both are set.
    const applySpeed = () => {
        video.defaultPlaybackRate = playbackSpeed;
        video.playbackRate = playbackSpeed;
    };

    const updateQualityHint = () => {
        if (!qualityHint) {
            return;
        }

        const limit = Number(controlsData.capHeights?.[qualityCap]) || null;
        const source = Number(controlsData.sourceHeight) || null;
        const variant = variantFor(effectiveMode);
        if (!limit || !variant) {
            qualityHint.textContent = "";
        } else if (variant.satisfiesCap === false) {
            qualityHint.textContent =
                `Device playback keeps the original ${source}p video. Choose Server playback to limit it to ${limit}p.`;
        } else if (source && source > limit) {
            qualityHint.textContent = `The server converts this stream to at most ${limit}p.`;
        } else {
            qualityHint.textContent = source
                ? `The original ${source}p already fits this limit, so nothing extra is converted.`
                : "Nothing extra is converted because the source resolution is unknown.";
        }
    };

    const buildMediaUrl = (mode, startSeconds = 0) => {
        const url = new URL(root.dataset.mediaUrl || window.location.href, window.location.origin);
        url.searchParams.set("mode", mode);

        if (streamIsLive(mode) && startSeconds > 0) {
            url.searchParams.set("start", String(startSeconds));
        } else {
            url.searchParams.delete("start");
        }

        if (selectedAudioTrackId && selectedAudioTrackId !== controlsData.fileDefaultAudioTrackId) {
            url.searchParams.set("audio", selectedAudioTrackId);
        } else {
            url.searchParams.delete("audio");
        }

        if (qualityCap !== "auto") {
            url.searchParams.set("quality", qualityCap);
        } else {
            url.searchParams.delete("quality");
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
        const live = streamIsLive(mode);
        const start = live ? clampToDuration(requestedStart) : 0;
        const selection = `${mode}|${selectedAudioTrackId || ""}|${qualityCap}`;
        const sourceKey = live
            ? `${selection}:${start.toFixed(3)}`
            : selection;

        if (video.dataset.playbackSource === sourceKey) {
            return false;
        }

        streamStartSeconds = start;
        loadedStreamLive = live;
        video.dataset.playbackMode = mode;
        video.dataset.playbackSource = sourceKey;
        video.src = buildMediaUrl(mode, streamStartSeconds);
        video.load();
        applySpeed();
        return true;
    };

    const showVideo = (mode) => {
        placeholder.hidden = true;
        video.hidden = false;
        stage.classList.remove("player-placeholder");

        const live = streamIsLive(mode);
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
        updateQualityHint();
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

    // Plain playback subtitle (an embedded text stream chosen for display).
    // It is independent of the learning overlay: when it differs from the
    // learning text, both stay visible.
    let playbackCues = [];
    let playbackCueKey = "";
    const playbackCueCache = new Map();

    const learningOverlayVisible = () => subtitleChoice !== "off" && cues.length > 0;

    const selectedSubtitleIsLearningSource = () =>
        subtitleSelect?.selectedOptions[0]?.dataset.learningSource === "true";

    const playbackTrackId = () =>
        subtitleChoice.startsWith("stream:") && !selectedSubtitleIsLearningSource()
            ? subtitleChoice
            : null;

    const renderPlaybackSubtitle = (timeMs) => {
        if (!playbackSubtitle) {
            return;
        }

        const lines = playbackTrackId()
            ? design.activeCuesAt(playbackCues, timeMs).map(cue => cue.text)
            : [];
        const key = lines.join("\n");
        if (key === playbackCueKey) {
            return;
        }

        playbackCueKey = key;
        playbackSubtitle.textContent = key;
        playbackSubtitle.hidden = key.length === 0;
    };

    const loadPlaybackCues = async (trackId) => {
        if (!trackId) {
            playbackCues = [];
            return;
        }

        if (playbackCueCache.has(trackId)) {
            playbackCues = playbackCueCache.get(trackId);
            return;
        }

        const template = controlsData.subtitleCuesUrlTemplate || "";
        try {
            const response = await fetch(template.replace("__track__", encodeURIComponent(trackId)), {
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });
            if (!response.ok) {
                throw new Error("Subtitle track unavailable.");
            }

            const payload = await response.json();
            const loaded = (payload.cues || []).slice().sort((a, b) => a.startMs - b.startMs);
            playbackCueCache.set(trackId, loaded);
            if (playbackTrackId() === trackId) {
                playbackCues = loaded;
            }
        } catch {
            playbackCues = [];
            if (error) {
                error.hidden = false;
                error.textContent = "This subtitle track could not be loaded.";
            }
        }
    };

    const updateRepeatAvailability = () => {
        if (repeatLineButton) {
            repeatLineButton.disabled = !(learningOverlayVisible() && cues.length > 0) &&
                playbackCues.length === 0;
        }
    };

    const currentLineStartMs = () => {
        const nowMs = Math.floor(absoluteCurrentTime() * 1000);
        const source = learningOverlayVisible() ? cues : playbackCues;
        return design.lineStartAt(source, nowMs);
    };

    const openLearning = (cue, token = null, selectedElement = null) => {
        if (!learningTools) {
            return;
        }

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
        if (!learningTools || inspector.hidden) {
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
            case design.actions.repeatCurrentCue: {
                // From the learning sheet repeat the inspected line; from the
                // transport controls repeat the line at the playhead.
                const startMs = learningTools && !inspector.hidden
                    ? selectedCueStartMs
                    : currentLineStartMs();
                closeLearningSheet(false);
                if (startMs !== null) {
                    seekToAbsolute(Math.max(0, startMs / 1000), true);
                }
                break;
            }
            case design.actions.seekBack10:
                seekToAbsolute(absoluteCurrentTime() - seekStepSeconds);
                break;
            case design.actions.seekForward10:
                seekToAbsolute(absoluteCurrentTime() + seekStepSeconds);
                break;
            case design.actions.seekTo:
                if (Number.isFinite(detail.seconds)) {
                    seekToAbsolute(detail.seconds);
                }
                break;
            case design.actions.closeOverlay:
                closeLearningSheet(true);
                break;
        }
    });

    root.querySelectorAll("[data-player-controls] [data-player-action]").forEach(button =>
        button.addEventListener("click", () =>
            design.dispatch(root, button.dataset.playerAction)));

    replay?.addEventListener("click", () =>
        design.dispatch(root, design.actions.repeatCurrentCue));
    closeLearning?.addEventListener("click", () =>
        design.dispatch(root, design.actions.closeOverlay));

    root.addEventListener("keydown", event => {
        if (event.key === "Escape" && learningTools && !inspector.hidden) {
            event.preventDefault();
            design.dispatch(root, design.actions.closeOverlay);
        }
    });

    // Both subtitle layers follow the media clock, so any playback speed keeps
    // cue timing exact; the frame loop only raises the sampling rate.
    const sync = () => {
        const nowMs = Math.floor(absoluteCurrentTime() * 1000);
        renderPlaybackSubtitle(nowMs);

        const index = learningOverlayVisible() ? design.cueIndexAt(cues, nowMs) : -1;
        if (index === activeIndex) {
            return;
        }

        activeIndex = index;
        renderCue(index);
    };

    let frameSyncActive = false;
    const frameSync = () => {
        if (video.paused || video.ended || video.hidden) {
            frameSyncActive = false;
            return;
        }

        sync();
        if (typeof video.requestVideoFrameCallback === "function") {
            video.requestVideoFrameCallback(frameSync);
        } else {
            window.requestAnimationFrame(frameSync);
        }
    };

    const startFrameSync = () => {
        if (!frameSyncActive) {
            frameSyncActive = true;
            frameSync();
        }
    };

    const applySubtitleChoice = async () => {
        activeIndex = -2;
        playbackCueKey = null;
        await loadPlaybackCues(playbackTrackId());
        updateRepeatAvailability();
        sync();
    };

    subtitleSelect?.addEventListener("change", () => {
        subtitleChoice = subtitleSelect.value;
        void applySubtitleChoice();
    });

    speedSelect?.addEventListener("change", () => {
        const requested = Number(speedSelect.value);
        playbackSpeed = Number.isFinite(requested) && requested > 0 ? requested : 1;
        applySpeed();
    });

    const restartWithSelection = () => {
        pendingResumeTime = absoluteCurrentTime();
        resumeShouldPlay = !video.paused && !video.ended;
        if (error) {
            error.hidden = true;
        }
        applyPlayback();
    };

    audioSelect?.addEventListener("change", () => {
        selectedAudioTrackId = audioSelect.value || null;
        restartWithSelection();
    });

    qualitySelect?.addEventListener("change", () => {
        qualityCap = qualityCaps.includes(qualitySelect.value) ? qualitySelect.value : "auto";
        storeQualityCap(qualityCap);
        restartWithSelection();
    });

    saveDefaults?.addEventListener("click", async () => {
        if (!preferencesUrl) {
            return;
        }

        const subtitleLanguage = subtitleChoice === "off"
            ? "off"
            : subtitleSelect?.selectedOptions[0]?.dataset.language || "";
        const body = {
            preferredAudioLanguage: audioSelect?.selectedOptions[0]?.dataset.language || "",
            preferredSubtitleLanguage: subtitleLanguage,
            defaultPlaybackSpeed: playbackSpeed
        };
        if (!audioSelect) {
            delete body.preferredAudioLanguage;
        }

        saveDefaults.disabled = true;
        try {
            const response = await fetch(preferencesUrl, {
                method: "PUT",
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(body)
            });
            if (!response.ok) {
                throw new Error("Preference update failed.");
            }

            if (qualityHint) {
                qualityHint.textContent = "Saved as the default for every episode on this profile.";
            }
        } catch {
            if (error) {
                error.hidden = false;
                error.textContent = "Your playback defaults could not be saved.";
            }
        } finally {
            saveDefaults.disabled = false;
        }
    });

    const seekToAbsolute = (requestedSeconds, shouldPlay = !video.paused && !video.ended) => {
        const target = clampToDuration(requestedSeconds);

        if (loadedStreamLive) {
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

    let autoplayTimer = null;

    const stopAutoplayCountdown = () => {
        if (autoplayTimer !== null) {
            window.clearInterval(autoplayTimer);
            autoplayTimer = null;
        }

        if (postPlayCountdown) {
            postPlayCountdown.hidden = true;
            postPlayCountdown.textContent = "";
        }

        if (postPlayCancel) {
            postPlayCancel.hidden = true;
        }
    };

    const hidePostPlay = () => {
        stopAutoplayCountdown();
        if (postPlay) {
            postPlay.hidden = true;
        }
    };

    const showPostPlay = () => {
        if (!postPlay) {
            return;
        }

        postPlay.hidden = false;
        stopAutoplayCountdown();

        const focusTarget = postPlay.querySelector("[data-post-play-next]") || postPlayReplay;
        focusTarget?.focus();

        if (!autoplayNext || !nextUrl || !postPlayCountdown) {
            return;
        }

        let remaining = autoplayDelaySeconds;
        const render = () => {
            postPlayCountdown.textContent = `Next episode in ${remaining}s`;
        };

        postPlayCountdown.hidden = false;
        if (postPlayCancel) {
            postPlayCancel.hidden = false;
        }
        render();

        autoplayTimer = window.setInterval(() => {
            remaining -= 1;
            if (remaining <= 0) {
                stopAutoplayCountdown();
                window.location.assign(nextUrl);
                return;
            }

            render();
        }, 1000);
    };

    postPlayReplay?.addEventListener("click", () => {
        hidePostPlay();
        seekToAbsolute(0, true);
    });

    postPlayCancel?.addEventListener("click", () => {
        stopAutoplayCountdown();
        postPlayReplay?.focus();
    });

    restartButton?.addEventListener("click", () => {
        hidePostPlay();
        pendingResumeTime = null;
        seekToAbsolute(0, true);
        if (video.paused) {
            void video.play().catch(() => {});
        }

        if (progressUrl) {
            sendProgress(0, false, false);
        }

        restartButton.hidden = true;
    });

    autoplayToggle?.addEventListener("change", async () => {
        const requested = autoplayToggle.checked;
        if (!preferencesUrl) {
            autoplayToggle.checked = autoplayNext;
            return;
        }

        autoplayToggle.disabled = true;
        try {
            const response = await fetch(preferencesUrl, {
                method: "PUT",
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ autoplayNext: requested })
            });

            if (!response.ok) {
                throw new Error("Preference update failed.");
            }

            const preferences = await response.json();
            autoplayNext = preferences.autoplayNext === true;
        } catch {
            if (error) {
                error.hidden = false;
                error.textContent = "The autoplay preference could not be saved.";
            }
        } finally {
            autoplayToggle.checked = autoplayNext;
            autoplayToggle.disabled = false;
        }

        if (!autoplayNext) {
            stopAutoplayCountdown();
        }
    });

    video.addEventListener("timeupdate", () => {
        updateTimeline();
        sync();
        persistProgress();
    });
    video.addEventListener("seeked", () => {
        updateTimeline();
        sync();
        // While playing the regular throttle applies; a seek while paused is
        // an explicit position and is flushed like a pause.
        persistProgress(false, video.paused);
    });
    video.addEventListener("loadedmetadata", () => {
        applySpeed();
        if (!loadedStreamLive &&
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
        hidePostPlay();
        startFrameSync();
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
        if (restartButton) {
            restartButton.hidden = true;
        }
        showPostPlay();
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
    applySpeed();
    applyPlayback();
    void applySubtitleChoice();
})();
