// Home hero carousel and row scrolling (docs/mockups/home). The slides are a plain CSS scroll-snap
// track, so touch swipe works without script; this adds arrows, dots, keyboard control and a slow
// auto-advance that pauses on hover, focus and touch and never runs with prefers-reduced-motion.
(() => {
    const INTERVAL_MS = 8000;
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const behavior = () => (reducedMotion.matches ? 'auto' : 'smooth');

    for (const hero of document.querySelectorAll('[data-home-hero]')) {
        const track = hero.querySelector('[data-home-hero-track]');
        const slides = Array.from(hero.querySelectorAll('[data-home-hero-slide]'));
        const dots = Array.from(hero.querySelectorAll('[data-home-hero-dot]'));
        const rotation = hero.querySelector('[data-home-hero-rotation]');
        if (!track || slides.length < 2) continue;

        let index = 0;
        let timer = null;
        let hovered = false;
        let focused = false;
        let touched = false;
        let stoppedByUser = false;

        const mark = (active) => {
            index = active;
            dots.forEach((dot, i) => {
                if (i === active) dot.setAttribute('aria-current', 'true');
                else dot.removeAttribute('aria-current');
            });
            // Off-screen slides leave the tab order, so keyboard users stay on the visible slide.
            slides.forEach((slide, i) => { slide.inert = i !== active; });
        };

        const go = (target) => {
            const next = (target + slides.length) % slides.length;
            track.scrollTo({ left: slides[next].offsetLeft, behavior: behavior() });
            mark(next);
        };

        const canRotate = () =>
            !reducedMotion.matches && !hovered && !focused && !touched && !stoppedByUser && !document.hidden;

        const sync = () => {
            if (canRotate()) {
                if (timer === null) timer = window.setInterval(() => go(index + 1), INTERVAL_MS);
            } else if (timer !== null) {
                window.clearInterval(timer);
                timer = null;
            }

            // While the hero rotates on its own, slide changes are not announced (WAI-ARIA carousel).
            track.setAttribute('aria-live', timer === null ? 'polite' : 'off');
            if (rotation) {
                const playing = !reducedMotion.matches && !stoppedByUser;
                rotation.hidden = reducedMotion.matches;
                rotation.classList.toggle('is-paused', !playing);
                rotation.setAttribute('aria-label', playing ? rotation.dataset.labelPause : rotation.dataset.labelPlay);
            }
        };

        // A manual step restarts the interval so the next automatic step is a full period away.
        const step = (target) => {
            go(target);
            if (timer !== null) {
                window.clearInterval(timer);
                timer = null;
            }
            sync();
        };

        hero.querySelector('[data-home-hero-prev]')?.addEventListener('click', () => step(index - 1));
        hero.querySelector('[data-home-hero-next]')?.addEventListener('click', () => step(index + 1));
        dots.forEach((dot, i) => dot.addEventListener('click', () => step(i)));
        rotation?.addEventListener('click', () => { stoppedByUser = !stoppedByUser; sync(); });

        hero.addEventListener('mouseenter', () => { hovered = true; sync(); });
        hero.addEventListener('mouseleave', () => { hovered = false; sync(); });
        hero.addEventListener('focusin', () => { focused = true; sync(); });
        hero.addEventListener('focusout', (event) => {
            if (!hero.contains(event.relatedTarget)) { focused = false; sync(); }
        });
        // Touch has no "leave", so a touched hero stays with the slide the user chose.
        hero.addEventListener('touchstart', () => { touched = true; sync(); }, { passive: true });
        hero.addEventListener('keydown', (event) => {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
            event.preventDefault();
            // Focus inside a slide moves with it; the slide it leaves becomes inert.
            const inSlide = slides.some((slide) => slide.contains(document.activeElement));
            step(index + (event.key === 'ArrowLeft' ? -1 : 1));
            if (inSlide) slides[index].querySelector('a')?.focus({ preventScroll: true });
        });

        reducedMotion.addEventListener?.('change', sync);
        document.addEventListener('visibilitychange', sync);

        // Swipes and trackpad scrolling change the slide without the buttons.
        const observer = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) mark(slides.indexOf(entry.target));
            }
        }, { root: track, threshold: 0.6 });
        slides.forEach((slide) => observer.observe(slide));

        mark(0);
        sync();
    }

    for (const row of document.querySelectorAll('[data-home-row]')) {
        const track = row.querySelector('[data-home-row-track]');
        const previous = row.querySelector('[data-home-row-prev]');
        const next = row.querySelector('[data-home-row-next]');
        if (!track) continue;

        const update = () => {
            const max = track.scrollWidth - track.clientWidth;
            if (previous) previous.hidden = track.scrollLeft <= 4;
            if (next) next.hidden = track.scrollLeft >= max - 4;
        };
        const page = (direction) =>
            track.scrollBy({ left: direction * Math.max(track.clientWidth * 0.85, 200), behavior: behavior() });

        previous?.addEventListener('click', () => page(-1));
        next?.addEventListener('click', () => page(1));
        track.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update);
        update();
    }
})();
