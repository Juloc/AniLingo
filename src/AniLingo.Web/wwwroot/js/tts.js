(() => {
    "use strict";

    const synth = typeof window !== "undefined" ? window.speechSynthesis : null;
    const Utterance = typeof window !== "undefined" ? window.SpeechSynthesisUtterance : null;

    const clamp = (value, min, max, fallback) => {
        const number = Number(value);
        return Number.isFinite(number) ? Math.min(max, Math.max(min, number)) : fallback;
    };

    const normalizeLanguage = (value) => {
        const raw = String(value || "").trim().replaceAll("_", "-");
        if (!raw) return "und";

        const parts = raw.split("-").filter(Boolean);
        if (!parts.length || parts.some((part) => !/^[A-Za-z0-9]{1,8}$/.test(part))) {
            return "und";
        }

        return parts.map((part, index) => {
            if (index === 0) return part.toLowerCase();
            if (/^[A-Za-z]{4}$/.test(part)) {
                return part[0].toUpperCase() + part.slice(1).toLowerCase();
            }
            if (/^[A-Za-z]{2}$/.test(part) || /^\d{3}$/.test(part)) {
                return part.toUpperCase();
            }
            return part.toLowerCase();
        }).join("-");
    };

    const baseLanguage = (value) => normalizeLanguage(value).split("-")[0];

    const languageRank = (voiceLanguage, requestedLanguage) => {
        const voice = normalizeLanguage(voiceLanguage);
        const requested = normalizeLanguage(requestedLanguage);
        if (requested === "und") return 0;
        if (voice.toLowerCase() === requested.toLowerCase()) return 0;
        return baseLanguage(voice).toLowerCase() === baseLanguage(requested).toLowerCase() ? 1 : 2;
    };

    const PROVIDER_RANK = { "offline-neural": 0, device: 1, cloud: 2 };

    const compareText = (left, right) => {
        const a = String(left || "").toLowerCase();
        const b = String(right || "").toLowerCase();
        return a < b ? -1 : a > b ? 1 : 0;
    };

    const compareOrdinal = (left, right) => {
        const a = String(left || "");
        const b = String(right || "");
        return a < b ? -1 : a > b ? 1 : 0;
    };

    const bestVoice = (voices, language, rank) => voices
        .filter((voice) => languageRank(voice.language, language) === rank)
        .sort((left, right) =>
            Number(Boolean(right.isDefault)) - Number(Boolean(left.isDefault)) ||
            compareText(left.name, right.name) ||
            compareOrdinal(left.voiceId, right.voiceId))[0] || null;

    const resolveWithinProvider = (provider, allVoices, requestedVoice, language) => {
        const providerId = String(provider.id).toLowerCase();
        const voices = allVoices.filter((voice) =>
            String(voice.providerId || "").toLowerCase() === providerId);
        const build = (voice, reason) => ({
            providerId: provider.id,
            voiceId: voice ? voice.voiceId : null,
            voice,
            language,
            usesProviderDefaultVoice: !voice,
            reason
        });

        if (requestedVoice) {
            const selected = voices.find((voice) =>
                String(voice.voiceId).toLowerCase() === requestedVoice.toLowerCase());
            if (selected && languageRank(selected.language, language) <= 1) {
                return build(selected, "selected-voice");
            }
        }

        const exact = bestVoice(voices, language, 0);
        if (exact) return build(exact, "exact-language");

        const base = bestVoice(voices, language, 1);
        if (base) return build(base, "base-language");

        return provider.capabilities?.platformDefaultVoice
            ? build(null, "platform-default")
            : null;
    };

    /**
     * Browser mirror of Features/Speech SpeechPreferenceResolver and
     * SpeechAvailabilityResolver: same provider order, voice matching and
     * unavailable reasons. Device voices only exist on the client, so the Reader
     * resolves them here instead of asking the server.
     */
    const resolveSpeech = (preferences = {}, providers = [], voices = []) => {
        const language = normalizeLanguage(preferences.language);
        const requestedVoice = String(preferences.voiceId || "").trim();
        const requestedProvider = String(preferences.providerId || "").trim().toLowerCase();
        const explicitProvider = requestedProvider === "auto" ? "" : requestedProvider;
        const available = providers
            .filter((provider) => provider && provider.isAvailable)
            .sort((left, right) =>
                (PROVIDER_RANK[left.kind] ?? 99) - (PROVIDER_RANK[right.kind] ?? 99) ||
                compareOrdinal(left.id, right.id));

        if (!available.length) {
            return {
                resolution: null,
                unavailableReason: providers.length ? "provider-unavailable" : "no-provider"
            };
        }

        if (explicitProvider) {
            const provider = available.find((item) =>
                String(item.id).toLowerCase() === explicitProvider);
            const resolution = provider &&
                resolveWithinProvider(provider, voices, requestedVoice, language);
            if (resolution) {
                return {
                    resolution: { ...resolution, reason: "selected-provider" },
                    unavailableReason: null
                };
            }
        }

        for (const provider of available) {
            const resolution = resolveWithinProvider(
                provider,
                voices,
                String(provider.id).toLowerCase() === explicitProvider ? requestedVoice : "",
                language);
            if (resolution) {
                return {
                    resolution: { ...resolution, reason: `${provider.kind}-fallback` },
                    unavailableReason: null
                };
            }
        }

        return { resolution: null, unavailableReason: "no-voice-for-language" };
    };

    const findBreak = (text, start, hardEnd) => {
        const windowText = text.slice(start, hardEnd);
        let best = -1;

        const paragraph = windowText.lastIndexOf("\n\n");
        if (paragraph >= Math.floor(windowText.length * 0.35)) {
            best = paragraph + 2;
        }

        if (best < 0) {
            const sentenceMatches = [...windowText.matchAll(/[.!?。！？]+(?:["'”’」』】）)]*)\s+/gu)];
            const candidate = sentenceMatches.at(-1);
            if (candidate && candidate.index >= Math.floor(windowText.length * 0.35)) {
                best = candidate.index + candidate[0].length;
            }
        }

        if (best < 0) {
            const newline = windowText.lastIndexOf("\n");
            if (newline >= Math.floor(windowText.length * 0.35)) {
                best = newline + 1;
            }
        }

        if (best < 0) {
            const whitespace = Math.max(
                windowText.lastIndexOf(" "),
                windowText.lastIndexOf("\t")
            );
            if (whitespace >= Math.floor(windowText.length * 0.35)) {
                best = whitespace + 1;
            }
        }

        return best > 0 ? start + best : hardEnd;
    };

    const splitText = (value, maxLength = 900) => {
        const text = String(value || "");
        const limit = Math.max(120, Math.min(4000, Math.trunc(maxLength) || 900));
        const chunks = [];
        let cursor = 0;

        while (cursor < text.length) {
            while (cursor < text.length && /\s/u.test(text[cursor])) cursor++;
            if (cursor >= text.length) break;

            const hardEnd = Math.min(text.length, cursor + limit);
            let end = hardEnd < text.length ? findBreak(text, cursor, hardEnd) : text.length;

            while (end > cursor && /\s/u.test(text[end - 1])) end--;
            if (end <= cursor) {
                end = Math.min(text.length, cursor + limit);
            }

            const chunkText = text.slice(cursor, end);
            if (chunkText) {
                chunks.push({ text: chunkText, start: cursor, end });
            }

            cursor = Math.max(end, cursor + 1);
        }

        return chunks;
    };

    class DeviceSpeechProvider extends EventTarget {
        constructor() {
            super();
            this.id = "device";
            this.kind = "device";
            this._session = 0;
            this._state = "idle";
            this._settleSession = null;
        }

        get supported() {
            return Boolean(synth && Utterance);
        }

        get state() {
            return this._state;
        }

        get capabilities() {
            return {
                pauseResume: this.supported &&
                    typeof synth.pause === "function" &&
                    typeof synth.resume === "function",
                boundaryEvents: this.supported,
                platformDefaultVoice: true,
                guaranteedOffline: false
            };
        }

        async getVoices({ timeoutMs = 1500 } = {}) {
            if (!this.supported) return [];

            const current = synth.getVoices();
            if (current.length) return this._mapVoices(current);

            const delay = Math.max(0, Math.min(5000, Number(timeoutMs) || 1500));
            await new Promise((resolve) => {
                let settled = false;
                let timer = null;

                const finish = () => {
                    if (settled) return;
                    settled = true;
                    synth.removeEventListener?.("voiceschanged", finish);
                    if (timer !== null) window.clearTimeout(timer);
                    resolve();
                };

                synth.addEventListener?.("voiceschanged", finish, { once: true });
                timer = window.setTimeout(finish, delay);
            });

            return this._mapVoices(synth.getVoices());
        }

        get descriptor() {
            return {
                id: this.id,
                kind: this.kind,
                isAvailable: this.supported,
                capabilities: this.capabilities
            };
        }

        async speak(options = {}) {
            const text = String(options.text || "").trim();
            if (!text) {
                this.stop();
                return { completed: true };
            }

            return this.speakSequence(
                [{ text, language: options.language, voiceId: options.voiceId }],
                { ...options, lookAhead: 0 });
        }

        /**
         * Speaks independent items (for example Reader paragraphs) as one cancellable
         * session. `items` may be an array or any iterable and is pulled lazily: only
         * the utterance being spoken plus `lookAhead` upcoming utterances ever exist in
         * the platform queue. `itemstart` / `itemend` / `boundary` events carry the
         * caller's item `key`; boundary offsets are relative to that item's text.
         * Resolves `{ completed: true }` after the last item and `{ completed: false }`
         * when Stop or a newer request cancelled the session.
         */
        async speakSequence(items, options = {}) {
            if (!this.supported) {
                throw new Error("Device speech synthesis is not available in this browser.");
            }

            this.stop();
            const session = ++this._session;
            const iterator = (items && typeof items[Symbol.iterator] === "function"
                ? items
                : [])[Symbol.iterator]();
            const lookAhead = Math.trunc(clamp(options.lookAhead, 0, 8, 2));
            const rate = clamp(options.rate, 0.5, 2.5, 1);
            const pitch = clamp(options.pitch, 0.5, 2, 1);
            const volume = clamp(options.volume, 0, 1, 1);
            const voices = await this.getVoices();
            if (session !== this._session) return { completed: false };

            const voiceCache = new Map();
            const voiceFor = (language, voiceId) => {
                const cacheKey = language + "|" + voiceId;
                if (!voiceCache.has(cacheKey)) {
                    voiceCache.set(cacheKey, this._selectVoice(voices, language, voiceId));
                }
                return voiceCache.get(cacheKey);
            };

            const pending = [];
            let itemIndex = 0;
            let exhausted = false;
            const pull = () => {
                while (!pending.length && !exhausted) {
                    const next = iterator.next();
                    if (next.done) {
                        exhausted = true;
                        break;
                    }

                    const value = next.value || {};
                    const text = String(value.text || "");
                    if (!text.trim()) continue;
                    const language = normalizeLanguage(value.language || options.language);
                    const voice = voiceFor(language, value.voiceId || options.voiceId || "");
                    const chunks = splitText(text, options.maxChunkLength);
                    const index = itemIndex++;
                    chunks.forEach((chunk, chunkIndex) => pending.push({
                        chunk,
                        language,
                        voice,
                        item: {
                            key: value.key ?? index,
                            index,
                            chunkIndex,
                            chunkCount: chunks.length,
                            textLength: text.length
                        }
                    }));
                }
                return pending.shift() || null;
            };

            this._setState("speaking", { session });

            let ordinal = 0;
            let inFlight = 0;
            let failure = null;
            await new Promise((resolve) => {
                // stop() settles the session even when the platform never reports the
                // cancelled utterances.
                this._settleSession = resolve;
                const pump = () => {
                    if (session !== this._session || failure) {
                        resolve();
                        return;
                    }

                    while (inFlight <= lookAhead) {
                        const job = pull();
                        if (!job) break;
                        inFlight++;
                        this._speakChunk({
                            chunk: job.chunk,
                            index: ordinal++,
                            count: null,
                            session,
                            language: job.language,
                            voice: job.voice,
                            rate,
                            pitch,
                            volume,
                            item: job.item
                        }).then(() => {
                            inFlight--;
                            pump();
                        }, (error) => {
                            failure = error;
                            resolve();
                        });
                    }

                    if (inFlight === 0 && exhausted && !pending.length) resolve();
                };

                pump();
            });

            if (session === this._session) this._settleSession = null;

            if (failure) {
                if (session === this._session) {
                    this._session++;
                    synth.cancel();
                    this._setState("idle", { session });
                }
                throw failure;
            }

            if (session !== this._session) return { completed: false };

            this._setState("idle", { session });
            this.dispatchEvent(new CustomEvent("complete", {
                detail: { session, itemCount: itemIndex }
            }));
            return { completed: true };
        }

        pause() {
            if (!this.supported || this._state !== "speaking") return false;
            synth.pause();
            this._setState("paused", { session: this._session });
            return true;
        }

        resume() {
            if (!this.supported || this._state !== "paused") return false;
            synth.resume();
            this._setState("speaking", { session: this._session });
            return true;
        }

        stop() {
            this._session++;
            if (this.supported) {
                synth.cancel();
            }
            const settle = this._settleSession;
            this._settleSession = null;
            settle?.();
            this._setState("idle", { session: this._session });
        }

        _mapVoices(items) {
            return items
                .map((voice) => ({
                    providerId: this.id,
                    voiceId: voice.voiceURI || voice.name,
                    name: voice.name,
                    language: normalizeLanguage(voice.lang),
                    isDefault: Boolean(voice.default),
                    isLocal: Boolean(voice.localService),
                    native: voice
                }))
                .sort((left, right) =>
                    left.language.localeCompare(right.language) ||
                    Number(right.isDefault) - Number(left.isDefault) ||
                    left.name.localeCompare(right.name) ||
                    left.voiceId.localeCompare(right.voiceId));
        }

        _selectVoice(voices, language, voiceId) {
            return resolveSpeech(
                { providerId: this.id, voiceId, language },
                [this.descriptor],
                voices).resolution?.voice || null;
        }

        _speakChunk({ chunk, index, count, session, language, voice, rate, pitch, volume, item = null }) {
            return new Promise((resolve, reject) => {
                if (session !== this._session) {
                    resolve();
                    return;
                }

                const utterance = new Utterance(chunk.text);
                utterance.lang = language === "und" ? "" : language;
                utterance.rate = rate;
                utterance.pitch = pitch;
                utterance.volume = volume;
                if (voice?.native) utterance.voice = voice.native;

                utterance.onstart = () => {
                    if (session !== this._session) return;
                    if (item && item.chunkIndex === 0) {
                        this.dispatchEvent(new CustomEvent("itemstart", {
                            detail: { session, item, voice: voice ? { voiceId: voice.voiceId, name: voice.name, language: voice.language } : null }
                        }));
                    }
                    this.dispatchEvent(new CustomEvent("utterancestart", {
                        detail: { session, index, count, start: chunk.start, end: chunk.end, item }
                    }));
                };

                utterance.onboundary = (event) => {
                    if (session !== this._session) return;
                    const localIndex = Number.isFinite(event.charIndex) ? event.charIndex : 0;
                    this.dispatchEvent(new CustomEvent("boundary", {
                        detail: {
                            session,
                            index,
                            count,
                            item,
                            name: event.name || null,
                            charIndex: chunk.start + localIndex,
                            charLength: Number.isFinite(event.charLength) ? event.charLength : 0
                        }
                    }));
                };

                utterance.onend = () => {
                    this.dispatchEvent(new CustomEvent("utteranceend", {
                        detail: { session, index, count, start: chunk.start, end: chunk.end, item }
                    }));
                    if (item && session === this._session && item.chunkIndex === item.chunkCount - 1) {
                        this.dispatchEvent(new CustomEvent("itemend", {
                            detail: { session, item }
                        }));
                    }
                    resolve();
                };

                utterance.onerror = (event) => {
                    if (session !== this._session || event.error === "canceled" || event.error === "interrupted") {
                        resolve();
                        return;
                    }

                    const error = new Error(`Device speech synthesis failed: ${event.error || "unknown"}`);
                    this.dispatchEvent(new CustomEvent("error", {
                        detail: { session, index, error: event.error || "unknown" }
                    }));
                    reject(error);
                };

                synth.speak(utterance);
            });
        }

        _setState(state, detail) {
            if (this._state === state && state === "idle") return;
            this._state = state;
            this.dispatchEvent(new CustomEvent("statechange", {
                detail: { state, ...detail }
            }));
        }
    }

    window.AniLingoTts = Object.freeze({
        DeviceSpeechProvider,
        createDeviceProvider: () => new DeviceSpeechProvider(),
        resolveSpeech,
        splitText,
        normalizeLanguage,
        baseLanguage
    });
})();
