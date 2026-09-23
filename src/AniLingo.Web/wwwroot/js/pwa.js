if ("serviceWorker" in navigator) {
  window.addEventListener("load", async () => {
    try {
      const registration = await navigator.serviceWorker.register("/service-worker.js", {
        scope: "/",
        updateViaCache: "none"
      });

      // Ask for a fresh worker definition on normal app loads. The browser
      // still controls update throttling; this avoids pinning old shell assets.
      void registration.update();
    } catch {
      // PWA support is optional. A registration failure must never block AniLingo.
    }
  });
}


document.addEventListener("submit", event => {
  if (event.target instanceof HTMLFormElement
      && event.target.matches("[data-offline-logout]")) {
    localStorage.removeItem("anilingo.activeProfile");
  }
});
