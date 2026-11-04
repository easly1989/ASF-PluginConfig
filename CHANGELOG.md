# Changelog

## 1.0.0.0 - 2026-11-04

First release. Requires ASF 6.3.11.7 or later.

### Added

- A page at `/plugin-config/` that edits the configuration of other installed plugins. A plugin gets a form by embedding a JSON Schema in its DLL as `<AssemblyName>.config.schema.json`; it needs no code change and no shared assembly. The supported subset is in the README.
- Global settings, written to the plugin's key in `ASF.json`, and per-bot settings, written to `config/<bot>.json`. Every other key of the file is kept, and so are keys inside the plugin's value that its schema does not know.
- Validation against the schema before anything is written, with errors shown next to their fields.
- A version check that refuses a save when the file changed after the form was loaded.
- Secrets: a `writeOnly` field is never returned by any endpoint. The form shows whether it is set, a save that leaves it out keeps it, and *Remove the stored value* deletes it.
- A raw JSON editor for the key of a plugin without a schema, in `ASF.json` or a bot's file.
- An **Update plugins** button that asks ASF's own `POST /Api/Plugins/Update` to update the plugins its settings allow. This plugin installs nothing itself.
- Plugin updates through ASF's GitHub updater (`IGitHubPluginUpdates`).

### Security

- Every endpoint is under `/Api/PluginConfig/`, so ASF's `IPCPassword` check applies. Saves are refused (403) while `IPCPassword` is empty, unless the owner turns on `ASFPluginConfig.AllowWritesWithoutIPCPassword` for an IPC that only a trusted network can reach. ASF then logs a warning at start-up, and the page shows a banner.
- The raw editor shows values as stored, so reading them needs a real `IPCPassword`: the opt-in does not cover it, since a web page opened on the LAN can reach an IPC that ASF leaves open to any `Host` name (DNS rebinding). Without a password the card is hidden and the endpoints answer 403.
- The raw editor refuses ASF's own settings and every key that has a schema, including one whose schema was rejected. If it cannot tell which keys are safe, it refuses all of them.
- A version, which a save must send back, is a keyed hash with a random key per process, not a plain hash of the file, so it cannot be used to test guesses for short secrets in the file.
- The page refuses to run inside a frame.
- The page keeps the IPC password for the browser tab only, and only after ASF accepted it.

### Known issues

- A write by ASF in the instant between this plugin's version check and its file swap is lost ([#18](https://github.com/easly1989/ASF-PluginConfig/issues/18)).
- A plugin loaded after start-up is not scanned for a schema ([#20](https://github.com/easly1989/ASF-PluginConfig/issues/20)).
- A save gives the config file the default file mode again, so a file you had restricted to its owner on Linux or macOS needs `chmod 600` after a save ([#21](https://github.com/easly1989/ASF-PluginConfig/issues/21)).
- When ASF cannot replace a config file because it is a single file bind-mounted into a container, the file is rewritten in place. That rewrite is not atomic, and the recovery after a failed rewrite has not been tested.
