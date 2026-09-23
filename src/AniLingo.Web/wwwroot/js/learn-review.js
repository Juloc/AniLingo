const stage = document.querySelector("[data-review-stage]");

if (stage) {
  const details = stage.querySelector("[data-review-details]");
  const reveal = stage.querySelector("[data-review-reveal]");
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
    if (!rating) {
      return;
    }

    const button = ratingButtons.find(item => item.dataset.reviewRating === rating);
    if (!button) {
      return;
    }

    event.preventDefault();
    button.click();
  });
}
