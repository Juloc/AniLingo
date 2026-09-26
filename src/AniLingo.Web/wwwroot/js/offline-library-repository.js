(() => {
  "use strict";

  /**
   * Reader ↔ offline library bridge (#221 part 2A): the
   * `Reader -> BookRepository -> {local source first, server source when
   * required}` seam the issue describes, shared by the Novel reader
   * (novel-position.js/novel-annotations.js/novel-reader.js) and the Books
   * reader (books-reader.js) so there is exactly one implementation instead
   * of per-reader offline logic.
   *
   * Design (documented in docs/OFFLINE_LIBRARY.md):
   *
   * 1. Chapter content is served local-first whenever a verified local copy
   *    exists, online or offline (resolveChapterSource) — not "only when
   *    offline". A download is only ever marked verified when its hash
   *    matches the manifest's current chapter hash (offline-library.js's
   *    isBookComplete), so the local copy and what the server would render
   *    are guaranteed identical; re-fetching/re-rendering an already-correct
   *    chapter on every page load would be pure waste. This keeps behavior
   *    deterministic regardless of connection quality and avoids re-fetching
   *    large chapter text on every read.
   * 2. Reading progress and bookmark add/remove/edit *always* go through the
   *    same local-first sync queue (queueProgress/queueBookmarkUpsert/
   *    queueBookmarkRemove below, wrapping manager.queueSyncEvent) — online
   *    and offline alike. This is the one canonical write path: the local
   *    write always succeeds immediately (optimistic UI, matching what the
   *    server would eventually return), then an opportunistic
   *    drainSyncQueue() is attempted when the browser reports it is online.
   *    The page's own Progress/Bookmark/RemoveBookmark/BookmarkLabel POST
   *    handlers are no longer called from the reader; they are left in place
   *    server-side (harmless, unused by this client) rather than removed, to
   *    avoid widening this change's server-side surface.
   * 3. Highlights are *not* part of the offline sync contract (PR #359 only
   *    covers progress and bookmarks) and intentionally keep using their
   *    existing online-only endpoints; see docs/OFFLINE_LIBRARY.md.
   * 4. In-page chapter-to-chapter navigation (footer previous/next links)
   *    additionally renders a downloaded chapter's text locally when the
   *    browser is offline, instead of letting a normal <a href> navigation
   *    fail. It reuses the exact same block/paragraph-to-HTML renderer used
   *    nowhere else — there is one function that turns an offline chapter
   *    payload into reader markup, not a second implementation living next
   *    to the server-rendered path. When the chapter has not been
   *    downloaded, a clear "not available offline" notice is shown instead
   *    of a broken navigation. A full cold-start offline page load (e.g.
   *    opening a Read URL directly while offline, before any reader page
   *    has loaded this session) is explicitly out of scope for this slice;
   *    see the Part 2 TODO in docs/OFFLINE_LIBRARY.md.
   */

  // ---- pure decision logic (tested under Jint, OfflineLibraryRepositoryEngineTests.cs) ----

  /**
   * Chooses where a chapter's content should come from. `hasVerifiedLocal`
   * must reflect the same verification rule the download manager itself
   * uses (see offline-library-manager.js's loadLocalChapter): a stored hash
   * equal to the manifest's current chapter hash, not merely "some payload
   * exists on disk".
   */
  const resolveChapterSource = ({ hasVerifiedLocal, isOnline }) => {
    if (hasVerifiedLocal) return "local";
    return isOnline ? "server" : "offline-missing";
  };

  const clampPermille = (value) =>
    Math.max(0, Math.min(1000, Math.round(Number(value) || 0)));

  const isoTimestamp = (nowMs) => new Date(nowMs ?? Date.now()).toISOString();

  /**
   * Builds a `ClientOfflineProgressEvent`-shaped payload (see
   * Features/ClientApi/ClientApiOfflineLibraryContracts.cs) for the sync
   * queue. Pure: the caller supplies `clientEventId`/`nowMs` so this stays
   * deterministic and testable.
   */
  const buildProgressEvent = ({
    workId,
    chapterId,
    positionPermille,
    anchorLanguage,
    anchorParagraphIndex,
    anchorOffset,
    clientEventId,
    nowMs
  }) => ({
    clientEventId,
    workId,
    chapterId,
    positionPermille: clampPermille(positionPermille),
    anchorLanguage: anchorLanguage || null,
    anchorParagraphIndex:
      anchorParagraphIndex === null || anchorParagraphIndex === undefined || anchorParagraphIndex === ""
        ? null
        : Number(anchorParagraphIndex),
    anchorOffset: Math.max(0, Math.round(Number(anchorOffset) || 0)),
    clientTimestampUtc: isoTimestamp(nowMs)
  });

  /**
   * Builds a `ClientOfflineBookmarkEvent`-shaped payload for an add/edit
   * ("upsert") or a removal. The bookmark id is always client-supplied (the
   * sync contract explicitly allows this, see docs/OFFLINE_LIBRARY.md) so a
   * bookmark created while offline can be edited/removed before the server
   * has ever seen it, and reused across devices.
   */
  const buildBookmarkEvent = ({
    type,
    workId,
    chapterId,
    bookmarkId,
    language,
    positionPermille,
    paragraphIndex,
    characterOffset,
    anchorText,
    label,
    style,
    color,
    clientEventId,
    nowMs
  }) => ({
    clientEventId,
    bookmarkId,
    type: type === "remove" ? "remove" : "upsert",
    workId,
    chapterId,
    language: language || null,
    positionPermille: clampPermille(positionPermille),
    paragraphIndex:
      paragraphIndex === null || paragraphIndex === undefined || paragraphIndex === ""
        ? null
        : Number(paragraphIndex),
    characterOffset: Math.max(0, Math.round(Number(characterOffset) || 0)),
    anchorText: anchorText || null,
    label: label || null,
    style: style || null,
    color: color || null,
    clientTimestampUtc: isoTimestamp(nowMs)
  });

  /**
   * Reconciles server-rendered bookmarks with bookmark events queued on this
   * device but not yet acknowledged by /sync (e.g. the page was reloaded
   * before drainSyncQueue ran). Pending events are applied oldest-first, a
   * simplified version of the server's own last-writer-wins rule: since
   * every event here originates from this one device in creation order,
   * later always wins and "remove" always wins over an earlier "upsert".
   * Used so a bookmark added/removed offline is never invisible after a
   * reload just because it has not reached the server yet.
   */
  const mergePendingBookmarks = (serverBookmarks, pendingEvents) => {
    const byId = new Map(
      (Array.isArray(serverBookmarks) ? serverBookmarks : [])
        .filter((bookmark) => bookmark && bookmark.id)
        .map((bookmark) => [bookmark.id, { ...bookmark }]));

    const ordered = (Array.isArray(pendingEvents) ? pendingEvents : [])
      .slice()
      .sort((a, b) => Date.parse(a.clientTimestampUtc || 0) - Date.parse(b.clientTimestampUtc || 0));

    for (const event of ordered) {
      if (!event || !event.bookmarkId) continue;

      if (event.type === "remove") {
        byId.delete(event.bookmarkId);
        continue;
      }

      byId.set(event.bookmarkId, {
        id: event.bookmarkId,
        chapterId: event.chapterId,
        positionPermille: event.positionPermille,
        language: event.language,
        paragraphIndex: event.paragraphIndex,
        characterOffset: event.characterOffset,
        anchorText: event.anchorText || "",
        label: event.label || "",
        style: event.style || null,
        color: event.color || null
      });
    }

    return Array.from(byId.values());
  };

  const ANCHOR_TEXT_LIMIT = 180;

  /**
   * Mirrors Features/Novels/NovelTextLayout.CreateAnchorText exactly:
   * whitespace-normalized, truncated preview text stored on a bookmark so it
   * can still be recognized/displayed without a round trip, including one
   * built client-side while offline.
   */
  const createAnchorText = (paragraph) => {
    if (!paragraph || !String(paragraph).trim()) return null;
    const normalized = String(paragraph).trim().replace(/\s+/g, " ");
    return normalized.length <= ANCHOR_TEXT_LIMIT
      ? normalized
      : normalized.slice(0, ANCHOR_TEXT_LIMIT);
  };

  /** Mirrors Features/Novels/NovelTextLayout.SplitParagraphs exactly (blank-line-separated paragraphs, trimmed, empties dropped). */
  const splitParagraphs = (text) => {
    if (!text || !String(text).trim()) return [];
    const normalized = String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n").trim();
    return normalized
      .split(/\n\s*\n+/)
      .map((paragraph) => paragraph.trim())
      .filter((paragraph) => paragraph.length > 0);
  };

  const escapeHtml = (value) => String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");

  /** Mirrors Features/Novels/NovelChapterDocument.RenderRuns exactly (ruby/emphasis/strong nesting). */
  const renderInlineRuns = (runs) => (Array.isArray(runs) ? runs : [])
    .map((run) => {
      let html = run.ruby
        ? `<ruby>${escapeHtml(run.text)}<rt data-rt="${escapeHtml(run.ruby)}"></rt></ruby>`
        : escapeHtml(run.text);
      if (run.emphasis) html = `<em>${html}</em>`;
      if (run.strong) html = `<strong>${html}</strong>`;
      return html;
    })
    .join("");

  /**
   * Renders an offline chapter payload's `blocks` (see
   * ClientOfflineLibraryContentBlock) into the exact markup shape
   * Pages/Novels/Read.cshtml renders server-side (novel-reader-segment /
   * novel-reader-paragraph, matching data-reader-paragraph/data-language/
   * data-index attributes) so every other reader module (position tracking,
   * selection, TTS) keeps working unmodified against it. `germanParagraphs`
   * is the already-split German translation (see splitParagraphs), aligned
   * by paragraph index exactly like the server does.
   */
  const renderNovelBlocksHtml = (blocks, { germanParagraphs } = {}) => {
    const german = Array.isArray(germanParagraphs) ? germanParagraphs : [];
    let paragraphIndex = 0;
    const parts = [];

    for (const block of Array.isArray(blocks) ? blocks : []) {
      if (block.kind === "img") {
        if (block.imageAssetUrl) {
          parts.push(
            `<figure class="novel-reader-illustration">` +
            `<img src="${escapeHtml(block.imageAssetUrl)}" alt="${escapeHtml(block.imageAlt || "")}" loading="lazy" decoding="async" />` +
            `</figure>`);
        }
        continue;
      }

      const index = paragraphIndex++;
      const isHeading = block.kind === "h";
      const headingClass = isHeading ? " is-heading" : "";
      const headingLevel = isHeading ? Math.min((Number(block.level) || 0) + 1, 6) : null;
      const roleAttr = isHeading ? ` role="heading"` : "";
      const levelAttr = headingLevel ? ` aria-level="${headingLevel}"` : "";

      let section = `<section class="novel-reader-segment" data-reader-segment="${index}">` +
        `<p class="novel-reader-paragraph ja${headingClass}" lang="ja"${roleAttr}${levelAttr} ` +
        `data-reader-paragraph data-language="ja" data-index="${index}">${renderInlineRuns(block.runs)}</p>`;

      if (index < german.length) {
        section += `<p class="novel-reader-paragraph de${headingClass}" lang="de"${roleAttr}${levelAttr} ` +
          `data-reader-paragraph data-language="de" data-index="${index}">${escapeHtml(german[index])}</p>`;
      }

      section += `</section>`;
      parts.push(section);
    }

    return parts.join("");
  };

  /** Mirrors Pages/Books/Read.cshtml's plain `<p data-book-paragraph="index">` column markup. */
  const renderBookParagraphsHtml = (paragraphs) => (Array.isArray(paragraphs) ? paragraphs : [])
    .map((text, index) => `<p data-book-paragraph="${index}">${escapeHtml(text)}</p>`)
    .join("");

  // ---- adapters (IndexedDB/network; not unit tested directly, mirrors offline-library-manager.js) ----

  const sharedManager = () => window.AniLingoOfflineLibraryManager.getSharedManager();

  const newEventId = () =>
    (window.crypto?.randomUUID ? window.crypto.randomUUID() : `${Date.now()}-${Math.random()}`);

  /** Fire-and-forget: a failed opportunistic drain simply leaves the event queued for the next attempt. */
  const opportunisticDrain = (instance) => {
    if (navigator.onLine) {
      instance.drainSyncQueue().catch(() => { /* retried on the next queue write or reconnect */ });
    }
  };

  /** Binds the canonical write/read operations above to one work, for a reader page. */
  const forWork = (workId) => {
    const queueProgress = async (fields) => {
      const instance = await sharedManager();
      const event = buildProgressEvent({
        ...fields,
        workId,
        clientEventId: fields.clientEventId || newEventId()
      });
      await instance.queueSyncEvent("progress", event);
      opportunisticDrain(instance);
      return event;
    };

    const queueBookmarkUpsert = async (fields) => {
      const instance = await sharedManager();
      const bookmarkId = fields.bookmarkId || newEventId();
      const event = buildBookmarkEvent({
        ...fields,
        type: "upsert",
        workId,
        bookmarkId,
        clientEventId: fields.clientEventId || newEventId()
      });
      await instance.queueSyncEvent("bookmark", event);
      opportunisticDrain(instance);

      return {
        id: bookmarkId,
        chapterId: event.chapterId,
        positionPermille: event.positionPermille,
        language: event.language,
        paragraphIndex: event.paragraphIndex,
        characterOffset: event.characterOffset,
        anchorText: event.anchorText || "",
        label: event.label || "",
        style: event.style,
        color: event.color
      };
    };

    const queueBookmarkRemove = async (bookmarkId, fields = {}) => {
      const instance = await sharedManager();
      const event = buildBookmarkEvent({
        ...fields,
        type: "remove",
        workId,
        bookmarkId,
        clientEventId: fields.clientEventId || newEventId()
      });
      await instance.queueSyncEvent("bookmark", event);
      opportunisticDrain(instance);
    };

    /** Pending (not yet acknowledged) bookmark events for one chapter of this work, oldest first. */
    const pendingBookmarkEvents = async (chapterId) => {
      const instance = await sharedManager();
      const events = await instance.listPendingSyncEvents();
      return events
        .filter((entry) => entry.kind === "bookmark")
        .map((entry) => entry.payload)
        .filter((payload) => payload.workId === workId && (!chapterId || payload.chapterId === chapterId));
    };

    const findLocalChapter = async (chapterId) => {
      const instance = await sharedManager();
      return instance.loadLocalChapter(workId, chapterId);
    };

    return Object.freeze({
      queueProgress,
      queueBookmarkUpsert,
      queueBookmarkRemove,
      pendingBookmarkEvents,
      findLocalChapter
    });
  };

  /**
   * Wires offline-aware in-page chapter navigation for a reader's footer
   * previous/next chapter links: while the browser is offline, following one
   * of these links to a downloaded chapter renders it locally instead of
   * attempting (and failing) a full page navigation; following one to a
   * chapter that has not been downloaded shows a clear notice instead of a
   * broken/generic offline page. Online, this never engages — the existing
   * full-page navigation is unchanged and remains the single online path.
   *
   * Deliberately narrow (#221 part 2A): only the footer previous/next links
   * are covered (always present in the initial server-rendered markup, no
   * network needed to discover them). The chapter drawer's list itself still
   * requires network to load; see docs/OFFLINE_LIBRARY.md's Part 2 TODO.
   */
  const initializeOfflineChapterNavigation = ({ shell, workId, linkSelector, contentSelector, renderer }) => {
    if (!shell || !workId || !linkSelector || !contentSelector) return;

    const repository = forWork(workId);

    const applyLocalChapter = async (chapterId) => {
      const { available, payload } = await repository.findLocalChapter(chapterId);
      const container = shell.querySelector(contentSelector);
      if (!available || !payload || !container) {
        shell.dispatchEvent(new CustomEvent("anilingo:offline-chapter-missing", {
          detail: { chapterId }
        }));
        return false;
      }

      if (renderer === "book") {
        const original = splitParagraphs(payload.originalText);
        container.innerHTML = renderBookParagraphsHtml(original);
      } else {
        const german = (payload.translations || []).find((t) => t.targetLanguage === "de");
        container.innerHTML = renderNovelBlocksHtml(
          payload.blocks,
          { germanParagraphs: german ? splitParagraphs(german.text) : [] });
      }

      shell.dataset.chapterId = chapterId;
      if (payload.number !== undefined) shell.dataset.chapterNumber = String(payload.number);
      if (payload.title) shell.dataset.chapterTitle = payload.title;
      window.scrollTo({ top: 0, behavior: "auto" });

      shell.dispatchEvent(new CustomEvent("anilingo:offline-chapter-changed", {
        detail: { chapterId, payload }
      }));
      return true;
    };

    document.addEventListener("click", (event) => {
      if (navigator.onLine) return;

      const link = event.target.closest(linkSelector);
      if (!link || !shell.contains(link)) return;

      let url;
      try {
        url = new URL(link.href, window.location.origin);
      } catch {
        return;
      }

      const chapterId = url.pathname.split("/").filter(Boolean).pop();
      if (!chapterId) return;

      event.preventDefault();
      void applyLocalChapter(chapterId).then((applied) => {
        if (applied) {
          window.history.pushState({}, "", url.pathname + url.search);
        }
      });
    }, true);
  };

  window.AniLingoOfflineLibraryRepository = Object.freeze({
    resolveChapterSource,
    buildProgressEvent,
    buildBookmarkEvent,
    mergePendingBookmarks,
    createAnchorText,
    splitParagraphs,
    renderNovelBlocksHtml,
    renderBookParagraphsHtml,
    forWork,
    initializeOfflineChapterNavigation
  });
})();
