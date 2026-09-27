// Settings → AI (#422): shows only the fields of the chosen provider and only the reasoning
// efforts the chosen model lists. Without JavaScript the server-rendered state still applies.
(() => {
    const form = document.querySelector("[data-ai-settings]");
    if (!form) {
        return;
    }

    const provider = form.querySelector("[data-ai-provider]");
    const syncProvider = () => {
        form.querySelectorAll("[data-ai-provider-fields]").forEach(element => {
            element.hidden = element.dataset.aiProviderFields !== provider.value;
        });
    };
    provider?.addEventListener("change", syncProvider);

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
})();
