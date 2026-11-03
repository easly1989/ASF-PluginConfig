"use strict";

// Builds a form from a schema in the subset PluginSchema accepts, and reads it back into a JSON value.
// Every field carries data-path (its dotted path in the value) so server errors land next to it.
const SchemaForm = (() => {
	let counter = 0;

	function element(tag, properties = {}, ...children) {
		const node = Object.assign(document.createElement(tag), properties);
		node.append(...children.filter(child => child !== null && child !== undefined));

		return node;
	}

	function label(name, schema, required) {
		return `${schema.title || name}${required ? " (required)" : ""}`;
	}

	function field(name, schema, storedValue, path, required) {
		// A stored null means unset, as for a missing key
		const value = storedValue ?? undefined;
		const id = `field-${++counter}`;
		const wrapper = element("div", { className: "field" });
		const describedBy = [];
		let description = null;

		if (schema.description) {
			description = element("p", { className: "hint", id: `${id}-hint`, textContent: schema.description });
			describedBy.push(description.id);
		}

		const error = element("p", { className: "error", id: `${id}-error`, hidden: true });
		error.dataset.errorFor = path;
		describedBy.push(error.id);
		let input;
		// A boolean without a default may be meant to stay unset (a per-bot value inheriting the global one), so it gets
		// a three-way choice instead of a checkbox that would always write true or false
		const choices = schema.enum ?? (schema.type === "boolean" && schema.default === undefined ? [true, false] : null);

		if (schema.type === "boolean" && !choices) {
			input = element("input", { type: "checkbox", id, checked: value ?? schema.default ?? false });
			wrapper.classList.add("checkbox");
			wrapper.append(input, element("label", { htmlFor: id, textContent: label(name, schema, false) }));
		} else {
			wrapper.append(element("label", { htmlFor: id, textContent: label(name, schema, required) }));

			if (choices) {
				input = element("select", { id, required });

				if (!required) {
					input.append(element("option", { value: "", textContent: schema.default !== undefined ? `Default (${schema.default})` : "Not set" }));
				} else if (value === undefined && schema.default === undefined) {
					input.append(element("option", { value: "", textContent: "Choose…", disabled: true }));
				}

				for (const option of choices) {
					input.append(element("option", { value: JSON.stringify(option), textContent: option === true ? "Yes" : option === false ? "No" : String(option) }));
				}

				// A stored value the schema no longer allows stays visible instead of being dropped on the next save
				if (value !== undefined && !choices.some(option => JSON.stringify(option) === JSON.stringify(value))) {
					input.append(element("option", { value: JSON.stringify(value), textContent: `${String(value)} (current, not allowed)` }));
				}

				input.value = value !== undefined ? JSON.stringify(value) : (required && schema.default !== undefined ? JSON.stringify(schema.default) : "");
			} else if (schema.type === "array") {
				input = element("textarea", { id, rows: 3, value: Array.isArray(value) ? value.join("\n") : value === undefined ? "" : String(value) });
				const hint = element("p", { className: "hint", id: `${id}-format`, textContent: "One value per line." });
				describedBy.push(hint.id);
				wrapper.append(input, hint);
			} else {
				const numeric = schema.type === "integer" || schema.type === "number";
				input = element("input", {
					id,
					required,
					type: numeric ? "number" : schema.writeOnly ? "password" : schema.format === "uri" ? "url" : "text",
					value: value ?? "",
					autocomplete: schema.writeOnly ? "new-password" : "off"
				});

				if (numeric) {
					input.step = schema.type === "integer" ? "1" : "any";
					input.inputMode = schema.type === "integer" ? "numeric" : "decimal";

					if (schema.minimum !== undefined) {
						input.min = schema.minimum;
					}

					if (schema.maximum !== undefined) {
						input.max = schema.maximum;
					}
				}

				if (schema.default !== undefined) {
					input.placeholder = `Default: ${schema.default}`;
				}
			}

			if (!input.isConnected && !wrapper.contains(input)) {
				wrapper.append(input);
			}
		}

		input.dataset.path = path;
		input.setAttribute("aria-describedby", describedBy.join(" "));
		// append() would print a null as the text "null"
		wrapper.append(...[description, error].filter(Boolean));

		return wrapper;
	}

	function fields(schema, value, prefix) {
		const nodes = [];
		const required = new Set(schema.required ?? []);

		for (const [name, child] of Object.entries(schema.properties ?? {})) {
			const path = prefix ? `${prefix}.${name}` : name;
			const childValue = value?.[name];

			if (child.type === "object") {
				const fieldset = element("fieldset", {}, element("legend", { textContent: child.title || name }));

				if (child.description) {
					const hint = element("p", { className: "hint", id: `group-${++counter}`, textContent: child.description });
					fieldset.setAttribute("aria-describedby", hint.id);
					fieldset.append(hint);
				}

				fieldset.append(...fields(child, childValue, path));
				nodes.push(fieldset);
			} else {
				nodes.push(field(name, child, childValue, path, required.has(name)));
			}
		}

		return nodes;
	}

	function parseItem(text, type) {
		if (type === "integer" || type === "number") {
			return text === "" ? NaN : Number(text);
		}

		if (type === "boolean") {
			return text === "true" ? true : text === "false" ? false : text;
		}

		return text;
	}

	// Empty optional fields are left out, so the plugin's own default applies
	function read(schema, container, prefix) {
		const value = {};

		for (const [name, child] of Object.entries(schema.properties ?? {})) {
			const path = prefix ? `${prefix}.${name}` : name;

			// Sent even when empty, so stored keys the schema does not know survive inside the group
			if (child.type === "object") {
				value[name] = read(child, container, path);

				continue;
			}

			const input = container.querySelector(`[data-path="${CSS.escape(path)}"]`);

			if (!input) {
				continue;
			}

			if (input.type === "checkbox") {
				value[name] = input.checked;
			} else if (input.tagName === "SELECT") {
				if (input.value !== "") {
					value[name] = JSON.parse(input.value);
				}
			} else if (child.type === "array") {
				const items = input.value.split("\n").map(line => line.trim()).filter(line => line !== "");

				if (items.length > 0) {
					value[name] = items.map(item => parseItem(item, child.items.type));
				}
			} else if (input.validity?.badInput) {
				// Not a number: send null, so the server names the field instead of the value vanishing
				value[name] = null;
			} else if (input.value !== "") {
				value[name] = child.type === "string" ? input.value : Number(input.value);
			}
		}

		return value;
	}

	function showErrors(container, errors) {
		for (const slot of container.querySelectorAll("[data-error-for]")) {
			slot.hidden = true;
			slot.textContent = "";
		}

		for (const invalid of container.querySelectorAll("[aria-invalid]")) {
			invalid.removeAttribute("aria-invalid");
		}

		const unplaced = [];

		for (const { Path, Message } of errors) {
			// Array items report as Tags[1]; the whole list is one field
			const fieldPath = Path.replace(/\[\d+\]$/, "");
			const slot = container.querySelector(`[data-error-for="${CSS.escape(fieldPath)}"]`);

			if (!slot) {
				unplaced.push(Path ? `${Path}: ${Message}` : Message);

				continue;
			}

			slot.textContent = slot.textContent ? `${slot.textContent} ${Message}` : `${Path.slice(fieldPath.length)} ${Message}`.trim();
			slot.hidden = false;
			container.querySelector(`[data-path="${CSS.escape(fieldPath)}"]`)?.setAttribute("aria-invalid", "true");
		}

		// Focus the first invalid field in page order, not in the order the server listed them
		const firstSlot = container.querySelector("[data-error-for]:not([hidden])");
		firstSlot && container.querySelector(`[data-path="${CSS.escape(firstSlot.dataset.errorFor)}"]`)?.focus();

		return unplaced;
	}

	return { fields, read, showErrors };
})();
