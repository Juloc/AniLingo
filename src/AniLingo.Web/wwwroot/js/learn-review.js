const stage = document.querySelector("[data-review-stage]");

if (stage && window.AniLingoOfflineReviews) {
  const store = window.AniLingoOfflineReviews;
  const profileId = stage.dataset.profileId;
  const syncUrl = stage.dataset.syncUrl;
  const dueTemplate = stage.dataset.dueTemplate || "{count}";
  const noAnswer = stage.dataset.noAnswer || "";
  const sessionNode = stage.querySelector("[data-review-session]");
  const antiForgeryToken = stage
    .querySelector('input[name="__RequestVerificationToken"]')
    ?.value;

  let cards = [];
  let modeLabels = {};
  let currentIndex = 0;
  let syncing = false;

  try {
    cards = JSON.parse(sessionNode?.textContent || "[]");
  } catch {
    cards = [];
  }

  try {
    modeLabels = JSON.parse(stage.dataset.modeLabels || "{}");
  } catch {
    modeLabels = {};
  }

  store.setActiveProfile(profileId);

  const speech = window.AniLingoTts?.createDeviceProvider();
  const details = stage.querySelector("[data-review-details]");
  const reveal = stage.querySelector("[data-review-reveal]");
  const form = stage.querySelector("[data-review-form]");
  const listen = stage.querySelector("[data-review-listen]");
  const writeInput = stage.querySelector("[data-review-write-input]");
  const ratingButtons = [...stage.querySelectorAll("[data-review-rating]")];

  if (listen && !speech?.supported) {
    listen.disabled = true;
  }

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

  const speakPrompt = () => {
    const card = cards[currentIndex];
    if (!card || !speech?.supported) {
      return;
    }

    void speech.speak({ text: card.prompt, language: card.promptLanguage }).catch(() => {});
  };

  reveal?.addEventListener("click", event => {
    event.preventDefault();
    revealAnswer();
  });

  listen?.addEventListener("click", speakPrompt);

  writeInput?.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      revealAnswer();
    }
  });

  function interval(card, rating) {
    return card?.intervals?.[rating] ?? "—";
  }

  function setText(selector, text, lang) {
    const node = stage.querySelector(selector);
    if (!node) {
      return null;
    }

    node.textContent = text ?? "";
    if (lang) {
      node.lang = lang;
    }

    return node;
  }

  function renderCard(card, remaining) {
    if (!card) {
      location.reload();
      return;
    }

    const listening = card.mode === "Listening";
    const writing = card.mode === "Writing";

    stage.querySelector(".review-counter").textContent =
      dueTemplate.replace("{count}", String(remaining));
    setText("[data-review-mode]", modeLabels[card.mode] ?? card.mode);

    if (listen) {
      listen.hidden = !listening;
    }

    setText("[data-review-prompt]", card.prompt, card.promptLanguage).hidden = listening;
    setText("[data-review-prompt-reading]", card.promptReading, card.promptLanguage)
      .hidden = listening || !card.promptReading;

    const write = stage.querySelector("[data-review-write]");
    if (write) {
      write.hidden = !writing;
    }

    if (writeInput) {
      writeInput.value = "";
      writeInput.lang = card.answerLanguage ?? "";
    }

    setText(
      "[data-review-heard]",
      [card.prompt, card.promptReading].filter(Boolean).join(" · "),
      card.promptLanguage).hidden = !listening;
    setText("[data-review-answer]", card.answer || noAnswer, card.answerLanguage);
    setText("[data-review-answer-reading]", card.answerReading, card.answerLanguage)
      .hidden = !card.answerReading;

    const cardInput = stage.querySelector("[data-review-card-id]");
    if (cardInput) {
      cardInput.value = card.cardId;
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
        const sentence = context.querySelector("[data-review-sentence]");
        sentence.textContent = card.context.sentence ?? "";
        sentence.lang = card.promptLanguage ?? "";
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

    if (writing) {
      writeInput?.focus();
    } else if (listening) {
      speakPrompt();
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
      await store.queueEvent(profileId, card.cardId, rating);

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
