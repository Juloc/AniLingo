const stage = document.querySelector("[data-review-stage]");

if (stage && window.AniLingoOfflineReviews) {
  const store = window.AniLingoOfflineReviews;
  const profileId = stage.dataset.profileId;
  const syncUrl = stage.dataset.syncUrl;
  const sessionNode = stage.querySelector("[data-review-session]");
  const antiForgeryToken = stage
    .querySelector('input[name="__RequestVerificationToken"]')
    ?.value;

  let cards = [];
  let currentIndex = 0;
  let syncing = false;

  try {
    cards = JSON.parse(sessionNode?.textContent || "[]");
  } catch {
    cards = [];
  }

  store.setActiveProfile(profileId);

  const details = stage.querySelector("[data-review-details]");
  const reveal = stage.querySelector("[data-review-reveal]");
  const form = stage.querySelector("[data-review-form]");
  const ratingButtons = [...stage.querySelectorAll("[data-review-rating]")];

  const revealAnswer = () => {
    if (!details) {
      return false;
    }

    if (!details.open) {
      details.open = true;
      requestAnimationFrame(() => {
        ratingButtons.find(button => button.dataset.reviewRating === "Good")?.focus();
      });
    }

    return true;
  };

  reveal?.addEventListener("click", event => {
    event.preventDefault();
    revealAnswer();
  });

  function interval(card, rating) {
    return card?.intervals?.[rating] ?? "—";
  }

  function renderCard(card, remaining) {
    if (!card) {
      location.reload();
      return;
    }

    stage.querySelector(".review-counter").textContent = remaining + " due";
    stage.querySelector(".review-term").textContent = card.canonical ?? "";

    const reading = stage.querySelector(".review-reading");
    if (reading) {
      reading.textContent = card.reading ?? "";
      reading.hidden = !card.reading;
    }

    stage.querySelector(".review-answer").textContent =
      card.meaning || "No local dictionary meaning available.";

    const termInput = stage.querySelector("[data-review-term-id]");
    if (termInput) {
      termInput.value = card.termId;
    }

    for (const button of ratingButtons) {
      const label = button.dataset.reviewRating;
      const span = button.querySelector("span");
      if (span) {
        span.textContent = interval(card, label);
      }
    }

    const context = stage.querySelector("[data-review-context]");
    if (context) {
      if (card.context) {
        context.hidden = false;
        context.querySelector("[data-review-sentence]").textContent =
          card.context.sentence ?? "";
        const link = context.querySelector("[data-review-scene]");
        if (link) {
          link.href = "/Library/Episode/" + card.context.episodeId
            + "?at=" + Math.max(0, card.context.cueStartMs ?? 0);
          link.textContent = [
            card.context.animeTitle,
            "S" + String(card.context.seasonNumber).padStart(2, "0"),
            "E" + String(card.context.episodeNumber).padStart(2, "0"),
            card.context.episodeTitle
          ].filter(Boolean).join(" · ");
        }
        context.querySelector("[data-review-enrichment]")?.setAttribute("hidden", "");
      } else {
        context.hidden = true;
      }
    }

    if (details) {
      details.open = false;
    }
  }

  async function syncPending() {
    if (syncing || !navigator.onLine || !antiForgeryToken) {
      return null;
    }

    syncing = true;
    try {
      return await store.sync(profileId, syncUrl, antiForgeryToken);
    } finally {
      syncing = false;
    }
  }

  async function bootstrap() {
    const pending = await store.listEvents(profileId);

    if (pending.length && navigator.onLine) {
      try {
        const result = await syncPending();
        if ((result?.synced ?? 0) > 0 || (result?.rejected ?? 0) > 0) {
          location.reload();
          return;
        }
      } catch {
        // Keep pending events. They will be retried with the same IDs.
      }
    }

    await store.saveSession(profileId, cards);
  }

  form?.addEventListener("submit", async event => {
    const submitter = event.submitter;
    const rating = submitter?.dataset.reviewRating;
    const card = cards[currentIndex];

    if (!rating || !card || !crypto?.randomUUID) {
      return;
    }

    event.preventDefault();
    ratingButtons.forEach(button => {
      button.disabled = true;
    });

    try {
      await store.queueEvent(profileId, card.termId, rating);

      if (navigator.onLine) {
        try {
          const result = await syncPending();
          if ((result?.synced ?? 0) > 0 || (result?.rejected ?? 0) > 0) {
            location.reload();
            return;
          }
        } catch {
          // Network state is ambiguous. Keep the event and continue offline.
        }
      }

      currentIndex += 1;
      renderCard(cards[currentIndex], Math.max(0, cards.length - currentIndex));
    } finally {
      ratingButtons.forEach(button => {
        button.disabled = false;
      });
    }
  });

  document.addEventListener("keydown", event => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }

    const target = event.target;
    if (target instanceof HTMLInputElement ||
        target instanceof HTMLTextAreaElement ||
        target instanceof HTMLSelectElement) {
      return;
    }

    if ((event.key === " " || event.key === "Enter") && !details?.open) {
      event.preventDefault();
      revealAnswer();
      return;
    }

    if (!details?.open) {
      return;
    }

    const shortcuts = {
      "1": "Again",
      "2": "Hard",
      "3": "Good",
      "4": "Easy"
    };
    const rating = shortcuts[event.key];
    const button = ratingButtons.find(item => item.dataset.reviewRating === rating);
    if (!button) {
      return;
    }

    event.preventDefault();
    button.click();
  });

  window.addEventListener("online", async () => {
    try {
      const result = await syncPending();
      if ((result?.synced ?? 0) > 0 || (result?.rejected ?? 0) > 0) {
        location.reload();
      }
    } catch {
      // Keep the durable queue for the next reconnect.
    }
  });

  void bootstrap();
}
