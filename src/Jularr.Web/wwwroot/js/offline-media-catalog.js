(() => {
  "use strict";
  const storage = window.JularrOfflineMediaStorage;
  const manager = window.JularrOfflineMediaManager;
  if (!storage || !manager) return;
  const shell = document.querySelector("[data-offline-media-shell]");
  if (!shell) return;
  const empty = shell.querySelector("[data-offline-media-empty]");
  const catalog = shell.querySelector("[data-offline-media-catalog]");
  const items = shell.querySelector("[data-offline-media-items]");
  const video = shell.querySelector("[data-offline-media-video]");
  const audio = shell.querySelector("[data-offline-media-audio]");
  const reader = shell.querySelector("[data-offline-media-reader]");
  const profileId = storage.activeProfile();
  if (!profileId) return;
  let available = 0;
  const revealCatalog = () => {
    if (!available) return;
    empty.hidden = true;
    catalog.hidden = false;
  };

  storage.listPackages(profileId).then(packages => {
    const ready = packages.filter(pkg => pkg.state === "ready");
    for (const pkg of ready) {
      const button = document.createElement("button");
      button.type = "button"; button.textContent = pkg.title || pkg.id;
      button.addEventListener("click", () => openPackage(pkg));
      items.append(button);
      available++;
    }
    revealCatalog();
  }).catch(() => {});

  // Light novels have an older, text-focused download store.  It uses the
  // same profile identifier and is intentionally opened explicitly because
  // offline.html has no signed-in server-rendered body.  Learning never uses
  // this store and therefore cannot appear in the offline catalogue.
  const textStorage = window.JularrOfflineLibraryStorage;
  if (textStorage?.openStoreForProfile) {
    textStorage.openStoreForProfile(profileId).then(async store => {
      const manifests = await store.listManifests();
      for (const record of manifests.filter(item => item.status === "available")) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = record.manifest?.title || record.title || "Saved light novel";
        button.addEventListener("click", () => void openLightNovel(store, record));
        items.append(button);
        available++;
      }
      revealCatalog();
    }).catch(() => {});
  }

  function reset() {
    video.pause(); audio.pause(); video.hidden = true; audio.hidden = true;
    reader.hidden = true; reader.replaceChildren();
  }

  function openPackage(pkg) {
    reset();
    const resources = pkg.resources || [];
    const playable = resources.find(item => item.type === "video" || item.type === "audio");
    if (playable?.type === "video") {
      const videos = resources.filter(item => item.type === "video");
      const play = resource => {
        video.src = manager.localUrl(profileId, pkg.id, resource.id);
        video.hidden = false;
        video.play().catch(() => {});
      };
      if (videos.length > 1) {
        const title = document.createElement("p"); title.textContent = "Saved videos";
        const chapters = document.createElement("div"); chapters.className = "actions";
        for (const resource of videos) {
          const button = document.createElement("button"); button.type = "button";
          button.textContent = resource.fileName || resource.name || "Video";
          button.onclick = () => play(resource); chapters.append(button);
        }
        reader.append(title, chapters); reader.hidden = false;
      }
      play(playable); return;
    }
    if (playable?.type === "audio") {
      const chapters = resources.filter(item => item.type === "audio");
      const play = resource => {
        audio.src = manager.localUrl(profileId, pkg.id, resource.id);
        audio.hidden = false;
        audio.play().catch(() => {});
      };
      const speedLabel = document.createElement("label"); speedLabel.textContent = "Playback speed ";
      const speed = document.createElement("select");
      for (const value of ["0.75", "1", "1.25", "1.5", "2"]) {
        const option = document.createElement("option"); option.value = value;
        option.textContent = `${value}×`; option.selected = value === "1"; speed.append(option);
      }
      speed.onchange = () => { audio.playbackRate = Number(speed.value); };
      speedLabel.append(speed);
      const chapterButtons = document.createElement("div"); chapterButtons.className = "actions";
      for (const resource of chapters) {
        const button = document.createElement("button"); button.type = "button";
        button.textContent = resource.fileName || resource.name || "Chapter";
        button.onclick = () => play(resource); chapterButtons.append(button);
      }
      reader.append(speedLabel, chapterButtons); reader.hidden = false;
      play(playable); return;
    }
    const pages = resources.filter(item => item.type === "image");
    if (pages.length) {
      const image = document.createElement("img");
      const status = document.createElement("p");
      let index = 0;
      const show = () => {
        image.src = manager.localUrl(profileId, pkg.id, pages[index].id);
        image.alt = `${pkg.title || "Manga"} – page ${index + 1}`;
        status.textContent = `Page ${index + 1} of ${pages.length}`;
      };
      const previous = document.createElement("button"); previous.textContent = "Previous";
      previous.onclick = () => { index = Math.max(0, index - 1); show(); };
      const next = document.createElement("button"); next.textContent = "Next";
      next.onclick = () => { index = Math.min(pages.length - 1, index + 1); show(); };
      reader.append(previous, status, next, image); reader.hidden = false; show(); return;
    }
    const documentResource = resources.find(item => item.type === "document");
    if (documentResource) {
      const frame = document.createElement("iframe");
      frame.title = pkg.title || "Saved document";
      frame.src = manager.localUrl(profileId, pkg.id, documentResource.id);
      reader.append(frame); reader.hidden = false;
    }
  }

  async function openLightNovel(store, record) {
    reset();
    const chapterId = record.selectedChapterIds?.[0]
      || record.manifest?.chapters?.[0]?.chapterId;
    if (!chapterId) return;
    const payload = await store.loadChapterPayload(record.workId, chapterId);
    if (!payload) return;
    const heading = document.createElement("h2");
    heading.textContent = payload.title || record.manifest?.title || "Saved light novel";
    const content = document.createElement("article");
    const blocks = payload.blocks || [];
    const text = typeof payload.originalText === "string" ? payload.originalText : "";
    if (blocks.length) {
      for (const block of blocks) {
        const paragraph = document.createElement("p");
        paragraph.textContent = typeof block === "string"
          ? block
          : (block.runs || []).map(run => run.text || "").join("");
        if (paragraph.textContent) content.append(paragraph);
      }
    } else if (text) {
      for (const paragraphText of text.split(/\n{2,}/)) {
        const paragraph = document.createElement("p");
        paragraph.textContent = paragraphText;
        content.append(paragraph);
      }
    } else {
      const unavailable = document.createElement("p");
      unavailable.textContent = "This saved chapter is unavailable on this device.";
      content.append(unavailable);
    }
    reader.append(heading, content);
    reader.hidden = false;
  }
})();
