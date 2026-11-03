"use strict";

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");
const passwordInput = document.getElementById("password");
const pluginList = document.getElementById("plugins");
let password = null;

function storage(action) {
	try {
		return action(sessionStorage);
	} catch {
		// Private mode or blocked storage: the password lives only for this page load
		return null;
	}
}

function announce(region, message) {
	// Clearing first makes a live region announce a message equal to the previous one
	region.textContent = "";
	requestAnimationFrame(() => {
		region.textContent = message;
	});
}

function show(message) {
	announce(status, message);
}

function askForPassword(message) {
	login.hidden = false;
	pluginList.hidden = true;
	show(message);
	passwordInput.focus();
}

async function request(path, init = {}) {
	const headers = { ...init.headers };

	if (password) {
		headers.Authentication = password;
	}

	const response = await fetch(`../Api/${path}`, { ...init, headers });

	return { status: response.status, body: await response.json().catch(() => null) };
}

// For reads: a 401 or 403 means the password is wrong or ASF refuses this address, and the page shows why
async function api(path) {
	const { status: code, body } = await request(path);

	if (code === 401) {
		storage(s => s.removeItem(passwordKey));
		askForPassword(password ? "Wrong IPC password." : "Enter ASF's IPC password.");

		return null;
	}

	if (code === 403) {
		storage(s => s.removeItem(passwordKey));
		login.hidden = true;
		pluginList.hidden = true;
		show("Access denied by ASF: too many wrong passwords from this address (retry in an hour), or ASF has no IPC password and you are not on localhost.");

		return null;
	}

	if (code < 200 || code > 299 || !body?.Success) {
		show(body?.Message || `ASF answered ${code}.`);

		return null;
	}

	return body.Result;
}

function settingsEditor(plugin, scope, schema, restartNote, savedNote) {
	const details = document.createElement("details");
	const summary = document.createElement("summary");
	summary.textContent = scope === "Global" ? "Global settings" : "Bot settings";
	details.append(summary);
	const body = document.createElement("div");
	body.className = "editor";
	details.append(body);
	const path = `PluginConfig/Plugins/${encodeURIComponent(plugin.ConfigKey)}/${scope}`;

	details.addEventListener("toggle", async () => {
		if (!details.open || body.childElementCount > 0) {
			return;
		}

		const loading = document.createElement("p");
		loading.textContent = "Loading…";
		body.append(loading);
		const stored = await api(path).catch(error => {
			loading.textContent = `Cannot reach ASF: ${error.message}`;
		});

		if (!stored) {
			loading.textContent ||= "Cannot load the settings.";

			return;
		}

		let version = stored.Version;
		const form = document.createElement("form");
		form.noValidate = true;
		form.append(...SchemaForm.fields(schema, stored.Value ?? {}, ""));
		const note = document.createElement("p");
		note.className = "hint";
		note.textContent = restartNote;
		const save = document.createElement("button");
		save.type = "submit";
		save.textContent = "Save";
		const result = document.createElement("p");
		result.setAttribute("role", "status");
		result.className = "result";
		form.append(note, save, result);
		body.replaceChildren(form);

		form.addEventListener("submit", async event => {
			event.preventDefault();

			if (!window.confirm(`${restartNote} Save now?`)) {
				return;
			}

			save.disabled = true;
			SchemaForm.showErrors(form, []);

			try {
				const value = SchemaForm.read(schema, form, "");
				const { status: code, body: answer } = await request(path, {
					method: "POST",
					headers: { "Content-Type": "application/json" },
					body: JSON.stringify({ Value: value, Version: version })
				});

				if (code === 401) {
					askForPassword("Wrong IPC password.");
				} else if (code === 400 && Array.isArray(answer?.Result)) {
					const unplaced = SchemaForm.showErrors(form, answer.Result);
					announce(result, [answer.Message, ...unplaced].join(" "));
				} else if (code >= 200 && code < 300 && answer?.Success) {
					version = answer.Result;
					announce(result, savedNote);
				} else {
					announce(result, answer?.Message || `ASF answered ${code}.`);
				}
			} catch (error) {
				announce(result, `Cannot reach ASF: ${error.message}`);
			} finally {
				save.disabled = false;
			}
		});
	});

	return details;
}

function renderPlugins(plugins) {
	pluginList.replaceChildren();

	if (plugins.length === 0) {
		const empty = document.createElement("li");
		empty.textContent = "No installed plugin ships a config schema.";
		pluginList.append(empty);
	}

	for (const plugin of plugins) {
		const card = document.createElement("article");
		const title = document.createElement("h2");
		title.textContent = plugin.Title;
		card.append(title);

		if (plugin.Description) {
			const description = document.createElement("p");
			description.textContent = plugin.Description;
			card.append(description);
		}

		const meta = document.createElement("p");
		meta.className = "meta";
		const scopes = [plugin.Global && "global", plugin.Bot && "per bot"].filter(Boolean).join(" and ");
		meta.textContent = `${plugin.Assembly} · key ${plugin.ConfigKey} · ${scopes} settings`;
		card.append(meta);

		if (plugin.Global) {
			card.append(settingsEditor(plugin, "Global", plugin.Global, "Saving writes ASF.json, and ASF restarts to apply it.", "Saved to ASF.json. ASF is restarting to apply it."));
		}

		const item = document.createElement("li");
		item.append(card);
		pluginList.append(item);
	}

	pluginList.hidden = false;
}

// ASF bans an IP for an hour after 5 wrong passwords, so a password is stored only once ASF accepted it
async function load(typed) {
	password = typed ?? storage(s => s.getItem(passwordKey));
	const host = await api("PluginConfig");

	if (!host) {
		return;
	}

	if (password) {
		storage(s => s.setItem(passwordKey, password));
	}

	if (!login.hidden) {
		// The focused password field is about to disappear; keep focus on the page instead of <body>
		login.hidden = true;
		document.querySelector("h1").focus();
	}

	const plugins = await api("PluginConfig/Plugins");

	if (!plugins) {
		return;
	}

	renderPlugins(plugins);
	show(`${host.Name} ${host.Version} · ${plugins.length} plugin(s) with a config schema`);
}

function reportError(error) {
	show(`Cannot reach ASF: ${error.message}`);
}

login.addEventListener("submit", event => {
	event.preventDefault();
	load(passwordInput.value).catch(reportError);
});

load().catch(reportError);
