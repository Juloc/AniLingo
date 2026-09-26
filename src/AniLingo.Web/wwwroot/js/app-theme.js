const AppThemeModes = ['system', 'light', 'dark'];

(() => {
    const root = document.documentElement;
    const themeColor = document.querySelector('meta[data-app-theme-color]');
    const forms = Array.from(document.querySelectorAll('[data-theme-form]'));

    const updateThemeColor = () => {
        if (!themeColor) return;
        const value = getComputedStyle(root)
            .getPropertyValue('--browser-theme-color')
            .trim();
        if (value) themeColor.setAttribute('content', value);
    };

    const updateControl = (form, mode) => {
        const button = form.querySelector('[data-theme-cycle]');
        const input = form.querySelector('[data-theme-input]');
        if (!button || !input) return;

        const label = button.dataset[`label${mode[0].toUpperCase()}${mode.slice(1)}`] || mode;
        input.value = mode;
        button.dataset.themeMode = mode;

        const current = button.querySelector('[data-theme-current]');
        if (current) current.textContent = label;

        const ariaTemplate = button.dataset.ariaTemplate || 'Theme: {mode}';
        button.setAttribute('aria-label', ariaTemplate.replace('{mode}', label));
    };

    const apply = (mode) => {
        root.dataset.appTheme = mode;
        for (const form of forms) updateControl(form, mode);
        updateThemeColor();
        window.dispatchEvent(new CustomEvent('jularr:themechange', { detail: { mode } }));
    };

    // Settings → Appearance switches the mode through the same path as the sidebar control.
    window.JularrTheme = Object.freeze({ apply });

    for (const form of forms) {
        const button = form.querySelector('[data-theme-cycle]');
        if (!button) continue;

        button.addEventListener('click', async () => {
            const current = AppThemeModes.includes(root.dataset.appTheme)
                ? root.dataset.appTheme
                : 'system';
            const next = AppThemeModes[(AppThemeModes.indexOf(current) + 1) % AppThemeModes.length];

            apply(next);
            button.disabled = true;

            try {
                const response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                });

                if (!response.ok) throw new Error(`Theme save failed: ${response.status}`);
                const payload = await response.json();
                apply(AppThemeModes.includes(payload.theme) ? payload.theme : next);
            } catch {
                apply(current);
            } finally {
                button.disabled = false;
            }
        });
    }

    const systemTheme = window.matchMedia('(prefers-color-scheme: dark)');
    systemTheme.addEventListener?.('change', () => {
        if (root.dataset.appTheme === 'system') updateThemeColor();
    });

    updateThemeColor();
})();
