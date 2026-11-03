# ASF-PluginConfig

An [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) (ASF) plugin that gives one page to edit the
configuration of other installed plugins. Each plugin that wants a form embeds a JSON Schema in its DLL; the host
builds the form from it and writes the plugin's own key in `ASF.json` or `config/<bot>.json`.

> Work in progress. The design is in [issue #1](https://github.com/easly1989/ASF-PluginConfig/issues/1); this
> version serves the page and its API, but no forms yet.

## Install

1. Download `ASFPluginConfig.zip` from the [latest release](https://github.com/easly1989/ASF-PluginConfig/releases/latest).
2. Unzip it into ASF's `plugins/ASFPluginConfig/` folder.
3. Restart ASF.
4. Open `http://<asf-host>:1242/plugin-config/` and enter ASF's IPC password when asked.

Requires ASF 6.3.10.6 or later, with IPC enabled.

## Build

Requires the .NET 10 SDK. Clone with submodules: `git clone --recursive`.

- Build: `dotnet build -c Release`
- Test: `dotnet test ASFPluginConfig.Tests -c Release`
- Package: `dotnet publish ASFPluginConfig -c Release -o out/plugin -p:UseAppHost=false`, then zip the folder's
  contents (not the folder) as `ASFPluginConfig.zip`.

## License

[Apache-2.0](LICENSE)
