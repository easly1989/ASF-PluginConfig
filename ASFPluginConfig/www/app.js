"use strict";

// Another site must not be able to frame this page and click through it for a visitor. Comparing the windows needs no
// access to the framing page, so it works across origins; stopping here leaves the page with nothing to run
if (window.top !== window.self) {
	document.body.replaceChildren(Object.assign(document.createElement("p"), { textContent: "This page cannot be shown inside another page. Open it in its own tab." }));

	throw new Error("framed");
}

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");
const passwordInput = document.getElementById("password");
const pluginList = document.getElementById("plugins");
const rail = document.getElementById("rail");
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
	rail.hidden = true;
	updates.hidden = true;
	updateResult.textContent = "";
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

// A 401 or 403 means the password is wrong or ASF refuses this address: the page shows why and forgets the password,
// so a reload does not resend a wrong one toward ASF's ban. Returns whether it handled the answer
function refused(code) {
	if (code === 401) {
		storage(s => s.removeItem(passwordKey));
		askForPassword(password ? "Wrong IPC password." : "Enter ASF's IPC password.");

		return true;
	}

	if (code === 403) {
		storage(s => s.removeItem(passwordKey));
		login.hidden = true;
		pluginList.hidden = true;
		rail.hidden = true;
		updates.hidden = true;
		show("Access denied by ASF: too many wrong passwords from this address (retry in an hour), or ASF has no IPC password and you are not on localhost.");

		return true;
	}

	return false;
}

async function api(path) {
	const { status: code, body } = await request(path);

	if (refused(code)) {
		return null;
	}

	if (code < 200 || code > 299 || !body?.Success) {
		show(body?.Message || `ASF answered ${code}.`);

		return null;
	}

	return body.Result;
}

// Shows on each rail link whether a form of its card holds edits that were not saved
function refreshDots() {
	for (const card of pluginList.querySelectorAll("article")) {
		card.railDot.hidden = !card.querySelector("form.dirty");
	}
}

window.addEventListener("beforeunload", event => {
	if (pluginList.querySelector("form.dirty")) {
		event.preventDefault();
	}
});

