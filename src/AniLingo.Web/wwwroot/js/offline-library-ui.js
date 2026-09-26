(() => {
  "use strict";

  /**
   * DOM wiring for the offline library (#221 part 1/2): the reusable
   * "Save offline" action (Pages/Shared/_OfflineLibraryAction.cshtml), the
   * Settings → Offline page, the compact global download indicator
   * (Pages/Shared/_AppAccountFooter.cshtml, rendered on every authenticated
   * page) and the Library "Offline" filter (Pages/Books/Index.cshtml,
   * Pages/Novels/Index.cshtml). Pure decisions and I/O live in
   * offline-library.js / offline-library-storage.js / offline-library-manager.js;
   * this file only reads/writes the DOM.
   *
   * Text is read from the UI catalog (offlineLibrary.* keys,
   * UiTranslationResources.cs) through a JSON script element rendered by
   * _Layout.cshtml, the same pattern pwa.js's shellText already uses for
   * pwa.* keys.
   */

  const catalog = (() => {
    try {
      return JSON.parse(document.getElementById("offline-library-text")?.textContent || "{}");
    } catch {
      return {};
    }
  })();
  const text = (key) => catalog[key] || key;
  const format = (key, replacements) =>
    Object.entries(replacements).reduce(
      (result, [name, value]) => result.replaceAll(`{${name}}`, String(value)),
      text(key));

  const manager = () => window.AniLingoOfflineLibraryManager.getSharedManager();

  const STATE_LABEL_KEYS = {
    unavailable: "offlineLibrary.action.unavailable",
    idle: "offlineLibrary.action.idle",
    downloading: "offlineLibrary.action.downloading",
    paused: "offlineLibrary.action.paused",
    failed: "offlineLibrary.action.failed",
    available: "offlineLibrary.action.available",
    "update-available": "offlineLibrary.action.updateAvailable"
  };
  const stateLabel = (state) => STATE_LABEL_KEYS[state] ? text(STATE_LABEL_KEYS[state]) : state;

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
      status.textContent = text("offlineLibrary.action.unavailable");
      button.disabled = true;
      return;
    }

    const refresh = async () => {
      const state = await bookState(instance, workId);
      button.disabled = false;
      status.textContent = stateLabel(state);
      button.textContent = state === "idle" || state === "failed"
        ? text("offlineLibrary.action.saveButton")
        : state === "downloading"
          ? text("offlineLibrary.action.pauseButton")
          : state === "paused"
            ? text("offlineLibrary.action.resumeButton")
            : text("offlineLibrary.action.checkUpdateButton");
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
        status.textContent = text("offlineLibrary.action.saveFailed");
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
      list.textContent = text("offlineLibrary.settings.signInRequired");
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
      usage.textContent = format("offlineLibrary.settings.storageUsed", { formatted: usageInfo.formatted })
        + (usageInfo.persisted ? "" : text("offlineLibrary.settings.notPersistedSuffix"));

      list.replaceChildren();
      if (books.length === 0) {
        const empty = document.createElement("p");
        empty.className = "muted";
        empty.textContent = text("offlineLibrary.settings.empty");
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
        state.textContent = stateLabel(book.status);
        row.appendChild(state);

        const removeButton = document.createElement("button");
        removeButton.type = "button";
        removeButton.className = "button";
        removeButton.textContent = text("offlineLibrary.settings.remove");
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

  /**
   * Compact global download indicator (issue #221's UX section): shown on
   * every authenticated page (Pages/Shared/_AppAccountFooter.cshtml, which
   * can render twice per page — desktop sidebar + mobile "more" sheet — so
   * every matching element is wired independently). Hidden entirely when
   * nothing is downloading, so it never distracts on the common case.
   */
  const renderDownloadIndicator = async (root) => {
    let instance;
    try {
      instance = await manager();
    } catch {
      return;
    }

    const refresh = async () => {
      const books = await instance.listBooks();
      const downloadingCount = books.filter((b) => b.status === "downloading").length;
      root.hidden = downloadingCount === 0;
      if (downloadingCount > 0) {
        root.textContent = format("offlineLibrary.indicator.downloading", { count: downloadingCount });
      }
    };

    root.setAttribute("aria-label", text("offlineLibrary.indicator.aria"));
    instance.onChange(() => { void refresh(); });
    await refresh();
  };

  /**
   * Library "Offline" filter (issue #221's UX section): purely a client-side
   * show/hide over server-rendered cards, since what is downloaded is
   * profile-and-device-local browser state the server never sees. Cards
   * carry `data-work-id`; filter buttons carry
   * `data-library-filter-option="all"|"offline"`.
   */
  const renderLibraryFilter = async (root) => {
    const cards = Array.from(root.querySelectorAll("[data-work-id]"));
    const options = Array.from(root.querySelectorAll("[data-library-filter-option]"));
    if (cards.length === 0 || options.length === 0) return;

    let instance;
    try {
      instance = await manager();
    } catch {
      return;
    }

    let offlineWorkIds = new Set();
    const applyFilter = (filter) => {
      for (const card of cards) {
        card.hidden = filter === "offline" && !offlineWorkIds.has(card.dataset.workId);
      }
    };

    const currentFilter = () =>
      options.find((option) => option.classList.contains("active"))?.dataset.libraryFilterOption || "all";

    const refresh = async () => {
      const books = await instance.listBooks();
      offlineWorkIds = new Set(
        books.filter((b) => b.status === "available").map((b) => b.workId));
      applyFilter(currentFilter());
    };

    options.forEach((option) => {
      option.addEventListener("click", () => {
        options.forEach((other) => other.classList.toggle("active", other === option));
        applyFilter(option.dataset.libraryFilterOption);
      });
    });

    instance.onChange(() => { void refresh(); });
    await refresh();
  };

  const initialize = () => {
    document.querySelectorAll("[data-offline-save]").forEach((root) => { void renderAction(root); });
    document.querySelectorAll("[data-offline-settings-page]").forEach((root) => { void renderSettingsPage(root); });
    document.querySelectorAll("[data-offline-download-indicator]").forEach((root) => { void renderDownloadIndicator(root); });
    document.querySelectorAll("[data-offline-library-filter]").forEach((root) => { void renderLibraryFilter(root); });
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialize, { once: true });
  } else {
    initialize();
  }
})();
