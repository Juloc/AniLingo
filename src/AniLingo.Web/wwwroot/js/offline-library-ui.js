(() => {
  "use strict";

  /**
   * DOM wiring for the offline library (#221 part 1): the reusable
   * "Save offline" action (Pages/Shared/_OfflineLibraryAction.cshtml) and the
   * Settings → Offline page. Pure decisions and I/O live in
   * offline-library.js / offline-library-storage.js / offline-library-manager.js;
   * this file only reads/writes the DOM.
   *
   * Text here is plain English source strings, same convention as the rest of
   * AniLingo's server-rendered UI (see UiTranslationResources.cs); wiring
   * these through the localization catalog like pwa.js's shellText is left
   * for a later pass, not required for the part 1 foundation.
   */

  let managerPromise = null;
  const manager = () => {
    managerPromise ??= window.AniLingoOfflineLibraryManager.createManager().catch((error) => {
      managerPromise = null;
      throw error;
    });
    return managerPromise;
  };

  const STATE_LABELS = {
    unavailable: "Offline unavailable (sign-in required)",
    idle: "Not saved offline",
    downloading: "Downloading…",
    paused: "Paused",
    failed: "Failed",
    available: "Available offline",
    "update-available": "Update available"
  };

  const bookState = async (instance, workId) => {
    const books = await instance.listBooks();
    const record = books.find((b) => b.workId === workId);
    return record?.status || "idle";
  };

  const renderAction = async (root) => {
    const workId = root.dataset.offlineSave;
    const button = root.querySelector("[data-offline-save-button]");
    const status = root.querySelector("[data-offline-save-status]");
    if (!button || !status) return;

    let instance;
    try {
      instance = await manager();
    } catch {
      status.textContent = STATE_LABELS.unavailable;
      button.disabled = true;
      return;
    }

    const refresh = async () => {
      const state = await bookState(instance, workId);
      button.disabled = false;
      status.textContent = STATE_LABELS[state] || state;
      button.textContent = state === "idle" || state === "failed"
        ? "Save offline"
        : state === "downloading"
          ? "Pause"
          : state === "paused"
            ? "Resume"
            : "Check for updates";
      button.dataset.offlineState = state;
    };

    button.addEventListener("click", async () => {
      button.disabled = true;
      try {
        const state = button.dataset.offlineState;
        if (state === "downloading") {
          await instance.pause(workId);
        } else if (state === "paused") {
          await instance.resume(workId);
        } else if (state === "failed") {
          await instance.retryFailed(workId);
        } else {
          await instance.enqueueBook(workId);
          await instance.processQueue(workId);
        }
      } catch {
        status.textContent = "Saving offline is not possible right now.";
      } finally {
        button.disabled = false;
        await refresh();
      }
    });

    instance.onChange(() => { void refresh(); });
    await refresh();
  };

  const renderSettingsPage = async (root) => {
    const list = root.querySelector("[data-offline-list]");
    const usage = root.querySelector("[data-offline-usage]");
    const degradedNotice = root.querySelector("[data-offline-degraded]");
    const wifiToggle = root.querySelector("[data-offline-wifi-only]");
    if (!list || !usage) return;

    let instance;
    try {
      instance = await manager();
    } catch {
      list.textContent = "Sign in to manage offline downloads.";
      return;
    }

    if (degradedNotice) {
      degradedNotice.hidden = !instance.isDegraded;
    }

    if (wifiToggle) {
      wifiToggle.checked = await instance.getWifiOnly();
      wifiToggle.addEventListener("change", () => {
        void instance.setWifiOnly(wifiToggle.checked);
      });
    }

    const refresh = async () => {
      const books = await instance.listBooks();
      const usageInfo = await instance.storageUsage();
      usage.textContent = `Storage used: ${usageInfo.formatted}`
        + (usageInfo.persisted ? "" : " (not persistently reserved)");

      list.replaceChildren();
      if (books.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted";
        empty.textContent = "No offline downloads.";
        list.appendChild(empty);
        return;
      }

      for (const book of books) {
        const row = document.createElement("div");
        row.className = "offline-library-row";

        const title = document.createElement("strong");
        title.textContent = book.manifest.title;
        row.appendChild(title);

        const state = document.createElement("span");
        state.textContent = STATE_LABELS[book.status] || book.status;
        row.appendChild(state);

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "button";
        removeButton.textContent = "Remove";
        removeButton.addEventListener("click", async () => {
          removeButton.disabled = true;
          await instance.removeBook(book.workId);
        });
        row.appendChild(removeButton);

        list.appendChild(row);
      }
    };

    instance.onChange(() => { void refresh(); });
    await refresh();
  };

  const initialize = () => {
    document.querySelectorAll("[data-offline-save]").forEach((root) => { void renderAction(root); });
    document.querySelectorAll("[data-offline-settings-page]").forEach((root) => { void renderSettingsPage(root); });
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }
})();