// Loads one stored value into a form inside container; saving posts it back with the version it was read at.
// editor builds the fields from the stored value and reads the value to save; a read that throws with invalid set
// shows its message and saves nothing
async function loadForm(container, path, editor, notes, message = "") {
	// A newer load into the same container (another bot picked) wins; an older one that answers late is dropped
	const ticket = container.loadTicket = (container.loadTicket ?? 0) + 1;
	const current = () => container.loadTicket === ticket;
	const loading = document.createElement("p");
	loading.setAttribute("role", "status");
	loading.textContent = "Loading…";
	container.replaceChildren(loading);
	refreshDots();
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
	form.className = "editing";
	form.noValidate = true;
	form.append(...editor.build(stored));
	const note = document.createElement("p");
	note.className = "hint";
	note.textContent = notes.before;
	const save = document.createElement("button");
	save.type = "submit";
	save.textContent = "Save";
	const discard = document.createElement("button");
	discard.type = "button";
	discard.className = "secondary discard";
	discard.textContent = "Discard changes";
	const result = document.createElement("p");
	result.setAttribute("role", "status");
	result.className = "result";
	const actions = document.createElement("div");
	actions.className = "actions";
	actions.append(note, save, discard, result);
	form.append(actions);
	container.replaceChildren(form);
	refreshDots();
	let busy = false;
	let locked = false;

	if (message) {
		announce(result, message);
	}

	form.addEventListener("input", () => {
		form.classList.add("dirty");
		refreshDots();
	});

	discard.addEventListener("click", async () => {
		await loadForm(container, path, editor, notes, "Changes discarded.");
		container.querySelector("input:not([type=checkbox]), select, textarea, button")?.focus();
	});

	form.addEventListener("submit", async event => {
		event.preventDefault();

		if (locked) {
			announce(result, `${notes.after} Reload the page once ASF is back to edit again.`);

			return;
		}

		if (busy) {
			return;
		}

		let value;

		// A value the editor rejects is reported before asking to save it
		try {
			value = editor.read(form);
		} catch (error) {
			if (!error.invalid) {
				throw error;
			}

			announce(result, error.message);

			return;
		}

		if (!window.confirm(`${notes.before} Save now?`)) {
			return;
		}

		// aria-disabled keeps focus on the button, which disabled would drop to <body>
		busy = true;
		save.setAttribute("aria-disabled", "true");
		SchemaForm.showErrors(form, []);

		try {
			const { status: code, body: answer } = await request(path, {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: editor.body?.(value, version) ?? JSON.stringify({ Value: value, Version: version, ClearSecrets: editor.clears?.(form) ?? [] })
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
				await loadForm(container, path, editor, notes, `${answer?.Message ?? "The file changed."} The form now shows its current content; your edits were not saved.`);

				return;
			} else if (code >= 200 && code < 300 && answer?.Success) {
				// A bot save leaves ASF up, so reload to show secrets as set or not set; an ASF.json save restarts ASF
				if (notes.reload) {
					await loadForm(container, path, editor, notes, notes.after);

					return;
				}

				// ASF is going away, so the form cannot reload: lock it rather than leave stale secret states to resend
				locked = true;
				form.classList.remove("dirty");
				refreshDots();

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

function schemaEditor(schema) {
	return {
		build: stored => SchemaForm.fields(schema, stored.Value ?? {}, "", new Set(stored.SetSecrets ?? [])),
		read: form => SchemaForm.read(schema, form, ""),
		clears: SchemaForm.clears
	};
}

let rawFields = 0;

// The value as JSON text; the host refuses ASF's own keys and plugins with a schema, so nothing here is masked
function rawEditor() {
	const id = `raw-value-${++rawFields}`;

	return {
		build: stored => {
			const label = document.createElement("label");
			label.htmlFor = id;
			label.textContent = "Value (JSON)";
			const text = document.createElement("textarea");
			text.id = id;
			text.rows = 10;
			text.spellcheck = false;
			text.className = "raw";
			text.setAttribute("aria-describedby", `${id}-hint`);
			text.value = stored.Text ?? "";
			const hint = document.createElement("p");
			hint.className = "hint";
			hint.id = `${id}-hint`;
			hint.textContent = stored.Text === null || stored.Text === undefined ? "The file has no such key yet; saving adds it." : "Saving null removes the key.";
			const field = document.createElement("div");
			field.className = "field";
			field.append(label, text, hint);

			return [field];
		},
		read: form => {
			const text = form.querySelector(`#${id}`);

			try {
				JSON.parse(text.value);
				text.removeAttribute("aria-invalid");

				// The text itself is sent, since JSON.parse would round numbers past 2^53 (a SteamID64)
				return text.value;
			} catch (error) {
				text.setAttribute("aria-invalid", "true");
				text.focus();
				const invalid = new Error(text.value.trim() === "" ? "Type a JSON value, or null to remove the key." : `Not valid JSON: ${error.message}`);
				invalid.invalid = true;

				throw invalid;
			}
		},
		// Parsed above, so the text is exactly one JSON value
		body: (json, version) => `{"Value":${json},"Version":${JSON.stringify(version)}}`
	};
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

// ASF restarts after any ASF.json change, or stops when AutoRestart is off
function globalNotes() {
	return host.AutoRestart
		? { before: "Saving writes ASF.json, and ASF restarts to apply it.", after: "Saved to ASF.json. ASF is restarting to apply it." }
		: { before: "Saving writes ASF.json, and ASF stops (AutoRestart is off), so start it again to apply the change.", after: "Saved to ASF.json. ASF is stopping; start it again to apply the change." };
}

function botNotes(bot) {
	return { before: `Saving writes config/${bot}.json, and ASF restarts that bot to apply it.`, after: `Saved to config/${bot}.json. ASF is restarting ${bot} to apply it.`, reload: true };
}

function globalEditor(plugin) {
	const notes = globalNotes();

	return section("Global settings", plugin, body => loadForm(body, `${pluginPath(plugin)}/Global`, schemaEditor(plugin.Global), notes));
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
			return loadForm(formHost, `${pluginPath(plugin)}/Bots/${encodeURIComponent(bot)}`, schemaEditor(plugin.Bot), botNotes(bot));
		};

		picker.addEventListener("change", () => picked().catch(reportError));
		const field = document.createElement("div");
		field.className = "field";
		field.append(label, picker);
		body.replaceChildren(field, formHost);
		await picked();
	});
}

// For plugins without a schema: one top-level key of ASF.json or a bot's file, edited as JSON
function rawCard() {
	const card = document.createElement("article");
	const title = document.createElement("h2");
	title.textContent = "Other plugins (raw JSON)";
	const description = document.createElement("p");
	description.textContent = "For a plugin without a config schema: edit its key in ASF.json or in a bot's config file as JSON. ASF's own settings and the plugins above are edited elsewhere.";
	card.append(title, description, section("Edit a key", { Title: "other plugins" }, async body => {
		const bots = await api("PluginConfig/Bots");

		if (!bots) {
			body.textContent = "Cannot load the bots.";

			return;
		}

		const scope = document.createElement("select");
		scope.id = "raw-scope";
		scope.append(new Option("ASF.json (global)", ""), ...bots.map(bot => new Option(`config/${bot}.json`, bot)));
		const scopeLabel = document.createElement("label");
		scopeLabel.htmlFor = scope.id;
		scopeLabel.textContent = "File";
		const key = document.createElement("input");
		key.id = "raw-key";
		key.autocomplete = "off";
		key.setAttribute("list", "raw-keys");
		const keyLabel = document.createElement("label");
		keyLabel.htmlFor = key.id;
		keyLabel.textContent = "Config key";
		const keyHint = document.createElement("p");
		keyHint.className = "hint";
		keyHint.id = "raw-key-hint";
		keyHint.textContent = "Pick a key the file already has, or type a new one.";
		key.setAttribute("aria-describedby", keyHint.id);
		const keys = document.createElement("datalist");
		keys.id = "raw-keys";
		const open = document.createElement("button");
		open.type = "button";
		open.textContent = "Open";
		const editorHost = document.createElement("div");
		const base = () => scope.value ? `PluginConfig/Raw/Bots/${encodeURIComponent(scope.value)}` : "PluginConfig/Raw/Global";

		let listTicket = 0;

		const listKeys = async () => {
			const ticket = ++listTicket;
			const names = await api(base());

			// A late answer for the file picked before must not fill the list of the one picked now
			if (ticket === listTicket) {
				keys.replaceChildren(...(names ?? []).map(name => new Option(name)));
			}
		};

		scope.addEventListener("change", () => {
			// Drops a load still running for the previous file, so its form never shows under this one
			editorHost.loadTicket = (editorHost.loadTicket ?? 0) + 1;
			editorHost.replaceChildren();
			listKeys().catch(reportError);
		});

		open.addEventListener("click", () => {
			const name = key.value.trim();

			if (!name) {
				key.focus();

				return;
			}

			loadForm(editorHost, `${base()}/${encodeURIComponent(name)}`, rawEditor(), scope.value ? botNotes(scope.value) : globalNotes()).catch(reportError);
		});

		const scopeField = document.createElement("div");
		scopeField.className = "field";
		scopeField.append(scopeLabel, scope);
		const keyField = document.createElement("div");
		keyField.className = "field";
		keyField.append(keyLabel, key, keyHint, keys);
		body.replaceChildren(scopeField, keyField, open, editorHost);
		await listKeys();
	}));

	railLink(card, "plugin-raw", "Other plugins (raw JSON)");
	const item = document.createElement("li");
	item.append(card);

	return item;
}

// A rail link to a card, with a dot that shows while the card holds unsaved edits
function railLink(card, id, label, tile) {
	card.id = id;
	const link = document.createElement("a");
	link.href = `#${id}`;
	const dot = document.createElement("span");
	dot.className = "dot";
	dot.hidden = true;
	dot.title = "Unsaved changes";
	dot.append(Object.assign(document.createElement("span"), { className: "visually-hidden", textContent: " (unsaved changes)" }));
	link.append(...(tile ? [tile.cloneNode(true)] : []), label, dot);
	const item = document.createElement("li");
	item.append(link);
	rail.firstElementChild.append(item);
	card.railDot = dot;
}

// The tile colour comes from the plugin's own name, so a card keeps its colour between visits
function tileFor(title) {
	let hue = 0;

	for (const character of title) {
		hue = (hue * 31 + character.codePointAt(0)) % 360;
	}

	const tile = document.createElement("span");
	tile.className = "tile";
	tile.style.setProperty("--hue", hue);
	tile.setAttribute("aria-hidden", "true");
	tile.textContent = [...title][0]?.toUpperCase() ?? "";

	return tile;
}

function renderPlugins(plugins) {
	pluginList.replaceChildren();
	rail.firstElementChild.replaceChildren();

	for (const [index, plugin] of plugins.entries()) {
		const card = document.createElement("article");
		const head = document.createElement("div");
		head.className = "head";
		const tile = tileFor(plugin.Title);
		const title = document.createElement("h2");
		title.textContent = plugin.Title;
		const chips = document.createElement("ul");
		chips.className = "chips";
		chips.setAttribute("aria-label", "Settings offered");

		for (const label of [plugin.Global && "Global", plugin.Bot && "Per bot"].filter(Boolean)) {
			chips.append(Object.assign(document.createElement("li"), { className: "chip", textContent: label }));
		}

		head.append(tile, title, chips);
		card.append(head);

		if (plugin.Description) {
			const description = document.createElement("p");
			description.className = "about";
			description.textContent = plugin.Description;
			card.append(description);
		}

		const meta = document.createElement("p");
		meta.className = "meta";
		meta.textContent = `${plugin.Assembly}, key ${plugin.ConfigKey}`;
		card.append(meta);

		railLink(card, `plugin-${index}`, plugin.Title, tile);

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

	// It shows values as stored, so its endpoints need a real IPCPassword; the opt-in does not open them
	if (host.WritesAllowed && !host.WritesWithoutPassword) {
		pluginList.append(rawCard());
	}

	rail.hidden = false;
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
		banner.textContent = "Saving works without an IPC password (AllowWritesWithoutIPCPassword is on). Anyone who can reach ASF's IPC can change these settings, so keep it reachable only from your LAN or VPN. The raw JSON editor stays off until IPCPassword is set, because it shows values as stored.";
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
	document.getElementById("update-hint").textContent = `ASF looks for updates of the plugins that PluginsUpdateMode and PluginsUpdateList in ASF.json allow, installs them, and ${host.AutoRestart ? "restarts" : "stops (AutoRestart is off)"} if anything was updated. By default that list is empty, so nothing updates until you add plugin assembly names to it.`;
	updates.hidden = false;
	const toUpdates = document.createElement("li");
	toUpdates.append(Object.assign(document.createElement("a"), { href: "#updates", textContent: "Plugin updates" }));
	rail.firstElementChild.append(toUpdates);
	const version = document.getElementById("version");
	version.textContent = `${host.Name} ${host.Version}`;
	version.hidden = false;
	const saving = document.getElementById("saving");
	saving.dataset.state = host.WritesWithoutPassword ? "risk" : host.WritesAllowed ? "on" : "off";
	saving.textContent = host.WritesWithoutPassword ? "Saving without a password" : host.WritesAllowed ? "Saving on" : "Saving off";
	saving.hidden = false;
	show(`${plugins.length} plugin${plugins.length === 1 ? "" : "s"} with a config schema.`);
}

function reportError(error) {
	show(`Cannot reach ASF: ${error.message}`);
}

// ASF does the update itself; the host has no endpoint that downloads or installs anything
updateButton.addEventListener("click", async () => {
	if (updateButton.getAttribute("aria-disabled") || !window.confirm(`ASF updates the plugins its settings allow, and ${host.AutoRestart ? "restarts" : "stops (AutoRestart is off)"} if anything changed. Update now?`)) {
		return;
	}

	updateButton.setAttribute("aria-disabled", "true");
	announce(updateResult, "Checking for plugin updates…");
	let answered = false;

	try {
		const { status: code, body } = await request("Plugins/Update", {
			method: "POST",
			headers: { "Content-Type": "application/json" },
			body: "{}"
		});

		if (!refused(code)) {
			// ASF's message is localized and does not say whether it updated anything, and if it did it is about to go
			// away; so the button stays off until the page is reloaded
			answered = code >= 200 && code < 300 && body?.Success;
			announce(updateResult, answered ? `${body.Message} Reload the page to check again.` : body?.Message || `ASF answered ${code}.`);
		}
	} catch (error) {
		announce(updateResult, `Cannot reach ASF: ${error.message}`);
	} finally {
		if (!answered) {
			updateButton.removeAttribute("aria-disabled");
		}
	}
});

login.addEventListener("submit", event => {
	event.preventDefault();
	load(passwordInput.value).catch(reportError);
});

load().catch(reportError);
