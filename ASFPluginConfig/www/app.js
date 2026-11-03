"use strict";

// The page holds no data: everything comes from /Api, which ASF guards with IPCPassword
const passwordKey = "asf-plugin-config.ipc-password";
const status = document.getElementById("status");
const login = document.getElementById("login");

function storedPassword() {
	try {
		return sessionStorage.getItem(passwordKey) || "";
	} catch {
		return "";
	}
}

async function api(path) {
	const headers = {};
	const password = storedPassword();

	if (password) {
		headers.Authentication = password;
	}

	return fetch(`../Api/${path}`, { headers });
}

async function load() {
	const response = await api("PluginConfig");

	if (response.status === 401 || response.status === 403) {
		login.hidden = false;
		status.textContent = "Enter ASF's IPC password.";

		return;
	}

	if (!response.ok) {
		status.textContent = `ASF answered ${response.status}.`;

		return;
	}

	const { Result } = await response.json();
	login.hidden = true;
	status.textContent = `${Result.Name} ${Result.Version}`;
}

login.addEventListener("submit", event => {
	event.preventDefault();

	try {
		sessionStorage.setItem(passwordKey, document.getElementById("password").value);
	} catch {
		// Private mode: the password lives only for this page load
	}

	load();
});

load().catch(error => {
	status.textContent = `Cannot reach ASF: ${error.message}`;
});
