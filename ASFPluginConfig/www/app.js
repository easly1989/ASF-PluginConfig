"use strict";

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");
const passwordInput = document.getElementById("password");
const pluginList = document.getElementById("plugins");
const updates = document.getElementById("updates");
const updateButton = document.getElementById("update");
const updateResult = document.getElementById("update-result");
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
	updates.hidden = true;
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
		updates.hidden = true;
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
	// A newer load into the same container (another bot picked) wins; an older one that answers late is dropped
	const ticket = container.loadTicket = (container.loadTicket ?? 0) + 1;
	const current = () => container.loadTicket === ticket;
	const loading = document.createElement("p");
	loading.setAttribute("role", "status");
	loading.textContent = "Loading…";
	container.replaceChildren(loading);
	const stored = await api(path).catch(error => {
		loading.textContent = `Cannot reach ASF: ${error.message}`;
	});

	if (!current()) {
		return;
	}

	if (!stored) {
		loading.textContent ||= "Cannot load the settings.";

		return;
	}

	const version = stored.Version;
	const form = document.createElement("form");
	form.noValidate = true;
	form.append(...SchemaForm.fields(schema, stored.Value ?? {}, "", new Set(stored.SetSecrets ?? [])));
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
	let locked = false;

	if (message) {
		announce(result, message);
	}

	form.addEventListener("submit", async event => {
		event.preventDefault();

		if (locked) {
			announce(result, `${notes.after} Reload the page once ASF is back to edit again.`);

			return;
		}

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
				body: JSON.stringify({ Value: value, Version: version, ClearSecrets: SchemaForm.clears(form) })
			});

			if (code === 401) {
				askForPassword("Wrong IPC password.");
			} else if (code === 400 && Array.isArray(answer?.Result)) {
				const unplaced = SchemaForm.showErrors(form, answer.Result);
				const count = answer.Result.length;
				announce(result, [`${answer.Message}: ${count} field${count === 1 ? "" : "s"} to fix.`, ...unplaced].join(" "));
			} else if (!current()) {
				// The form was replaced while saving; say where the answer went instead of dropping it
				show(code >= 200 && code < 300 && answer?.Success ? notes.after : `Not saved: ${answer?.Message || `ASF answered ${code}.`}`);
			} else if (code === 409) {
				// The file changed elsewhere: show what it holds now, so the next save starts from it
				await loadForm(container, path, schema, notes, `${answer?.Message ?? "The file changed."} The form now shows its current content; your edits were not saved.`);

				return;
			} else if (code >= 200 && code < 300 && answer?.Success) {
				// A bot save leaves ASF up, so reload to show secrets as set or not set; an ASF.json save restarts ASF
				if (notes.reload) {
					await loadForm(container, path, schema, notes, notes.after);

					return;
				}

				// ASF is going away, so the form cannot reload: lock it rather than leave stale secret states to resend
				locked = true;

				for (const input of form.querySelectorAll("input[type=password]")) {
					input.value = "";
				}

				announce(result, notes.after);
			} else {
				announce(result, answer?.Message || `ASF answered ${code}.`);
			}
		} catch (error) {
			announce(result, `Cannot reach ASF: ${error.message}`);
		} finally {
			busy = false;

			if (!locked) {
				save.removeAttribute("aria-disabled");
			}
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

function botEditor(plugin) {
	return section("Bot settings", plugin, async body => {
		const bots = await api("PluginConfig/Bots");

		if (!bots) {
			body.textContent = "Cannot load the bots.";

			return;
		}

		if (bots.length === 0) {
			body.textContent = "ASF has no bot config files.";

			return;
		}

		const id = `bot-${plugin.ConfigKey}`;
		const label = document.createElement("label");
		label.htmlFor = id;
		label.textContent = `Bot for ${plugin.Title}`;
		const picker = document.createElement("select");
		picker.id = id;

		for (const bot of bots) {
			picker.append(new Option(bot, bot));
		}

		const formHost = document.createElement("div");

		const picked = () => {
			const bot = picker.value;
			const notes = { before: `Saving writes config/${bot}.json, and ASF restarts that bot to apply it.`, after: `Saved to config/${bot}.json. ASF is restarting ${bot} to apply it.`, reload: true };

			return loadForm(formHost, `${pluginPath(plugin)}/Bots/${encodeURIComponent(bot)}`, plugin.Bot, notes);
		};

		picker.addEventListener("change", () => picked().catch(reportError));
		const field = document.createElement("div");
		field.className = "field";
		field.append(label, picker);
		body.replaceChildren(field, formHost);
		await picked();
	});
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

		if (plugin.Bot) {
			card.append(botEditor(plugin));
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

	const banner = document.getElementById("banner");

	// Persistent on purpose: with the opt-in on, nothing but the network protects these settings
	if (host.WritesWithoutPassword) {
		banner.textContent = "Saving works without an IPC password (AllowWritesWithoutIPCPassword is on). Anyone who can reach ASF's IPC can change these settings, so keep it reachable only from your LAN or VPN.";
		banner.hidden = false;
	} else if (!host.WritesAllowed) {
		banner.textContent = "Saving is off: set IPCPassword in ASF.json, or turn on \"Allow saving without IPCPassword\" in this plugin's settings.";
		banner.hidden = false;
	} else {
		banner.hidden = true;
	}

	const plugins = await api("PluginConfig/Plugins");

	if (!plugins) {
		return;
	}

	renderPlugins(plugins);
	updates.hidden = false;
	show(`${host.Name} ${host.Version} · ${plugins.length} plugin(s) with a config schema`);
}

function reportError(error) {
	show(`Cannot reach ASF: ${error.message}`);
}

// ASF does the update itself; the host has no endpoint that downloads or installs anything
updateButton.addEventListener("click", async () => {
	if (updateButton.getAttribute("aria-disabled") || !window.confirm("ASF updates the plugins its settings allow, and restarts if anything changed. Update now?")) {
		return;
	}

	updateButton.setAttribute("aria-disabled", "true");
	announce(updateResult, "Checking for plugin updates…");

	try {
		const { status: code, body } = await request("Plugins/Update", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: "{}"
		});

		if (code === 401) {
			askForPassword("Wrong IPC password.");
		} else {
			announce(updateResult, body?.Message || body?.Result || `ASF answered ${code}.`);
		}
	} catch (error) {
		announce(updateResult, `Cannot reach ASF: ${error.message}`);
	} finally {
		updateButton.removeAttribute("aria-disabled");
	}
});

login.addEventListener("submit", event => {
	event.preventDefault();
	load(passwordInput.value).catch(reportError);
});

load().catch(reportError);
