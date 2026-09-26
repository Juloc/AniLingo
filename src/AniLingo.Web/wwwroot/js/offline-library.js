(() => {
  "use strict";

  /**
   * Offline library engine (#221 part 1): the pure decision logic behind the
   * PWA download manager, kept free of IndexedDB/OPFS/network calls so it can
   * run and be tested the same way in a browser or under Jint (see
   * tests/AniLingo.Tests/OfflineLibraryEngineTests.cs, mirroring how
   * wwwroot/js/tts.js is tested). Storage and network I/O live in
   * offline-library-storage.js; this file only decides *what* to do.
   *
   * A book is "available offline" only once every selected chapter's stored
   * hash matches its manifest hash (see isBookComplete) — an interrupted or
   * partially failed download must never be presented as complete.
   */

  const MAX_BACKOFF_ATTEMPTS = 10;

  /**
   * Compares a manifest's chapter refs (`{chapterId, hash}`, in reading order)
   * against the chapters currently verified on the device. Differential:
   * unchanged chapters are neither fetched nor removed.
   */
  const diffManifest = (remoteChapters, storedChapters) => {
    const remote = Array.isArray(remoteChapters) ? remoteChapters : [];
    const stored = Array.isArray(storedChapters) ? storedChapters : [];
    const storedByChapter = new Map(stored.map((c) => [c.chapterId, c.hash]));
    const remoteIds = new Set(remote.map((c) => c.chapterId));

    const toFetch = [];
    const unchanged = [];
    for (const chapter of remote) {
      if (storedByChapter.get(chapter.chapterId) === chapter.hash) {
        unchanged.push(chapter.chapterId);
      } else {
        toFetch.push(chapter.chapterId);
      }
    }

    const toRemove = stored
      .filter((c) => !remoteIds.has(c.chapterId))
      .map((c) => c.chapterId);

    return { toFetch, toRemove, unchanged };
  };

  /**
   * A book is complete only when every chapter the manifest lists (or, for a
   * partial/selected download, every chapter the caller selected) has a
   * verified local hash equal to the manifest hash. `verified` maps
   * chapterId -> verified hash.
   */
  const isBookComplete = (chapterRefs, verified) => {
    const refs = Array.isArray(chapterRefs) ? chapterRefs : [];
    if (refs.length === 0) return false;

    const map = verified instanceof Map
      ? verified
      : new Map(Object.entries(verified || {}));

    return refs.every((chapter) => map.get(chapter.chapterId) === chapter.hash);
  };

  /** Bounded exponential backoff for a failed chapter/asset transfer. */
  const computeBackoffMs = (attempt, options = {}) => {
    const baseMs = Number(options.baseMs) > 0 ? Number(options.baseMs) : 1000;
    const maxMs = Number(options.maxMs) > 0 ? Number(options.maxMs) : 30000;
    const bounded = Math.min(Math.max(0, Math.trunc(attempt) || 0), MAX_BACKOFF_ATTEMPTS);
    return Math.min(maxMs, baseMs * 2 ** bounded);
  };

  /**
   * Pure queue-item transition. Items move
   * queued -> downloading -> verified
   * with paused/failed/cancelled side branches. Unknown transitions
   * (for example resuming an item that is not paused) are no-ops so callers
   * can dispatch events without checking current state first.
   */
  const QUEUE_STATES = Object.freeze({
    QUEUED: "queued",
    DOWNLOADING: "downloading",
    VERIFIED: "verified",
    PAUSED: "paused",
    FAILED: "failed",
    CANCELLED: "cancelled"
  });

  const transitionQueueItem = (item, event) => {
    const state = item?.state;
    const attempts = Number(item?.attempts) || 0;

    switch (event?.type) {
      case "start":
        return state === QUEUE_STATES.QUEUED
          ? { ...item, state: QUEUE_STATES.DOWNLOADING }
          : item;

      case "success":
        return state === QUEUE_STATES.DOWNLOADING
          ? { ...item, state: QUEUE_STATES.VERIFIED, attempts: 0 }
          : item;

      case "failure":
        return state === QUEUE_STATES.DOWNLOADING
          ? { ...item, state: QUEUE_STATES.FAILED, attempts: attempts + 1 }
          : item;

      case "pause":
        return state === QUEUE_STATES.QUEUED || state === QUEUE_STATES.DOWNLOADING
          ? { ...item, state: QUEUE_STATES.PAUSED }
          : item;

      case "resume":
        return state === QUEUE_STATES.PAUSED
          ? { ...item, state: QUEUE_STATES.QUEUED }
          : item;

      case "retry":
        return state === QUEUE_STATES.FAILED
          ? { ...item, state: QUEUE_STATES.QUEUED }
          : item;

      case "cancel":
        return state === QUEUE_STATES.VERIFIED
          ? item
          : { ...item, state: QUEUE_STATES.CANCELLED };

      default:
        return item;
    }
  };

  /**
   * Picks the next queue item to transfer: the earliest-queued item that is
   * not paused/cancelled/verified and, if it previously failed, whose backoff
   * has elapsed. Returns null when nothing is eligible right now (either the
   * queue is empty of eligible work, or only items still in backoff remain,
   * in which case `retryAtMs` on the item indicates when to look again).
   */
  const nextEligibleItem = (queue, nowMs) => {
    const items = Array.isArray(queue) ? queue : [];
    let earliestRetryAtMs = null;

    for (const item of items) {
      if (item.state !== QUEUE_STATES.QUEUED) continue;

      const retryAtMs = Number(item.retryAtMs) || 0;
      if (retryAtMs > nowMs) {
        earliestRetryAtMs = earliestRetryAtMs === null
          ? retryAtMs
          : Math.min(earliestRetryAtMs, retryAtMs);
        continue;
      }

      return { item, nextEligibleAtMs: null };
    }

    return { item: null, nextEligibleAtMs: earliestRetryAtMs };
  };

  /**
   * Network Information API is Chromium-only and explicitly best-effort: when
   * it is unavailable, a Wi-Fi-only preference cannot be enforced and the
   * download proceeds rather than silently never starting.
   */
  const isNetworkEligible = (connection, wifiOnly) => {
    if (!wifiOnly) return true;
    if (!connection || typeof connection !== "object") return true;

    if (typeof connection.type === "string" && connection.type.length > 0) {
      return connection.type === "wifi" || connection.type === "ethernet";
    }

    if (connection.saveData === true) return false;

    if (typeof connection.effectiveType === "string") {
      return !["slow-2g", "2g", "3g"].includes(connection.effectiveType);
    }

    return true;
  };

  /** Namespaces a storage key by account id, so switching accounts never mixes state. */
  const namespaceKey = (accountId, key) => {
    const account = String(accountId || "").trim() || "anonymous";
    return `${account}:${key}`;
  };

  const STORAGE_UNITS = ["B", "KB", "MB", "GB", "TB"];

  /** Human-readable storage size for the Settings → Offline usage display. */
  const formatBytes = (bytes) => {
    const value = Number(bytes);
    if (!Number.isFinite(value) || value <= 0) return "0 B";

    let size = value;
    let unitIndex = 0;
    while (size >= 1024 && unitIndex < STORAGE_UNITS.length - 1) {
      size /= 1024;
      unitIndex++;
    }

    // One decimal below 10 units, none at/above (matches "2.5 MB"/"150 KB");
    // dividing back through Math.round drops a redundant ".0" (1 KB, not 1.0 KB).
    const text = unitIndex === 0 || size >= 10
      ? String(Math.round(size))
      : String(Math.round(size * 10) / 10);

    return `${text} ${STORAGE_UNITS[unitIndex]}`;
  };

  /** Total downloaded bytes across every book's verified chapters/assets. */
  const totalStorageBytes = (books) =>
    (Array.isArray(books) ? books : []).reduce((sum, book) => {
      const chapters = Array.isArray(book?.chapters) ? book.chapters : [];
      return sum + chapters.reduce((chapterSum, chapter) => chapterSum + (Number(chapter.sizeBytes) || 0), 0);
    }, 0);

  window.AniLingoOfflineLibrary = Object.freeze({
    QUEUE_STATES,
    diffManifest,
    isBookComplete,
    computeBackoffMs,
    transitionQueueItem,
    nextEligibleItem,
    isNetworkEligible,
    namespaceKey,
    formatBytes,
    totalStorageBytes
  });
})();
