// Settings → Appearance. The palette itself is always computed on the server
// (Features/Appearance/AccentPalette.cs): picking a colour fetches the exact stylesheet the layout
// would render for it and swaps it into <style id="app-accent-palette">, so the whole page — sidebar,
// buttons, artwork tint, logo ring — previews live without a second copy of the colour maths here.
(() => {
    const root = document.documentElement;
    const host = document.querySelector('[data-appearance-settings]');
    const paletteStyle = document.getElementById('app-accent-palette');
    if (!host || !paletteStyle) return;

    const accentForm = host.querySelector('[data-appearance-accent-form]');
    const accentValue = host.querySelector('[data-appearance-accent-value]');
    const modeForm = host.querySelector('[data-appearance-mode-form]');
    const picker = host.querySelector('[data-accent-picker]');
    const hexField = host.querySelector('[data-accent-hex]');
    const custom = host.querySelector('[data-accent-custom]');
    const monoNote = host.querySelector('[data-accent-mono-note]');
    const status = host.querySelector('[data-appearance-status]');
    const swatches = Array.from(host.querySelectorAll('[data-accent-preset]'));
    const brandSeed = host.dataset.brandSeed;
    const hexPattern = /^#?([0-9a-f]{6})$/i;

    let previewController = null;
    let previewTimer = 0;
    let savedAccent = accentValue.value || '';

    const normalize = (value) => {
        const match = hexPattern.exec((value || '').trim());
        return match ? `#${match[1].toLowerCase()}` : null;
    };

    const setStatus = (text, state) => {
        status.textContent = text || '';
        status.dataset.state = state || '';
    };

    // --- readout -------------------------------------------------------------------------------
    const channel = (value) => {
        const c = value / 255;
        return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    };
    const luminance = (hex) => {
        const n = parseInt(hex.slice(1, 7), 16);
        return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255);
    };
    const contrast = (a, b) => {
        const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
        return (hi + 0.05) / (lo + 0.05);
    };

    const refreshReadout = () => {
        const computed = getComputedStyle(root);
        for (const row of host.querySelectorAll('[data-contrast-pair]')) {
            const [fg, bg] = row.dataset.contrastPair.split('|').map((name) => computed.getPropertyValue(name).trim());
            const output = row.querySelector('[data-contrast-value]');
            if (!/^#[0-9a-f]{6}/i.test(fg) || !/^#[0-9a-f]{6}/i.test(bg)) continue;
            const ratio = contrast(fg, bg);
            output.textContent = `${ratio.toFixed(1)}:1`;
            output.dataset.level = ratio >= 7 ? 'aaa' : ratio >= 4.5 ? 'aa' : 'low';
        }
        monoNote.hidden = computed.getPropertyValue('--art-saturate').trim() !== '0';
        const themeColor = document.querySelector('meta[data-app-theme-color]');
        const browserColor = computed.getPropertyValue('--browser-theme-color').trim();
        if (themeColor && browserColor) themeColor.setAttribute('content', browserColor);
    };

    const reflectSelection = (accent) => {
        let matched = false;
        for (const swatch of swatches) {
            const selected = swatch.dataset.accentPreset === accent;
            matched ||= selected;
            swatch.classList.toggle('selected', selected);
            swatch.setAttribute('aria-checked', String(selected));
        }
        custom.classList.toggle('selected', !matched);
        if (picker.value !== accent) picker.value = accent;
        if (document.activeElement !== hexField) hexField.value = accent;
        hexField.removeAttribute('aria-invalid');
    };

    // --- preview & save --------------------------------------------------------------------------
    const preview = async (accent) => {
        previewController?.abort();
        previewController = new AbortController();
        const url = new URL(host.dataset.previewUrl, window.location.origin);
        url.searchParams.set('accent', accent);
        try {
            const response = await fetch(url, {
                credentials: 'same-origin',
                signal: previewController.signal,
                headers: { Accept: 'text/css' }
            });
            if (!response.ok) return;
            paletteStyle.textContent = await response.text();
            root.dataset.accent = accent;
            refreshReadout();
        } catch (error) {
            if (error.name !== 'AbortError') throw error;
        }
    };

    const schedulePreview = (accent) => {
        window.clearTimeout(previewTimer);
        previewTimer = window.setTimeout(() => preview(accent), 40);
        reflectSelection(accent);
    };

    const save = async (accent) => {
        // The brand red is stored as "no choice", so a future brand refresh reaches this profile too.
        const stored = accent === brandSeed ? '' : accent;
        if (stored === savedAccent) {
            await preview(accent);
            return;
        }

        accentValue.value = stored;
        setStatus(host.dataset.textSaving, 'busy');
        try {
            const [response] = await Promise.all([
                fetch(accentForm.action, {
                    method: 'POST',
                    body: new FormData(accentForm),
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                }),
                preview(accent)
            ]);
            if (!response.ok) throw new Error(`Accent save failed: ${response.status}`);
            const payload = await response.json();
            savedAccent = payload.accent || '';
            setStatus(host.dataset.textSaved, 'ok');
        } catch {
            setStatus(host.dataset.textFailed, 'error');
        }
    };

    for (const swatch of swatches) {
        swatch.addEventListener('click', () => {
            const accent = swatch.dataset.accentPreset;
            reflectSelection(accent);
            save(accent);
        });
    }

    // Dragging in the native picker previews continuously; letting go saves.
    picker.addEventListener('input', () => schedulePreview(picker.value.toLowerCase()));
    picker.addEventListener('change', () => save(picker.value.toLowerCase()));

    hexField.addEventListener('input', () => {
        const accent = normalize(hexField.value);
        if (accent) {
            hexField.removeAttribute('aria-invalid');
            schedulePreview(accent);
        }
    });
    hexField.addEventListener('change', () => {
        const accent = normalize(hexField.value);
        if (!accent) {
            hexField.setAttribute('aria-invalid', 'true');
            setStatus(host.dataset.textInvalid, 'error');
            return;
        }
        hexField.value = accent;
        save(accent);
    });
    hexField.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
            event.preventDefault();
            hexField.dispatchEvent(new Event('change'));
        }
    });

    host.querySelector('[data-accent-reset]').addEventListener('click', () => {
        reflectSelection(brandSeed);
        save(brandSeed);
    });

    // --- theme mode ------------------------------------------------------------------------------
    const applyMode = (mode) => {
        if (window.JularrTheme) window.JularrTheme.apply(mode);
        else root.dataset.appTheme = mode;
    };

    for (const radio of modeForm.querySelectorAll('[data-appearance-mode]')) {
        radio.addEventListener('change', async () => {
            if (!radio.checked) return;
            const previous = root.dataset.appTheme;
            applyMode(radio.value);
            refreshReadout();
            try {
                const response = await fetch(modeForm.action, {
                    method: 'POST',
                    body: new FormData(modeForm),
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                });
                if (!response.ok) throw new Error(`Theme save failed: ${response.status}`);
                setStatus(host.dataset.textSaved, 'ok');
            } catch {
                applyMode(previous);
                modeForm.querySelector(`[value="${previous}"]`).checked = true;
                refreshReadout();
                setStatus(host.dataset.textFailed, 'error');
            }
        });
    }

    // Keep the page's radios in sync when the sidebar theme button is used.
    window.addEventListener('jularr:themechange', (event) => {
        const radio = modeForm.querySelector(`[value="${event.detail.mode}"]`);
        if (radio) radio.checked = true;
        refreshReadout();
    });
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener?.('change', refreshReadout);

    refreshReadout();
})();
