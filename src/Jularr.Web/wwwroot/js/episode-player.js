(() => {
    const root = document.querySelector("[data-episode-player]");
    if (!root) {
        return;
    }

    const design = window.JularrPlayerDesign;
    if (!design) {
        return;
    }

    const profileId = document.body?.dataset.profileId || "unknown";
    // Device-local choices (mode override, quality) live in this browser only.
    const preferenceKey = `anilingo.profile.${profileId}.playbackMode`;
    const legacyQualityKey = `anilingo.profile.${profileId}.qualityCap`;
    const qualityKey = `anilingo.profile.${profileId}.qualityPreset`;
    const progressUrl = root.dataset.progressUrl || "";
    const planUrl = root.dataset.playbackPlanUrl || "";
    const persistedResumeSeconds = Number(root.dataset.resumeSeconds);
    const video = root.querySelector("[data-playback-video]");
    const stage = root.querySelector("[data-video-stage]");
    const placeholder = root.querySelector("[data-playback-placeholder]");
    const playbackStatus = root.querySelector("[data-playback-status]");
    const playbackSummary = root.querySelector("[data-playback-summary]");
    const playbackBadge = root.querySelector("[data-playback-badge]");
    const modeSelect = root.querySelector("[data-playback-mode]");
    const reasonsBlock = root.querySelector("[data-playback-reasons]");
    const reasonList = root.querySelector("[data-playback-reason-list]");
    const capabilityProbe = window.JularrPlaybackCapabilities;
    const text = (() => {
        try {
            return JSON.parse(root.querySelector("[data-player-text]")?.textContent || "{}");
        } catch {
            return {};
        }
    })();

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

    let durationSeconds = Number(root.dataset.durationSeconds);
    let hasKnownDuration = Number.isFinite(durationSeconds) && durationSeconds > 0;

    const readStored = (key) => {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    };

    const store = (key, value) => {
        try {
            window.localStorage.setItem(key, value);
        } catch {
        }
    };

    // Earlier player versions stored "device"/"server"; device-only maps onto Direct only.
    const modePreferences = ["auto", "direct_only", "always_transcode"];
    const readPreference = () => {
        const value = readStored(preferenceKey);
        return value === "device" ? "direct_only" : modePreferences.includes(value) ? value : "auto";
    };

    const qualityPresets = ["auto", "original", "20mbps", "12mbps", "8mbps", "4mbps", "2mbps", "1mbps"];
    const legacyQuality = { "1080p": "8mbps", "720p": "4mbps", low: "2mbps" };
    // No stored choice means the server's network default (Original at home, Automatic away).
    const readQualityPreset = () => {
        const value = readStored(qualityKey) ?? legacyQuality[readStored(legacyQualityKey)] ?? null;
        return qualityPresets.includes(value) ? value : null;
    };

    // Session-only selections: they survive fallback and stream restarts of
    // this page but are never stored; "Save as my defaults" writes the
    // profile-scoped preference instead.
    let selectedAudioTrackId = audioSelect?.value || controlsData.initialAudioTrackId || null;
    let qualityPreset = readQualityPreset();
    let playbackSpeed = Number(speedSelect?.value) > 0 ? Number(speedSelect.value) : 1;
    let subtitleChoice = subtitleSelect?.value || "learning";
    if (qualitySelect) {
        qualitySelect.value = qualityPreset || "auto";
    }

    // The resolved plan of the current playback session. The server decides; this player
    // only follows the plan and reports failures and its selections back.
    let plan = null;
    let delivery = null;
    let streamSessionId = null;
    let planGeneration = 0;
    const failedModes = new Set();
    const streamIsLive = () => delivery !== null && delivery.transport !== "file";

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

    // A new source resets playbackRate to defaultPlaybackRate, so both are set.
    const applySpeed = () => {
        video.defaultPlaybackRate = playbackSpeed;
        video.playbackRate = playbackSpeed;
    };

    const format = (key, values = {}) =>
        (text[key] || "").replace(/\{([A-Za-z0-9_]+)\}/g, (_, name) =>
            name in values ? displayName(String(values[name])) : "?");

    const codecNames = {
        h264: "H.264", hevc: "HEVC", av1: "AV1", vp9: "VP9", vp8: "VP8", mpeg4: "MPEG-4",
        aac: "AAC", mp3: "MP3", opus: "Opus", flac: "FLAC", ac3: "AC-3", eac3: "E-AC-3",
        dts: "DTS", truehd: "TrueHD", vorbis: "Vorbis", alac: "ALAC",
        matroska: "MKV", mp4: "MP4", webm: "WebM", mpegts: "MPEG-TS", avi: "AVI"
    };
    const displayName = (value) => codecNames[value?.toLowerCase?.()] || value;

    const modeLabel = (mode) => text[`playback.mode.${mode}`] || mode;

    // Compact status for normal users: "Direct Play · HEVC · 1080p · AAC".
    const compactStatus = () => {
        if (!plan || plan.mode === "unavailable") {
            return modeLabel("unavailable");
        }

        const parts = [modeLabel(plan.mode)];
        const output = plan.video;
        if (output) {
            parts.push(output.copy || !output.sourceCodec || output.sourceCodec === output.outputCodec
                ? displayName(output.outputCodec)
                : `${displayName(output.sourceCodec)} → ${displayName(output.outputCodec)}`);
            const height = output.copy
                ? output.sourceHeight
                : Math.min(output.sourceHeight || Infinity, output.maxOutputHeight || Infinity);
            if (Number.isFinite(height)) {
                parts.push(`${height}p`);
            }
        }

        if (plan.audio) {
            parts.push(displayName(plan.audio.outputCodec));
        }

        return parts.join(" · ");
    };

    // "Why not Direct Play?": the reasons that ruled out the untouched file (and, for a
    // transcode, the remux), then at most two notes. Never a text wall.
    const renderReasons = () => {
        if (!reasonsBlock || !reasonList) {
            return;
        }

        reasonList.replaceChildren();
        if (!plan) {
            reasonsBlock.hidden = true;
            return;
        }

        const shown = new Set();
        const lines = [];
        const add = (reason) => {
            const key = `${reason.code}|${JSON.stringify(reason.values || {})}`;
            if (shown.has(reason.code) || shown.has(key) || !text[`playback.reason.${reason.code}`]) {
                return;
            }

            shown.add(reason.code);
            lines.push(format(`playback.reason.${reason.code}`, reason.values || {}));
        };

        const reasons = plan.reasons || [];
        reasons.filter(x => x.rulesOut === "direct_play").forEach(add);
        if (plan.mode === "transcode" || plan.mode === "unavailable") {
            reasons.filter(x => x.rulesOut === "direct_stream" || x.rulesOut === "transcode").forEach(add);
        }

        const blockers = lines.length;
        reasons.filter(x => !x.rulesOut && x.severity !== "info").slice(0, 2).forEach(add);
        if (plan.mode !== "direct_play") {
            reasons.filter(x => !x.rulesOut && x.severity === "info").slice(0, 2).forEach(add);
        }

        for (const line of lines.slice(0, 6)) {
            const item = document.createElement("li");
            item.textContent = line;
            reasonList.append(item);
        }

        const heading = reasonsBlock.querySelector("[data-playback-reasons-heading]");
        if (heading) {
            heading.hidden = blockers === 0;
        }

        reasonsBlock.hidden = lines.length === 0;
    };

    const setBadge = () => {
        playbackBadge.classList.remove("status-ok", "status-warning", "status-error");
        if (!plan) {
            playbackBadge.classList.add("status-warning");
            playbackBadge.textContent = text["playback.status.checking"] || "…";
            return;
        }

        playbackBadge.classList.add(
            plan.mode === "direct_play" ? "status-ok" : plan.mode === "unavailable" ? "status-error" : "status-warning");
        playbackBadge.textContent = modeLabel(plan.mode);
    };

    const renderPlan = () => {
        setBadge();
        playbackSummary.textContent = plan ? compactStatus() : text["playback.status.checking"] || "";
        renderReasons();
        root.dataset.playbackMode = plan?.mode || "";
    };

    const hideVideo = () => {
        video.pause();
        video.hidden = true;
        placeholder.hidden = false;
        stage.classList.add("player-placeholder");
    };

    const buildMediaUrl = (startSeconds) => {
        const url = new URL(delivery.url, window.location.origin);
        if (streamIsLive() && delivery.startParameter && startSeconds > 0) {
            url.searchParams.set(delivery.startParameter, startSeconds.toFixed(3));
        }

        return url.toString();
    };

    const loadSource = (requestedStart = 0) => {
        if (!delivery) {
            return false;
        }

        const live = streamIsLive();
        const start = live ? clampToDuration(requestedStart) : 0;
        const sourceKey = live
            ? `${streamSessionId}:${start.toFixed(3)}`
            : `${streamSessionId}`;

        if (video.dataset.playbackSource === sourceKey) {
            return false;
        }

        streamStartSeconds = start;
        loadedStreamLive = live;
        video.dataset.playbackSource = sourceKey;
        video.src = buildMediaUrl(streamStartSeconds);
        video.load();
        applySpeed();
        return true;
    };

    const showVideo = () => {
        placeholder.hidden = true;
        video.hidden = false;
        stage.classList.remove("player-placeholder");

        const live = streamIsLive();
        const requestedStart = pendingResumeTime !== null && Number.isFinite(pendingResumeTime)
            ? clampToDuration(pendingResumeTime)
            : 0;

        if (live) {
            pendingResumeTime = null;
        }

        const changed = loadSource(live ? requestedStart : 0);
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

            durationSeconds = Number(bootstrap.media.durationMs) / 1000;
            hasKnownDuration = Number.isFinite(durationSeconds) && durationSeconds > 0;

            if (bootstrap.media.availability) {
                storageState = bootstrap.media.availability.state || "unknown";
                root.dataset.storageWakeUrl = bootstrap.media.availability.wakeUrl || "";
            }

            failedModes.clear();
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

    // Picture (bitmap) subtitles cannot be drawn by this player; choosing one asks the
    // server to render it into the video. Text subtitles stay client-side cues.
    const burnInSubtitleTrackId = () => {
        const option = subtitleSelect?.selectedOptions[0];
        return option?.dataset.image === "true" ? option.value : null;
    };

    const requestPlan = async () => {
        let capabilities = null;
        try {
            capabilities = capabilityProbe ? await capabilityProbe.detect() : null;
        } catch {
            capabilities = null;
        }

        const response = await fetch(planUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                capabilities,
                audioTrackId: selectedAudioTrackId,
                subtitleTrackId: burnInSubtitleTrackId(),
                quality: qualityPreset,
                mode: preference,
                network: capabilityProbe?.networkReport() || null,
                failedModes: [...failedModes],
                replacesSessionId: streamSessionId
            })
        });

        if (!response.ok) {
            throw new Error(`Playback plan failed with ${response.status}.`);
        }

        return response.json();
    };

    const showPlayerError = (message) => {
        if (error) {
            error.hidden = !message;
            error.textContent = message || "";
        }
    };

    const applyPlayback = async () => {
        if (!storageIsAvailable()) {
            startStorageRetry(false);
            return;
        }

        const generation = ++planGeneration;
        plan = null;
        renderPlan();

        let response;
        try {
            response = await requestPlan();
        } catch {
            if (generation === planGeneration) {
                hideVideo();
                playbackStatus.textContent = text["playback.status.planFailed"] || "";
                showPlayerError(text["playback.status.planFailed"]);
            }
            return;
        }

        // A newer selection (quality, audio, mode) superseded this request.
        if (generation !== planGeneration) {
            return;
        }

        plan = response.plan;
        streamSessionId = response.sessionId || null;
        delivery = response.delivery || null;
        renderPlan();

        if (plan.mode === "unavailable" || !delivery) {
            if (response.availability && response.availability.state !== "available") {
                storageState = response.availability.state || "unknown";
                root.dataset.storageWakeUrl = response.availability.wakeUrl || "";
                startStorageRetry(false);
                return;
            }

            hideVideo();
            playbackStatus.textContent = text["playback.status.failed"] || "";
            return;
        }

        showPlayerError(null);
        showVideo();
    };

    modeSelect.addEventListener("change", () => {
        const resumeAt = absoluteCurrentTime();
        const shouldResume = !video.paused && !video.ended;

        preference = modePreferences.includes(modeSelect.value) ? modeSelect.value : "auto";
        failedModes.clear();
        pendingResumeTime = resumeAt;
        resumeShouldPlay = shouldResume;
        store(preferenceKey, preference);
        showPlayerError(null);
        void applyPlayback();
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

    // The shared language inspector (issue #233) replaces this player's own
    // learning sheet wherever the resolved scope renders it; the sheet below
    // stays only as the fallback for scopes without PlayerTools.
    const sharedInspector = window.JularrLanguageInspector;
    const sharedInspectorAvailable = sharedInspector?.available === true;

    const openLearning = (cue, token = null, selectedElement = null) => {
        overlay.querySelectorAll('[aria-pressed="true"]').forEach(element =>
            element.removeAttribute("aria-pressed"));
        if (selectedElement instanceof HTMLElement) {
            selectedElement.setAttribute("aria-pressed", "true");
        }

        selectedCueStartMs = cue.startMs;

        if (sharedInspectorAvailable) {
            const sentence = design.cueText(cue);
            void sharedInspector.open(token ? token.surface : sentence, {
                sentence,
                cueStartMs: cue.startMs
            });
            return;
        }

        if (!learningTools) {
            return;
        }

        if (inspector.hidden) {
            learningResumeOnClose = !video.paused && !video.ended;
        }

        video.pause();

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

    if (sharedInspectorAvailable) {
        let inspectorResumeOnClose = false;

        sharedInspector.addEventListener("open", () => {
            inspectorResumeOnClose = !video.paused && !video.ended;
            video.pause();
        });

        sharedInspector.addEventListener("close", () => {
            const shouldResume = inspectorResumeOnClose;
            inspectorResumeOnClose = false;
            if (shouldResume) {
                void video.play().catch(() => {});
            }
        });

        // Update the cached cue tokens so a word saved/learned/known/ignored
        // through the inspector re-renders with its new state right away,
        // through the same renderCue design.js already uses for the overlay.
        sharedInspector.addEventListener("statechange", event => {
            const detail = event.detail || {};
            if (!detail.text) {
                return;
            }

            let changed = false;
            for (const cue of cues) {
                for (const cueToken of cue.tokens || []) {
                    if ((cueToken.canonical || cueToken.surface) === detail.text) {
                        cueToken.state = detail.state;
                        changed = true;
                    }
                }
            }

            if (changed && activeIndex >= 0) {
                renderCue(activeIndex);
            }
        });
    }

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

    let plannedBurnIn = null;
    subtitleSelect?.addEventListener("change", () => {
        subtitleChoice = subtitleSelect.value;
        void applySubtitleChoice();
        // Picture subtitles change the stream itself, so they need a new plan.
        if (burnInSubtitleTrackId() !== plannedBurnIn) {
            plannedBurnIn = burnInSubtitleTrackId();
            restartWithSelection();
        }
    });

    speedSelect?.addEventListener("change", () => {
        const requested = Number(speedSelect.value);
        playbackSpeed = Number.isFinite(requested) && requested > 0 ? requested : 1;
        applySpeed();
    });

    function restartWithSelection() {
        pendingResumeTime = absoluteCurrentTime();
        resumeShouldPlay = !video.paused && !video.ended;
        failedModes.clear();
        showPlayerError(null);
        void applyPlayback();
    }

    audioSelect?.addEventListener("change", () => {
        selectedAudioTrackId = audioSelect.value || null;
        restartWithSelection();
    });

    // A quality picked here overrides the network default for this device.
    qualitySelect?.addEventListener("change", () => {
        qualityPreset = qualityPresets.includes(qualitySelect.value) ? qualitySelect.value : "auto";
        store(qualityKey, qualityPreset);
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
            loadSource(target);
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

        // Report the failed mode and let the server choose the next one (Direct Play →
        // Direct Stream → Transcode) instead of deciding a fallback here.
        if (plan && plan.mode !== "unavailable" && !failedModes.has(plan.mode)) {
            failedModes.add(plan.mode);
            pendingResumeTime = absoluteCurrentTime();
            resumeShouldPlay = playbackWasRequested;
            showPlayerError(text["playback.status.retrying"]);
            void applyPlayback();
            return;
        }

        showPlayerError(text["playback.status.failed"]);
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

        // Ending the stream session stops its server remux/transcode right away instead of
        // waiting for the idle cleanup.
        if (streamSessionId && root.dataset.streamSessionUrlTemplate) {
            void fetch(root.dataset.streamSessionUrlTemplate.replace("__session__", streamSessionId), {
                method: "DELETE",
                credentials: "same-origin",
                keepalive: true
            }).catch(() => {});
        }
    });

    // Back from the page cache: the stream session was ended on pagehide, so plan again.
    window.addEventListener("pageshow", event => {
        if (event.persisted && streamSessionId) {
            pendingResumeTime = absoluteCurrentTime();
            streamSessionId = null;
            delete video.dataset.playbackSource;
            void applyPlayback();
        }
    });

    updateTimeline();
    applySpeed();
    void applyPlayback();
    void applySubtitleChoice();
})();
