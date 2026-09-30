// Home hero carousel and row scrolling (docs/mockups/home).
//
// Hero: the track is positioned with a transform. It can be dragged with the mouse or swiped (pointer
// events, following the pointer 1:1), and is also driven by arrows, segment buttons and ←/→ keys.
// One progress segment per slide fills over the hero's data-interval-ms; at 100 % the hero advances.
// The fill is driven by elapsed time, so hover, keyboard focus, a pressed pointer, the pause button
// and a hidden tab freeze it exactly where it is and it resumes from the same point. Any manual
// navigation restarts the fill for the slide it shows. prefers-reduced-motion: no autoplay, no
// animated fill and no slide animation.
(() => {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const DRAG_START_PX = 6;          // movement before a press becomes a drag (a smaller one stays a click)
    const SNAP_RATIO = 0.2;           // a drag past 20 % of the width changes the slide
    const FLICK_PX_PER_MS = 0.45;     // ... and so does a fast flick
    const EDGE_RESISTANCE = 0.35;     // rubber band before the first and after the last slide
    const CLICK_GUARD_MS = 400;       // a click right after a drag is swallowed

    for (const hero of document.querySelectorAll('[data-home-hero]')) {
        const track = hero.querySelector('[data-home-hero-track]');
        const slides = Array.from(hero.querySelectorAll('[data-home-hero-slide]'));
        const segments = Array.from(hero.querySelectorAll('[data-home-hero-segment]'));
        const rotation = hero.querySelector('[data-home-hero-rotation]');
        if (!track || slides.length < 2) continue;

        const count = slides.length;
        let index = 0;

        // Autoplay state. elapsed only grows while the hero may rotate.
        let elapsed = 0;
        let lastFrame = null;
        let frame = null;
        let hovered = false;
        let focused = false;
        let pressed = false;
        let stoppedByUser = false;

        // The duration is read on every frame, so it is the single source of truth for the fill.
        const intervalMs = () => {
            const value = Number(hero.dataset.intervalMs);
            return Number.isFinite(value) && value > 0 ? value : 0;
        };
        const mayRotate = () => intervalMs() > 0 && !reducedMotion.matches && !stoppedByUser;
        const rotating = () => mayRotate() && !hovered && !focused && !pressed && !document.hidden;

        const place = (offset = 0, animate = true) => {
            hero.classList.toggle('is-instant', !animate || reducedMotion.matches);
            track.style.transform = `translate3d(${-index * hero.clientWidth + offset}px, 0, 0)`;
        };

        const renderSegments = () => {
            const progress = intervalMs() > 0 ? Math.min(elapsed / intervalMs(), 1) : 0;
            segments.forEach((segment, i) => {
                // Reduced motion: static segments, only the active one highlighted.
                const fill = reducedMotion.matches
                    ? (i === index ? 1 : 0)
                    : i < index ? 1 : i > index ? 0 : progress;
                segment.style.setProperty('--fill', fill.toFixed(4));
                if (i === index) segment.setAttribute('aria-current', 'true');
                else segment.removeAttribute('aria-current');
            });
        };

        const syncChrome = () => {
            // While the hero rotates on its own, slide changes are not announced (WAI-ARIA carousel).
            track.setAttribute('aria-live', rotating() ? 'off' : 'polite');
            if (rotation) {
                rotation.hidden = reducedMotion.matches || intervalMs() <= 0;
                rotation.classList.toggle('is-paused', stoppedByUser);
                rotation.setAttribute('aria-label', stoppedByUser ? rotation.dataset.labelPlay : rotation.dataset.labelPause);
            }
            renderSegments();
        };

        const tick = (now) => {
            frame = null;
            if (!rotating()) {
                lastFrame = null;
                syncChrome();
                return;
            }

            if (lastFrame !== null) elapsed += now - lastFrame;
            lastFrame = now;
            if (elapsed >= intervalMs()) {
                elapsed = 0;
                show(index + 1);
            }

            renderSegments();
            frame = window.requestAnimationFrame(tick);
        };

        const sync = () => {
            syncChrome();
            if (rotating()) {
                if (frame === null) {
                    lastFrame = null;   // the paused time is never counted
                    frame = window.requestAnimationFrame(tick);
                }
            } else if (frame !== null) {
                window.cancelAnimationFrame(frame);
                frame = null;
                lastFrame = null;
            }
        };

        // Arrows, segments and keys wrap around; the automatic advance wraps too.
        const show = (target) => {
            index = (target + count) % count;
            // Off-screen slides leave the tab order, so keyboard users stay on the visible slide.
            slides.forEach((slide, i) => { slide.inert = i !== index; });
            place(0, true);
        };

        const navigate = (target) => {
            show(target);
            elapsed = 0;
            sync();
        };

        hero.querySelector('[data-home-hero-prev]')?.addEventListener('click', () => navigate(index - 1));
        hero.querySelector('[data-home-hero-next]')?.addEventListener('click', () => navigate(index + 1));
        segments.forEach((segment, i) => segment.addEventListener('click', () => navigate(i)));
        rotation?.addEventListener('click', () => { stoppedByUser = !stoppedByUser; sync(); });

        hero.addEventListener('pointerenter', (event) => {
            if (event.pointerType === 'mouse') { hovered = true; sync(); }
        });
        hero.addEventListener('pointerleave', (event) => {
            if (event.pointerType === 'mouse') { hovered = false; sync(); }
        });
        // Keyboard focus pauses; a mouse click on a control does not keep the hero frozen.
        hero.addEventListener('focusin', (event) => {
            if (event.target instanceof Element && event.target.matches(':focus-visible')) { focused = true; sync(); }
        });
        hero.addEventListener('focusout', (event) => {
            if (!hero.contains(event.relatedTarget)) { focused = false; sync(); }
        });
        hero.addEventListener('keydown', (event) => {
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
            event.preventDefault();
            // Focus inside a slide moves with it; the slide it leaves becomes inert.
            const inSlide = slides.some((slide) => slide.contains(document.activeElement));
            navigate(index + (event.key === 'ArrowLeft' ? -1 : 1));
            if (inSlide) slides[index].querySelector('a')?.focus({ preventScroll: true });
        });

        // Dragging and swiping. touch-action: pan-y (home.css) leaves vertical page scrolling to the
        // browser; a vertical touch gesture arrives here as pointercancel.
        let drag = null;
        let clickGuardUntil = 0;

        const release = (event, cancelled) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { active, dx, velocity } = drag;
            drag = null;
            pressed = false;
            hero.classList.remove('is-dragging');
            if (!active) {
                sync();
                return;
            }

            if (track.hasPointerCapture?.(event.pointerId)) track.releasePointerCapture(event.pointerId);
            clickGuardUntil = performance.now() + CLICK_GUARD_MS;
            const passed = Math.abs(dx) > hero.clientWidth * SNAP_RATIO || Math.abs(velocity) > FLICK_PX_PER_MS;
            const target = !cancelled && passed ? index + (dx < 0 ? 1 : -1) : index;
            if (target >= 0 && target < count && target !== index) {
                navigate(target);
            } else {
                place(0, true);   // snap back (rubber band)
                sync();
            }
        };

        track.addEventListener('pointerdown', (event) => {
            if (event.pointerType === 'mouse' && event.button !== 0) return;
            drag = {
                id: event.pointerId,
                x: event.clientX,
                y: event.clientY,
                dx: 0,
                lastX: event.clientX,
                lastTime: event.timeStamp,
                velocity: 0,
                active: false
            };
            pressed = true;
            sync();
        });

        track.addEventListener('pointermove', (event) => {
            if (!drag || event.pointerId !== drag.id) return;
            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.active) {
                if (Math.abs(dy) > DRAG_START_PX && Math.abs(dy) > Math.abs(dx)) {
                    // A vertical gesture is not a slide drag.
                    drag = null;
                    pressed = false;
                    sync();
                    return;
                }
                if (Math.abs(dx) < DRAG_START_PX) return;
                drag.active = true;
                try {
                    track.setPointerCapture(event.pointerId);
                } catch {
                    // The pointer is no longer active; the drag still follows its events.
                }
                hero.classList.add('is-dragging');
            }

            event.preventDefault();
            const dt = event.timeStamp - drag.lastTime;
            if (dt > 0) drag.velocity = (event.clientX - drag.lastX) / dt;
            drag.lastX = event.clientX;
            drag.lastTime = event.timeStamp;
            drag.dx = dx;
            const pastEdge = (index === 0 && dx > 0) || (index === count - 1 && dx < 0);
            place(pastEdge ? dx * EDGE_RESISTANCE : dx, false);
        });

        track.addEventListener('pointerup', (event) => release(event, false));
        track.addEventListener('pointercancel', (event) => release(event, true));
        // A drag never follows the link or presses the button it started on; a plain click still does.
        track.addEventListener('click', (event) => {
            if (performance.now() < clickGuardUntil) {
                event.preventDefault();
                event.stopPropagation();
            }
        }, true);
        track.addEventListener('dragstart', (event) => event.preventDefault());

        reducedMotion.addEventListener?.('change', () => { place(0, false); sync(); });
        document.addEventListener('visibilitychange', sync);
        window.addEventListener('resize', () => place(0, false));

        show(0);
        place(0, false);
        sync();
    }

    for (const row of document.querySelectorAll('[data-home-row]')) {
        const track = row.querySelector('[data-home-row-track]');
        const previous = row.querySelector('[data-home-row-prev]');
        const next = row.querySelector('[data-home-row-next]');
        if (!track) continue;

        const behavior = () => (reducedMotion.matches ? 'auto' : 'smooth');
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
