// Settings → AI (#422): shows only the fields of the chosen provider and only the reasoning
// efforts the chosen model lists, and loads the model list once after the page rendered when it
// was never loaded. Without JavaScript the server-rendered state and "Refresh models" still apply.
(() => {
    const form = document.querySelector("[data-ai-settings]");
    if (!form) {
        return;
    }

    const savedProvider = form.dataset.savedProvider;
    const provider = form.querySelector("[data-ai-provider]");
    let dirty = false;
    form.addEventListener("input", () => { dirty = true; });
    form.addEventListener("change", () => { dirty = true; });

    // Per-task overrides, the preset and the connection state describe the saved provider; after a
    // provider switch they only apply once the new provider was saved.
    const syncProvider = () => {
        const current = provider?.value ?? savedProvider;
        form.querySelectorAll("[data-ai-provider-fields]").forEach(element => {
            element.hidden = element.dataset.aiProviderFields !== current;
        });
        form.querySelectorAll("[data-ai-saved-only]").forEach(element => {
            element.hidden = current !== savedProvider;
        });
        form.querySelectorAll("[data-ai-switched-only]").forEach(element => {
            element.hidden = current === savedProvider;
        });
    };
    provider?.addEventListener("change", syncProvider);
    syncProvider();

    // Personal picker: "Other model ID…" reveals the free-text field (always visible without JS).
    const personalModel = form.querySelector("[data-ai-personal-model]");
    const customModel = form.querySelector("[data-ai-custom-model]");
    if (personalModel && customModel) {
        const syncCustom = () => {
            customModel.hidden = personalModel.value !== "__custom";
        };
        personalModel.addEventListener("change", () => {
            syncCustom();
            if (!customModel.hidden) {
                customModel.focus();
            }
        });
        syncCustom();
    }

    const model = form.querySelector("[data-ai-model]");
    const effort = form.querySelector("[data-ai-effort]");
    const effortField = form.querySelector("[data-ai-effort-field]");
    if (model && effort && effortField) {
        const efforts = JSON.parse(effort.dataset.efforts || "{}");
        const syncEfforts = () => {
            const allowed = efforts[model.value] || [];
            let available = 0;
            Array.from(effort.options).forEach(option => {
                if (!option.value) {
                    return;
                }
                option.hidden = !allowed.includes(option.value);
                available += option.hidden ? 0 : 1;
            });
            if (effort.selectedOptions[0]?.hidden) {
                effort.value = "";
            }
            effortField.hidden = available === 0;
        };
        model.addEventListener("change", syncEfforts);
    }

    const discoverUrl = form.dataset.discoverUrl;
    if (!discoverUrl) {
        return;
    }

    const status = form.querySelector(`[data-ai-catalog-status="${savedProvider}"]`);
    const warning = form.querySelector("[data-ai-model-warning]");
    const token = form.querySelector("input[name='__RequestVerificationToken']")?.value;
    if (status) {
        status.textContent = form.dataset.labelLoading;
    }

    const body = new FormData();
    if (token) {
        body.append("__RequestVerificationToken", token);
    }

    fetch(discoverUrl, { method: "POST", body, credentials: "same-origin", headers: { Accept: "application/json" } })
        .then(response => response.ok ? response.json() : Promise.reject(new Error(String(response.status))))
        .then(result => {
            if (result.models > 0) {
                if (!dirty) {
                    window.location.reload();
                    return;
                }

                // Keep the user's edits: offer the picker instead of replacing the page under them.
                if (status) {
                    status.textContent = result.status;
                    const show = document.createElement("button");
                    show.type = "button";
                    show.className = "button ai-button-small";
                    show.textContent = form.dataset.labelShow;
                    show.addEventListener("click", () => window.location.reload());
                    status.after(show);
                }
                return;
            }

            if (status) {
                status.textContent = result.status;
            }
            if (warning) {
                warning.hidden = false;
            }
        })
        .catch(() => {
            if (status) {
                status.textContent = form.dataset.labelFailed;
            }
            if (warning) {
                warning.hidden = false;
            }
        });
})();
