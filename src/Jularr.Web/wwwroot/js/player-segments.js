// Skip actions and seek previews for the web player. Both render the canonical
// server descriptor (the same shape the native player bootstrap exposes); the
// browser never derives its own segment boundaries, confidence rules or thumbnails.
// Seeking goes through the player's timeline control so live-stream offsets stay
// owned by episode-player.js.
(() => {
    const SKIP_LABELS = Object.freeze({
        intro: "Skip intro",
        recap: "Skip recap",
        outro: "Skip outro",
        credits: "Skip credits",
        preview: "Skip preview"
    });
    const TRICKPLAY_POLL_MS = 15000;
    const TRICKPLAY_MAX_POLLS = 40;
    const PREVIEW_WIDTH = 160;

    // The page renders one player and one descriptor right after the player markup.
    const readDescriptor = () => {
        const element = document.querySelector("script[data-media-navigation]");
        if (!element) {
            return null;
        }

        try {
            return JSON.parse(element.textContent || "null");
        } catch {
            return null;
        }
    };

    const formatTime = seconds => {
        const total = Math.max(0, Math.floor(seconds));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const remainder = String(total % 60).padStart(2, "0");
        return hours > 0
            ? `${hours}:${String(minutes).padStart(2, "0")}:${remainder}`
            : `${minutes}:${remainder}`;
    };

    const setUpSkip = (root, video, timeline, descriptor) => {
        const stage = root.querySelector("[data-video-stage]");
        const segments = (descriptor?.segments || [])
            .filter(segment => segment.canSkip === true && SKIP_LABELS[segment.kind]);
        if (!stage || segments.length === 0) {
            return;
        }

        const button = document.createElement("button");
        button.type = "button";
        button.className = "button player-skip-segment";
        button.hidden = true;
        button.setAttribute("data-skip-segment", "");
        stage.append(button);

        let active = null;

        const currentMs = () => Number(timeline.value) * 1000;

        const update = () => {
            const now = currentMs();
            const next = timeline.disabled || Number(timeline.max) <= 0
                ? null
                : segments.find(segment => now >= segment.startMs && now < segment.endMs - 500) || null;

            if (next === active) {
                return;
            }

            active = next;
            button.hidden = active === null;
            if (active) {
                button.textContent = `${SKIP_LABELS[active.kind]} ›`;
                button.setAttribute("aria-label", `${SKIP_LABELS[active.kind]}, jump to ${formatTime(active.endMs / 1000)}`);
            }
        };

        button.addEventListener("click", () => {
            if (!active) {
                return;
            }

            const target = Math.min(Number(timeline.max), active.endMs / 1000);
            timeline.value = String(target);
            timeline.dispatchEvent(new Event("change", { bubbles: true }));
            active = null;
            button.hidden = true;
        });

        video.addEventListener("timeupdate", update);
        video.addEventListener("seeked", update);
    };

    const setUpTrickplay = (root, timeline, initial) => {
        const host = root.querySelector("[data-playback-timeline-row]");
        if (!host || !initial?.descriptorUrl) {
            return;
        }

        let trickplay = initial;
        const preview = document.createElement("div");
        preview.className = "player-trickplay-preview";
        preview.hidden = true;
        preview.setAttribute("aria-hidden", "true");
        const image = document.createElement("div");
        image.className = "player-trickplay-image";
        const label = document.createElement("span");
        label.className = "player-trickplay-time";
        preview.append(image, label);
        host.classList.add("player-trickplay-host");
        host.append(preview);

        const isReady = () =>
            trickplay?.state === "ready" &&
            trickplay.intervalMs > 0 &&
            trickplay.columns > 0 &&
            trickplay.rows > 0 &&
            trickplay.thumbnailCount > 0 &&
            (trickplay.spriteUrls || []).length > 0;

        const show = (seconds, clientX) => {
            const max = Number(timeline.max);
            if (!isReady() || timeline.disabled || !(max > 0)) {
                preview.hidden = true;
                return;
            }

            const perSprite = trickplay.columns * trickplay.rows;
            const index = Math.min(
                trickplay.thumbnailCount - 1,
                Math.max(0, Math.floor((seconds * 1000) / trickplay.intervalMs)));
            const sprite = trickplay.spriteUrls[Math.floor(index / perSprite)];
            if (!sprite) {
                preview.hidden = true;
                return;
            }

            const height = Math.round(PREVIEW_WIDTH * trickplay.tileHeight / trickplay.tileWidth);
            const column = index % trickplay.columns;
            const row = Math.floor(index / trickplay.columns) % trickplay.rows;
            image.style.width = `${PREVIEW_WIDTH}px`;
            image.style.height = `${height}px`;
            image.style.backgroundImage = `url("${sprite}")`;
            image.style.backgroundSize = `${trickplay.columns * PREVIEW_WIDTH}px ${trickplay.rows * height}px`;
            image.style.backgroundPosition = `-${column * PREVIEW_WIDTH}px -${row * height}px`;
            label.textContent = formatTime(seconds);

            const hostRect = host.getBoundingClientRect();
            const half = PREVIEW_WIDTH / 2;
            const x = Math.min(hostRect.width - half, Math.max(half, clientX - hostRect.left));
            preview.style.left = `${x}px`;
            preview.hidden = false;
        };

        const secondsAt = clientX => {
            const rect = timeline.getBoundingClientRect();
            const fraction = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
            return Math.min(1, Math.max(0, fraction)) * Number(timeline.max);
        };

        const thumbX = () => {
            const rect = timeline.getBoundingClientRect();
            const max = Number(timeline.max);
            return rect.left + (max > 0 ? Number(timeline.value) / max : 0) * rect.width;
        };

        timeline.addEventListener("pointermove", event => show(secondsAt(event.clientX), event.clientX));
        timeline.addEventListener("pointerleave", () => { preview.hidden = true; });
        timeline.addEventListener("input", () => show(Number(timeline.value), thumbX()));
        timeline.addEventListener("change", () => { preview.hidden = true; });
        timeline.addEventListener("blur", () => { preview.hidden = true; });

        // Generation runs as a background operation; poll briefly until it settles.
        let polls = 0;
        const poll = async () => {
            if (trickplay?.state !== "generating" || polls >= TRICKPLAY_MAX_POLLS) {
                return;
            }

            polls++;
            try {
                const response = await fetch(trickplay.descriptorUrl, {
                    credentials: "same-origin",
                    headers: { Accept: "application/json" }
                });
                if (response.ok) {
                    trickplay = await response.json();
                }
            } catch {
                // Seek previews are optional; playback continues without them.
            }

            if (trickplay?.state === "generating") {
                window.setTimeout(poll, TRICKPLAY_POLL_MS);
            }
        };

        if (trickplay.state === "generating") {
            window.setTimeout(poll, TRICKPLAY_POLL_MS);
        }
    };

    for (const root of document.querySelectorAll("[data-episode-player]")) {
        const video = root.querySelector("video[data-playback-video]");
        const timeline = root.querySelector("[data-playback-timeline]");
        const descriptor = readDescriptor();
        if (!video || !timeline || !descriptor) {
            continue;
        }

        setUpSkip(root, video, timeline, descriptor.segments);
        setUpTrickplay(root, timeline, descriptor.trickplay);
    }
})();
