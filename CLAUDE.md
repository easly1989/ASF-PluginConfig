# ASF-PluginConfig

An ArchiSteamFarm (ASF) plugin that hosts the configuration of other plugins: each plugin embeds a JSON Schema, the
host serves a page at `/plugin-config/` that builds forms from it and writes the plugin's key in `ASF.json` or
`config/<bot>.json`. The design lives in issue #1; the tickets are the issues it links.

## Layout

Mirrors JustArchiNET's `ASF-PluginTemplate`, like `easly1989/ASF-Notifier`:

- `ArchiSteamFarm/`: git submodule pinned to the ASF release tag we build against (6.3.11.7); `.github/workflows/bump-asf-reference.yml` moves the `.gitmodules` branch and the pointer together, after a green build and test.
- `ASFPluginConfig/`: the plugin project. Assembly and plugin name `ASFPluginConfig`. `www/` is the static page ASF
  serves through `IWebInterface`.
- `ASFPluginConfig.Tests/`: MSTest unit tests.
- `Directory.Build.props` / `Directory.Packages.props`: import ASF's. The plugin `<Version>` lives here.
- `tools/TrimCheck`, `tools/smoke`: the trimmed-ASF checks, shared in shape with ASF-Notifier.

## Commands

Requires the .NET 10 SDK. Clone with submodules.

- Build: `dotnet build -c Release`
- Test: `dotnet test ASFPluginConfig.Tests -c Release`
- Package: `dotnet publish ASFPluginConfig -c Release -o out/plugin -p:UseAppHost=false`, zip the folder's contents.
- Trimmed-ASF check (CI, and required by the release job): unzip an official OS-specific ASF release, then
  `dotnet run --project tools/TrimCheck -c Release -- <asf-dir> <published>/ASFPluginConfig.dll` and
  `tools/smoke/smoke.sh <asf-dir> <published-dir> <published-demo-dir>` (publish `ASFPluginConfig.Demo` for the last one) (bash and `curl`; a win-x64 release works under Git Bash).

## Conventions

- Everything in the repo is English.
- ASF code style: tabs, `.editorconfig` from the template. Warnings are errors in Release.
- ASF's OS-specific builds are trimmed: use only what the trim check accepts. Plugin controllers and a plugin-defined
  response type serialized by ASF's MVC were verified on trimmed ASF (ticket #2; the smoke test runs on the release we build against). `System.Text.Json` is trimmed
  unevenly: `JsonNode.ToJsonString`, `JsonElement.TryGetProperty(string)` and `JsonProperty.NameEquals` are gone, while
  `JsonNode.Parse`, its indexers and `JsonDocument.EnumerateObject` stay; `JsonProperty.WriteTo` is gone, so write
  `WritePropertyName` plus `JsonElement.WriteTo`. MVC too: no `[HttpPut]`, `NotFound(object)` or `Conflict(object)`; use
  `[HttpPost]` and `StatusCode(code, body)`. Run TrimCheck after any change that touches a framework API.
- Every endpoint lives under `/Api/PluginConfig/`, where ASF's `IPCPassword` middleware applies. The static page
  holds no data. Saves are refused (403) while `IPCPassword` is empty, unless the host's own
  `ASFPluginConfig.AllowWritesWithoutIPCPassword` opt-in is on (`HostSettings`).
- Never log, return or commit a secret. Values of `writeOnly` schema fields never leave the host: reads mask them
  and name the set ones, a save that omits one keeps it, and `ClearSecrets` removes one (`Secrets`). The raw editor
  (`Raw/...` endpoints) refuses ASF's own keys and every key that has a schema (`RawKeys`), so it can never bypass the
  masking. A schema that fails to parse still puts its `configKey` off limits (`SchemaCatalog.RejectedKeys`), and an unreadable one, or a scan that threw (`SchemaCatalog.Incomplete`/`Failed`), turns the raw editor off. Raw reads need a real `IPCPassword` (`HostSettings.HasIpcPassword`), not just `WritesAllowed`: they are unmasked, and the opt-in leaves protection to a network that DNS rebinding can cross, since ASF filters no `Host`. `ConfigFile.Version` is an HMAC with a per-process random key, so a version cannot be used to test guesses for short secrets. The page refuses to run in a frame. Fixtures use obviously fake values.
- Release tags are plain versions (`1.0.0.0`, no `v`): ASF's plugin updater parses them with `new Version(tag)`.

## Agent skills

### Issue tracker

GitHub Issues on `easly1989/ASF-PluginConfig`. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context. See `docs/agents/domain.md`.
