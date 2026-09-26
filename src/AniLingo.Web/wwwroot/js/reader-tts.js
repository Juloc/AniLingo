(() => {
    "use strict";

    // Shared read-aloud integration for every unified Reader (Books and Novels).
    // It consumes the provider-neutral speech engine (tts.js) and the Reader shell
    // API (reader-shell.js). The TTS cursor lives only in this page session: it is
    // never written to reading progress, and spoken text is never sent or stored.

    const CONTINUE_KEY = "anilingo:reader-tts-continue";
    const CONTINUE_TTL_MS = 60000;
    const LOOK_AHEAD = 2;
    const MAX_CHUNK_LENGTH = 260;
    const USER_SCROLL_GRACE_MS = 4000;
    const PAGE_TURN_GAP_MS = 700;
    const AUTOSTART_WATCHDOG_MS = 4000;
    const PARAGRAPH_SELECTOR = "[data-reader-paragraph],[data-book-paragraph]";
    const WORD_HIGHLIGHT = "reader-tts-word";

    const unavailableText = (reason, label) => ({
        "unsupported": "Dieser Browser bietet keine Sprachausgabe an.",
        "no-provider": "Keine Sprachausgabe verfügbar.",
        "provider-unavailable": "Die Gerätestimme ist gerade nicht verfügbar.",
        "no-voice-for-language": `Keine Stimme für ${label} verfügbar.`
    }[reason] || "Vorlesen ist nicht verfügbar.");

    const mount = api => {
        const root = api?.root;
        if (!root || root.dataset.readerTtsMounted === "true") return;
        const toggle = root.querySelector("[data-reader-tts-toggle]");
        const bar = root.querySelector("[data-reader-tts-bar]");
        if (!toggle || !bar) return;
        root.dataset.readerTtsMounted = "true";

        const speech = window.AniLingoTts;
        const provider = speech?.createDeviceProvider?.() || null;
        const supported = Boolean(provider?.supported);
        const surface = api.surface || root;
        const section = root.querySelector("[data-reader-tts-settings]");
        const statusLine = section?.querySelector("[data-reader-tts-status]");
        const voiceMapInput = section?.querySelector("[data-reader-tts-voice-map]");
        const voiceSelects = Array.from(section?.querySelectorAll("[data-reader-tts-voice]") || []);
        const settingControls = Array.from(
            section?.querySelectorAll("[data-reader-tts-setting]") || []);
        const barStatus = bar.querySelector("[data-reader-tts-bar-status]");
        const pauseButton = bar.querySelector("[data-reader-tts-pause]");
        const stopButton = bar.querySelector("[data-reader-tts-stop]");
        const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
        const supportsHighlights =
            typeof CSS !== "undefined" && CSS.highlights && typeof Highlight === "function";

        // The toolbar auto-hides; the player must stay one tap away while active.
        root.append(bar);

        const normalize = value => speech ? speech.normalizeLanguage(value) : "und";
        const baseOf = value => normalize(value).split("-")[0].toLowerCase();

        // Playback state; the cursor (plan.index/offset) is session-only.
        let run = 0;
        let state = "idle";
        let plan = null;
        let play = () => {};

        // ---- Settings (canonical store: ReaderPreferences via the shell) ------------

        let voices = [];

        const control = key =>
            settingControls.find(item => item.dataset.readerTtsSetting === key) || null;

        const readVoiceMap = () => {
            try {
                const value = JSON.parse(voiceMapInput?.value || "{}");
                return value && typeof value === "object" && !Array.isArray(value) ? value : {};
            } catch {
                return {};
            }
        };

        const writeVoiceMap = map => {
            if (voiceMapInput) {
                voiceMapInput.value = Object.keys(map).length ? JSON.stringify(map) : "";
            }
        };

        // Exact tag first, then base language (mirrors SpeechVoiceMap.VoiceIdFor).
        const voiceIdFor = (map, language) => {
            const tag = normalize(language).toLowerCase();
            if (tag === "und") return "";
            const entries = Object.entries(map);
            const exact = entries.find(([key]) => normalize(key).toLowerCase() === tag);
            if (exact) return String(exact[1] || "");
            const base = entries.find(([key]) => normalize(key).toLowerCase() === baseOf(tag));
            return base ? String(base[1] || "") : "";
        };

        const preferences = () => ({
            providerId: control("ttsProviderId")?.value || "auto",
            rate: Number(control("ttsRate")?.value || 1),
            pitch: Number(control("ttsPitch")?.value || 1),
            volume: Number(control("ttsVolume")?.value ?? 1),
            autoContinueChapters: Boolean(control("ttsAutoContinueChapters")?.checked),
            voices: readVoiceMap()
        });

        const resolveFor = language => {
            if (!supported) return { resolution: null, unavailableReason: "unsupported" };
            const prefs = preferences();
            return speech.resolveSpeech(
                {
                    providerId: prefs.providerId,
                    voiceId: voiceIdFor(prefs.voices, language),
                    language
                },
                [provider.descriptor],
                voices);
        };

        const describeVoice = (language, label, storedVoice) => {
            const outcome = resolveFor(language);
            const resolution = outcome.resolution;
            if (!resolution) return unavailableText(outcome.unavailableReason, label);
            if (storedVoice && resolution.voiceId !== storedVoice) {
                return "Gewählte Stimme fehlt auf diesem Gerät – " +
                    (resolution.voice ? resolution.voice.name : "der Gerätestandard") +
                    " liest vor.";
            }
            if (resolution.usesProviderDefaultVoice) {
                return `Keine Stimme für ${label} installiert – der Gerätestandard liest vor.`;
            }
            return storedVoice ? "" : `Automatisch: ${resolution.voice.name}`;
        };

        const renderVoices = () => {
            const map = readVoiceMap();
            for (const select of voiceSelects) {
                const language = select.dataset.readerTtsVoice;
                const label = select.dataset.readerTtsVoiceLabel || language;
                const stored = voiceIdFor(map, language);
                const matching = voices.filter(voice =>
                    baseOf(voice.language) === baseOf(language));
                const options = [new Option("Automatisch", "auto")];
                for (const voice of matching) {
                    options.push(new Option(
                        voice.name + (voice.isLocal ? "" : " · Online"),
                        voice.voiceId));
                }
                if (stored && !matching.some(voice => voice.voiceId === stored)) {
                    options.push(new Option(`${stored} (nicht auf diesem Gerät)`, stored));
                }
                select.replaceChildren(...options);
                select.value = stored || "auto";

                const status = section?.querySelector(
                    `[data-reader-tts-voice-status="${CSS.escape(language)}"]`);
                if (status) status.textContent = describeVoice(language, label, stored);
            }

            if (statusLine) {
                statusLine.textContent = supported ? "" : unavailableText("unsupported");
                statusLine.hidden = supported;
            }
        };

        const formatOutput = (key, value) =>
            key === "ttsVolume"
                ? `${Math.round(value * 100)}%`
                : `${value.toFixed(2)}×`;

        const syncOutputs = () => {
            section?.querySelectorAll("[data-reader-tts-output]").forEach(output => {
                const key = output.dataset.readerTtsOutput;
                const input = control(key);
                if (input) output.textContent = formatOutput(key, Number(input.value));
            });
        };

        const applySettings = settings => {
            if (!settings || !section) return;
            for (const input of settingControls) {
                const key = input.dataset.readerTtsSetting;
                if (!(key in settings)) continue;
                if (input.type === "checkbox") input.checked = Boolean(settings[key]);
                else input.value = String(settings[key]);
            }
            if (settings.ttsVoiceIds && typeof settings.ttsVoiceIds === "object") {
                writeVoiceMap({ ...settings.ttsVoiceIds });
            }
            syncOutputs();
            renderVoices();
            api.updateSourceBadges();
        };

        const save = async changedKey => {
            try {
                await api.postSettingsCommand({ changedKey });
            } catch (error) {
                console.warn(error);
            }
        };

        let restartTimer = 0;
        const restartWithNewSettings = () => {
            window.clearTimeout(restartTimer);
            if (!supported || state !== "speaking" || !plan) return;
            restartTimer = window.setTimeout(() => {
                if (state === "speaking" && plan) play(plan, plan.index, plan.offset);
            }, 250);
        };

        section?.querySelectorAll("[data-reader-tts-field]").forEach(wrapper => {
            api.addResetButton(wrapper, wrapper.dataset.readerTtsField);
        });

        for (const input of settingControls) {
            if (input.type === "range") input.addEventListener("input", syncOutputs);
            input.addEventListener("change", () => {
                syncOutputs();
                renderVoices();
                restartWithNewSettings();
                save(input.dataset.readerTtsSetting);
            });
        }

        for (const select of voiceSelects) {
            select.addEventListener("change", () => {
                const language = select.dataset.readerTtsVoice;
                const map = readVoiceMap();
                for (const key of Object.keys(map)) {
                    if (normalize(key).toLowerCase() === language.toLowerCase()) delete map[key];
                }
                if (select.value && select.value !== "auto") map[language] = select.value;
                writeVoiceMap(map);
                renderVoices();
                restartWithNewSettings();
                save("ttsVoiceId:" + language);
            });
        }

        root.addEventListener("anilingo:reader-settings-response", event => {
            applySettings(event.detail?.settings);
        });

        applySettings(api.getSettings());

        if (!supported) return;

        const refreshVoices = () => provider.getVoices().then(list => {
            voices = list;
            renderVoices();
        }, () => {});
        refreshVoices();
        window.speechSynthesis?.addEventListener?.("voiceschanged", refreshVoices);
        root.querySelector("[data-reader-settings-container]")
            ?.addEventListener("toggle", event => {
                if (event.target.open) refreshVoices();
            });

        // ---- Document adapter: paragraphs, language, position --------------------

        const isVisible = element =>
            typeof element.checkVisibility === "function"
                ? element.checkVisibility()
                : element.getClientRects().length > 0;

        const languageOf = element =>
            normalize(element.closest("[lang]")?.getAttribute("lang") || "");

        const paragraphs = () =>
            Array.from(surface.querySelectorAll(PARAGRAPH_SELECTOR))
                .filter(element => isVisible(element) && element.textContent.trim());

        const paged = () =>
            (root.dataset.readingMode || api.getSettings().readingMode) === "paged";

        const scrollContainerOf = element => {
            for (let node = element.parentElement; node && node !== root; node = node.parentElement) {
                if (node.scrollWidth > node.clientWidth + 1 &&
                    /(auto|scroll|hidden)/.test(getComputedStyle(node).overflowX)) {
                    return node;
                }
            }
            return surface;
        };

        const topLimit = () => {
            const chrome = root.querySelector("[data-reader-chrome-primary]");
            if (!chrome || root.classList.contains("reader-chrome-hidden")) return 0;
            return Math.max(0, chrome.getBoundingClientRect().bottom);
        };

        const bottomLimit = () => {
            let limit = window.innerHeight;
            if (!bar.hidden) limit = Math.min(limit, bar.getBoundingClientRect().top);
            const mobile = root.querySelector("[data-reader-mobile-actions]");
            if (mobile && !root.classList.contains("reader-chrome-hidden")) {
                const rect = mobile.getBoundingClientRect();
                if (rect.height > 0) limit = Math.min(limit, rect.top);
            }
            return limit;
        };

        const inView = element => {
            if (paged()) {
                const viewport = scrollContainerOf(element).getBoundingClientRect();
                return Array.from(element.getClientRects()).some(rect =>
                    rect.width > 0 &&
                    rect.right > viewport.left + 4 &&
                    rect.left < viewport.right - 4);
            }
            const rect = element.getBoundingClientRect();
            return rect.bottom > topLimit() + 4 && rect.top < bottomLimit() - 4;
        };

        // The TTS start point is derived from what is on screen; it does not read or
        // write the stored reading progress.
        const currentParagraph = list => {
            if (paged()) return list.find(inView) || list[0];
            const top = topLimit();
            const line = top + (bottomLimit() - top) * 0.12;
            return list.find(element => element.getBoundingClientRect().bottom > line) || list[0];
        };

        const segmentOf = element => ({
            element,
            start: 0,
            end: null,
            language: languageOf(element)
        });

        const offsetWithin = (element, container, offset) => {
            const range = document.createRange();
            range.selectNodeContents(element);
            range.setEnd(container, offset);
            return range.toString().length;
        };

        const selectionSegments = () => {
            const selection = window.getSelection();
            if (!selection || selection.isCollapsed || !selection.rangeCount) return null;
            const range = selection.getRangeAt(0);
            if (!surface.contains(range.commonAncestorContainer)) return null;

            const segments = [];
            for (const element of paragraphs()) {
                if (!range.intersectsNode(element)) continue;
                const text = element.textContent;
                const start = element.contains(range.startContainer)
                    ? offsetWithin(element, range.startContainer, range.startOffset)
                    : 0;
                const end = element.contains(range.endContainer)
                    ? offsetWithin(element, range.endContainer, range.endOffset)
                    : text.length;
                if (end > start && text.slice(start, end).trim()) {
                    segments.push({ element, start, end, language: languageOf(element) });
                }
            }
            return segments.length ? segments : null;
        };

        const buildPlan = (mode, fromStart = false) => {
            const all = paragraphs();
            if (!all.length) return null;
            const anchor = fromStart ? all[0] : currentParagraph(all);
            const language = languageOf(anchor);
            const sameLanguage = all.filter(element => languageOf(element) === language);
            let elements;
            if (mode === "paragraph") elements = [anchor];
            else if (mode === "page") {
                elements = sameLanguage.filter(inView);
                if (!elements.length) elements = [anchor];
            } else {
                elements = sameLanguage.slice(Math.max(0, sameLanguage.indexOf(anchor)));
            }
            return { mode, segments: elements.map(segmentOf), index: 0, offset: 0 };
        };

        function* itemsFrom(currentPlan, fromIndex, fromOffset) {
            const voiceMap = readVoiceMap();
            for (let index = fromIndex; index < currentPlan.segments.length; index++) {
                const segment = currentPlan.segments[index];
                const text = segment.element.textContent || "";
                const end = Math.min(segment.end ?? text.length, text.length);
                const start = index === fromIndex
                    ? Math.max(segment.start, Math.min(fromOffset, end))
                    : segment.start;
                if (end <= start) continue;
                yield {
                    key: { index, start },
                    text: text.slice(start, end),
                    language: segment.language,
                    voiceId: voiceIdFor(voiceMap, segment.language)
                };
            }
        }

        // ---- Highlight and follow ---------------------------------------------------

        let activeElement = null;
        let lastUserScroll = 0;
        let lastTurn = 0;

        const rangeFor = (element, start, end) => {
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            const range = document.createRange();
            let position = 0;
            let started = false;
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                const length = node.data.length;
                if (!started && start <= position + length) {
                    range.setStart(node, Math.max(0, start - position));
                    started = true;
                }
                if (started && end <= position + length) {
                    range.setEnd(node, Math.max(0, end - position));
                    return range;
                }
                position += length;
            }
            return null;
        };

        const markParagraph = element => {
            if (activeElement === element) return;
            activeElement?.classList.remove("reader-tts-active");
            element?.classList.add("reader-tts-active");
            activeElement = element || null;
        };

        const markWord = range => {
            if (!supportsHighlights) return;
            if (range) CSS.highlights.set(WORD_HIGHLIGHT, new Highlight(range));
            else CSS.highlights.delete(WORD_HIGHLIGHT);
        };

        const clearMarks = () => {
            markParagraph(null);
            markWord(null);
        };

        const follow = (rect, element) => {
            if (!rect || Date.now() - lastUserScroll < USER_SCROLL_GRACE_MS) return;

            if (paged()) {
                const viewport = scrollContainerOf(element).getBoundingClientRect();
                const direction = rect.left >= viewport.right - 2
                    ? 1
                    : rect.right <= viewport.left + 2 ? -1 : 0;
                if (!direction || Date.now() - lastTurn < PAGE_TURN_GAP_MS) return;
                lastTurn = Date.now();
                root.dispatchEvent(new CustomEvent("anilingo:reader-page-edge", {
                    detail: { direction }
                }));
                return;
            }

            const top = topLimit();
            const bottom = bottomLimit();
            if (rect.top >= top + 8 && rect.bottom <= bottom - 8) return;
            window.scrollTo({
                top: Math.max(0, window.scrollY + rect.top - top - (bottom - top) * 0.25),
                behavior: reduceMotion.matches ? "auto" : "smooth"
            });
        };

        const markUserScroll = () => {
            lastUserScroll = Date.now();
        };
        window.addEventListener("wheel", markUserScroll, { passive: true });
        window.addEventListener("touchmove", markUserScroll, { passive: true });
        document.addEventListener("keydown", event => {
            if (["PageDown", "PageUp", "ArrowDown", "ArrowUp", " "].includes(event.key)) {
                markUserScroll();
            }
        });

        // ---- Playback ---------------------------------------------------------------

        let flashTimer = 0;
        let watchdog = 0;
        let pendingSelection = null;
        let mobileButton = null;

        const progressText = () =>
            plan && plan.segments.length > 1
                ? ` · Absatz ${plan.index + 1} von ${plan.segments.length}`
                : "";

        const render = () => {
            const active = state !== "idle";
            root.dataset.readerTts = state;
            toggle.setAttribute("aria-pressed", active ? "true" : "false");
            const label = state === "speaking"
                ? "Vorlesen pausieren"
                : active ? "Vorlesen fortsetzen" : "Vorlesen";
            toggle.setAttribute("aria-label", label);
            toggle.title = label;
            if (mobileButton) {
                mobileButton.textContent = state === "speaking"
                    ? "Pause"
                    : active ? "Weiter" : "Vorlesen";
            }

            window.clearTimeout(flashTimer);
            bar.hidden = !active;
            if (pauseButton) {
                pauseButton.hidden = false;
                pauseButton.textContent = state === "speaking" ? "Pause" : "Weiter";
            }
            if (stopButton) stopButton.textContent = "Stopp";
            if (barStatus) {
                barStatus.textContent = state === "speaking"
                    ? "Liest vor" + progressText()
                    : state === "paused"
                        ? "Pausiert" + progressText()
                        : state === "blocked" ? "Zum Vorlesen auf Weiter tippen" : "";
            }
        };

        const setState = next => {
            state = next;
            render();
        };

        const flash = message => {
            setState("idle");
            if (barStatus) barStatus.textContent = message;
            if (pauseButton) pauseButton.hidden = true;
            if (stopButton) stopButton.textContent = "Schließen";
            bar.hidden = false;
            flashTimer = window.setTimeout(() => {
                if (state === "idle") bar.hidden = true;
            }, 4500);
        };

        const stopPlayback = () => {
            run++;
            window.clearTimeout(watchdog);
            window.clearTimeout(restartTimer);
            provider.stop();
            plan = null;
            clearMarks();
            setState("idle");
        };

        const nextChapterLink = () => root.querySelector("[data-reader-next-chapter]");

        const finished = currentPlan => {
            if (currentPlan.mode !== "continue") {
                stopPlayback();
                return;
            }

            const next = nextChapterLink();
            if (next?.href && preferences().autoContinueChapters) {
                try {
                    // Only the target chapter id is kept; never any text.
                    window.sessionStorage.setItem(CONTINUE_KEY, JSON.stringify({
                        chapterId: next.dataset.readerNextChapter,
                        expires: Date.now() + CONTINUE_TTL_MS
                    }));
                } catch {
                }
                stopPlayback();
                window.location.assign(next.href);
                return;
            }

            stopPlayback();
            flash(next
                ? "Kapitelende – automatisches Weiterlesen ist ausgeschaltet."
                : "Ende erreicht.");
        };

        const failed = error => {
            const message = String(error?.message || "");
            run++;
            window.clearTimeout(watchdog);
            provider.stop();
            if (message.includes("not-allowed") && plan) {
                setState("blocked");
                return;
            }
            plan = null;
            clearMarks();
            flash("Vorlesen wurde vom Gerät abgebrochen.");
        };

        play = (nextPlan, index, offset, { guarded = false } = {}) => {
            const id = ++run;
            plan = nextPlan;
            plan.index = index;
            plan.offset = offset;
            plan.started = false;
            window.clearTimeout(watchdog);
            setState("speaking");

            if (guarded) {
                // Without a user gesture some browsers silently refuse to speak.
                watchdog = window.setTimeout(() => {
                    if (id !== run || plan?.started) return;
                    run++;
                    provider.stop();
                    setState("blocked");
                }, AUTOSTART_WATCHDOG_MS);
            }

            const prefs = preferences();
            provider.speakSequence(itemsFrom(plan, index, offset), {
                lookAhead: LOOK_AHEAD,
                maxChunkLength: MAX_CHUNK_LENGTH,
                rate: prefs.rate,
                pitch: prefs.pitch,
                volume: prefs.volume
            }).then(result => {
                if (id !== run) return;
                if (result?.completed) finished(nextPlan);
            }, error => {
                if (id === run) failed(error);
            });
        };

        const pause = () => {
            if (state !== "speaking" || !plan) return;
            // Cancel and resume from the last spoken word: Web Speech pause() is not
            // reliable across mobile browsers.
            run++;
            window.clearTimeout(restartTimer);
            provider.stop();
            setState("paused");
        };

        const resume = () => {
            if (!plan) {
                stopPlayback();
                return;
            }
            play(plan, plan.index, plan.offset);
        };

        const start = (mode, selection) => {
            const nextPlan = selection?.length
                ? { mode: "selection", segments: selection, index: 0, offset: 0 }
                : buildPlan(mode);
            if (!nextPlan || !nextPlan.segments.length) {
                flash("Kein Text zum Vorlesen gefunden.");
                return;
            }
            play(nextPlan, 0, nextPlan.segments[0].start);
        };

        const primary = selection => {
            if (state === "speaking") pause();
            else if (state === "paused" || state === "blocked") resume();
            else start("continue", selection);
        };

        // Mobile taps can collapse a selection before `click`; capture it first.
        const captureSelection = () => {
            pendingSelection = selectionSegments();
        };
        const takeSelection = () => {
            const selection = pendingSelection || selectionSegments();
            pendingSelection = null;
            return selection;
        };

        provider.addEventListener("itemstart", event => {
            const key = event.detail?.item?.key;
            const segment = key && plan?.segments[key.index];
            if (!segment) return;
            window.clearTimeout(watchdog);
            plan.started = true;
            plan.index = key.index;
            plan.offset = key.start;
            markParagraph(segment.element);
            markWord(null);
            const range = rangeFor(segment.element, key.start, key.start + 1);
            follow(range?.getBoundingClientRect() || segment.element.getBoundingClientRect(),
                segment.element);
            render();
        });

        provider.addEventListener("boundary", event => {
            const detail = event.detail || {};
            const key = detail.item?.key;
            const segment = key && plan?.segments[key.index];
            if (!segment || (detail.name && detail.name !== "word")) return;
            const offset = key.start + Number(detail.charIndex || 0);
            plan.index = key.index;
            plan.offset = offset;
            const text = segment.element.textContent || "";
            const length = Number(detail.charLength) ||
                (/^[\p{L}\p{N}\p{M}'’-]+/u.exec(text.slice(offset))?.[0].length ?? 1);
            const range = rangeFor(segment.element, offset, offset + length);
            markWord(range);
            follow(range?.getBoundingClientRect(), segment.element);
        });

        toggle.hidden = false;
        toggle.addEventListener("pointerdown", captureSelection);
        toggle.addEventListener("click", () => primary(takeSelection()));

        mobileButton = api.addMobileAction("Vorlesen", () => primary(takeSelection()));
        mobileButton?.addEventListener("pointerdown", captureSelection);

        api.addOverflowAction("Absatz vorlesen", () => {
            stopPlayback();
            start("paragraph");
        });
        api.addOverflowAction("Seite vorlesen", () => {
            stopPlayback();
            start("page");
        });

        pauseButton?.addEventListener("click", () => {
            if (state === "speaking") pause();
            else resume();
        });
        stopButton?.addEventListener("click", stopPlayback);

        // Switching the visible language changes which paragraphs exist on screen.
        new MutationObserver(() => {
            if (state !== "idle") stopPlayback();
        }).observe(root, { attributes: true, attributeFilter: ["data-view"] });

        window.addEventListener("pagehide", () => provider.stop());

        render();

        // ---- Explicit chapter auto-continue -----------------------------------------

        let marker = null;
        try {
            marker = JSON.parse(window.sessionStorage.getItem(CONTINUE_KEY) || "null");
            window.sessionStorage.removeItem(CONTINUE_KEY);
        } catch {
            marker = null;
        }

        if (marker &&
            marker.chapterId === root.dataset.chapterId &&
            Number(marker.expires) > Date.now()) {
            const startedAt = Date.now();
            const startWhenReady = () => {
                if (root.dataset.readerReady !== "true" && Date.now() - startedAt < 5000) {
                    window.setTimeout(startWhenReady, 150);
                    return;
                }
                const nextPlan = buildPlan("continue", true);
                if (nextPlan?.segments.length) play(nextPlan, 0, 0, { guarded: true });
            };
            startWhenReady();
        }
    };

    window.AniLingoReaderTts = Object.freeze({ mount });

    // reader-shell.js may have initialised before this script loaded.
    document.querySelectorAll("[data-unified-reader]").forEach(root => {
        if (root.readerShell) mount(root.readerShell);
    });
})();
