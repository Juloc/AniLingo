(() => {
  "use strict";
  const core = window.JularrOfflineMedia;
  if (!core) return;

  const ACTIVE_PROFILE_KEY = "jularr.offline-media.active-profile";
  const DB_PREFIX = "jularr-offline-media:";
  const DB_VERSION = 2;
  const opened = new Map();

  function profileKey(profileId) {
    if (!/^[A-Za-z0-9_.:@-]{1,200}$/.test(profileId || "")) {
      throw new Error("A valid profile id is required for offline media.");
    }
    return profileId;
  }

  function open(profileId) {
    const id = profileKey(profileId);
    if (opened.has(id)) return opened.get(id);
    const promise = new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_PREFIX + id, DB_VERSION);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains("packages")) db.createObjectStore("packages", { keyPath: "id" });
        if (!db.objectStoreNames.contains("chunks")) db.createObjectStore("chunks", { keyPath: ["packageId", "resourceId", "index"] });
        if (!db.objectStoreNames.contains("settings")) db.createObjectStore("settings", { keyPath: "key" });
        if (!db.objectStoreNames.contains("progress")) db.createObjectStore("progress", { keyPath: "id" });
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error("Cannot open offline media storage."));
    });
    opened.set(id, promise);
    return promise;
  }

  async function transaction(profileId, stores, mode, action) {
    const db = await open(profileId);
    return new Promise((resolve, reject) => {
      const tx = db.transaction(stores, mode);
      let result;
      tx.oncomplete = () => resolve(result);
      tx.onerror = () => reject(tx.error || new Error("Offline media transaction failed."));
      tx.onabort = () => reject(tx.error || new Error("Offline media transaction aborted."));
      try { result = action(tx); } catch (error) { tx.abort(); reject(error); }
    });
  }

  function requestValue(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error || new Error("Offline media storage request failed."));
    });
  }

  async function listPackages(profileId) {
    return transaction(profileId, ["packages"], "readonly", tx => requestValue(tx.objectStore("packages").getAll()));
  }

  async function getPackage(profileId, id) {
    return transaction(profileId, ["packages"], "readonly", tx => requestValue(tx.objectStore("packages").get(id)));
  }

  async function putPackage(profileId, pkg) {
    if (!pkg?.id) throw new Error("Offline package id is required.");
    return transaction(profileId, ["packages"], "readwrite", tx => tx.objectStore("packages").put(pkg));
  }

  async function putChunk(profileId, packageId, resourceId, index, bytes) {
    const data = bytes instanceof Blob ? bytes : new Blob([bytes]);
    return transaction(profileId, ["chunks"], "readwrite", tx => tx.objectStore("chunks").put({ packageId, resourceId, index, bytes: data }));
  }

  async function getChunk(profileId, packageId, resourceId, index) {
    return transaction(profileId, ["chunks"], "readonly", tx => requestValue(tx.objectStore("chunks").get([packageId, resourceId, index])));
  }

  async function packageUsage(profileId) {
    const packages = await listPackages(profileId);
    return packages.reduce((total, pkg) => total + (pkg.state === "ready" || pkg.state === "downloading" ? (pkg.sizeBytes || 0) : 0), 0);
  }

  async function removePackage(profileId, id) {
    return transaction(profileId, ["packages", "chunks"], "readwrite", tx => {
      tx.objectStore("packages").delete(id);
      const index = tx.objectStore("chunks");
      const range = IDBKeyRange.bound([id, "", 0], [id, "\uffff", Number.MAX_SAFE_INTEGER]);
      index.openCursor(range).onsuccess = event => {
        const cursor = event.target.result;
        if (!cursor) return;
        cursor.delete();
        cursor.continue();
      };
    });
  }

  async function clearProfile(profileId) {
    return transaction(profileId, ["packages", "chunks", "settings", "progress"], "readwrite", tx => {
      for (const name of ["packages", "chunks", "settings", "progress"]) tx.objectStore(name).clear();
    });
  }

  async function queueProgress(profileId, checkpoint) {
    return transaction(profileId, ["progress"], "readwrite", tx => tx.objectStore("progress").put(checkpoint));
  }
  async function listProgress(profileId) {
    return transaction(profileId, ["progress"], "readonly", tx => requestValue(tx.objectStore("progress").getAll()));
  }
  async function clearProgress(profileId) {
    return transaction(profileId, ["progress"], "readwrite", tx => tx.objectStore("progress").clear());
  }

  async function deleteProfile(profileId) {
    const id = profileKey(profileId);
    const db = await open(id);
    db.close();
    opened.delete(id);
    return new Promise((resolve, reject) => {
      const request = indexedDB.deleteDatabase(DB_PREFIX + id);
      request.onsuccess = () => resolve();
      request.onerror = () => reject(request.error || new Error("Cannot remove offline media profile."));
      request.onblocked = () => reject(new Error("Close other Jularr tabs before removing offline media."));
    });
  }

  function setActiveProfile(profileId) { localStorage.setItem(ACTIVE_PROFILE_KEY, profileKey(profileId)); }
  function activeProfile() { return localStorage.getItem(ACTIVE_PROFILE_KEY); }
  function clearActiveProfile() { localStorage.removeItem(ACTIVE_PROFILE_KEY); }

  window.JularrOfflineMediaStorage = Object.freeze({
    open, listPackages, getPackage, putPackage, putChunk, getChunk, packageUsage,
    removePackage, clearProfile, deleteProfile, queueProgress, listProgress, clearProgress,
    setActiveProfile, activeProfile, clearActiveProfile
  });
})();
