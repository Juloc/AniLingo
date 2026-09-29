// Visual builder for a smart collection's nested ALL/ANY rule tree (#427). It renders a rule tree of
// groups and conditions, keeps an in-memory model, and serializes it to the shape the server's
// CollectionRuleSerializer reads: { type:"group", combinator:"all|any", children:[...] } and
// { type:"condition", field, operator, value }.
(function () {
    "use strict";

    const root = document.getElementById("rules-builder");
    const form = document.getElementById("rules-form");
    const hidden = document.getElementById("rules-json");
    if (!root || !form || !hidden) {
        return;
    }

    const fields = JSON.parse(root.getAttribute("data-fields") || "[]");
    const fieldByValue = {};
    for (const field of fields) {
        fieldByValue[field.value] = field;
    }

    function parseInitial() {
        try {
            const parsed = JSON.parse(root.getAttribute("data-rule") || "null");
            if (parsed && parsed.type === "group") {
                return parsed;
            }
            if (parsed && parsed.type === "condition") {
                return { type: "group", combinator: "all", children: [parsed] };
            }
        } catch (error) {
            // Fall through to a fresh tree.
        }
        return { type: "group", combinator: "all", children: [] };
    }

    let model = parseInitial();

    function newCondition() {
        const first = fields[0];
        return {
            type: "condition",
            field: first.value,
            operator: first.operators[0],
            value: defaultValue(first)
        };
    }

    function defaultValue(field) {
        if (field.editor === "enum" && field.values.length > 0) {
            return field.values[0].value;
        }
        return "";
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined) {
            node.textContent = text;
        }
        return node;
    }

    function renderGroup(group, parent, onRemove) {
        const box = el("div", "rules-group");

        const head = el("div", "rules-group-head");
        const toggle = el("div", "rules-combinator");
        ["all", "any"].forEach(function (value) {
            const button = el("button", "rules-combinator-option" + (group.combinator === value ? " is-active" : ""), value.toUpperCase());
            button.type = "button";
            button.addEventListener("click", function () {
                group.combinator = value;
                render();
            });
            toggle.appendChild(button);
        });
        head.appendChild(toggle);
        head.appendChild(el("span", "rules-group-hint", group.combinator === "all" ? "match every rule below" : "match any rule below"));

        if (onRemove) {
            const remove = el("button", "collections-link-button", "Remove group");
            remove.type = "button";
            remove.addEventListener("click", onRemove);
            head.appendChild(remove);
        }
        box.appendChild(head);

        const children = el("div", "rules-children");
        group.children.forEach(function (child, index) {
            const removeChild = function () {
                group.children.splice(index, 1);
                render();
            };
            if (child.type === "group") {
                renderGroup(child, children, removeChild);
            } else {
                children.appendChild(renderCondition(child, removeChild));
            }
        });
        box.appendChild(children);

        const actions = el("div", "rules-group-actions");
        const addCondition = el("button", "collections-link-button", "+ Condition");
        addCondition.type = "button";
        addCondition.addEventListener("click", function () {
            group.children.push(newCondition());
            render();
        });
        const addGroup = el("button", "collections-link-button", "+ Group");
        addGroup.type = "button";
        addGroup.addEventListener("click", function () {
            group.children.push({ type: "group", combinator: "all", children: [] });
            render();
        });
        actions.appendChild(addCondition);
        actions.appendChild(addGroup);
        box.appendChild(actions);

        parent.appendChild(box);
    }

    function renderCondition(condition, onRemove) {
        const row = el("div", "rules-condition");

        const fieldSelect = el("select", "rules-field");
        fields.forEach(function (field) {
            const option = el("option", null, field.label);
            option.value = field.value;
            if (field.value === condition.field) {
                option.selected = true;
            }
            fieldSelect.appendChild(option);
        });
        fieldSelect.addEventListener("change", function () {
            condition.field = fieldSelect.value;
            const field = fieldByValue[condition.field];
            condition.operator = field.operators[0];
            condition.value = defaultValue(field);
            render();
        });
        row.appendChild(fieldSelect);

        const field = fieldByValue[condition.field];
        const operatorSelect = el("select", "rules-operator");
        field.operators.forEach(function (op) {
            const option = el("option", null, op);
            option.value = op;
            if (op === condition.operator) {
                option.selected = true;
            }
            operatorSelect.appendChild(option);
        });
        operatorSelect.addEventListener("change", function () {
            condition.operator = operatorSelect.value;
            render();
        });
        row.appendChild(operatorSelect);

        const needsValue = condition.operator !== "isPresent" && condition.operator !== "isAbsent";
        if (needsValue && field.editor === "enum") {
            const valueSelect = el("select", "rules-value");
            field.values.forEach(function (value) {
                const option = el("option", null, value.label);
                option.value = value.value;
                if (value.value === condition.value) {
                    option.selected = true;
                }
                valueSelect.appendChild(option);
            });
            valueSelect.addEventListener("change", function () { condition.value = valueSelect.value; });
            row.appendChild(valueSelect);
        } else if (needsValue && field.editor !== "none") {
            const valueInput = el("input", "rules-value");
            valueInput.type = field.editor === "number" ? "number" : "text";
            valueInput.value = condition.value || "";
            valueInput.addEventListener("input", function () { condition.value = valueInput.value; });
            row.appendChild(valueInput);
        }

        const remove = el("button", "collections-link-button", "Remove");
        remove.type = "button";
        remove.addEventListener("click", onRemove);
        row.appendChild(remove);

        return row;
    }

    function render() {
        root.textContent = "";
        renderGroup(model, root, null);
    }

    form.addEventListener("submit", function () {
        hidden.value = JSON.stringify(model);
    });

    render();
})();
