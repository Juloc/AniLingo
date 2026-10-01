(() => {
  "use strict";
  const manager = window.JularrOfflineMediaManager;
  if (!manager) return;
  document.addEventListener("click", async event => {
    const button = event.target.closest("[data-offline-episode-save], [data-offline-media-save]");
    if (!button) return;
    const status = button.parentElement?.querySelector("[data-offline-media-status]");
    button.disabled = true;
    if (status) status.textContent = "Saving for offline use…";
    try {
      if (button.dataset.offlineEpisodeSave) {
        await manager.saveEpisode(button.dataset.offlineEpisodeSave);
      } else {
        await manager.saveMedia(button.dataset.offlineMediaKind, button.dataset.offlineMediaSave);
      }
      if (status) status.textContent = "Saved for offline use.";
    } catch (error) {
      if (status) status.textContent = error?.message || "Could not save this media offline.";
    } finally { button.disabled = false; }
  });
})();
