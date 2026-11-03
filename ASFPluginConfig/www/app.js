"use strict";

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");
const passwordInput = document.getElementById("password");

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
	show(message);
	passwordInput.focus();
}

async function api(path, password) {
	const headers = {};

	if (password) {
		headers.Authentication = password;
	}

	return fetch(`../Api/${path}`, { headers });
}

// ASF bans an IP for an hour after 5 wrong passwords, so a password is stored only once ASF accepted it
async function load(typed) {
	const password = typed ?? storage(s => s.getItem(passwordKey));
	const response = await api("PluginConfig", password);

	if (response.status === 401) {
		storage(s => s.removeItem(passwordKey));
		askForPassword(password ? "Wrong IPC password." : "Enter ASF's IPC password.");

		return;
	}

	if (response.status === 403) {
		storage(s => s.removeItem(passwordKey));
		login.hidden = true;
		show("Access denied by ASF: too many wrong passwords from this address (retry in an hour), or ASF has no IPC password and you are not on localhost.");

		return;
	}

	if (!response.ok) {
		show(`ASF answered ${response.status}.`);

		return;
	}

	if (password) {
		storage(s => s.setItem(passwordKey, password));
	}

	const { Result } = await response.json();
	login.hidden = true;
	show(`${Result.Name} ${Result.Version}`);
}

function reportError(error) {
	show(`Cannot reach ASF: ${error.message}`);
}

login.addEventListener("submit", event => {
	event.preventDefault();
	load(passwordInput.value).catch(reportError);
});

load().catch(reportError);
