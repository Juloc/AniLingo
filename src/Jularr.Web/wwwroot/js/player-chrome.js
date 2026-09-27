// The player's own controls. episode-player.js owns sources, the (absolute) timeline, subtitles
// and progress; this file only drives what the native <video controls> used to: play/pause,
// volume, full screen, picture-in-picture, the settings menu and auto-hiding the chrome. There is
// deliberately no second timeline and no native control bar.
(() => {
    const root = document.querySelector("[data-episode-player]");
    const stage = root?.querySelector("[data-player-chrome]");
    const video = root?.querySelector("[data-playback-video]");
    if (!root || !stage || !video) return;

    const design = window.JularrPlayerDesign;
    // Alternate icon shapes come from design/player/player-icons.json via the page, never copied here.
    const icons = (() => {
        try {
            return JSON.parse(root.querySelector("[data-player-icons]")?.textContent || "{}");
        } catch {
            return {};
        }
    })();
    const settings = stage.querySelector("[data-chrome-settings]");
    const timeline = stage.querySelector("[data-playback-timeline]");
    const volumeSlider = stage.querySelector("[data-chrome-volume]");
    const speedButton = stage.querySelector("[data-chrome-speed]");
    const speedSelect = stage.querySelector("[data-playback-speed]");
    const subtitleSelect = stage.querySelector("[data-subtitle-track]");
    const subtitleButton = stage.querySelector("[data-chrome-subtitles]");
    const pipButton = stage.querySelector("[data-chrome-pip]");
    const volumeKey = "jularr.player.volume";
    const hideDelayMs = 2600;
    let hideTimer = 0;
    let lastSubtitleChoice = null;

    const setIcon = (button, name) => {
        const path = button?.querySelector("svg path");
        const icon = icons[name];
        if (path && icon) {
            path.setAttribute("d", icon.path);
            path.parentElement.setAttribute("viewBox", icon.viewBox);
        }
    };

    // --- chrome visibility ------------------------------------------------------------------
    const settingsOpen = () => settings && !settings.hidden;
    const show = () => {
        stage.dataset.chromeState = "visible";
        window.clearTimeout(hideTimer);
        if (!video.paused && !settingsOpen()) {
            hideTimer = window.setTimeout(() => {
                if (!video.paused && !settingsOpen() && !stage.contains(document.activeElement?.closest?.(".player-settings"))) {
                    stage.dataset.chromeState = "hidden";
                }
            }, hideDelayMs);
        }
    };
    stage.addEventListener("pointermove", show);
    stage.addEventListener("pointerdown", show);
    stage.addEventListener("focusin", show);
    stage.addEventListener("pointerleave", () => {
        if (!video.paused && !settingsOpen()) stage.dataset.chromeState = "hidden";
    });

    // --- play / pause ----------------------------------------------------------------------
    const togglePlay = () => {
        if (video.hidden) return;
        if (video.paused || video.ended) {
            void video.play().catch(() => {});
        } else {
            video.pause();
        }
    };
    const renderPlayState = () => {
        const playing = !video.paused && !video.ended;
        stage.dataset.playing = String(playing);
        for (const button of stage.querySelectorAll("[data-chrome-play]")) {
            setIcon(button, playing ? "pause" : "play");
            button.setAttribute("aria-label", playing ? button.dataset.labelPause : button.dataset.labelPlay);
        }
        show();
    };
    for (const button of stage.querySelectorAll("[data-chrome-play]")) {
        button.addEventListener("click", togglePlay);
    }
    root.addEventListener(design?.actionEvent || "jularr:player-action", event => {
        if (event.detail?.action === "playPause") togglePlay();
    });
    video.addEventListener("click", togglePlay);
    video.addEventListener("dblclick", () => toggleFullscreen());
    for (const name of ["play", "pause", "ended", "emptied"]) video.addEventListener(name, renderPlayState);

    // --- timeline fill (the value itself is written by episode-player.js) -------------------
    const renderTimelineFill = () => {
        const max = Number(timeline?.max) || 0;
        const value = Number(timeline?.value) || 0;
        timeline?.style.setProperty("--progress", max > 0 ? `${(value / max) * 100}%` : "0%");
    };
    timeline?.addEventListener("input", renderTimelineFill);
    video.addEventListener("timeupdate", renderTimelineFill);
    video.addEventListener("seeked", renderTimelineFill);
    video.addEventListener("loadedmetadata", renderTimelineFill);

    // --- volume ----------------------------------------------------------------------------
    const muteButton = stage.querySelector("[data-chrome-mute]");
    const renderVolume = () => {
        const muted = video.muted || video.volume === 0;
        setIcon(muteButton, muted ? "volumeMuted" : "volume");
        muteButton?.setAttribute("aria-label", muted ? muteButton.dataset.labelUnmute : muteButton.dataset.labelMute);
        if (volumeSlider) {
            volumeSlider.value = String(video.muted ? 0 : video.volume);
            volumeSlider.style.setProperty("--progress", `${(video.muted ? 0 : video.volume) * 100}%`);
        }
    };
    try {
        const stored = Number(localStorage.getItem(volumeKey));
        if (Number.isFinite(stored) && stored >= 0 && stored <= 1 && localStorage.getItem(volumeKey) !== null) video.volume = stored;
    } catch { /* storage unavailable */ }
    muteButton?.addEventListener("click", () => {
        if (video.muted || video.volume === 0) {
            video.muted = false;
            if (video.volume === 0) video.volume = 0.6;
        } else {
            video.muted = true;
        }
    });
    volumeSlider?.addEventListener("input", () => {
        video.volume = Number(volumeSlider.value);
        video.muted = video.volume === 0;
    });
    video.addEventListener("volumechange", () => {
        renderVolume();
        try { localStorage.setItem(volumeKey, String(video.volume)); } catch { /* storage unavailable */ }
    });
    renderVolume();

    // --- full screen & picture-in-picture --------------------------------------------------
    const fullscreenElement = () => document.fullscreenElement || document.webkitFullscreenElement;
    const toggleFullscreen = async () => {
        try {
            if (fullscreenElement()) {
                await (document.exitFullscreen || document.webkitExitFullscreen).call(document);
            } else if (stage.requestFullscreen || stage.webkitRequestFullscreen) {
                await (stage.requestFullscreen || stage.webkitRequestFullscreen).call(stage);
            } else if (video.webkitEnterFullscreen) {
                // iOS Safari only allows the <video> element itself to go full screen.
                video.webkitEnterFullscreen();
            }
        } catch { /* user gesture or platform refused */ }
    };
    const renderFullscreen = () => {
        const active = fullscreenElement() === stage;
        stage.classList.toggle("is-fullscreen", active);
        for (const button of stage.querySelectorAll("[data-chrome-fullscreen]")) {
            setIcon(button, active ? "fullscreenExit" : "fullscreen");
            button.setAttribute("aria-label", active ? button.dataset.labelExit : button.dataset.labelEnter);
            button.title = button.getAttribute("aria-label");
        }
    };
    for (const button of stage.querySelectorAll("[data-chrome-fullscreen]")) {
        button.addEventListener("click", toggleFullscreen);
    }
    document.addEventListener("fullscreenchange", renderFullscreen);
    document.addEventListener("webkitfullscreenchange", renderFullscreen);

    const supportsPip = (document.pictureInPictureEnabled === true && typeof video.requestPictureInPicture === "function")
        || typeof video.webkitSetPresentationMode === "function";
    if (pipButton && supportsPip && !video.disablePictureInPicture) {
        pipButton.hidden = false;
        pipButton.addEventListener("click", async () => {
            try {
                if (document.pictureInPictureElement) {
                    await document.exitPictureInPicture();
                } else if (typeof video.requestPictureInPicture === "function" && document.pictureInPictureEnabled) {
                    await video.requestPictureInPicture();
                } else {
                    // Safari on iOS/iPadOS.
                    video.webkitSetPresentationMode(video.webkitPresentationMode === "picture-in-picture" ? "inline" : "picture-in-picture");
                }
            } catch { /* not ready yet */ }
        });
    }

    // --- settings menu --------------------------------------------------------------------
    const settingsToggles = stage.querySelectorAll("[data-chrome-settings-toggle]");
    const setSettings = (open) => {
        if (!settings) return;
        settings.hidden = !open;
        for (const toggle of settingsToggles) toggle.setAttribute("aria-expanded", String(open));
        if (open) {
            stage.dataset.chromeState = "visible";
            window.clearTimeout(hideTimer);
            settings.querySelector("select, input, button:not([data-chrome-settings-close])")?.focus({ preventScroll: true });
        } else {
            show();
        }
    };
    for (const toggle of settingsToggles) toggle.addEventListener("click", () => setSettings(settings.hidden));
    stage.querySelector("[data-chrome-settings-close]")?.addEventListener("click", () => setSettings(false));
    stage.addEventListener("pointerdown", event => {
        if (settingsOpen() && !event.target.closest(".player-settings, [data-chrome-settings-toggle]")) setSettings(false);
    });

    // --- speed & subtitle shortcuts ---------------------------------------------------------
    const renderSpeed = () => {
        if (!speedButton) return;
        const rate = Number(speedSelect?.value || video.playbackRate || 1);
        speedButton.textContent = `${Number.isInteger(rate) ? rate : rate.toString().replace(/0+$/, "")}×`;
    };
    speedButton?.addEventListener("click", () => {
        if (!speedSelect) return;
        // Cycle through the offered speeds; the select stays the one source of truth.
        const index = (speedSelect.selectedIndex + 1) % speedSelect.options.length;
        speedSelect.selectedIndex = index;
        speedSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });
    speedSelect?.addEventListener("change", renderSpeed);
    video.addEventListener("ratechange", renderSpeed);
    renderSpeed();

    const renderSubtitleButton = () => {
        if (!subtitleButton) return;
        const on = Boolean(subtitleSelect) && subtitleSelect.value !== "off";
        subtitleButton.setAttribute("aria-pressed", String(on));
        subtitleButton.hidden = !subtitleSelect || subtitleSelect.options.length < 2;
        if (on) lastSubtitleChoice = subtitleSelect.value;
    };
    subtitleButton?.addEventListener("click", () => {
        if (!subtitleSelect) return;
        const enabled = [...subtitleSelect.options].filter(option => option.value !== "off" && !option.disabled);
        const next = subtitleSelect.value !== "off"
            ? "off"
            : (lastSubtitleChoice && enabled.some(option => option.value === lastSubtitleChoice) ? lastSubtitleChoice : enabled[0]?.value);
        if (!next) return;
        subtitleSelect.value = next;
        subtitleSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });
    subtitleSelect?.addEventListener("change", renderSubtitleButton);
    renderSubtitleButton();

    // --- keyboard (when focus is in the player and not in a form control) -------------------
    stage.addEventListener("keydown", event => {
        if (event.target.closest("select, input:not([type=range]), textarea, .player-settings")) {
            if (event.key === "Escape" && settingsOpen()) {
                event.preventDefault();
                setSettings(false);
                stage.focus({ preventScroll: true });
            }
            return;
        }
        const dispatch = action => design ? design.dispatch(root, action) : null;
        switch (event.key) {
            case " ":
            case "k":
                event.preventDefault();
                togglePlay();
                break;
            case "ArrowLeft":
            case "j":
                event.preventDefault();
                dispatch("seekBack10");
                break;
            case "ArrowRight":
            case "l":
                event.preventDefault();
                dispatch("seekForward10");
                break;
            case "ArrowUp":
                event.preventDefault();
                video.volume = Math.min(1, video.volume + 0.05);
                video.muted = false;
                break;
            case "ArrowDown":
                event.preventDefault();
                video.volume = Math.max(0, video.volume - 0.05);
                break;
            case "m":
                video.muted = !video.muted;
                break;
            case "f":
                void toggleFullscreen();
                break;
            case "c":
                subtitleButton?.click();
                break;
            case "Escape":
                if (settingsOpen()) setSettings(false);
                break;
        }
        show();
    });

    renderPlayState();
    renderTimelineFill();
    renderFullscreen();
})();

// Season filter for the episode list next to the player, and the owner's sources dialog.
(() => {
    const select = document.querySelector("[data-watch-season]");
    select?.addEventListener("change", () => {
        for (const item of document.querySelectorAll("[data-watch-episodes] li[data-season]")) {
            item.hidden = item.dataset.season !== select.value;
        }
    });

    for (const opener of document.querySelectorAll("[data-open-dialog]")) {
        const dialog = document.getElementById(opener.dataset.openDialog);
        opener.addEventListener("click", () => dialog?.showModal());
    }
    const autoOpen = document.querySelector("dialog[data-open-on-load='true']");
    autoOpen?.showModal();
})();
