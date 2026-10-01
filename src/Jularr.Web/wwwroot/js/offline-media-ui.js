(() => {
  "use strict";
  const manager = window.JularrOfflineMediaManager;
  const core = window.JularrOfflineMedia;
  if (!manager || !core) return;

  const text = (key, fallback) => {
    try { return JSON.parse(document.getElementById("offline-library-text")?.textContent || "{}")[key] || fallback; }
    catch { return fallback; }
  };
  const bytes = value => {
    if (!Number.isFinite(value) || value <= 0) return "0 B";
    const units = ["B", "KB", "MB", "GB"];
    const exponent = Math.min(units.length - 1, Math.floor(Math.log(value) / Math.log(1024)));
    return `${(value / (1024 ** exponent)).toFixed(exponent ? 1 : 0)} ${units[exponent]}`;
  };
  const itemData = root => {
    const button = root.querySelector("[data-offline-episode-save], [data-offline-media-save]");
    if (!button) return null;
    const kind = root.dataset.offlineMediaKind || (button.dataset.offlineEpisodeSave ? "episode" : button.dataset.offlineMediaKind);
    const id = root.dataset.offlineMediaId || button.dataset.offlineEpisodeSave || button.dataset.offlineMediaSave;
    return kind && id ? { kind, id: String(id), packageId: core.packageId(kind, id), button } : null;
  };
  const downloadedBytes = pkg => (pkg?.resources || []).reduce((total, resource) => {
    const chunks = Math.max(0, Math.min(resource.completedChunks || 0, core.chunkCount(resource.sizeBytes || 0)));
    return total + Math.min(resource.sizeBytes || 0, chunks * core.CHUNK_BYTES);
  }, 0);
  const percentage = pkg => {
    const total = pkg?.sizeBytes || 0;
    return total ? Math.min(100, Math.round(downloadedBytes(pkg) / total * 100)) : (pkg?.state === "ready" ? 100 : 0);
  };
  const stateText = pkg => {
    if (!pkg) return text("offlineLibrary.action.idle", "Not saved offline");
    if (pkg.state === "ready") return text("offlineLibrary.action.available", "Available offline");
    if (pkg.state === "paused") return text("offlineLibrary.action.paused", "Paused");
    if (pkg.state === "failed") return text("offlineLibrary.media.failed", "Download failed — retry");
    return text("offlineLibrary.action.downloading", "Downloading…");
  };
  const actionText = pkg => {
    if (!pkg) return text("offlineLibrary.action.saveButton", "Save offline");
    if (pkg.state === "downloading") return text("offlineLibrary.action.pauseButton", "Pause");
    if (pkg.state === "paused") return text("offlineLibrary.action.resumeButton", "Resume");
    if (pkg.state === "failed") return text("offlineLibrary.media.retryButton", "Retry");
    return text("offlineLibrary.action.available", "Available offline");
  };
  const summary = pkg => {
    if (!pkg) return stateText(pkg);
    const percent = percentage(pkg);
    const transferred = bytes(downloadedBytes(pkg));
    const total = bytes(pkg.sizeBytes || 0);
    return pkg.state === "ready"
      ? `${stateText(pkg)} · ${total}`
      : `${stateText(pkg)} · ${percent}% · ${transferred} / ${total}`;
  };

  async function renderItem(root, packages) {
    const entry = itemData(root);
    if (!entry) return;
    const pkg = packages.find(item => item.id === entry.packageId);
    const status = root.querySelector("[data-offline-media-status]");
    const progress = root.querySelector("[data-offline-media-progress]");
    const remove = root.querySelector("[data-offline-media-remove]");
    if (entry.button.querySelector("svg")) {
      entry.button.setAttribute("aria-label", actionText(pkg));
      entry.button.setAttribute("title", actionText(pkg));
    } else {
      entry.button.textContent = actionText(pkg);
    }
    entry.button.dataset.offlineMediaState = pkg?.state || "idle";
    entry.button.disabled = pkg?.state === "ready";
    if (status) status.textContent = summary(pkg);
    if (progress) {
      progress.hidden = !pkg || pkg.state === "ready";
      progress.value = percentage(pkg);
      progress.setAttribute("aria-valuetext", summary(pkg));
    }
    if (remove) remove.hidden = !pkg;
  }

  async function renderAll() {
    let packages = [];
    try { packages = await manager.packages(); } catch { /* signed out or storage unavailable */ }
    document.querySelectorAll("[data-offline-media-item]").forEach(root => { void renderItem(root, packages); });
    const list = document.querySelector("[data-offline-media-list]");
    const empty = document.querySelector("[data-offline-media-empty]");
    if (list && empty) empty.hidden = list.querySelectorAll("[data-offline-media-item]").length > 0;
  }

  document.addEventListener("click", async event => {
    const remove = event.target.closest("[data-offline-media-remove]");
    if (remove) {
      const root = remove.closest("[data-offline-media-item]");
      const entry = root && itemData(root);
      if (!entry) return;
      remove.disabled = true;
      try { await manager.remove(entry.packageId); } finally { remove.disabled = false; await renderAll(); }
      return;
    }
    const button = event.target.closest("[data-offline-episode-save], [data-offline-media-save]");
    if (!button) return;
    const root = button.closest("[data-offline-media-item]") || button.parentElement;
    const entry = root && itemData(root);
    if (!entry) return;
    const state = button.dataset.offlineMediaState;
    if (state === "ready") return;
    if (state === "downloading") {
      manager.pause(entry.packageId);
      button.disabled = true;
      return;
    }
    button.disabled = true;
    try {
      if (entry.kind === "episode") await manager.saveEpisode(entry.id);
      else await manager.saveMedia(entry.kind, entry.id);
    } catch (error) {
      const status = root.querySelector("[data-offline-media-status]");
      if (status && error?.name !== "AbortError") status.textContent = error?.message || text("offlineLibrary.media.failed", "Download failed — retry");
    } finally { await renderAll(); }
  });

  let scheduled = false;
  const refresh = () => {
    if (scheduled) return;
    scheduled = true;
    setTimeout(() => { scheduled = false; void renderAll(); }, 50);
  };
  window.addEventListener("jularr:offline-media-progress", refresh);
  window.addEventListener("jularr:offline-media-ready", refresh);
  window.addEventListener("jularr:offline-media-removed", refresh);
  document.addEventListener("DOMContentLoaded", refresh);
  refresh();
})();
