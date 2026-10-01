// Legacy prefix kept after the Jularr rename so activation still finds and deletes older caches.
const CACHE_PREFIX = "anilingo-static-";
const CACHE_VERSION = CACHE_PREFIX + "v6";
// Keeping the immediately preceding shell lets already-open tabs finish using the
// assets they were rendered with.  The worker deliberately does not claim those
// tabs; an update becomes active only after the user accepts it in pwa.js.
const PREVIOUS_CACHE_VERSION = CACHE_PREFIX + "v5";
const PRECACHE = [
  "/offline.html",
  "/js/offline-media.js",
  "/js/offline-media-storage.js",
  "/js/offline-library.js",
  "/js/offline-library-storage.js",
  "/js/offline-media-worker.js?v=1",
  "/js/offline-media-catalog.js",
  "/brand/jularr-mark.svg",
  "/icons/jularr-192.png",
  "/icons/jularr-512.png",
  "/icons/jularr-maskable-512.png",
  "/icons/apple-touch-icon.png",
  "/manifest.webmanifest"
];

importScripts("/js/offline-media-worker.js?v=1");

self.addEventListener("install", event => {
  event.waitUntil(
    caches.open(CACHE_VERSION)
      .then(cache => cache.addAll(PRECACHE))
  );
});

self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(
        keys
          .filter(key => key.startsWith(CACHE_PREFIX)
            && key !== CACHE_VERSION
            && key !== PREVIOUS_CACHE_VERSION)
          .map(key => caches.delete(key))
      ))
  );
});

self.addEventListener("message", event => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
    return;
  }

  if (event.data?.type === "CACHE_CURRENT_ASSETS") {
    event.waitUntil(cacheCurrentAssets(event.data.urls));
  }
});

self.addEventListener("fetch", event => {
  const request = event.request;

  if (request.method !== "GET") {
    return;
  }

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) {
    return;
  }

  // Private package bytes are never written to CacheStorage.  The module below
  // obtains them from profile-scoped IndexedDB and can answer video/audio Range
  // requests without a network connection.
  if (url.pathname.startsWith("/_offline-media/")) {
    event.respondWith(self.JularrOfflineMediaWorker.respond(request, url));
    return;
  }

  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request).catch(() => caches.match("/offline.html"))
    );
    return;
  }

  if (!isStaticAsset(url.pathname)) {
    return;
  }

  event.respondWith(networkFirstStatic(request));
});

// App shell only. Private library manifests, chapters and assets are intentionally
// never matched here: they live in IndexedDB/OPFS, a separate storage area this
// cache's version bumps and cleanup never touch.
function isStaticAsset(pathname) {
  return pathname === "/manifest.webmanifest"
    || pathname.startsWith("/css/")
    || pathname.startsWith("/js/")
    || pathname.startsWith("/icons/");
}

async function networkFirstStatic(request) {
  const cache = await caches.open(CACHE_VERSION);

  try {
    const response = await fetch(request);
    if (response.ok) {
      await putLatestAsset(cache, request, response.clone());
    }
    return response;
  } catch {
    return (await cache.match(request))
      || (await cache.match(new URL(request.url).pathname))
      || Response.error();
  }
}

async function cacheCurrentAssets(urls) {
  if (!Array.isArray(urls)) {
    return;
  }

  const cache = await caches.open(CACHE_VERSION);
  const uniqueUrls = [...new Set(urls)].slice(0, 32);

  await Promise.all(uniqueUrls.map(async value => {
    try {
      const url = new URL(value, self.location.origin);
      if (url.origin !== self.location.origin
          || !isStaticAsset(url.pathname)
          || !url.searchParams.has("v")) {
        return;
      }

      const request = new Request(url.href, { cache: "reload" });
      const response = await fetch(request);
      if (response.ok) {
        await putLatestAsset(cache, request, response);
      }
    } catch {
      // A failed optional refresh must never break the installed PWA.
    }
  }));
}

async function putLatestAsset(cache, request, response) {
  // Fingerprinted asset URLs are immutable.  Retaining each URL is essential
  // for old HTML/tabs during a staged service-worker update.
  await cache.put(request, response);
}
