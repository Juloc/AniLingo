(() => {
    const shell = document.querySelector("[data-reader-personalization]");
    if (!shell) return;

    const genreSelect = shell.querySelector("[data-reader-genre-select]");
    const backgroundSelect = shell.querySelector("[data-reader-background-select]");
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    const stack = document.createElement("div");
    stack.className = "reader-theme-stack";
    stack.setAttribute("aria-hidden", "true");
    stack.innerHTML = [
        '<div class="reader-theme-layer" data-theme-layer="back"></div>',
        '<div class="reader-theme-layer" data-theme-layer="mid"></div>',
        '<div class="reader-theme-layer" data-theme-layer="front"></div>',
        '<div class="reader-theme-layer" data-theme-layer="static"></div>',
        '<div class="reader-theme-layer" data-theme-layer="page"></div>',
        '<div class="reader-theme-overlay"></div>',
        '<div class="reader-theme-tint"></div>',
        '<div class="reader-theme-vignette"></div>',
        '<div class="reader-theme-grain"></div>',
        '<div class="reader-theme-effect"></div>'
    ].join("");
    shell.prepend(stack);

    const layers = {
        back: stack.querySelector('[data-theme-layer="back"]'),
        mid: stack.querySelector('[data-theme-layer="mid"]'),
        front: stack.querySelector('[data-theme-layer="front"]'),
        static: stack.querySelector('[data-theme-layer="static"]'),
        page: stack.querySelector('[data-theme-layer="page"]')
    };

    let state = null;
    let catalog = [];
    let suggestedId = null;
    let queryKey = "";
    let selectedTheme = null;
    let resizeTimer = null;
    let scrollFrame = null;

    const clamp = (value, min, max) =>
        Math.min(max, Math.max(min, Number(value)));

    const cssUrl = value =>
        value && value.startsWith("/reader-backgrounds/")
            ? `url("${value.replace(/["\\()]/g, character => encodeURIComponent(character))}")`
            : "none";

    const chooseSource = sources => {
        if (!sources) return null;
        const width = window.innerWidth;
        if (width <= 680 && sources.mobile) return sources.mobile;
        if (width <= 1180 && sources.tablet) return sources.tablet;
        if (width >= 1900 && sources.wide) return sources.wide;
        if (width > 1180 && sources.desktop) return sources.desktop;
        return sources.default || sources.tablet || sources.desktop || sources.mobile || sources.wide || null;
    };

    const setLayer = (layer, sources, visible) => {
        const url = visible ? chooseSource(sources) : null;
        layer.style.backgroundImage = cssUrl(url);
        layer.hidden = !url;
    };

    const themeValue = (base, multiplier, min, max) =>
        clamp(Number(base) * Number(multiplier ?? 1), min, max);

    const populateControls = () => {
        if (genreSelect) {
            const selected = state?.genreTheme || "auto";
            const genres = new Map();
            for (const theme of catalog) {
                if (!theme?.genre || genres.has(theme.genre)) continue;
                genres.set(theme.genre, theme.genreLabel || theme.genre);
            }

            genreSelect.replaceChildren();
            const automatic = new Option("Automatisch", "auto");
            genreSelect.append(automatic);

            for (const [genre, label] of [...genres.entries()]
                .sort((a, b) => a[1].localeCompare(b[1]))) {
                genreSelect.append(new Option(label, genre));
            }

            if (selected !== "auto" && !genres.has(selected)) {
                genreSelect.append(new Option(selected + " (nicht verfügbar)", selected));
            }
            genreSelect.value = selected;
        }

        if (backgroundSelect) {
            const selected = state?.backgroundAssetId || "auto";
            backgroundSelect.replaceChildren();
            backgroundSelect.append(new Option("Automatisch", "auto"));

            const groups = new Map();
            for (const theme of catalog) {
                const label = theme.genreLabel || theme.genre || "Weitere";
                let group = groups.get(label);
                if (!group) {
                    group = document.createElement("optgroup");
                    group.label = label;
                    groups.set(label, group);
                    backgroundSelect.append(group);
                }
                group.append(new Option(theme.name || theme.variant || theme.id, theme.id));
            }

            if (selected !== "auto" && !catalog.some(theme => theme.id === selected)) {
                backgroundSelect.append(new Option(selected + " (nicht verfügbar)", selected));
            }
            backgroundSelect.value = selected;
        }
    };

    const resolveMotion = theme => {
        if (!theme || state?.readingMode === "paged") return "page";

        const requested = state?.backgroundMotionMode || "auto";
        if (reduceMotion.matches) return "static";
        if (requested === "static") return "static";
        if (requested === "parallax") return theme.hasParallax ? "parallax" : "static";
        return theme.hasParallax ? "parallax" : "static";
    };

    const applyTheme = theme => {
        selectedTheme = theme || null;

        if (!state?.genreArtworkEnabled || !theme) {
            stack.hidden = true;
            shell.dataset.themeEffect = "none";
            shell.dataset.themeMotion = "none";
            return;
        }

        stack.hidden = false;
        const appearance = theme.appearance || {};
        const motion = theme.motion || {};
        const resolvedMotion = resolveMotion(theme);

        shell.dataset.genreTheme = theme.genre || state.genreTheme || "auto";
        shell.dataset.readerTheme = theme.id;
        shell.dataset.themeMotion = resolvedMotion;
        shell.dataset.themeEffect =
            reduceMotion.matches || resolvedMotion !== "parallax"
                ? "none"
                : (motion.effect || "none");

        shell.style.setProperty(
            "--reader-theme-image-opacity",
            clamp(state.backgroundIntensity ?? .055, 0, .2));
        shell.style.setProperty(
            "--reader-theme-overlay",
            clamp(appearance.overlay ?? .08, 0, .8));
        shell.style.setProperty(
            "--reader-theme-brightness",
            themeValue(appearance.brightness ?? .92, state.themeBrightness, .35, 1.8));
        shell.style.setProperty(
            "--reader-theme-contrast",
            themeValue(appearance.contrast ?? .96, state.themeContrast, .4, 1.7));
        shell.style.setProperty(
            "--reader-theme-saturation",
            themeValue(appearance.saturation ?? .9, state.themeSaturation, 0, 2));
        shell.style.setProperty(
            "--reader-theme-blur",
            clamp((appearance.blurPx ?? 0) + (state.themeBlurPx ?? 0), 0, 12).toFixed(1) + "px");
        shell.style.setProperty(
            "--reader-theme-vignette",
            themeValue(appearance.vignette ?? .12, state.themeVignetteStrength, 0, .9));
        shell.style.setProperty(
            "--reader-theme-grain",
            themeValue(appearance.grain ?? .02, state.themeGrainStrength, 0, .35));
        const textBackdrop = themeValue(
            appearance.textBackdrop ?? .12,
            state.themeTextBackdropStrength,
            0,
            .75);
        shell.style.setProperty("--reader-theme-text-backdrop", textBackdrop);
        shell.style.setProperty(
            "--reader-theme-text-backdrop-pct",
            (textBackdrop * 100).toFixed(1) + "%");
        shell.style.setProperty(
            "--reader-theme-tint",
            /^#[0-9a-f]{6}$/i.test(appearance.tint || "") ? appearance.tint : "#000000");
        shell.style.setProperty(
            "--reader-theme-tint-strength",
            themeValue(appearance.tintStrength ?? 0, state.themeTintStrength, 0, .65));
        shell.style.setProperty(
            "--reader-theme-page-shadow",
            clamp(appearance.pageShadow ?? .18, 0, .7));
        shell.style.setProperty(
            "--reader-theme-page-curl",
            clamp((appearance.pageCurl ?? .8) * (state.themeEffectStrength ?? 1), 0, 1.8));
        shell.style.setProperty(
            "--reader-theme-effect-strength",
            reduceMotion.matches
                ? 0
                : themeValue(motion.effectStrength ?? 0, state.themeEffectStrength, 0, 1));
        shell.style.setProperty(
            "--reader-theme-parallax-strength",
            reduceMotion.matches
                ? 0
                : themeValue(motion.parallaxStrength ?? .55, state.themeParallaxStrength, 0, 1.5));

        const assets = theme.assets || {};
        setLayer(layers.page, assets.page, resolvedMotion === "page");
        setLayer(
            layers.static,
            assets.scrollStatic || assets.page,
            resolvedMotion === "static");
        setLayer(
            layers.back,
            assets.parallaxBack || assets.scrollStatic || assets.page,
            resolvedMotion === "parallax");
        setLayer(layers.mid, assets.parallaxMid, resolvedMotion === "parallax");
        setLayer(layers.front, assets.parallaxFront, resolvedMotion === "parallax");
        updateParallax();
    };

    const updateParallax = () => {
        scrollFrame = null;
        if (!selectedTheme || shell.dataset.themeMotion !== "parallax" || reduceMotion.matches) {
            for (const layer of [layers.back, layers.mid, layers.front]) {
                layer.style.removeProperty("--reader-theme-layer-shift");
            }
            return;
        }

        const base = selectedTheme.motion || {};
        const strength = clamp(
            Number(base.parallaxStrength ?? .55) *
                Number(state?.themeParallaxStrength ?? 1),
            0,
            1.5);
        const scroll = window.scrollY;
        const shift = speed =>
            (-(scroll * Number(speed || 0) * strength)).toFixed(2) + "px";

        layers.back.style.setProperty(
            "--reader-theme-layer-shift",
            shift(base.backSpeed ?? .08));
        layers.mid.style.setProperty(
            "--reader-theme-layer-shift",
            shift(base.midSpeed ?? .16));
        layers.front.style.setProperty(
            "--reader-theme-layer-shift",
            shift(base.frontSpeed ?? .28));
    };

    const requestParallax = () => {
        if (scrollFrame != null) return;
        scrollFrame = requestAnimationFrame(updateParallax);
    };

    const buildQuery = nextState => {
        const params = new URLSearchParams();
        if (nextState.genreTheme && nextState.genreTheme !== "auto") {
            params.append("genre", nextState.genreTheme);
        } else {
            for (const genre of nextState.sourceGenres || []) {
                if (genre) params.append("genre", genre);
            }
        }
        return params.toString();
    };

    const load = async nextState => {
        state = nextState;
        const nextQuery = buildQuery(nextState);

        if (catalog.length === 0 || queryKey !== nextQuery) {
            try {
                const response = await fetch(
                    "/api/reader-themes" + (nextQuery ? "?" + nextQuery : ""),
                    {
                        credentials: "same-origin",
                        headers: { "X-Requested-With": "fetch" }
                    });
                if (!response.ok) throw new Error("theme catalog unavailable");
                const payload = await response.json();
                catalog = Array.isArray(payload?.themes) ? payload.themes : [];
                suggestedId = payload?.suggestedId || null;
                queryKey = nextQuery;
            } catch {
                catalog = [];
                suggestedId = null;
            }
        }

        populateControls();

        const requested = state.backgroundAssetId || "auto";
        const id = requested === "auto" ? suggestedId : requested;
        const theme = catalog.find(candidate => candidate.id === id) || null;
        applyTheme(theme);
    };

    shell.addEventListener("anilingo:reader-settings", event => {
        const next = event.detail?.settings;
        if (!next) return;
        void load(next);
    });

    window.addEventListener("scroll", requestParallax, { passive: true });
    window.addEventListener("resize", () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (selectedTheme) applyTheme(selectedTheme);
        }, 120);
    });
    reduceMotion.addEventListener?.("change", () => {
        if (selectedTheme) applyTheme(selectedTheme);
    });
})();
