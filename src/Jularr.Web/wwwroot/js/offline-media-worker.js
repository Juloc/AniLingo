/* global self */
(() => {
  "use strict";
  const DB_PREFIX = "jularr-offline-media:";
  const CHUNK_BYTES = 4 * 1024 * 1024;

  function open(profileId) {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_PREFIX + profileId, 1);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error("Offline storage unavailable."));
    });
  }

  function value(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error("Offline storage request failed."));
    });
  }

  async function packageAndResource(profileId, packageId, resourceId) {
    const db = await open(profileId);
    try {
      const tx = db.transaction("packages", "readonly");
      const pkg = await value(tx.objectStore("packages").get(packageId));
      const resource = pkg?.resources?.find(item => item.id === resourceId);
      return { pkg, resource };
    } finally { db.close(); }
  }

  function rangeOf(value, size) {
    if (!value) return { start: 0, end: size - 1, partial: false };
    const match = /^bytes=(\d*)-(\d*)$/.exec(value.trim());
    if (!match) return null;
    let start;
    let end;
    if (!match[1]) {
      const suffix = Number(match[2]);
      if (!Number.isSafeInteger(suffix) || suffix < 1) return null;
      start = Math.max(0, size - suffix); end = size - 1;
    } else {
      start = Number(match[1]); end = match[2] ? Number(match[2]) : size - 1;
      if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start > end || start >= size) return null;
      end = Math.min(end, size - 1);
    }
    return { start, end, partial: true };
  }

  async function bytesForRange(profileId, packageId, resourceId, start, end) {
    const db = await open(profileId);
    try {
      const tx = db.transaction("chunks", "readonly");
      const store = tx.objectStore("chunks");
      const first = Math.floor(start / CHUNK_BYTES);
      const last = Math.floor(end / CHUNK_BYTES);
      const blobs = [];
      for (let index = first; index <= last; index += 1) {
        const item = await value(store.get([packageId, resourceId, index]));
        if (!item?.bytes) throw new Error("A saved media chunk is missing.");
        blobs.push(item.bytes);
      }
      const joined = new Blob(blobs);
      const offset = start - first * CHUNK_BYTES;
      return joined.slice(offset, offset + end - start + 1);
    } finally { db.close(); }
  }

  async function respond(request, url) {
    // /_offline-media/{profile}/{package}/{resource}; each part is encoded by the caller.
    const parts = url.pathname.split("/").filter(Boolean);
    if (parts.length !== 4) return new Response("Not found", { status: 404 });
    const [prefix, profileId, packageId, resourceId] = parts.map(decodeURIComponent);
    if (prefix !== "_offline-media") return new Response("Not found", { status: 404 });
    try {
      const { pkg, resource } = await packageAndResource(profileId, packageId, resourceId);
      if (!pkg || pkg.state !== "ready" || !resource || resource.completedChunks !== Math.ceil(resource.sizeBytes / CHUNK_BYTES)) {
        return new Response("Saved media is incomplete", { status: 404 });
      }
      const range = rangeOf(request.headers.get("Range"), resource.sizeBytes);
      if (!range) return new Response(null, { status: 416, headers: { "Content-Range": `bytes */${resource.sizeBytes}` } });
      const body = await bytesForRange(profileId, packageId, resourceId, range.start, range.end);
      const headers = new Headers({
        "Accept-Ranges": "bytes",
        "Content-Type": resource.mimeType || "application/octet-stream",
        "Content-Length": String(range.end - range.start + 1),
        "ETag": resource.etag || `\"offline-${packageId}-${resourceId}\"`,
        "Cache-Control": "no-store"
      });
      if (range.partial) headers.set("Content-Range", `bytes ${range.start}-${range.end}/${resource.sizeBytes}`);
      return new Response(body, { status: range.partial ? 206 : 200, headers });
    } catch {
      return new Response("Saved media is unavailable", { status: 404 });
    }
  }

  self.JularrOfflineMediaWorker = Object.freeze({ respond });
})();
