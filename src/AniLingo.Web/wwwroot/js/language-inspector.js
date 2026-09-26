/*
 * Shared language inspector (issue #233).
 *
 * window.AniLingoLanguageInspector exposes one contract for the Anime player,
 * readers and Learning modules:
 *
 *   available                -> true when the page rendered _LanguageInspector
 *   open(text, context?)     -> Promise<inspection|null>; shows reading, meaning,
 *                               optional explanation and, with Study, Save/Learn/
 *                               Known/Ignore for the text
 *   close()                  -> hides the inspector and restores focus
 *   isOpen()                 -> boolean
 *   bindSelection(surface, { paragraphAttribute, context }) -> offers "Look up"
 *                               for text selected inside surface
 *   speak(text, language, rate)
 *   addEventListener(type, handler) / removeEventListener(type, handler)
 *     "open"        detail: { text, context }
 *     "close"       detail: {}
 *     "statechange" detail: { text, language, state }
 *
 * context: { language, sourceType: "anime"|"novel"|"book"|"manga", contentKey,
 *            sentence, cueStartMs, paragraph, page, region }
 * Missing fields fall back to the page context rendered by the partial.
 * The server resolves capabilities for the source; this script never decides
 * what a profile may do.
 */
(() => {
    "use strict";

    if (window.AniLingoLanguageInspector) {
        return;
    }

    const events = new EventTarget();
    let speechProvider = null;

    const speak = (text, language, rate) => {
        if (!text || !speechSupported()) return false;
        void speechProvider.speak({
            text,
            language: language || "und",
            rate: Number.isFinite(Number(rate)) ? Number(rate) : 1
        }).catch(() => {});
        return true;
    };

    const speechSupported = () => {
        if (!window.AniLingoTts) return false;
        speechProvider ??= window.AniLingoTts.createDeviceProvider();
        return !!speechProvider.supported;
    };

    const bindSpeakButtons = () => {
        const supported = speechSupported();
        document.querySelectorAll("[data-language-speak]").forEach((button) => {
            if (button.dataset.languageSpeakBound) return;
            button.dataset.languageSpeakBound = "true";
            button.disabled = !supported;
            button.addEventListener("click", () => {
                speak(
                    button.getAttribute("data-language-speak") || "",
                    button.getAttribute("data-speak-language") || "",
                    button.getAttribute("data-speak-rate") || "1");
            });
        });
    };

    const configNode = document.querySelector("script[data-language-inspector-config]");
    const panel = document.querySelector("[data-language-inspector]");
    let config = null;
    try {
        config = configNode ? JSON.parse(configNode.textContent || "null") : null;
    } catch {
        config = null;
    }

    const available = !!(config && panel);
    const text = (key) => (config?.text || {})["languageInspector." + key] || "";
    const pick = (selector) => panel?.querySelector(selector) || null;

    const nodes = available ? {
        kicker: pick("[data-li-kicker]"),
        word: pick("[data-li-word]"),
        reading: pick("[data-li-reading]"),
        meaning: pick("[data-li-meaning]"),
        tokens: pick("[data-li-tokens]"),
        speak: pick("[data-li-speak]"),
        state: pick("[data-li-state]"),
        actions: pick("[data-li-actions]"),
        explanationBlock: pick("[data-li-explanation-block]"),
        explain: pick("[data-li-explain]"),
        explanation: pick("[data-li-explanation]"),
        translation: pick("[data-li-translation]"),
        grammarBlock: pick("[data-li-grammar-block]"),
        grammar: pick("[data-li-grammar]"),
        colloquialBlock: pick("[data-li-colloquial-block]"),
        colloquial: pick("[data-li-colloquial]"),
        status: pick("[data-li-status]"),
        close: pick("[data-li-close]"),
        lookup: document.querySelector("[data-li-lookup]")
    } : null;

    let current = null;
    let requestId = 0;
    let returnFocus = null;

    const toInt = (value) => {
        if (value === undefined || value === null || value === "") return null;
        const number = Number(value);
        return Number.isInteger(number) ? number : null;
    };

    const normalizeContext = (context) => {
        const base = config?.context || {};
        const merged = { ...base, ...(context || {}) };
        return {
            language: merged.language || document.documentElement.lang || "",
            sourceType: merged.sourceType || null,
            contentKey: merged.contentKey || null,
            sentence: merged.sentence ? String(merged.sentence).slice(0, 500) : null,
            cueStartMs: toInt(merged.cueStartMs),
            paragraph: toInt(merged.paragraph),
            page: toInt(merged.page),
            region: toInt(merged.region)
        };
    };

    const post = async (path, body) => {
        const response = await fetch(config.endpoint + path, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "RequestVerificationToken": config.requestToken || ""
            },
            body: JSON.stringify(body)
        });

        let payload = null;
        try {
            payload = await response.json();
        } catch {
            payload = null;
        }

        if (!response.ok) {
            const error = new Error(payload?.error || text("error"));
            error.status = response.status;
            throw error;
        }

        return payload;
    };

    const setStatus = (message) => {
        if (nodes) nodes.status.textContent = message || "";
    };

    const stateLabel = (state) => text("state." + (state || "untracked"));

    const renderExplanation = (explanation) => {
        if (!explanation) {
            nodes.explanation.hidden = true;
            return;
        }

        nodes.translation.textContent = explanation.translation || "";
        const fill = (list, block, items) => {
            list.replaceChildren(...(items || []).map((item) => {
                const li = document.createElement("li");
                li.textContent = item;
                return li;
            }));
            block.hidden = !items || items.length === 0;
        };
        fill(nodes.grammar, nodes.grammarBlock, explanation.grammar);
        fill(nodes.colloquial, nodes.colloquialBlock, explanation.colloquial);
        nodes.explanation.hidden = false;
        nodes.explain.hidden = true;
    };

    const renderToken = (token) => {
        const inspection = current.inspection;
        const features = inspection.features;
        current.token = token;

        nodes.word.textContent = token ? (token.canonical || token.surface) : inspection.text;
        nodes.word.lang = inspection.language;

        const reading = token?.reading || "";
        nodes.reading.textContent = reading;
        nodes.reading.lang = inspection.language;
        nodes.reading.hidden = !reading;

        if (token && features.lookup) {
            nodes.meaning.textContent = token.meaning || text("noMeaning");
            nodes.meaning.lang = token.meaningLanguage || "";
        } else {
            nodes.meaning.textContent = "";
        }

        nodes.speak.hidden = !speechSupported();

        const canAct = !!token && features.save;
        nodes.actions.hidden = !canAct;
        nodes.state.hidden = !canAct;
        if (canAct) {
            nodes.state.textContent = stateLabel(token.state);
            nodes.actions.querySelectorAll("[data-li-action]").forEach((button) => {
                const action = button.getAttribute("data-li-action");
                button.hidden = action === "learning" && !features.learn;
                button.disabled = false;
                button.classList.toggle("selected", token.state === action);
                button.setAttribute("aria-pressed", token.state === action ? "true" : "false");
            });
        }

        nodes.tokens.querySelectorAll("button").forEach((button) => {
            const selected = button.__token === token;
            button.classList.toggle("selected", selected);
            button.setAttribute("aria-pressed", selected ? "true" : "false");
        });
    };

    const renderInspection = (inspection) => {
        const interactive = inspection.tokens.filter((token) => token.interactive);
        nodes.kicker.textContent = interactive.length > 1 ? text("selection") : text("word");

        nodes.tokens.replaceChildren();
        if (interactive.length > 1) {
            interactive.forEach((token) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "chip";
                button.lang = inspection.language;
                button.textContent = token.surface;
                button.__token = token;
                button.addEventListener("click", () => renderToken(token));
                nodes.tokens.appendChild(button);
            });
        }
        nodes.tokens.hidden = interactive.length <= 1;

        nodes.explanationBlock.hidden = !inspection.features.explanation;
        nodes.explain.hidden = false;
        nodes.explain.disabled = false;
        renderExplanation(inspection.explanation);

        renderToken(interactive[0] || null);
    };

    const showPanel = () => {
        if (panel.hidden) {
            returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        }
        panel.hidden = false;
        hideLookup();
    };

    const open = async (value, context) => {
        const selected = String(value || "").trim().slice(0, 500);
        if (!available || !selected) return null;

        const normalized = normalizeContext(context);
        const id = ++requestId;
        current = { text: selected, context: normalized, inspection: null, token: null };

        showPanel();
        nodes.kicker.textContent = text("word");
        nodes.word.textContent = selected;
        nodes.reading.hidden = true;
        nodes.meaning.textContent = "";
        nodes.tokens.hidden = true;
        nodes.actions.hidden = true;
        nodes.state.hidden = true;
        nodes.speak.hidden = true;
        nodes.explanationBlock.hidden = true;
        setStatus(text("loading"));
        nodes.close.focus();
        events.dispatchEvent(new CustomEvent("open", { detail: { text: selected, context: normalized } }));

        try {
            const inspection = await post("/inspect", { text: selected, context: normalized });
            if (id !== requestId) return null;
            current.inspection = inspection;
            setStatus("");
            renderInspection(inspection);
            return inspection;
        } catch (error) {
            if (id === requestId) setStatus(error.message || text("error"));
            return null;
        }
    };

    const close = () => {
        if (!available || panel.hidden) return;
        requestId++;
        panel.hidden = true;
        current = null;
        setStatus("");
        if (returnFocus && document.contains(returnFocus)) {
            returnFocus.focus({ preventScroll: true });
        }
        returnFocus = null;
        events.dispatchEvent(new CustomEvent("close", { detail: {} }));
    };

    const setState = async (state) => {
        const token = current?.token;
        if (!token) return;
        const context = current.context;
        const buttons = [...nodes.actions.querySelectorAll("[data-li-action]")];
        buttons.forEach((button) => { button.disabled = true; });
        setStatus("");

        try {
            const result = await post("/state", {
                text: token.canonical || token.surface,
                state,
                context
            });
            current.inspection.tokens
                .filter((item) => (item.canonical || item.surface) === (token.canonical || token.surface))
                .forEach((item) => { item.state = result.state; });
            renderToken(token);
            events.dispatchEvent(new CustomEvent("statechange", {
                detail: { text: result.text, language: result.language, state: result.state }
            }));
        } catch (error) {
            setStatus(error.message || text("actionFailed"));
            buttons.forEach((button) => { button.disabled = false; });
        }
    };

    const explain = async () => {
        if (!current?.inspection) return;
        const sentence = current.context.sentence || current.inspection.sentence || current.inspection.text;
        nodes.explain.disabled = true;
        setStatus(text("explaining"));
        try {
            const explanation = await post("/explain", { sentence, context: current.context });
            setStatus("");
            renderExplanation(explanation);
        } catch (error) {
            setStatus(error.message || text("error"));
            nodes.explain.disabled = false;
        }
    };

    // Selection "Look up" for readers.
    let lookupState = null;

    function hideLookup() {
        lookupState = null;
        if (nodes?.lookup) nodes.lookup.hidden = true;
    }

    const sentenceAround = (paragraphText, start, end) => {
        const terminators = /[。！？!?.\n]/;
        let from = start;
        while (from > 0 && !terminators.test(paragraphText[from - 1])) from--;
        let to = end;
        while (to < paragraphText.length && !terminators.test(paragraphText[to])) to++;
        if (to < paragraphText.length) to++;
        return paragraphText.slice(from, to).trim().slice(0, 500);
    };

    const offsetWithin = (element, node, offset) => {
        const range = document.createRange();
        range.selectNodeContents(element);
        range.setEnd(node, offset);
        return range.toString().length;
    };

    const bindSelection = (surface, options = {}) => {
        if (!available || !nodes.lookup) return;
        const surfaces = typeof surface === "string"
            ? () => [...document.querySelectorAll(surface)]
            : () => [surface].filter(Boolean);
        const paragraphAttribute = options.paragraphAttribute || null;

        const inspectSelection = () => {
            const selection = window.getSelection();
            if (!selection || selection.isCollapsed || selection.rangeCount === 0) {
                hideLookup();
                return;
            }

            const range = selection.getRangeAt(0);
            const host = surfaces().find((element) => element.contains(range.commonAncestorContainer));
            const selected = selection.toString().trim();
            if (!host || !selected || selected.length > 500) {
                hideLookup();
                return;
            }

            const context = { ...(options.context || {}) };
            const paragraph = paragraphAttribute
                ? (range.startContainer.nodeType === Node.ELEMENT_NODE
                    ? range.startContainer
                    : range.startContainer.parentElement)?.closest(`[${paragraphAttribute}]`)
                : null;
            if (paragraph && host.contains(paragraph)) {
                context.paragraph = toInt(paragraph.getAttribute(paragraphAttribute));
                const start = offsetWithin(paragraph, range.startContainer, range.startOffset);
                const end = paragraph.contains(range.endContainer)
                    ? offsetWithin(paragraph, range.endContainer, range.endOffset)
                    : start + selected.length;
                context.sentence = sentenceAround(paragraph.textContent || "", start, end);
            }

            lookupState = { text: selected, context };
            const rect = range.getBoundingClientRect();
            const button = nodes.lookup;
            button.hidden = false;
            const width = button.offsetWidth || 96;
            const height = button.offsetHeight || 36;
            const left = Math.max(8, Math.min(window.innerWidth - width - 8, rect.left + rect.width / 2 - width / 2));
            const below = rect.bottom + 10;
            const top = below + height > window.innerHeight - 8 ? Math.max(8, rect.top - height - 54) : below;
            button.style.left = left + "px";
            button.style.top = top + "px";
        };

        let timer = 0;
        document.addEventListener("selectionchange", () => {
            window.clearTimeout(timer);
            timer = window.setTimeout(inspectSelection, 60);
        });
        window.addEventListener("scroll", hideLookup, { passive: true });
    };

    if (available) {
        nodes.close.addEventListener("click", close);
        nodes.speak.addEventListener("click", () => {
            const token = current?.token;
            speak(token ? (token.canonical || token.surface) : current?.text, current?.inspection?.language, 0.9);
        });
        nodes.explain.addEventListener("click", explain);
        nodes.actions.querySelectorAll("[data-li-action]").forEach((button) => {
            button.addEventListener("click", () => setState(button.getAttribute("data-li-action")));
        });
        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape" && !panel.hidden) {
                event.preventDefault();
                close();
            }
        });

        if (nodes.lookup) {
            // Keep the text selection while the button is pressed.
            nodes.lookup.addEventListener("pointerdown", (event) => event.preventDefault());
            nodes.lookup.addEventListener("click", () => {
                const state = lookupState;
                hideLookup();
                if (state) void open(state.text, state.context);
            });
        }

        document.addEventListener("click", (event) => {
            const trigger = event.target instanceof Element
                ? event.target.closest("[data-language-inspect]")
                : null;
            if (!trigger) return;
            const host = trigger.closest("[data-language-context]");
            const data = host ? host.dataset : {};
            void open(trigger.getAttribute("data-language-inspect") || trigger.textContent, {
                language: data.language || trigger.closest("[lang]")?.getAttribute("lang") || undefined,
                sourceType: data.sourceType || undefined,
                contentKey: data.contentKey || undefined,
                sentence: data.sentence || undefined,
                cueStartMs: data.cueStartMs,
                paragraph: data.paragraph,
                page: data.page,
                region: data.region
            });
        });

        if (config.selection?.surface) {
            bindSelection(config.selection.surface, {
                paragraphAttribute: config.selection.paragraphAttribute
            });
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bindSpeakButtons, { once: true });
    } else {
        bindSpeakButtons();
    }
    window.addEventListener("load", bindSpeakButtons, { once: true });

    window.AniLingoLanguageInspector = Object.freeze({
        available,
        open,
        close,
        isOpen: () => available && !panel.hidden,
        bindSelection,
        speak,
        addEventListener: (type, handler) => events.addEventListener(type, handler),
        removeEventListener: (type, handler) => events.removeEventListener(type, handler)
    });
})();
