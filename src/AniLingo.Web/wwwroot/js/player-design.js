(() => {
    const ACTION_EVENT = "anilingo:player-action";
    const actions = Object.freeze({
        playPause: "playPause",
        seekBack10: "seekBack10",
        seekForward10: "seekForward10",
        seekTo: "seekTo",
        selectAudioTrack: "selectAudioTrack",
        selectSubtitleTrack: "selectSubtitleTrack",
        repeatCurrentCue: "repeatCurrentCue",
        learnCurrentCue: "learnCurrentCue",
        openWord: "openWord",
        markKnown: "markKnown",
        addToLearning: "addToLearning",
        openCompanion: "openCompanion",
        closeOverlay: "closeOverlay",
        exitPlayer: "exitPlayer"
    });

    const dispatch = (root, action, detail = {}) => {
        root.dispatchEvent(new CustomEvent(ACTION_EVENT, {
            bubbles: true,
            detail: { action, ...detail }
        }));
    };

    const cueText = cue =>
        (cue?.tokens || []).map(token => token.surface || "").join("");

    const tokenLabel = token => {
        const bits = [token.surface];
        if (token.reading) bits.push(token.reading);
        if (token.meaning) bits.push(token.meaning);
        return bits.filter(Boolean).join(" · ");
    };

    const renderCue = (root, overlay, cue) => {
        overlay.replaceChildren();

        if (!cue) {
            overlay.hidden = true;
            overlay.removeAttribute("data-active-cue");
            return;
        }

        overlay.hidden = false;
        overlay.dataset.activeCue = String(cue.startMs ?? "");
        overlay.setAttribute("aria-label", `Japanese subtitle: ${cueText(cue)}. Tap the line to learn it.`);

        const bubble = document.createElement("div");
        bubble.className = "player-subtitle-bubble";
        bubble.dataset.playerAction = actions.learnCurrentCue;
        bubble.tabIndex = 0;
        bubble.setAttribute("role", "button");
        bubble.setAttribute("aria-label", "Open learning for this subtitle line");

        for (const token of cue.tokens || []) {
            if (token.isVocabulary) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "player-subtitle-token";
                button.dataset.playerAction = actions.openWord;
                button.textContent = token.surface;
                button.title = tokenLabel(token);
                button.setAttribute("aria-label", tokenLabel(token));
                button.addEventListener("click", event => {
                    event.stopPropagation();
                    dispatch(root, actions.openWord, { token, cue });
                });
                bubble.append(button);
            } else {
                const span = document.createElement("span");
                span.textContent = token.surface;
                bubble.append(span);
            }
        }

        const learn = () => dispatch(root, actions.learnCurrentCue, { cue });
        bubble.addEventListener("click", event => {
            if (event.target === bubble || !(event.target instanceof HTMLButtonElement)) {
                learn();
            }
        });
        bubble.addEventListener("keydown", event => {
            if (event.target !== bubble) return;
            if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                learn();
            }
        });

        overlay.append(bubble);
    };

    window.AniLingoPlayerDesign = Object.freeze({
        actionEvent: ACTION_EVENT,
        actions,
        dispatch,
        cueText,
        renderCue
    });
})();
