"use strict";

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");
const passwordInput = document.getElementById("password");
const pluginList = document.getElementById("plugins");
let password = null;
let host = null;

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

// Loads one stored value into a form inside container; saving posts it back with the version it was read at
async function loadForm(container, path, schema, notes, message = "") {
	const loading = document.createElement("p");
	loading.textContent = "Loading…";
	container.replaceChildren(loading);
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
	note.textContent = notes.before;
	const save = document.createElement("button");
	save.type = "submit";
	save.textContent = "Save";
	const result = document.createElement("p");
	result.setAttribute("role", "status");
	result.className = "result";
	form.append(note, save, result);
	container.replaceChildren(form);
	let busy = false;

	if (message) {
		announce(result, message);
	}

	form.addEventListener("submit", async event => {
		event.preventDefault();

		if (busy || !window.confirm(`${notes.before} Save now?`)) {
			return;
		}

		// aria-disabled keeps focus on the button, which disabled would drop to <body>
		busy = true;
		save.setAttribute("aria-disabled", "true");
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
				const count = answer.Result.length;
				announce(result, [`${answer.Message}: ${count} field${count === 1 ? "" : "s"} to fix.`, ...unplaced].join(" "));
			} else if (code === 409) {
				// The file changed elsewhere: show what it holds now, so the next save starts from it
				await loadForm(container, path, schema, notes, `${answer?.Message ?? "The file changed."} The form now shows its current content; your edits were not saved.`);

				return;
			} else if (code >= 200 && code < 300 && answer?.Success) {
				version = answer.Result;
				announce(result, notes.after);
			} else {
				announce(result, answer?.Message || `ASF answered ${code}.`);
			}
		} catch (error) {
			announce(result, `Cannot reach ASF: ${error.message}`);
		} finally {
			busy = false;
			save.removeAttribute("aria-disabled");
		}
	});
}

// A collapsible section whose content is built the first time it opens
function section(title, plugin, build) {
	const details = document.createElement("details");
	const summary = document.createElement("summary");
	const context = document.createElement("span");
	context.className = "visually-hidden";
	context.textContent = ` for ${plugin.Title}`;
	summary.append(title, context);
	const body = document.createElement("div");
	body.className = "editor";
	details.append(summary, body);
	let built = false;

	details.addEventListener("toggle", () => {
		if (details.open && !built) {
			built = true;
			build(body).catch(error => {
				body.textContent = `Cannot reach ASF: ${error.message}`;
			});
		}
	});

	return details;
}

function pluginPath(plugin) {
	return `PluginConfig/Plugins/${encodeURIComponent(plugin.ConfigKey)}`;
}

function globalEditor(plugin) {
	// ASF restarts after any ASF.json change, or stops when AutoRestart is off
	const notes = host.AutoRestart
		? { before: "Saving writes ASF.json, and ASF restarts to apply it.", after: "Saved to ASF.json. ASF is restarting to apply it." }
		: { before: "Saving writes ASF.json, and ASF stops (AutoRestart is off), so start it again to apply the change.", after: "Saved to ASF.json. ASF is stopping; start it again to apply the change." };

	return section("Global settings", plugin, body => loadForm(body, `${pluginPath(plugin)}/Global`, plugin.Global, notes));
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
			card.append(globalEditor(plugin));
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
	host = await api("PluginConfig");

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
