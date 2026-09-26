(() => {
  "use strict";

  /**
   * Download manager (#221 part 1): wires the pure engine
   * (window.AniLingoOfflineLibrary) to the storage adapter
   * (window.AniLingoOfflineLibraryStorage) and the server contract
   * (Features/OfflineLibrary + ClientApiOfflineLibrary*). Not unit tested
   * directly (it is all IndexedDB/OPFS/network I/O); the decisions it makes
   * (what to fetch, when an item is eligible, when a book is complete) are
   * delegated to the tested pure functions.
   *
   * A book only becomes "available" after every selected chapter is
   * downloaded AND its server-computed hash matches the manifest
   * (isBookComplete) AND the local OPFS/IndexedDB copy reads back identical to
   * what was written (corruption/incomplete-write guard). Nothing marks a
   * book available before that.
   */

  const engine = window.AniLingoOfflineLibrary;
  const BASE_PATH = "/api/client/v1/offline-library";

  const jsonFetch = async (url, options) => {
    const response = await fetch(url, {
      credentials: "same-origin",
      headers: { Accept: "application/json", ...(options?.headers || {}) },
      ...options
    });

    if (!response.ok) {
      const error = new Error(`Request to ${url} failed with ${response.status}.`);
      error.status = response.status;
      throw error;
    }

    return response.json();
  };

  const connectionInfo = () =>
    navigator.connection || navigator.mozConnection || navigator.webkitConnection || null;

  const queueId = (workId, kind, refId) => `${workId}:${kind}:${refId}`;

  /** Creates a manager bound to the signed-in profile's offline store. Throws if signed out. */
  const createManager = async () => {
    const store = await window.AniLingoOfflineLibraryStorage.openStore();
    const listeners = new Set();
    const notify = () => listeners.forEach((listener) => {
      try { listener(); } catch { /* listener errors must not break the manager */ }
    });

    const getWifiOnly = () => store.getSetting("wifiOnly", true);
    const setWifiOnly = (value) => store.putSetting("wifiOnly", Boolean(value));

    /** Fetches the manifest, diffs it against what is stored, and queues the difference. */
    const enqueueBook = async (workId, { chapterIds = null } = {}) => {
      const manifest = await jsonFetch(`${BASE_PATH}/works/${workId}/manifest`);
      const existing = await store.getManifest(workId);
      const verified = await store.listVerified(workId);

      const selectedChapters = chapterIds
        ? manifest.chapters.filter((c) => chapterIds.includes(c.chapterId))
        : manifest.chapters;

      const diff = engine.diffManifest(selectedChapters, verified.map((v) => ({ chapterId: v.chapterId, hash: v.hash })));

      await store.putManifest({
        workId,
        manifest,
        selectedChapterIds: selectedChapters.map((c) => c.chapterId),
        status: diff.toFetch.length === 0 && diff.toRemove.length === 0 && existing?.status === "available"
          ? "available"
          : "downloading",
        updatedAt: Date.now()
      });

      await Promise.all(diff.toRemove.map(async (chapterId) => {
        await store.deleteQueueItem(queueId(workId, "chapter", chapterId));
        await store.deleteVerified(`${workId}:${chapterId}`);
        await store.deleteChapterPayload(workId, chapterId);
      }));

      await Promise.all(diff.toFetch.map((chapterId) => store.putQueueItem({
        id: queueId(workId, "chapter", chapterId),
        workId,
        kind: "chapter",
        refId: chapterId,
        state: engine.QUEUE_STATES.QUEUED,
        attempts: 0,
        retryAtMs: 0
      })));

      notify();
      return diff;
    };

    const transferChapter = async (workId, chapterId) => {
      const payload = await jsonFetch(`${BASE_PATH}/chapters/${chapterId}`);
      const manifestRecord = await store.getManifest(workId);
      const ref = manifestRecord?.manifest?.chapters?.find((c) => c.chapterId === chapterId);

      if (!ref || payload.hash !== ref.hash) {
        throw new Error("Downloaded chapter did not match the current manifest version.");
      }

      await store.saveChapterPayload(workId, chapterId, payload);

      // Corruption/incomplete-write guard: read back what was just written and
      // compare before ever counting the chapter as verified.
      const roundTrip = await store.loadChapterPayload(workId, chapterId);
      if (!roundTrip || roundTrip.hash !== ref.hash) {
        throw new Error("Local offline copy did not verify after writing; will retry.");
      }

      const sizeBytes = new TextEncoder().encode(JSON.stringify(payload)).length;
      await store.putVerified({
        id: `${workId}:${chapterId}`,
        workId,
        chapterId,
        hash: ref.hash,
        sizeBytes
      });

      // Referenced images are best-effort: a missing illustration must not
      // block the chapter (and therefore the book) from becoming available.
      for (const block of payload.blocks || []) {
        if (!block.imageAssetUrl) continue;
        try {
          const match = /\/offline-library\/assets\/([0-9a-fA-F-]+)\/([^/?#]+)/.exec(block.imageAssetUrl);
          if (!match) continue;
          const [, volumeId, assetName] = match;
          const existing = await store.loadAsset(workId, volumeId, decodeURIComponent(assetName));
          if (existing) continue;

          const response = await fetch(block.imageAssetUrl, { credentials: "same-origin" });
          if (response.ok) {
            await store.saveAsset(workId, volumeId, decodeURIComponent(assetName), await response.arrayBuffer());
          }
        } catch {
          // Best-effort: text is already usable offline without the image.
        }
      }
    };

    const finalizeIfComplete = async (workId) => {
      const record = await store.getManifest(workId);
      if (!record) return;

      const verified = await store.listVerified(workId);
      const verifiedMap = new Map(verified.map((v) => [v.chapterId, v.hash]));
      const selected = record.manifest.chapters.filter((c) => record.selectedChapterIds.includes(c.chapterId));

      if (engine.isBookComplete(selected, verifiedMap)) {
        await store.putManifest({ ...record, status: "available", updatedAt: Date.now() });
        notify();
      }
    };

    /** Processes one eligible queue item, if any. Returns false when nothing could run right now. */
    const processOne = async (workId) => {
      const items = await store.listQueue(workId);
      const wifiOnly = await getWifiOnly();
      if (!engine.isNetworkEligible(connectionInfo(), wifiOnly)) {
        return false;
      }

      const { item } = engine.nextEligibleItem(items, Date.now());
      if (!item) return false;

      await store.putQueueItem(engine.transitionQueueItem(item, { type: "start" }));

      try {
        if (item.kind === "chapter") {
          await transferChapter(workId, item.refId);
        }

        await store.deleteQueueItem(item.id);
        await finalizeIfComplete(workId);
      } catch {
        const failed = engine.transitionQueueItem(item, { type: "failure" });
        await store.putQueueItem({
          ...failed,
          retryAtMs: Date.now() + engine.computeBackoffMs(failed.attempts)
        });
      }

      notify();
      return true;
    };

    /** Drains the queue for one book until nothing more is eligible right now. */
    const processQueue = async (workId) => {
      // eslint-disable-next-line no-empty
      while (await processOne(workId)) { /* keep draining */ }
    };

    const pause = async (workId) => {
      const items = await store.listQueue(workId);
      await Promise.all(items.map((item) =>
        store.putQueueItem(engine.transitionQueueItem(item, { type: "pause" }))));
      notify();
    };

    const resume = async (workId) => {
      const items = await store.listQueue(workId);
      await Promise.all(items.map((item) =>
        store.putQueueItem(engine.transitionQueueItem(item, { type: "resume" }))));
      await processQueue(workId);
    };

    const retryFailed = async (workId) => {
      const items = await store.listQueue(workId);
      await Promise.all(items.map((item) =>
        store.putQueueItem(engine.transitionQueueItem(item, { type: "retry" }))));
      await processQueue(workId);
    };

    const removeChapter = async (workId, chapterId) => {
      await store.deleteQueueItem(queueId(workId, "chapter", chapterId));
      await store.deleteVerified(`${workId}:${chapterId}`);
      await store.deleteChapterPayload(workId, chapterId);
      const record = await store.getManifest(workId);
      if (record) {
        await store.putManifest({
          ...record,
          selectedChapterIds: record.selectedChapterIds.filter((id) => id !== chapterId),
          status: "downloading",
          updatedAt: Date.now()
        });
      }
      notify();
    };

    const removeBook = async (workId) => {
      await store.removeWork(workId);
      notify();
    };

    const listBooks = () => store.listManifests();

    /**
     * Resolves whether a chapter has a verified local copy and, if so, its
     * stored payload. Consulted by the reader repository
     * (offline-library-repository.js, #221 part 2) to serve chapter content
     * local-first: "available" mirrors the exact finalization rule used by
     * the download manager itself (hash match against the manifest's current
     * ref, not merely "a payload happens to exist locally").
     */
    const loadLocalChapter = async (targetWorkId, chapterId) => {
        const record = await store.getManifest(targetWorkId);
        const ref = record?.manifest?.chapters?.find((c) => c.chapterId === chapterId);
        if (!ref) return { available: false, payload: null };

        const verified = await store.listVerified(targetWorkId);
        const match = verified.find((v) => v.chapterId === chapterId && v.hash === ref.hash);
        if (!match) return { available: false, payload: null };

        const payload = await store.loadChapterPayload(targetWorkId, chapterId);
        if (!payload || payload.hash !== ref.hash) return { available: false, payload: null };

        return { available: true, payload };
    };

    /** Raw pending reading-state events, for reconciling optimistic UI after a reload before the queue drains. */
    const listPendingSyncEvents = () => store.listSyncQueue();

    const storageUsage = async () => {
      const manifests = await store.listManifests();
      const books = await Promise.all(manifests.map(async (m) => ({
        chapters: await store.listVerified(m.workId)
      })));
      return {
        totalBytes: engine.totalStorageBytes(books),
        formatted: engine.formatBytes(engine.totalStorageBytes(books)),
        isDegraded: store.isDegraded,
        persisted: store.persisted
      };
    };

    // --- Reading-state sync queue (consumed once the reader, part 2, writes
    // progress/bookmark events offline-first; queued here so the transport
    // and conflict handling already exist for it to call into). ---

    const queueSyncEvent = (kind, payload) =>
      store.putSyncEvent({ clientEventId: payload.clientEventId, kind, payload, queuedAtMs: Date.now() });

    const drainSyncQueue = async () => {
      const events = await store.listSyncQueue();
      if (events.length === 0) return { progress: [], bookmarks: [] };

      const batch = {
        progress: events.filter((e) => e.kind === "progress").map((e) => e.payload),
        bookmarks: events.filter((e) => e.kind === "bookmark").map((e) => e.payload)
      };

      const result = await jsonFetch(`${BASE_PATH}/sync`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(batch)
      });

      const acknowledged = [
        ...result.progress.map((r) => r.clientEventId),
        ...result.bookmarks.map((r) => r.clientEventId)
      ];
      await Promise.all(acknowledged.map((id) => store.deleteSyncEvent(id)));
      return result;
    };

    return Object.freeze({
      profileId: store.profileId,
      isDegraded: store.isDegraded,
      persisted: store.persisted,
      getWifiOnly,
      setWifiOnly,
      enqueueBook,
      processQueue,
      pause,
      resume,
      retryFailed,
      removeChapter,
      removeBook,
      listBooks,
      loadLocalChapter,
      listPendingSyncEvents,
      storageUsage,
      queueSyncEvent,
      drainSyncQueue,
      onChange: (listener) => { listeners.add(listener); return () => listeners.delete(listener); }
    });
  };

  // One manager per profile per page: every consumer (the "Save offline"
  // action, Settings → Offline, the global download indicator and the
  // reader repository, #221 part 2) shares the same IndexedDB/OPFS handle
  // and Wi-Fi-only/queue state instead of each opening its own.
  let sharedManagerPromise = null;
  const getSharedManager = () => {
    sharedManagerPromise ??= createManager().catch((error) => {
      sharedManagerPromise = null;
      throw error;
    });
    return sharedManagerPromise;
  };

  window.AniLingoOfflineLibraryManager = Object.freeze({ createManager, getSharedManager });
})();
