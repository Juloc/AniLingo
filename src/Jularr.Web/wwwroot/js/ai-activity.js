// Live AI activity (#422): renders the list pushed over server-sent events and lets the user cancel
// active requests. Without JavaScript the server-rendered list stays as a snapshot.
(() => {
    const root = document.querySelector("[data-ai-activity]");
    if (!root || !("EventSource" in window)) {
        return;
    }

    const config = JSON.parse(root.querySelector("[data-ai-activity-config]").textContent);
    const list = root.querySelector("[data-ai-activity-list]");
    const empty = root.querySelector("[data-ai-activity-empty]");
    const labels = config.labels;
    const numberFormat = new Intl.NumberFormat(document.documentElement.lang || undefined);
    let items = [];
    let receivedAt = Date.now();

    const format = (template, values) =>
        Object.entries(values).reduce((text, [name, value]) => text.replaceAll(`{${name}}`, String(value)), template);

    const duration = ms => {
        const total = Math.max(0, Math.floor(ms / 1000));
        const hours = Math.floor(total / 3600);
        const minutes = Math.floor((total % 3600) / 60);
        const seconds = String(total % 60).padStart(2, "0");
        return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${seconds}` : `${minutes}:${seconds}`;
    };

    const span = (text, className) => {
        const element = document.createElement("span");
        element.textContent = text;
        if (className) {
            element.className = className;
        }
        return element;
    };

    const cancel = async (id, button) => {
        button.disabled = true;
        try {
            const response = await fetch(config.cancel.replace("{id}", encodeURIComponent(id)), {
                method: "POST",
                credentials: "same-origin",
                headers: { RequestVerificationToken: config.token }
            });
            if (!response.ok && response.status !== 409) {
                button.disabled = false;
                button.title = labels.cancelFailed;
            }
        } catch {
            button.disabled = false;
            button.title = labels.cancelFailed;
        }
    };

    const render = () => {
        empty.hidden = items.length > 0;
        list.replaceChildren(...items.map(item => {
            const row = document.createElement("li");
            row.className = "ai-activity-item";
            row.dataset.active = item.active ? "true" : "false";

            const line = document.createElement("div");
            line.className = "ai-activity-line";
            const title = document.createElement("strong");
            title.textContent = labels.operations[item.operation] || item.operation;
            line.append(title, span(labels.states[item.state] || item.state, `status-pill ai-state-${item.state}`));
            const elapsed = span(duration(item.elapsedMs), "ai-activity-elapsed");
            elapsed.dataset.elapsed = String(item.elapsedMs);
            line.append(elapsed);

            if (item.active) {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "button";
                button.textContent = labels.cancel;
                button.addEventListener("click", () => void cancel(item.id, button));
                line.append(button);
            }

            const meta = document.createElement("div");
            meta.className = "ai-activity-meta muted";
            if (item.profileId) {
                meta.append(span(`${labels.profile} ${(config.profiles && config.profiles[item.profileId]) || item.profileId}`));
            }
            const model = item.model || item.providerId;
            meta.append(span(item.reasoningEffort ? `${model} · ${item.reasoningEffort}` : model));
            if (item.usage) {
                meta.append(span(format(labels.tokens, {
                    input: (item.usage.estimated ? "~" : "") + numberFormat.format(item.usage.input),
                    output: numberFormat.format(item.usage.output)
                })));
                if (item.usage.cached > 0) {
                    meta.append(span(format(labels.cached, { count: numberFormat.format(item.usage.cached) })));
                }
                if (item.usage.reasoning > 0) {
                    meta.append(span(format(labels.reasoning, { count: numberFormat.format(item.usage.reasoning) })));
                }
            }
            if (item.contextWindowPercent != null) {
                meta.append(span(format(labels.context, { percent: `${item.contextWindowPercent} %` })));
            }
            if (item.contextTokens > 0) {
                meta.append(span(format(labels.sharedContext, { count: numberFormat.format(item.contextTokens) })));
            }
            if (item.progressTotal > 0) {
                meta.append(span(format(labels.progress, { current: item.progressCurrent || 0, total: item.progressTotal })));
            }
            if (item.retries > 0) {
                meta.append(span(format(labels.retries, { count: item.retries })));
            }

            row.append(line, meta);
            if (item.error) {
                const error = document.createElement("p");
                error.className = "validation";
                error.textContent = item.error;
                row.append(error);
            }
            return row;
        }));
    };

    // Elapsed time of active rows advances locally between pushes.
    setInterval(() => {
        const delta = Date.now() - receivedAt;
        list.querySelectorAll("[data-active='true'] [data-elapsed]").forEach(element => {
            element.textContent = duration(Number(element.dataset.elapsed) + delta);
        });
    }, 1000);

    const source = new EventSource(config.stream, { withCredentials: true });
    source.addEventListener("activity", event => {
        try {
            items = JSON.parse(event.data);
            receivedAt = Date.now();
            render();
        } catch {
            // Keep the last rendered state when a message cannot be read.
        }
    });
})();
