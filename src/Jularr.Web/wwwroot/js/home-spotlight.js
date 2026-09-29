// Home spotlight banner: the slides are a plain CSS scroll-snap track. This only keeps the dot
// indicators in sync and scrolls the track horizontally (a bare #anchor would also jump the page).
(() => {
    for (const banner of document.querySelectorAll('[data-home-spotlight]')) {
        const track = banner.querySelector('[data-home-spotlight-track]');
        const dots = Array.from(banner.querySelectorAll('[data-home-spotlight-dot]'));
        if (!track || dots.length === 0) continue;

        const slides = Array.from(track.children);
        const mark = (active) => {
            dots.forEach((dot, index) => {
                if (index === active) dot.setAttribute('aria-current', 'true');
                else dot.removeAttribute('aria-current');
            });
        };

        dots.forEach((dot, index) => {
            dot.addEventListener('click', (event) => {
                event.preventDefault();
                track.scrollTo({ left: slides[index].offsetLeft, behavior: 'smooth' });
                mark(index);
            });
        });

        const observer = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) mark(slides.indexOf(entry.target));
            }
        }, { root: track, threshold: 0.6 });
        slides.forEach((slide) => observer.observe(slide));
    }
})();
