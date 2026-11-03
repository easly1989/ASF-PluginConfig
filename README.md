# ASF-PluginConfig

An [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) (ASF) plugin that gives one page to edit the
configuration of other installed plugins. Each plugin that wants a form embeds a JSON Schema in its DLL; the host
builds the form from it and writes the plugin's own key in `ASF.json` or `config/<bot>.json`.

> Work in progress. The design is in [issue #1](https://github.com/easly1989/ASF-PluginConfig/issues/1); this
> version lists the plugins that ship a schema, but has no forms yet.

## Install

1. Download `ASFPluginConfig.zip` from the [latest release](https://github.com/easly1989/ASF-PluginConfig/releases/latest).
2. Unzip it into ASF's `plugins/ASFPluginConfig/` folder.
3. Restart ASF.
4. Open `http://<asf-host>:1242/plugin-config/` (with the trailing slash) and enter ASF's IPC password when asked.
   ASF bans an address for an hour after 5 wrong passwords; the page stores the password for the browser tab only
   after ASF accepts it.

Requires ASF 6.3.10.6 or later, with IPC enabled.

## For plugin authors: the schema contract

Your plugin keeps reading its settings as it does today, from `OnASFInit` and `OnBotInitModules`. To get a form, embed one
JSON file in your DLL. You need no code change and no shared assembly.

1. Add `config.schema.json` to your plugin project and embed it under a fixed name:

   ```xml
   <ItemGroup>
   	<EmbeddedResource Include="config.schema.json" LogicalName="$(AssemblyName).config.schema.json" />
   </ItemGroup>
   ```

2. Describe your config key and its settings:

   ```json
   {
   	"configKey": "MyPlugin",
   	"title": "My plugin",
   	"description": "What the plugin does, shown on the page.",
   	"global": {
   		"type": "object",
   		"required": ["ServerUrl"],
   		"properties": {
   			"Enabled": { "type": "boolean", "default": true },
   			"ServerUrl": { "type": "string", "format": "uri", "title": "Server URL" },
   			"Token": { "type": "string", "writeOnly": true },
   			"DelayMinutes": { "type": "integer", "minimum": 1, "maximum": 1440, "default": 5 },
   			"Level": { "type": "string", "enum": ["Info", "Warning", "Error"] },
   			"Tags": { "type": "array", "items": { "type": "string" } }
   		}
   	},
   	"bot": {
   		"type": "object",
   		"properties": { "Enabled": { "type": "boolean" } }
   	}
   }
   ```

What the document must contain:
- `configKey`: required. Your top-level key in `ASF.json` and in `config/<bot>.json`. The first plugin to claim a key keeps it.
- `global` and `bot`: at least one, each with `"type": "object"`. `global` is the key in `ASF.json`; `bot` is the key in each bot's config.
- `title` and `description`: optional. `title` falls back to `configKey`.
- `$schema`: allowed, and ignored.

Keywords inside a schema; anything else makes the host ignore your schema and log why:

| Keyword | Applies to |
|---|---|
| `type` (required): `object`, `string`, `integer`, `number`, `boolean`, `array` | every schema |
| `title`, `description`, `default`, `enum` (non-empty) | every schema |
| `properties`, `required` (names among `properties`) | `object` |
| `items` (a `string`, `integer`, `number` or `boolean` schema) | `array` |
| `minimum`, `maximum` | `integer`, `number` |
| `format`: only `uri` | `string` |
| `writeOnly`: a secret, never sent back to the page | `string` |

## Build

Requires the .NET 10 SDK. Clone with submodules: `git clone --recursive`.

- Build: `dotnet build -c Release`
- Test: `dotnet test ASFPluginConfig.Tests -c Release`
- Package: `dotnet publish ASFPluginConfig -c Release -o out/plugin -p:UseAppHost=false`, then zip the folder's
  contents (not the folder) as `ASFPluginConfig.zip`.

## License

[Apache-2.0](LICENSE)
