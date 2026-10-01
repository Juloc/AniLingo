(() => {
  "use strict";
  const core = window.JularrOfflineMedia;
  const storage = window.JularrOfflineMediaStorage;
  if (!core || !storage) return;

  const controllers = new Map();
  const event = (name, detail) => window.dispatchEvent(new CustomEvent(name, { detail }));
  const profile = () => document.body?.dataset.profileId || "";
  const escaped = value => encodeURIComponent(value);

  function localUrl(profileId, packageId, resourceId = "main") {
    return `/_offline-media/${escaped(profileId)}/${escaped(packageId)}/${escaped(resourceId)}`;
  }

  async function downloadResource(profileId, pkg, resource, signal) {
    const chunks = core.chunkCount(resource.sizeBytes);
    let complete = resource.completedChunks || 0;
    for (let index = complete; index < chunks; index += 1) {
      if (signal.aborted) throw new DOMException("Download paused", "AbortError");
      const start = index * core.CHUNK_BYTES;
      const end = Math.min(resource.sizeBytes - 1, start + core.CHUNK_BYTES - 1);
      const headers = { Range: `bytes=${start}-${end}` };
      if (resource.etag) headers["If-Range"] = resource.etag;
      const response = await fetch(resource.url, { headers, signal, credentials: "same-origin" });
      if (!(response.ok || response.status === 206)) throw new Error(`Download failed (${response.status}).`);
      const bytes = await response.blob();
      if (bytes.size !== end - start + 1) throw new Error("The server returned an incomplete offline chunk.");
      await storage.putChunk(profileId, pkg.id, resource.id, index, bytes);
      complete = index + 1;
      resource.completedChunks = complete;
      pkg.resources = pkg.resources.map(item => item.id === resource.id ? resource : item);
      pkg.state = "downloading";
      pkg.updatedAt = new Date().toISOString();
      await storage.putPackage(profileId, pkg);
      event("jularr:offline-media-progress", { package: pkg, resource });
    }
  }

  async function downloadManifest(manifest) {
    const profileId = profile();
    if (!profileId) throw new Error("Sign in before saving media offline.");
    storage.setActiveProfile(profileId);
    const id = manifest.id;
    let existing = await storage.getPackage(profileId, id);
    // A resumed range is valid only for the exact entity described by the new
    // manifest.  Drop stale chunks before requesting bytes from a replacement.
    if (existing?.resources?.some(old => {
      const next = (manifest.resources || []).find(item => item.id === old.id);
      return !next || next.etag !== old.etag || next.sizeBytes !== old.sizeBytes;
    })) {
      await storage.removePackage(profileId, id);
      existing = null;
    }
    const resources = (manifest.resources || []).map(resource => ({ ...resource, completedChunks: existing?.resources?.find(item => item.id === resource.id)?.completedChunks || 0 }));
    const required = resources.reduce((sum, item) => sum + item.sizeBytes, 0);
    const usage = await storage.packageUsage(profileId);
    const delta = Math.max(0, required - (existing?.sizeBytes || 0));
    const admission = await core.canStore({ requestedBytes: delta, storedBytes: usage });
    if (!admission.allowed) {
      const message = admission.reason === "profile-limit"
        ? "This download exceeds the 10 GiB offline limit for this profile. Remove saved media first."
        : "Your browser does not have enough available storage for this download.";
      throw new Error(message);
    }
    controllers.get(id)?.abort();
    const controller = new AbortController();
    controllers.set(id, controller);
    const pkg = {
      ...existing,
      ...manifest,
      resources,
      sizeBytes: required,
      state: "downloading",
      createdAt: existing?.createdAt || new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };
    await storage.putPackage(profileId, pkg);
    event("jularr:offline-media-progress", { package: pkg });
    try {
      for (const resource of resources) await downloadResource(profileId, pkg, resource, controller.signal);
      pkg.state = "ready";
      pkg.updatedAt = new Date().toISOString();
      await storage.putPackage(profileId, pkg);
      const primary = pkg.resources.find(item => item.type === "video" || item.type === "audio") || pkg.resources[0];
      event("jularr:offline-media-ready", { package: pkg, url: primary ? localUrl(profileId, pkg.id, primary.id) : "" });
      return pkg;
    } catch (error) {
      pkg.state = error?.name === "AbortError" ? "paused" : "failed";
      pkg.error = error?.message || "Offline download failed.";
      pkg.updatedAt = new Date().toISOString();
      await storage.putPackage(profileId, pkg);
      event("jularr:offline-media-progress", { package: pkg });
      throw error;
    } finally { controllers.delete(id); }
  }

  async function saveEpisode(episodeId) {
    const response = await fetch(`/api/client/v1/episodes/${encodeURIComponent(episodeId)}/offline-download`, { credentials: "same-origin" });
    if (!response.ok) throw new Error("This episode is not currently available for offline saving.");
    const descriptor = await response.json();
    // Learning cues remain part of the native compatibility endpoint, but deliberately never enter the PWA package.
    const packageResponse = await fetch(`/api/client/v1/offline-media/episode/${encodeURIComponent(episodeId)}/manifest`, { credentials: "same-origin" });
    if (!packageResponse.ok) throw new Error("This episode cannot be prepared for portable offline playback.");
    const manifest = await packageResponse.json();
    return downloadManifest({
      id: manifest.id, kind: "episode", title: descriptor.episode?.title || manifest.title || "Episode",
      artworkUrl: null, progress: descriptor.progress || null,
      resources: (manifest.resources || []).map(resource => ({
        id: resource.id, type: resource.type, url: resource.contentUrl, etag: resource.eTag,
        sizeBytes: resource.sizeBytes, mimeType: resource.mimeType, fileName: resource.name,
        hash: resource.sha256, hashAlgorithm: "sha-256",
        tracks: { audio: descriptor.audioTracks || [], subtitles: descriptor.subtitleTracks || [] }
      }))
    });
  }

  async function saveMedia(kind, mediaId) {
    const response = await fetch(`/api/client/v1/offline-media/${encodeURIComponent(kind)}/${encodeURIComponent(mediaId)}/manifest`, { credentials: "same-origin" });
    if (!response.ok) throw new Error("This media is not currently available for offline saving.");
    const manifest = await response.json();
    return downloadManifest({
      id: manifest.id, kind: manifest.kind, title: manifest.title, issuedAtUtc: manifest.issuedAtUtc,
      resources: (manifest.resources || []).map(resource => ({
        id: resource.id, type: resource.type, url: resource.contentUrl, etag: resource.eTag,
        sizeBytes: resource.sizeBytes, mimeType: resource.mimeType, fileName: resource.name,
        hash: resource.sha256, hashAlgorithm: "sha-256"
      }))
    });
  }

  function pause(packageId) { controllers.get(packageId)?.abort(); }
  async function remove(packageId) { const id = profile(); if (id) await storage.removePackage(id, packageId); event("jularr:offline-media-removed", { packageId }); }
  async function packages() { const id = profile() || storage.activeProfile(); return id ? storage.listPackages(id) : []; }
  async function clearForProfile(profileId) {
    controllers.forEach(controller => controller.abort());
    await storage.deleteProfile(profileId);
    // Text downloads predate the binary-media package store, but are subject
    // to the same account boundary.  Clear both before a logout/profile switch.
    const textStore = window.JularrOfflineLibraryStorage?.openStoreForProfile
      ? await window.JularrOfflineLibraryStorage.openStoreForProfile(profileId)
      : null;
    await textStore?.clearProfile();
    if (storage.activeProfile() === profileId) storage.clearActiveProfile();
  }

  window.addEventListener("jularr:profile-changed", event => {
    const oldProfile = event.detail?.previousProfileId;
    if (oldProfile) clearForProfile(oldProfile).catch(() => {});
  });
  window.addEventListener("jularr:episode-progress", event => {
    const profileId = profile();
    const checkpoint = event.detail;
    if (!profileId || !checkpoint?.episodeId) return;
    storage.queueProgress(profileId, { ...checkpoint, id: `${checkpoint.episodeId}:${Date.now()}:${Math.random()}` }).catch(() => {});
  });
  async function syncProgress() {
    const profileId = profile();
    if (!profileId || !navigator.onLine) return;
    const queued = await storage.listProgress(profileId);
    if (!queued.length) return;
    const response = await fetch("/api/client/v1/offline/progress", {
      method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ items: queued.map(({ episodeId, positionMs, durationMs, completed }) => ({ episodeId, positionMs, durationMs, completed })) })
    });
    if (response.ok) await storage.clearProgress(profileId);
  }
  window.addEventListener("online", () => { syncProgress().catch(() => {}); });
  syncProgress().catch(() => {});
  document.addEventListener("submit", event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches("[data-offline-logout]") || form.dataset.offlineCleanupDone) return;
    const profileId = profile();
    if (!profileId) return;
    event.preventDefault();
    form.dataset.offlineCleanupDone = "true";
    clearForProfile(profileId).catch(() => {}).finally(() => HTMLFormElement.prototype.submit.call(form));
  });
  window.addEventListener("beforeunload", () => controllers.forEach(controller => controller.abort()));

  window.JularrOfflineMediaManager = Object.freeze({ saveEpisode, saveMedia, downloadManifest, pause, remove, packages, clearForProfile, syncProgress, localUrl });
})();
