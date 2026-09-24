const CACHE_PREFIX = "anilingo-static-";
const CACHE_VERSION = CACHE_PREFIX + "v4";
const PRECACHE = [
  "/offline.html",
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
  const current = new URL(request.url);
  const keys = await cache.keys();

  await Promise.all(keys
    .filter(key => {
      const cached = new URL(key.url);
      return cached.pathname === current.pathname
        && cached.href !== current.href;
    })
    .map(key => cache.delete(key)));

  await cache.put(request, response);
}
