# Changelog

## 1.1.0.0 - 2026-11-05

Adds a settings file per plugin, storage standard v1 ([#31](https://github.com/easly1989/ASF-PluginConfig/issues/31)), and a new look for the `/plugin-config/` page ([#32](https://github.com/easly1989/ASF-PluginConfig/pull/32)). Requires ASF 6.3.11.7 or later.

### Changed

- The page has a new layout: a side list of plugins (a row of chips on a phone), a card per plugin with a monogram tile and chips for the settings it offers, a header with the plugin version and whether saving is on, and forms in two columns on wide screens. Checkboxes are drawn as switches, and a secret field shows whether a value is stored. The page still uses system fonts only and makes no request outside the host.

### Added

- A plugin can keep its settings in `config/plugins/<AssemblyName>.json` instead of `ASF.json` and the bot files, by declaring `"storage": "file"` and `"formatVersion": 1` in its schema. The file holds `{ "formatVersion": 1, "global": {}, "bots": {} }`; the first save creates it. Plugins without these keys work exactly as before. The README has the contract and a reader to copy.
- `"reload": "restart"` (the default for file storage) makes the host restart ASF after a save, or stop it when `AutoRestart` is off, since ASF does not watch that file; with file storage a bot save restarts all of ASF. `"reload": "live"` restarts nothing: the plugin applies its file itself.
- Every save's answer now carries `Restart`: `"asf"`, `"bot"` or `"none"`, telling the page what happens next. The plugin list gives each plugin's `Storage` and `Reload`.
- A plugin shows a dot in the side list while its form holds unsaved edits, and the browser asks before the page is closed or reloaded with such edits.
- **Discard changes** in the sticky save bar reloads the form from the host.

### Security

- Secrets in a plugin file are out of reach of ASF's `GET /Api/ASF`, which returns `ASF.json` as stored; `writeOnly` fields are masked as everywhere.
- The file's path comes only from a loaded plugin's assembly name, which must be a plain file name of at most 100 characters; a symbolic link or junction at the file or at `config/plugins` is refused, not followed. A file with an unknown `formatVersion` is never rewritten.
- The raw editor refuses the legacy `ASF.json` key of a plugin with file storage, and never touches plugin files.
- A read does not return a stored property the schema does not declare, since the host cannot tell whether it is a secret; a save keeps it. A plugin file with an unknown format is warned about once per file version on reads.
- The temp file of a save is created with `CreateNew` after a leftover one is deleted, on every OS, so a link planted at its path is not followed.
- A new plugin file gets the default mode; the README tells operators to pre-create a file that will hold secrets with the mode they want. An existing file keeps its mode.

### Known issues

- A write by ASF in the instant between the last check before the file swap and the move is still lost ([#18](https://github.com/easly1989/ASF-PluginConfig/issues/18)). The window was narrowed in 1.0.1.0 and again in [#28](https://github.com/easly1989/ASF-PluginConfig/pull/28). Closing it needs a lock across processes, and ASF takes none on its config files.

## 1.0.1.0 - 2026-11-04

Fixes the three known issues of 1.0.0.0, in whole or in part. Requires ASF 6.3.11.7 or later.

### Fixed

- A save keeps the config file's mode on Linux and macOS. The temp file used to take the default mode, so a file you had restricted to its owner (for the `IPCPassword` in it) came back readable by others. The mode is set before any content is written ([#21](https://github.com/easly1989/ASF-PluginConfig/issues/21)).
- A plugin that loads after start-up and may carry a config schema turns the raw editor off until ASF restarts, and ASF logs a warning. Before, its secrets under an unscanned key could show up as plain raw JSON ([#20](https://github.com/easly1989/ASF-PluginConfig/issues/20)).
- The config file is compared once more right before it is replaced, after the new content is flushed to disk. A write by ASF or another editor while the save was being prepared now makes the save fail with the usual "changed since you loaded it" instead of being overwritten ([#18](https://github.com/easly1989/ASF-PluginConfig/issues/18)).

### Known issues

- A write in the instant between that last comparison and the replacement is still lost. Closing it needs a lock across processes, and ASF takes none on its config files.
- When ASF cannot replace a config file because it is a single file bind-mounted into a container, the file is rewritten in place. That rewrite is not atomic, and the recovery after a failed rewrite has not been tested.

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
