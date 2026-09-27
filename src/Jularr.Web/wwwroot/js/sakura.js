// Optional Sakura particle effect (#387). One reusable global script loaded from _Layout.cshtml for
// every page instead of per-page code. Off/Subtle/Full is the per-profile setting rendered into
// <html data-sakura>; Settings/Appearance saves it through /Appearance/Sakura and calls
// window.JularrSakura.apply(mode) to update this same running instance (see appearance-settings.js,
// mirroring how window.JularrTheme is driven from Settings/Appearance).
const SakuraModes = ['off', 'subtle', 'full'];

(() => {
    const root = document.documentElement;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const burstFlagKey = 'jularr:sakuraBurst';
    // The Books, Novels and Manga readers share the reader frame. Petals would drift across the
    // text and the reader panels, so the effect stays paused there (this script is deferred, so
    // the frame is already in the DOM). Leaving a reader still records the navigation burst.
    const inReader = document.querySelector('[data-reader-frame]') !== null;

    // Suggested tuning from #387: ~4-10 visible petals normally, ~30-50 in a burst.
    const AMBIENT_COUNT = { subtle: 6, full: 10 };
    const BURST_COUNT = { subtle: 30, full: 50 };
    const MAX_DPR = 2;

    let mode = SakuraModes.includes(root.dataset.sakura) ? root.dataset.sakura : 'off';
    let canvas = null;
    let ctx = null;
    let rafId = 0;
    let resizeTimer = 0;
    let running = false;
    let width = 0;
    let height = 0;
    let dpr = 1;
    let petals = [];
    let lastTime = 0;

    const rand = (min, max) => min + Math.random() * (max - min);

    const accentRgb = () => {
        const raw = getComputedStyle(root).getPropertyValue('--accent').trim() || '#c8102e';
        const hex = raw.replace('#', '');
        const n = parseInt(hex.length === 3
            ? hex.split('').map((c) => c + c).join('')
            : hex, 16);
        return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
    };

    const resize = () => {
        if (!canvas) return;
        dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
        width = window.innerWidth;
        height = window.innerHeight;
        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const scheduleResize = () => {
        window.clearTimeout(resizeTimer);
        resizeTimer = window.setTimeout(resize, 150);
    };

    const makeAmbientPetal = (rgb, spawnAnywhere) => ({
        x: rand(0, width),
        y: spawnAnywhere ? rand(0, height) : rand(-40, -10),
        size: rand(4, 9),
        baseOpacity: rand(0.15, 0.45),
        rotation: rand(0, Math.PI * 2),
        spin: rand(-0.6, 0.6),
        fallSpeed: rand(14, 30),
        swayAmplitude: rand(10, 28),
        swaySpeed: rand(0.4, 0.9),
        swayPhase: rand(0, Math.PI * 2),
        rgb,
        life: Infinity,
        age: 0
    });

    const makeBurstPetal = (rgb, direction) => ({
        x: rand(-40, width + 40),
        y: rand(-40, height * 0.6),
        size: rand(5, 11),
        baseOpacity: rand(0.35, 0.75),
        rotation: rand(0, Math.PI * 2),
        spin: rand(-2.2, 2.2),
        fallSpeed: rand(70, 160),
        swayAmplitude: rand(30, 70) * direction,
        swaySpeed: rand(0.8, 1.6),
        swayPhase: rand(0, Math.PI * 2),
        rgb,
        life: rand(300, 500),
        age: 0
    });

    const drawPetal = (petal, opacity) => {
        ctx.save();
        ctx.translate(petal.x, petal.y);
        ctx.rotate(petal.rotation);
        ctx.fillStyle = `rgba(${petal.rgb.r}, ${petal.rgb.g}, ${petal.rgb.b}, ${opacity})`;
        ctx.beginPath();
        ctx.ellipse(0, 0, petal.size, petal.size * 0.62, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    };

    const step = (timestamp) => {
        if (!running) return;
        const dt = Math.min(timestamp - (lastTime || timestamp), 50);
        lastTime = timestamp;
        const dtSeconds = dt / 1000;

        ctx.clearRect(0, 0, width, height);

        const next = [];
        for (const petal of petals) {
            petal.age += dt;
            if (petal.life !== Infinity && petal.age >= petal.life) continue;

            petal.rotation += petal.spin * dtSeconds;
            petal.y += petal.fallSpeed * dtSeconds;
            petal.x += Math.sin(petal.swayPhase + petal.age / 1000 * petal.swaySpeed * 6) * petal.swayAmplitude * dtSeconds;

            if (petal.life === Infinity) {
                if (petal.y > height + 20) {
                    Object.assign(petal, makeAmbientPetal(petal.rgb, false));
                }
                if (petal.x < -30) petal.x = width + 20;
                if (petal.x > width + 30) petal.x = -20;
                drawPetal(petal, petal.baseOpacity);
            } else {
                // Bursts fade out over their last ~120ms instead of popping out of existence.
                const fadeIn = Math.min(petal.age / 80, 1);
                const fadeOut = Math.min((petal.life - petal.age) / 120, 1);
                drawPetal(petal, petal.baseOpacity * Math.max(0, Math.min(fadeIn, fadeOut)));
            }

            next.push(petal);
        }
        petals = next;

        rafId = requestAnimationFrame(step);
    };

    const ensureCanvas = () => {
        if (canvas) return;
        canvas = document.createElement('canvas');
        canvas.className = 'sakura-layer';
        canvas.setAttribute('aria-hidden', 'true');
        document.body.appendChild(canvas);
        ctx = canvas.getContext('2d');
        resize();
        window.addEventListener('resize', scheduleResize, { passive: true });
    };

    const seedAmbient = () => {
        const rgb = accentRgb();
        const target = AMBIENT_COUNT[mode] || 0;
        const ambientCount = petals.filter((p) => p.life === Infinity).length;
        for (let i = ambientCount; i < target; i++) {
            petals.push(makeAmbientPetal(rgb, true));
        }
    };

    const start = () => {
        if (running) return;
        ensureCanvas();
        seedAmbient();
        running = true;
        lastTime = 0;
        rafId = requestAnimationFrame(step);
    };

    const stop = () => {
        running = false;
        if (rafId) cancelAnimationFrame(rafId);
        rafId = 0;
        if (canvas) {
            canvas.remove();
            canvas = null;
            ctx = null;
        }
        window.removeEventListener('resize', scheduleResize);
        window.clearTimeout(resizeTimer);
        petals = [];
    };

    const isDisabled = () => mode === 'off' || reducedMotion.matches || inReader;

    const apply = (nextMode) => {
        mode = SakuraModes.includes(nextMode) ? nextMode : 'off';
        root.dataset.sakura = mode;
        if (isDisabled()) {
            stop();
            return;
        }
        if (!running) start();
        else seedAmbient();
    };

    const burst = () => {
        if (isDisabled()) return;
        ensureCanvas();
        if (!running) start();

        const rgb = accentRgb();
        const direction = Math.random() < 0.5 ? -1 : 1;
        const count = BURST_COUNT[mode] || 0;
        for (let i = 0; i < count; i++) {
            petals.push(makeBurstPetal(rgb, direction));
        }
    };

    window.JularrSakura = Object.freeze({ apply, burst });

    reducedMotion.addEventListener?.('change', () => apply(mode));
    window.addEventListener('pagehide', stop);

    // Internal navigation triggers a short, denser burst on the *next* page load. Recording the
    // intent must never delay the click: this only writes a flag and lets the browser navigate.
    document.addEventListener('click', (event) => {
        if (mode === 'off' || event.defaultPrevented || event.button !== 0
            || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
        if (!link || (link.target && link.target !== '_self') || link.hasAttribute('download')) return;

        let url;
        try {
            url = new URL(link.href, window.location.href);
        } catch {
            return;
        }
        if (url.origin !== window.location.origin) return;
        if (url.pathname === window.location.pathname && url.search === window.location.search) return;

        try {
            sessionStorage.setItem(burstFlagKey, '1');
        } catch {
            /* Private browsing or storage disabled: skip the next-page burst, navigation still works. */
        }
    }, true);

    apply(mode);

    try {
        if (sessionStorage.getItem(burstFlagKey)) {
            sessionStorage.removeItem(burstFlagKey);
            burst();
        }
    } catch {
        /* Storage unavailable: ambient effect already started above. */
    }
})();
