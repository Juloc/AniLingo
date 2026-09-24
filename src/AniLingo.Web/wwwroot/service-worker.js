const CACHE_PREFIX = "anilingo-static-";
const CACHE_VERSION = CACHE_PREFIX + "v3";
const PRECACHE = [
  "/offline.html",
  "/css/site.css",
  "/js/pwa.js",
  "/js/offline-review.js",
  "/icons/anilingo.svg",
  "/icons/anilingo-192.png",
  "/icons/anilingo-512.png",
  "/icons/anilingo-maskable-512.png",
  "/icons/apple-touch-icon.png",
  "/manifest.webmanifest"
];

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
          .filter(key => key.startsWith(CACHE_PREFIX) && key !== CACHE_VERSION)
          .map(key => caches.delete(key))
      ))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("message", event => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
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
      await cache.put(request, response.clone());
    }
    return response;
  } catch {
    return (await cache.match(request))
      || (await cache.match(new URL(request.url).pathname))
      || Response.error();
  }
}
