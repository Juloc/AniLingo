(() => {
  "use strict";

  const CHUNK_BYTES = 4 * 1024 * 1024;
  const DEFAULT_LIMIT_BYTES = 10 * 1024 * 1024 * 1024;

  function packageId(kind, id) {
    if (!/^[a-z][a-z0-9-]*$/.test(kind || "") || !id) {
      throw new Error("An offline package requires a media kind and stable id.");
    }
    return `${kind}:${String(id).toLowerCase()}`;
  }

  function parseRange(value, size) {
    if (!value || !value.startsWith("bytes=") || size < 1) return null;
    const match = /^bytes=(\d*)-(\d*)$/.exec(value.trim());
    if (!match) return null;
    const [, startText, endText] = match;
    if (!startText && !endText) return null;
    let start;
    let end;
    if (!startText) {
      const suffix = Number(endText);
      if (!Number.isSafeInteger(suffix) || suffix < 1) return null;
      start = Math.max(0, size - suffix);
      end = size - 1;
    } else {
      start = Number(startText);
      end = endText ? Number(endText) : size - 1;
      if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start > end || start >= size) return null;
      end = Math.min(end, size - 1);
    }
    return { start, end };
  }

  function chunkCount(size) {
    return Math.ceil(Math.max(0, size) / CHUNK_BYTES);
  }

  function isComplete(pkg) {
    return Boolean(pkg)
      && pkg.state === "ready"
      && Number.isSafeInteger(pkg.sizeBytes)
      && pkg.sizeBytes >= 0
      && pkg.completedChunks === chunkCount(pkg.sizeBytes);
  }

  async function canStore({ requestedBytes, storedBytes, limitBytes = DEFAULT_LIMIT_BYTES }) {
    if (!Number.isSafeInteger(requestedBytes) || requestedBytes < 0) {
      return { allowed: false, reason: "invalid-size" };
    }
    if (storedBytes + requestedBytes > limitBytes) {
      return { allowed: false, reason: "profile-limit" };
    }
    if (!navigator.storage?.estimate) return { allowed: true };
    const estimate = await navigator.storage.estimate();
    const available = Math.max(0, (estimate.quota || 0) - (estimate.usage || 0));
    return available >= requestedBytes
      ? { allowed: true }
      : { allowed: false, reason: "browser-quota", availableBytes: available };
  }

  window.JularrOfflineMedia = Object.freeze({
    CHUNK_BYTES,
    DEFAULT_LIMIT_BYTES,
    packageId,
    parseRange,
    chunkCount,
    isComplete,
    canStore
  });
})();
