(() => {
  "use strict";

  const INSTALL_DISMISS_KEY = "anilingo.pwa.installDismissedUntil";
  const INSTALL_DISMISS_MS = 7 * 24 * 60 * 60 * 1000;
  let deferredInstallPrompt = null;
  let reloadForServiceWorker = false;
  let didReloadForServiceWorker = false;

  const isStandalone = () =>
    window.matchMedia?.("(display-mode: standalone)")?.matches === true
    || window.navigator.standalone === true;

  const isAppleMobile = () => {
    const userAgent = navigator.userAgent || "";
    return /iPad|iPhone|iPod/.test(userAgent)
      || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
  };

  const isMacSafari = () => {
    const userAgent = navigator.userAgent || "";
    return navigator.platform === "MacIntel"
      && navigator.maxTouchPoints <= 1
      && /Safari\//.test(userAgent)
      && !/(Chrome|Chromium|Edg|OPR)\//.test(userAgent);
  };

  const safeLocalStorageGet = key => {
    try {
      return window.localStorage.getItem(key);
    } catch {
      return null;
    }
  };

  const safeLocalStorageSet = (key, value) => {
    try {
      window.localStorage.setItem(key, value);
    } catch {
      // Storage can be unavailable in strict/private browser modes.
    }
  };

  const ensureMeta = (name, content) => {
    let element = document.head.querySelector('meta[name="' + name + '"]');
    if (!element) {
      element = document.createElement("meta");
      element.name = name;
      document.head.appendChild(element);
    }
    element.content = content;
  };

  const ensureLink = (rel, href) => {
    let element = document.head.querySelector('link[rel="' + rel + '"]');
    if (!element) {
      element = document.createElement("link");
      element.rel = rel;
      document.head.appendChild(element);
    }
    element.href = href;
  };

  const configurePlatformMetadata = () => {
    const viewport = document.head.querySelector('meta[name="viewport"]');
    if (viewport && !viewport.content.includes("viewport-fit=cover")) {
      viewport.content = viewport.content.trim().replace(/,+$/, "")
        + ", viewport-fit=cover";
    }

    ensureMeta("apple-mobile-web-app-capable", "yes");
    ensureMeta("apple-mobile-web-app-status-bar-style", "black-translucent");
    ensureMeta("apple-mobile-web-app-title", "AniLingo");
    ensureLink("apple-touch-icon", "/icons/apple-touch-icon.png");

    document.documentElement.dataset.displayMode = isStandalone()
      ? "standalone"
      : "browser";
    document.documentElement.dataset.platform = isAppleMobile()
      ? "apple-mobile"
      : isMacSafari()
        ? "apple-desktop"
        : "standard";
  };

  const noticeHost = () => {
    let host = document.querySelector("[data-pwa-notice-host]");
    if (!host) {
      host = document.createElement("div");
      host.className = "pwa-notice-host";
      host.dataset.pwaNoticeHost = "";
      host.setAttribute("aria-live", "polite");
      document.body.appendChild(host);
    }
    return host;
  };

  const removeNotice = id => {
    document.getElementById(id)?.remove();
  };

  const showNotice = ({
    id,
    message,
    primaryLabel,
    onPrimary,
    secondaryLabel,
    onSecondary
  }) => {
    removeNotice(id);

    const notice = document.createElement("section");
    notice.id = id;
    notice.className = "pwa-notice";

    const text = document.createElement("p");
    text.textContent = message;
    notice.appendChild(text);

    const actions = document.createElement("div");
    actions.className = "pwa-notice-actions";

    if (secondaryLabel) {
      const secondary = document.createElement("button");
      secondary.type = "button";
      secondary.className = "button";
      secondary.textContent = secondaryLabel;
      secondary.addEventListener("click", async () => {
        if (onSecondary) {
          await onSecondary();
        }
        removeNotice(id);
      });
      actions.appendChild(secondary);
    }

    const primary = document.createElement("button");
    primary.type = "button";
    primary.className = "button button-primary";
    primary.textContent = primaryLabel;
    primary.addEventListener("click", async () => {
      primary.disabled = true;
      try {
        await onPrimary?.();
      } finally {
        primary.disabled = false;
      }
    });
    actions.appendChild(primary);

    notice.appendChild(actions);
    noticeHost().appendChild(notice);
    return notice;
  };

  const showToast = message => {
    const toast = document.createElement("div");
    toast.className = "toast pwa-runtime-toast";
    toast.textContent = message;
    document.body.appendChild(toast);
    window.setTimeout(() => toast.remove(), 3500);
  };

  const installDismissed = () => {
    const value = Number(safeLocalStorageGet(INSTALL_DISMISS_KEY));
    return Number.isFinite(value) && value > Date.now();
  };

  const dismissInstall = () => {
    safeLocalStorageSet(
      INSTALL_DISMISS_KEY,
      String(Date.now() + INSTALL_DISMISS_MS));
  };

  const offerBrowserInstall = () => {
    if (!deferredInstallPrompt || isStandalone() || installDismissed()) {
      return;
    }

    showNotice({
      id: "pwa-install-notice",
      message: "Install AniLingo for a standalone app window and faster access.",
      primaryLabel: "Install",
      secondaryLabel: "Not now",
      onSecondary: () => dismissInstall(),
      onPrimary: async () => {
        const prompt = deferredInstallPrompt;
        deferredInstallPrompt = null;
        removeNotice("pwa-install-notice");

        await prompt.prompt();
        const choice = await prompt.userChoice;
        if (choice?.outcome !== "accepted") {
          dismissInstall();
        }
      }
    });
  };

  const offerAppleInstallGuide = () => {
    const appleMobile = isAppleMobile();
    const macSafari = isMacSafari();

    if ((!appleMobile && !macSafari) || isStandalone() || installDismissed()) {
      return;
    }

    window.setTimeout(() => {
      if (isStandalone() || installDismissed()) {
        return;
      }

      showNotice({
        id: "pwa-install-notice",
        message: appleMobile
          ? "Install AniLingo on iPhone/iPad: open Share, then choose Add to Home Screen."
          : "Install AniLingo on Mac: in Safari, choose File, then Add to Dock.",
        primaryLabel: "Got it",
        secondaryLabel: "Not now",
        onSecondary: () => dismissInstall(),
        onPrimary: async () => {
          dismissInstall();
          removeNotice("pwa-install-notice");
        }
      });
    }, 2500);
  };

  const offerServiceWorkerUpdate = worker => {
    if (!worker || !navigator.serviceWorker.controller) {
      return;
    }

    showNotice({
      id: "pwa-update-notice",
      message: "A new AniLingo version is ready.",
      primaryLabel: "Update",
      secondaryLabel: "Later",
      onPrimary: async () => {
        reloadForServiceWorker = true;
        worker.postMessage({ type: "SKIP_WAITING" });
      }
    });
  };

  const registerServiceWorker = async () => {
    if (!("serviceWorker" in navigator)) {
      return;
    }

    try {
      const registration = await navigator.serviceWorker.register(
        "/service-worker.js",
        { scope: "/", updateViaCache: "none" });

      if (registration.waiting) {
        offerServiceWorkerUpdate(registration.waiting);
      }

      registration.addEventListener("updatefound", () => {
        const worker = registration.installing;
        if (!worker) {
          return;
        }

        worker.addEventListener("statechange", () => {
          if (worker.state === "installed" && navigator.serviceWorker.controller) {
            offerServiceWorkerUpdate(worker);
          }
        });
      });

      navigator.serviceWorker.addEventListener("controllerchange", () => {
        if (reloadForServiceWorker && !didReloadForServiceWorker) {
          didReloadForServiceWorker = true;
          window.location.reload();
        }
      });

      void registration.update();
    } catch {
      // PWA support is progressive enhancement and must never block AniLingo.
    }
  };

  const copyText = async value => {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return true;
    }

    const textarea = document.createElement("textarea");
    textarea.value = value;
    textarea.setAttribute("readonly", "");
    textarea.style.position = "fixed";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();
    const copied = document.execCommand?.("copy") === true;
    textarea.remove();
    return copied;
  };

  const shareCurrentPage = async details => {
    const payload = {
      title: details?.title || document.title,
      text: details?.text || "",
      url: details?.url || window.location.href
    };

    if (navigator.share) {
      try {
        await navigator.share(payload);
        return true;
      } catch (error) {
        if (error?.name === "AbortError") {
          return false;
        }
      }
    }

    try {
      if (await copyText(payload.url)) {
        showToast("Link copied.");
        return true;
      }
    } catch {
      // Clipboard fallback is optional.
    }

    return false;
  };

  const effectiveDuration = (root, video) => {
    if (Number.isFinite(video.duration) && video.duration > 0) {
      return video.duration;
    }

    const declared = Number(root.dataset.durationSeconds);
    return Number.isFinite(declared) && declared > 0 ? declared : null;
  };

  const clampPosition = (value, duration) =>
    Math.min(duration, Math.max(0, Number.isFinite(value) ? value : 0));

  const enhanceMediaSession = (root, video) => {
    if (!("mediaSession" in navigator)) {
      return;
    }

    const title = document.querySelector(".page-header h1")?.textContent?.trim()
      || document.title.replace(/\s+-\s+AniLingo$/, "");
    const anime = document.querySelector(".page-header .back-link")?.textContent
      ?.replace(/^\s*←\s*/, "")
      ?.trim()
      || "AniLingo";

    if ("MediaMetadata" in window) {
      navigator.mediaSession.metadata = new MediaMetadata({
        title,
        artist: anime,
        album: "AniLingo",
        artwork: [
          { src: "/icons/anilingo-192.png", sizes: "192x192", type: "image/png" },
          { src: "/icons/anilingo-512.png", sizes: "512x512", type: "image/png" }
        ]
      });
    }

    const setHandler = (action, handler) => {
      try {
        navigator.mediaSession.setActionHandler(action, handler);
      } catch {
        // Browser exposes Media Session but not this individual action.
      }
    };

    const seekBy = delta => {
      const duration = effectiveDuration(root, video);
      const next = video.currentTime + delta;
      video.currentTime = duration
        ? clampPosition(next, duration)
        : Math.max(0, next);
    };

    setHandler("play", () => void video.play().catch(() => {}));
    setHandler("pause", () => video.pause());
    setHandler("seekbackward", details =>
      seekBy(-(details?.seekOffset || 10)));
    setHandler("seekforward", details =>
      seekBy(details?.seekOffset || 10));
    setHandler("seekto", details => {
      const target = Number(details?.seekTime);
      if (!Number.isFinite(target)) {
        return;
      }

      const duration = effectiveDuration(root, video);
      const position = duration ? clampPosition(target, duration) : Math.max(0, target);
      if (details.fastSeek && typeof video.fastSeek === "function") {
        video.fastSeek(position);
      } else {
        video.currentTime = position;
      }
    });

    let lastPositionUpdate = 0;
    const updateState = force => {
      navigator.mediaSession.playbackState = video.paused ? "paused" : "playing";

      const now = performance.now();
      if (!force && now - lastPositionUpdate < 750) {
        return;
      }
      lastPositionUpdate = now;

      const duration = effectiveDuration(root, video);
      if (!duration || !Number.isFinite(video.currentTime)) {
        return;
      }

      try {
        navigator.mediaSession.setPositionState({
          duration,
          playbackRate: Number.isFinite(video.playbackRate) && video.playbackRate > 0
            ? video.playbackRate
            : 1,
          position: clampPosition(video.currentTime, duration)
        });
      } catch {
        // Position state is not available on every Media Session implementation.
      }
    };

    video.addEventListener("play", () => updateState(true));
    video.addEventListener("pause", () => updateState(true));
    video.addEventListener("timeupdate", () => updateState(false));
    video.addEventListener("durationchange", () => updateState(true));
    video.addEventListener("ratechange", () => updateState(true));
    video.addEventListener("loadedmetadata", () => updateState(true));
  };

  const enhanceWakeLock = (video) => {
    if (!navigator.wakeLock?.request) {
      return;
    }

    let lock = null;

    const release = async () => {
      const active = lock;
      lock = null;
      if (active) {
        try {
          await active.release();
        } catch {
          // The browser may already have released the lock.
        }
      }
    };

    const acquire = async () => {
      if (lock || video.paused || video.ended || document.visibilityState !== "visible") {
        return;
      }

      try {
        lock = await navigator.wakeLock.request("screen");
        lock.addEventListener("release", () => {
          lock = null;
        }, { once: true });
      } catch {
        lock = null;
      }
    };

    video.addEventListener("play", () => void acquire());
    video.addEventListener("pause", () => void release());
    video.addEventListener("ended", () => void release());
    window.addEventListener("pagehide", () => void release());
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "visible") {
        void acquire();
      }
    });
  };

  const createPlayerAction = (label, text, handler) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "pwa-player-action";
    button.setAttribute("aria-label", label);
    button.title = label;
    button.textContent = text;
    button.addEventListener("click", handler);
    return button;
  };

  const supportsPictureInPicture = video =>
    (document.pictureInPictureEnabled === true
      && typeof video.requestPictureInPicture === "function")
    || typeof video.webkitSetPresentationMode === "function";

  const togglePictureInPicture = async video => {
    try {
      if (document.pictureInPictureElement) {
        await document.exitPictureInPicture();
        return;
      }

      if (document.pictureInPictureEnabled === true
          && typeof video.requestPictureInPicture === "function") {
        await video.requestPictureInPicture();
        return;
      }

      if (typeof video.webkitSetPresentationMode === "function") {
        const mode = video.webkitPresentationMode === "picture-in-picture"
          ? "inline"
          : "picture-in-picture";
        video.webkitSetPresentationMode(mode);
      }
    } catch {
      showToast("Picture-in-Picture is not available for this video.");
    }
  };

  const supportsFullscreen = (stage, video) =>
    typeof stage?.requestFullscreen === "function"
    || typeof video.webkitEnterFullscreen === "function";

  const enterFullscreen = async (stage, video) => {
    try {
      if (document.fullscreenElement) {
        await document.exitFullscreen();
        return;
      }

      if (typeof stage?.requestFullscreen === "function") {
        await stage.requestFullscreen();
        return;
      }

      if (typeof video.webkitEnterFullscreen === "function") {
        video.webkitEnterFullscreen();
      }
    } catch {
      showToast("Fullscreen is not available here.");
    }
  };

  const enhanceEpisodePlayer = root => {
    if (!(root instanceof HTMLElement) || root.dataset.pwaEnhanced === "true") {
      return;
    }

    const video = root.querySelector("[data-playback-video]");
    const stage = root.querySelector("[data-video-stage]") || video?.parentElement;
    if (!(video instanceof HTMLVideoElement) || !(stage instanceof HTMLElement)) {
      return;
    }

    root.dataset.pwaEnhanced = "true";
    enhanceMediaSession(root, video);
    enhanceWakeLock(video);

    const actions = document.createElement("div");
    actions.className = "pwa-player-actions";
    actions.setAttribute("aria-label", "Player window actions");

    if (supportsPictureInPicture(video)) {
      const pictureInPicture = createPlayerAction(
        "Picture-in-Picture",
        "PiP",
        () => void togglePictureInPicture(video));
      pictureInPicture.disabled = video.readyState < 1;
      video.addEventListener("loadedmetadata", () => {
        pictureInPicture.disabled = false;
      });
      actions.appendChild(pictureInPicture);
    }

    if (supportsFullscreen(stage, video)) {
      actions.appendChild(createPlayerAction(
        "Toggle fullscreen",
        "⛶",
        () => void enterFullscreen(stage, video)));
    }

    if (navigator.share || navigator.clipboard?.writeText) {
      actions.appendChild(createPlayerAction(
        "Share episode",
        "↗",
        () => void shareCurrentPage({
          title: document.querySelector(".page-header h1")?.textContent?.trim()
            || document.title
        })));
    }

    if (actions.childElementCount > 0) {
      stage.appendChild(actions);
    }
  };

  const enhanceExistingPlayers = () => {
    document.querySelectorAll("[data-episode-player]")
      .forEach(enhanceEpisodePlayer);
  };

  window.addEventListener("beforeinstallprompt", event => {
    event.preventDefault();
    deferredInstallPrompt = event;
    offerBrowserInstall();
  });

  window.addEventListener("appinstalled", () => {
    deferredInstallPrompt = null;
    removeNotice("pwa-install-notice");
    safeLocalStorageSet(INSTALL_DISMISS_KEY, "0");
    document.documentElement.dataset.displayMode = "standalone";
  });

  window.addEventListener("offline", () =>
    showToast("Offline. Cached app resources remain available."));
  window.addEventListener("online", () =>
    showToast("Back online."));

  document.addEventListener("submit", event => {
    if (event.target instanceof HTMLFormElement
        && event.target.matches("[data-offline-logout]")) {
      try {
        localStorage.removeItem("anilingo.activeProfile");
      } catch {
        // Ignore unavailable local storage.
      }
    }
  });

  const initialize = () => {
    configurePlatformMetadata();
    enhanceExistingPlayers();
    offerAppleInstallGuide();
    void registerServiceWorker();

    const observer = new MutationObserver(() => enhanceExistingPlayers());
    observer.observe(document.body, { childList: true, subtree: true });
  };

  window.AniLingoPwa = Object.freeze({
    isStandalone,
    shareCurrentPage
  });

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }
})();
