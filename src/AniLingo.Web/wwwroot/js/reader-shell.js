(() => {
    const roots = document.querySelectorAll("[data-unified-reader]");
    if (!roots.length) return;

    const interactiveSelector =
        "a,button,input,select,textarea,summary,label,[contenteditable='true'],[role='button']";

    const humanType = value => ({
        "book": "Bücher",
        "light-novel": "Light Novels",
        "web-novel": "Web Novels",
        "manga": "Manga",
        "fixed": "Fixed Documents"
    }[value] || value || "Bücher");

    const humanSource = source => {
        if (!source || source === "system") return "System-Preset";
        if (source === "default") return "Mein globaler Standard";
        if (source.startsWith("type:")) return humanType(source.slice(5));
        if (source.startsWith("genre:")) {
            const parts = source.split(":");
            return "Genre " + (parts.at(-1) || "").replaceAll("-", " ");
        }
        if (source.startsWith("work:")) return "Dieses Buch";
        return source;
    };

    for (const root of roots) {
        const surface =
            root.querySelector("[data-reader-surface]") ||
            root.querySelector("[data-reader-content]") ||
            root.querySelector("[data-book-reader-content]");
        const settingsContainer =
            root.querySelector("[data-reader-settings-container]") ||
            root.querySelector("[data-reader-settings-panel]") ||
            root.querySelector(".book-reader-settings");
        const settingsForm =
            root.querySelector("[data-reader-settings-form]") ||
            root.querySelector("[data-book-settings-form]");
        const settingsJson =
            root.querySelector("[data-reader-settings-json]") ||
            root.querySelector("[data-book-settings-json]");
        const topChrome =
            root.querySelector("[data-reader-chrome-primary]") ||
            root.querySelector(".novel-reader-toolbar") ||
            root.querySelector(".book-reader-toolbar");

        let settings = {};
        try {
            settings = JSON.parse(settingsJson?.textContent || "{}") || {};
        } catch {
            settings = {};
        }

        let restoring = true;
        let pointerStart = null;
        let lastScrollY = window.scrollY;
        let accumulatedScroll = 0;
        let activeSettingsTab = "reading";

        root.classList.remove("reader-chrome-hidden");
        root.dataset.readerChrome = "visible";

        const overlayOpen = () => {
            if (settingsContainer?.open) return true;
            if (root.querySelector("dialog[open]")) return true;
            return Boolean(root.querySelector(
                "[data-reader-notes]:not([hidden])," +
                "[data-chapter-drawer]:not([hidden])," +
                "[data-book-drawer][open]"));
        };

        const showChrome = () => {
            root.classList.remove("reader-chrome-hidden");
            root.dataset.readerChrome = "visible";
        };

        const hideChrome = () => {
            if (restoring || overlayOpen()) return;
            root.classList.add("reader-chrome-hidden");
            root.dataset.readerChrome = "hidden";
        };

        const toggleChrome = () => {
            if (root.classList.contains("reader-chrome-hidden")) showChrome();
            else hideChrome();
        };

        const dispatchPage = direction => {
            root.dispatchEvent(new CustomEvent("anilingo:reader-page-edge", {
                detail: { direction },
                bubbles: false
            }));
        };

        const updateModeVisibility = () => {
            const mode = root.dataset.readingMode || settings.readingMode || "continuous";
            root.querySelectorAll("[data-reader-mode-choice]").forEach(button => {
                const active = button.dataset.readerModeChoice === mode;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-pressed", active ? "true" : "false");
            });
            root.querySelectorAll("[data-reader-mode-only]").forEach(element => {
                element.hidden = element.dataset.readerModeOnly !== mode;
            });
        };

        const controlFor = key =>
            settingsForm?.querySelector(
                `[data-reader-setting][data-setting-key="${CSS.escape(key)}"],` +
                `[data-book-setting="${CSS.escape(key)}"]`);

        const keyForControl = control =>
            control?.dataset.settingKey || control?.dataset.bookSetting || "";

        const advancedKeys = new Set([
            "themeEffectStrength", "themeBrightness", "themeContrast",
            "themeSaturation", "themeBlurPx", "themeVignetteStrength",
            "themeGrainStrength", "themeTextBackdropStrength",
            "themeParallaxStrength", "themeTintStrength"
        ]);

        const sectionForKey = key => {
            if ([
                "readingMode", "pageTransition", "twoPageSpread", "autoScrollSpeed"
            ].includes(key)) return "reading";
            if ([
                "fontFamily", "fontSizeRem", "lineHeight", "paragraphSpacingEm",
                "textWidthPx", "textAlignment", "chapterStyle"
            ].includes(key)) return "text";
            if ([
                "paperStyle", "genreArtworkEnabled", "genreTheme",
                "backgroundAssetId", "backgroundIntensity", "backgroundMotionMode",
                "themeEffectStrength", "themeBrightness", "themeContrast",
                "themeSaturation", "themeBlurPx", "themeVignetteStrength",
                "themeGrainStrength", "themeTextBackdropStrength",
                "themeParallaxStrength", "themeTintStrength"
            ].includes(key)) return "appearance";
            return "defaults";
        };

        const wrapperFor = (control, key) => {
            if (advancedKeys.has(key)) {
                const details = control.closest(
                    ".novel-theme-advanced,.book-theme-advanced");
                if (details) return details;
            }
            return control.closest("label") || control;
        };

        const ensureHidden = (name, value = "") => {
            if (!settingsForm) return null;
            let input = settingsForm.querySelector(
                `input[type="hidden"][name="${CSS.escape(name)}"]`);
            if (!input) {
                input = document.createElement("input");
                input.type = "hidden";
                input.name = name;
                settingsForm.append(input);
            }
            input.value = value;
            return input;
        };

        const selectedTarget = () =>
            settingsForm?.querySelector('[name="scope"]')?.value || "work";

        const selectedGenre = () =>
            settingsForm?.querySelector('[name="genre"]')?.value || "";

        const setTarget = (target, genre = "") => {
            const scope = ensureHidden("scope", target);
            ensureHidden("genre", genre);
            ensureHidden("genrePriority", "500");
            if (scope) scope.value = target;
            root.dataset.readerPreferenceTarget = target;
            root.dataset.readerPreferenceGenre = genre;
            root.querySelectorAll("[data-reader-reset-field]").forEach(button => {
                button.title = target === "work"
                    ? "Für dieses Buch wieder erben"
                    : "Auf dieser Ebene wieder erben";
            });
        };

        const applySettingsPayload = next => {
            if (!next) return;
            settings = next;
            if (next.readingMode) root.dataset.readingMode = next.readingMode;
            updateModeVisibility();
            updateSourceBadges();
            root.dispatchEvent(new CustomEvent("anilingo:reader-settings-response", {
                detail: { settings: next }
            }));
        };

        const postSettingsCommand = async ({
            changedKey = "",
            resetField = false,
            resetScope = false
        } = {}) => {
            if (!settingsForm) return null;
            const data = new FormData(settingsForm);
            data.set("scope", selectedTarget());
            data.set("genre", selectedGenre());
            data.set(
                "genrePriority",
                settingsForm.querySelector('[name="genrePriority"]')?.value || "500");
            data.set("changedKey", changedKey);
            data.set("resetField", resetField ? "true" : "false");
            data.set("resetScope", resetScope ? "true" : "false");

            const response = await fetch(settingsForm.action, {
                method: "POST",
                body: data,
                credentials: "same-origin",
                headers: { "X-Requested-With": "fetch" }
            });
            if (!response.ok) {
                throw new Error(
                    (await response.text()) || "Reader-Einstellungen konnten nicht gespeichert werden.");
            }

            const payload = await response.json();
            applySettingsPayload(payload?.settings);
            return payload?.settings || null;
        };

        const updateSourceBadges = () => {
            const sources = settings.effectiveSources || {};
            root.querySelectorAll("[data-reader-setting-source]").forEach(badge => {
                const key = badge.dataset.readerSettingSource;
                badge.textContent = humanSource(sources[key]);
            });
        };

        const addResetButton = (wrapper, key) => {
            if (!wrapper || wrapper.querySelector("[data-reader-reset-field]")) return;
            const button = document.createElement("button");
            button.type = "button";
            button.className = "reader-setting-reset";
            button.dataset.readerResetField = key;
            button.textContent = "↶";
            button.setAttribute("aria-label", "Diese Einstellung wieder erben");
            button.addEventListener("click", async event => {
                event.preventDefault();
                event.stopPropagation();
                try {
                    await postSettingsCommand({ changedKey: key, resetField: true });
                } catch (error) {
                    console.warn(error);
                }
            });
            wrapper.append(button);

            const badge = document.createElement("small");
            badge.className = "reader-setting-source";
            badge.dataset.readerSettingSource = key;
            wrapper.append(badge);
        };

        const enhanceSettings = () => {
            if (!settingsForm || settingsForm.dataset.unifiedSettingsReady === "true") {
                return;
            }
            settingsForm.dataset.unifiedSettingsReady = "true";
            settingsContainer?.setAttribute("data-reader-settings-container", "");

            const workspace = document.createElement("div");
            workspace.className = "reader-settings-workspace";

            const tabs = document.createElement("div");
            tabs.className = "reader-settings-tabs";
            tabs.setAttribute("role", "tablist");

            const panels = {};
            for (const [key, label] of [
                ["reading", "Lesen"],
                ["text", "Text"],
                ["appearance", "Aussehen"],
                ["defaults", "Defaults"]
            ]) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "reader-settings-tab";
                button.dataset.readerSettingsTab = key;
                button.textContent = label;
                button.setAttribute("role", "tab");
                button.addEventListener("click", () => activateTab(key));
                tabs.append(button);

                const panel = document.createElement("section");
                panel.className = "reader-settings-tab-panel";
                panel.dataset.readerSettingsPanel = key;
                panel.setAttribute("role", "tabpanel");
                panels[key] = panel;
            }

            workspace.append(tabs, panels.reading, panels.text, panels.appearance, panels.defaults);
            settingsForm.prepend(workspace);

            const heading = settingsForm.querySelector(".reader-settings-heading");
            if (heading) {
                workspace.before(heading);
            }

            const moved = new Set();
            const controls = Array.from(settingsForm.querySelectorAll(
                "[data-reader-setting],[data-book-setting]"));

            for (const control of controls) {
                const key = keyForControl(control);
                if (!key) continue;
                const wrapper = wrapperFor(control, key);
                if (!wrapper || moved.has(wrapper) || workspace.contains(wrapper)) continue;
                moved.add(wrapper);
                const section = sectionForKey(key);
                panels[section].append(wrapper);
                wrapper.dataset.readerSettingWrapper = key;
                addResetButton(wrapper, key);

                if (key === "readingMode") {
                    wrapper.classList.add("reader-native-mode-field");
                }
                if (key === "autoScrollSpeed") {
                    wrapper.dataset.readerModeOnly = "continuous";
                }
                if (key === "pageTransition" || key === "twoPageSpread") {
                    wrapper.dataset.readerModeOnly = "paged";
                }
            }

            const googleFont = settingsForm.querySelector(".novel-google-font");
            if (googleFont && !workspace.contains(googleFont)) {
                panels.text.append(googleFont);
            }

            const actions = settingsForm.querySelector(
                ".novel-settings-actions,.book-settings-actions");
            if (actions && !workspace.contains(actions)) {
                actions.classList.add("reader-legacy-settings-actions");
                panels.defaults.append(actions);
            }

            settingsForm.querySelectorAll(
                ".novel-settings-group,.book-theme-advanced-grid").forEach(group => {
                if (!group.querySelector("input,select,button,textarea,details")) {
                    group.remove();
                }
            });

            const modeControl = controlFor("readingMode");
            if (modeControl) {
                const switcher = document.createElement("div");
                switcher.className = "reader-mode-switch";
                switcher.setAttribute("role", "group");
                switcher.setAttribute("aria-label", "Lesemodus");
                for (const [value, label] of [["continuous", "Scrollen"], ["paged", "Seiten"]]) {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.dataset.readerModeChoice = value;
                    button.textContent = label;
                    button.addEventListener("click", () => {
                        if (modeControl.value === value) return;
                        modeControl.value = value;
                        modeControl.dispatchEvent(new Event("change", { bubbles: true }));
                        root.dataset.readingMode = value;
                        settings.readingMode = value;
                        updateModeVisibility();
                    });
                    switcher.append(button);
                }
                panels.reading.prepend(switcher);
            }

            const sourceAuto =
                root.querySelector("[data-reader-autoscroll-toggle]") ||
                root.querySelector("[data-book-autoscroll]");
            if (sourceAuto) {
                const auto = document.createElement("button");
                auto.type = "button";
                auto.className = "reader-inline-action";
                auto.dataset.readerModeOnly = "continuous";
                auto.textContent = "Auto-Scroll starten / pausieren";
                auto.addEventListener("click", () => sourceAuto.click());
                panels.reading.append(auto);
            }

            const paperControl = controlFor("paperStyle");
            if (paperControl) {
                const swatches = document.createElement("div");
                swatches.className = "reader-paper-swatches";
                for (const option of Array.from(paperControl.options)) {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.dataset.paper = option.value;
                    button.title = option.textContent || option.value;
                    button.setAttribute("aria-label", option.textContent || option.value);
                    button.addEventListener("click", () => {
                        paperControl.value = option.value;
                        paperControl.dispatchEvent(new Event("change", { bubbles: true }));
                    });
                    swatches.append(button);
                }
                paperControl.closest("label")?.append(swatches);
            }

            const targetBox = document.createElement("div");
            targetBox.className = "reader-default-target";
            const title = document.createElement("strong");
            title.textContent = "Änderungen speichern für";
            const targetSelect = document.createElement("select");
            targetSelect.dataset.readerPreferenceTarget = "";

            const addOption = (value, label, genre = "") => {
                const option = document.createElement("option");
                option.value = value + (genre ? ":" + genre : "");
                option.textContent = label;
                targetSelect.append(option);
            };

            addOption("work", "Dieses Buch");
            addOption("type", "Alle " + humanType(
                root.dataset.readerContentType || settings.contentTypeKey));
            for (const genre of settings.sourceGenres || []) {
                addOption("genre", "Genre: " + genre, genre);
            }
            addOption("default", "Mein globaler Standard");

            targetSelect.addEventListener("change", () => {
                const [target, ...rest] = targetSelect.value.split(":");
                setTarget(target, rest.join(":"));
                updateSourceBadges();
            });

            const resetScope = document.createElement("button");
            resetScope.type = "button";
            resetScope.className = "button";
            resetScope.textContent = "Diese Ebene zurücksetzen";
            resetScope.addEventListener("click", async () => {
                try {
                    await postSettingsCommand({ resetScope: true });
                } catch (error) {
                    console.warn(error);
                }
            });

            const copyCurrent = document.createElement("button");
            copyCurrent.type = "button";
            copyCurrent.className = "button";
            copyCurrent.textContent = "Aktuelle Werte auf diese Ebene kopieren";
            copyCurrent.addEventListener("click", async () => {
                try {
                    await postSettingsCommand();
                } catch (error) {
                    console.warn(error);
                }
            });

            targetBox.append(title, targetSelect, resetScope, copyCurrent);
            panels.defaults.prepend(targetBox);
            setTarget("work");

            activateTab(activeSettingsTab);
            updateModeVisibility();
            updateSourceBadges();
        };

        const activateTab = tab => {
            activeSettingsTab = tab;
            root.querySelectorAll("[data-reader-settings-tab]").forEach(button => {
                const active = button.dataset.readerSettingsTab === tab;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-selected", active ? "true" : "false");
            });
            root.querySelectorAll(
                '[data-reader-settings-panel][role="tabpanel"]'
            ).forEach(panel => {
                if (panel.closest("[data-reader-settings-form]") !== settingsForm) {
                    return;
                }
                panel.hidden = panel.dataset.readerSettingsPanel !== tab;
            });
        };

        const buildMobileActions = () => {
            if (root.querySelector("[data-reader-mobile-actions]")) return;

            const nav = document.createElement("nav");
            nav.className = "reader-mobile-actions";
            nav.dataset.readerMobileActions = "";
            nav.dataset.readerChrome = "";
            nav.setAttribute("aria-label", "Reader");

            const addProxy = (label, selectors, action) => {
                const source = root.querySelector(selectors);
                if (!source && !action) return;
                const button = document.createElement("button");
                button.type = "button";
                button.textContent = label;
                button.addEventListener("click", () => {
                    showChrome();
                    if (action) action();
                    else source?.click();
                });
                nav.append(button);
            };

            addProxy(
                "Kapitel",
                "[data-reader-chapters-toggle],[data-book-drawer-open]");
            addProxy(
                "Sprache",
                "[data-reader-language-control],.novel-view-switch,.book-reader-view-switch",
                () => root.classList.toggle("reader-language-expanded"));
            addProxy(
                "Aa",
                "[data-reader-settings-container],.novel-reader-settings,.book-reader-settings",
                () => {
                    if (settingsContainer && "open" in settingsContainer) {
                        settingsContainer.open = true;
                    }
                });
            addProxy(
                "Notizen",
                "[data-reader-notes-toggle]");

            if (nav.childElementCount) root.append(nav);
        };

        const buildOverflow = () => {
            if (!topChrome || topChrome.querySelector("[data-reader-overflow]")) return;
            const sources = [
                root.querySelector("[data-reader-wake-lock-toggle]"),
                root.querySelector("[data-reader-immersive-toggle]")
            ].filter(Boolean);
            if (!sources.length) return;

            const wrap = document.createElement("details");
            wrap.className = "reader-overflow";
            wrap.dataset.readerOverflow = "";
            const summary = document.createElement("summary");
            summary.textContent = "•••";
            summary.setAttribute("aria-label", "Weitere Reader-Aktionen");
            const menu = document.createElement("div");
            menu.className = "reader-overflow-menu";

            for (const source of sources) {
                source.classList.add("reader-overflow-source");
                const button = document.createElement("button");
                button.type = "button";
                button.textContent =
                    source.dataset.readerWakeLockToggle !== undefined
                        ? "Bildschirm an"
                        : "Immersiv";
                button.addEventListener("click", () => {
                    source.click();
                    wrap.open = false;
                });
                menu.append(button);
            }
            wrap.append(summary, menu);
            const target =
                topChrome.querySelector(".novel-toolbar-end,.book-reader-toolbar") ||
                topChrome;
            target.append(wrap);
        };

        if (topChrome) {
            topChrome.dataset.readerChrome = "";
            topChrome.dataset.readerChromePrimary = "";
        }
        if (surface) surface.dataset.readerSurface = "";
        settingsContainer?.setAttribute("data-reader-settings-container", "");

        enhanceSettings();
        buildMobileActions();
        buildOverflow();
        updateModeVisibility();

        settingsContainer?.addEventListener("toggle", () => {
            if (settingsContainer.open) {
                showChrome();
                restoring = false;
            }
        });

        root.addEventListener("anilingo:reader-restoring", event => {
            restoring = event.detail?.active !== false;
            if (restoring) showChrome();
        });

        root.addEventListener("anilingo:reader-settings", event => {
            if (event.detail?.settings) {
                settings = event.detail.settings;
                if (settings.readingMode) {
                    root.dataset.readingMode = settings.readingMode;
                }
                updateModeVisibility();
                updateSourceBadges();
            }
        });

        if (surface) {
            surface.addEventListener("pointerdown", event => {
                if (event.pointerType === "mouse" && event.button !== 0) return;
                if (event.target.closest(interactiveSelector)) return;
                pointerStart = {
                    id: event.pointerId,
                    x: event.clientX,
                    y: event.clientY,
                    time: performance.now()
                };
            }, { passive: true });

            surface.addEventListener("pointerup", event => {
                const start = pointerStart;
                pointerStart = null;
                if (!start || start.id !== event.pointerId) return;
                const dx = event.clientX - start.x;
                const dy = event.clientY - start.y;
                const distance = Math.hypot(dx, dy);
                const selection = window.getSelection();
                if (selection && !selection.isCollapsed && selection.toString().trim()) return;

                if ((root.dataset.readingMode || settings.readingMode) === "paged") {
                    if (Math.abs(dx) > 55 && Math.abs(dx) > Math.abs(dy) * 1.25) {
                        dispatchPage(dx < 0 ? 1 : -1);
                        return;
                    }

                    const rect = surface.getBoundingClientRect();
                    const x = event.clientX - rect.left;
                    if (distance < 14 && x < rect.width * .24) {
                        dispatchPage(-1);
                        return;
                    }
                    if (distance < 14 && x > rect.width * .76) {
                        dispatchPage(1);
                        return;
                    }
                }

                if (distance < 14 && performance.now() - start.time < 700) {
                    toggleChrome();
                }
            }, { passive: true });

            surface.addEventListener("pointercancel", () => {
                pointerStart = null;
            }, { passive: true });
        }

        window.addEventListener("scroll", () => {
            const nextY = window.scrollY;
            const delta = nextY - lastScrollY;
            lastScrollY = nextY;

            if (restoring || overlayOpen()) {
                accumulatedScroll = 0;
                return;
            }

            if (Math.sign(delta) !== Math.sign(accumulatedScroll)) {
                accumulatedScroll = delta;
            } else {
                accumulatedScroll += delta;
            }

            if (accumulatedScroll > 64) {
                hideChrome();
                accumulatedScroll = 0;
            } else if (accumulatedScroll < -40) {
                showChrome();
                accumulatedScroll = 0;
            }
        }, { passive: true });

        document.addEventListener("mousemove", event => {
            if (event.clientY <= 24) showChrome();
        }, { passive: true });

        document.addEventListener("keydown", event => {
            if (event.key !== "Escape") return;
            if (settingsContainer?.open) {
                settingsContainer.open = false;
                showChrome();
                event.preventDefault();
            }
        });

        window.addEventListener("load", () => {
            window.setTimeout(() => {
                restoring = false;
                root.dataset.readerReady = "true";
                showChrome();
            }, 250);
        }, { once: true });

        // Scripts are normally loaded after DOMContentLoaded; do not wait forever
        // if the load event already fired.
        if (document.readyState === "complete") {
            window.setTimeout(() => {
                restoring = false;
                root.dataset.readerReady = "true";
                showChrome();
            }, 50);
        }
    }
})();
