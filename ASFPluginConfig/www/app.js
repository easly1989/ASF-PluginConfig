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

function show(message) {
	// Clearing first makes the live region announce a message equal to the previous one
	status.textContent = "";
	requestAnimationFrame(() => {
		status.textContent = message;
	});
}

function askForPassword(message) {
	login.hidden = false;
	pluginList.hidden = true;
	show(message);
	passwordInput.focus();
}

async function api(path) {
	const headers = {};

	if (password) {
		headers.Authentication = password;
	}

	const response = await fetch(`../Api/${path}`, { headers });

	if (response.status === 401) {
		storage(s => s.removeItem(passwordKey));
		askForPassword(password ? "Wrong IPC password." : "Enter ASF's IPC password.");

		return null;
	}

	if (response.status === 403) {
		storage(s => s.removeItem(passwordKey));
		login.hidden = true;
		pluginList.hidden = true;
		show("Access denied by ASF: too many wrong passwords from this address (retry in an hour), or ASF has no IPC password and you are not on localhost.");

		return null;
	}

	const body = await response.json().catch(() => null);

	if (!response.ok || !body?.Success) {
		show(body?.Message || `ASF answered ${response.status}.`);

		return null;
	}

	return body.Result;
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
