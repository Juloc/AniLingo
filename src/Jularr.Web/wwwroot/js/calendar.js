// Release calendar: tells the server the browser's time zone so days and times are grouped
// locally. The page is rendered in the zone it names in data-calendar-zone; when that differs
// from the browser's zone the cookie is updated and the page reloads once per session.
(() => {
    const page = document.querySelector("[data-calendar-zone]");
    let zone = "";
    try {
        zone = Intl.DateTimeFormat().resolvedOptions().timeZone || "";
    } catch {
        return;
    }

    if (!page || !zone || !/^[A-Za-z0-9/_+-]{1,64}$/.test(zone)) {
        return;
    }

    const cookie = document.cookie.split("; ").find(part => part.startsWith("jularr-tz="));
    const stored = cookie ? decodeURIComponent(cookie.slice("jularr-tz=".length)) : "";
    if (stored !== zone) {
        document.cookie = `jularr-tz=${encodeURIComponent(zone)}; path=/; max-age=31536000; samesite=lax`;
    }

    const reloadKey = "jularr-tz-reloaded";
    if (page.dataset.calendarZone !== zone && stored !== zone && !sessionStorage.getItem(reloadKey)) {
        sessionStorage.setItem(reloadKey, "1");
        location.reload();
    }
})();
