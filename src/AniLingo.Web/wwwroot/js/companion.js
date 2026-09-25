(() => {
    const root = document.querySelector("[data-companion]");
    if (!root) {
        return;
    }

    const connectPanel = root.querySelector("[data-companion-connect]");
    const livePanel = root.querySelector("[data-companion-live]");
    const pairForm = root.querySelector("[data-pair-form]");
    const pairCode = root.querySelector("[data-pair-code]");
    const pairError = root.querySelector("[data-pair-error]");
    const sessionError = root.querySelector("[data-session-error]");
    const sessionTitle = root.querySelector("[data-session-title]");
    const sessionEpisode = root.querySelector("[data-session-episode]");
    const sessionStatus = root.querySelector("[data-session-status]");
    const currentSentence = root.querySelector("[data-current-sentence]");
    const currentTokens = root.querySelector("[data-current-tokens]");
    const selectedWord = root.querySelector("[data-selected-word]");
    const wordSurface = root.querySelector("[data-word-surface]");
    const wordReading = root.querySelector("[data-word-reading]");
    const wordMeaning = root.querySelector("[data-word-meaning]");
    const wordState = root.querySelector("[data-word-state]");
    const position = root.querySelector("[data-position]");
    const revision = root.querySelector("[data-revision]");
    const openLearning = root.querySelector("[data-open-learning]");

    let grant = null;
    let state = null;
    let selectedTermId = null;
    let socket = null;
    let reconnectAttempt = 0;
    let ended = false;

    const formatTime = valueMs => {
        const totalSeconds = Math.max(0, Math.floor((Number(valueMs) || 0) / 1000));
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        return hours > 0
            ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
            : `${minutes}:${String(seconds).padStart(2, "0")}`;
    };

    const showError = (node, message) => {
        if (!node) {
            return;
        }
        node.textContent = message;
        node.hidden = !message;
    };

    const selectedToken = () => {
        const tokens = state?.currentCueTokens || [];
        if (selectedTermId) {
            return tokens.find(token => token.termId === selectedTermId) || null;
        }
        return null;
    };

    const render = () => {
        if (!state) {
            return;
        }

        connectPanel.hidden = true;
        livePanel.hidden = false;

        sessionTitle.textContent = state.animeTitle || "AniLingo TV";
        sessionEpisode.textContent = state.episodeTitle || `Episode ${state.episodeId}`;
        sessionStatus.textContent = state.isPlaying ? "Playing" : "Paused";
        currentSentence.textContent =
            state.currentCueText || "Waiting for the current subtitle…";
        position.textContent =
            `${formatTime(state.positionMs)} / ${formatTime(state.durationMs)}`;
        revision.textContent = `Revision ${state.revision}`;
        openLearning.href = `/Library/Episode/${encodeURIComponent(state.episodeId)}`;

        currentTokens.replaceChildren();
        for (const token of state.currentCueTokens || []) {
            const button = document.createElement(token.termId ? "button" : "span");
            button.textContent = token.surface;
            button.className = token.termId ? "button companion-token" : "companion-token";
            if (token.termId) {
                button.type = "button";
                button.addEventListener("click", () => {
                    selectedTermId = token.termId;
                    renderSelectedWord();
                    void sendCommand("openWord", { termId: token.termId });
                });
            }
            currentTokens.appendChild(button);
        }

        if (state.selectedTermId) {
            selectedTermId = state.selectedTermId;
        }
        renderSelectedWord();
    };

    const renderSelectedWord = () => {
        const token = selectedToken();
        selectedWord.hidden = !token;
        if (!token) {
            return;
        }

        wordSurface.textContent = token.surface || token.canonical || "";
        wordReading.textContent = token.reading || "";
        wordMeaning.textContent = token.meaning || "No meaning available.";
        wordState.textContent = token.state || "new";
    };

    const pair = async payload => {
        showError(pairError, "");
        const response = await fetch(root.dataset.pairUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const error = await response.json().catch(() => null);
            throw new Error(error?.message || "Pairing failed.");
        }

        grant = await response.json();
        state = grant.state;
        ended = false;
        render();
        await connectHub();
    };

    const sendCommand = async (type, payload = {}) => {
        if (!grant || !state || ended) {
            return;
        }

        showError(sessionError, "");
        const url = `/api/client/v1/playback-sessions/${encodeURIComponent(grant.sessionId)}/commands`;
        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-AniLingo-Companion-Token": grant.accessToken
            },
            body: JSON.stringify({
                commandId: crypto.randomUUID(),
                expectedRevision: state.revision,
                type,
                payload
            })
        });

        const result = await response.json().catch(() => null);
        if (response.status === 409 && result?.state) {
            state = result.state;
            render();
            showError(
                sessionError,
                "The TV changed before this command arrived. State was refreshed; try again."
            );
            return;
        }

        if (!response.ok) {
            showError(
                sessionError,
                result?.message || "The TV command could not be sent."
            );
        }
    };

    const signalRFrames = raw =>
        String(raw)
            .split("\u001e")
            .map(value => value.trim())
            .filter(Boolean);

    const handleHubMessage = message => {
        if (!message || message.type !== 1) {
            return;
        }

        const argument = message.arguments?.[0];
        switch (message.target) {
            case "SessionState":
                if (argument) {
                    state = argument;
                    render();
                }
                break;
            case "AccessRevoked":
                ended = true;
                sessionStatus.textContent = "Revoked";
                showError(sessionError, "The TV revoked companion access.");
                socket?.close();
                break;
            case "SessionEnded":
                ended = true;
                sessionStatus.textContent = "Ended";
                showError(sessionError, "The TV playback session ended.");
                socket?.close();
                break;
        }
    };

    const connectHub = async () => {
        if (!grant || ended) {
            return;
        }

        socket?.close();

        const hubUrl = new URL(grant.hubUrl, window.location.origin);
        const negotiate = new URL(hubUrl.toString());
        negotiate.pathname = `${negotiate.pathname.replace(/\/$/, "")}/negotiate`;
        negotiate.searchParams.set("negotiateVersion", "1");

        const negotiateResponse = await fetch(negotiate, {
            method: "POST",
            credentials: "same-origin",
            headers: { "Accept": "application/json" }
        });
        if (!negotiateResponse.ok) {
            throw new Error("Live companion connection negotiation failed.");
        }

        const negotiated = await negotiateResponse.json();
        const connectionToken = negotiated.connectionToken || negotiated.connectionId;
        if (!connectionToken) {
            throw new Error("Live companion connection token is missing.");
        }

        const websocketUrl = new URL(hubUrl.toString());
        websocketUrl.protocol = websocketUrl.protocol === "https:" ? "wss:" : "ws:";
        websocketUrl.searchParams.set("id", connectionToken);

        socket = new WebSocket(websocketUrl);
        socket.addEventListener("open", () => {
            reconnectAttempt = 0;
            socket.send('{"protocol":"json","version":1}\u001e');
        });
        socket.addEventListener("message", event => {
            for (const frame of signalRFrames(event.data)) {
                const message = JSON.parse(frame);
                handleHubMessage(message);
            }
        });
        socket.addEventListener("close", () => {
            if (ended || !grant) {
                return;
            }
            const delays = [1000, 2000, 5000, 10000];
            const delay = delays[Math.min(reconnectAttempt, delays.length - 1)];
            reconnectAttempt += 1;
            window.setTimeout(() => {
                void connectHub().catch(error => {
                    showError(sessionError, error.message);
                });
            }, delay);
        });
        socket.addEventListener("error", () => {
            showError(
                sessionError,
                "Live connection interrupted. AniLingo will reconnect automatically."
            );
        });
    };

    pairForm?.addEventListener("submit", event => {
        event.preventDefault();
        const code = pairCode.value.trim();
        if (!/^\d{6}$/.test(code)) {
            showError(pairError, "Enter the 6-digit code shown on the TV.");
            return;
        }

        void pair({ code }).catch(error => {
            showError(pairError, error.message);
        });
    });

    root.querySelectorAll("[data-command]").forEach(button => {
        button.addEventListener("click", () => {
            const type = button.dataset.command;
            if (!type) {
                return;
            }

            const payload =
                (type === "markKnown" || type === "addToLearning") && selectedTermId
                    ? { termId: selectedTermId }
                    : {};
            void sendCommand(type, payload);
        });
    });

    const token = new URLSearchParams(window.location.search).get("token");
    if (token) {
        void pair({ token }).catch(error => {
            showError(pairError, error.message);
        });
    }
})();
