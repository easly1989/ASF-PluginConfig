using System;
using System.Text.Json;

namespace ASFPluginConfig;

// Where one plugin value lives, as its schema's storage says: under the config key in ASF.json or a bot's config file
// ("asf-json"), or in the plugin's own file ("file"). Both give the same snapshots, versions and outcomes
internal sealed class ValueStore {
	// The bot whose value this is; null for the global value
	internal string? Bot { get; }

	internal string FileName => System.IO.Path.GetFileName(Path);

	private string Path { get; }
	private PluginSchema Schema { get; }

	private ValueStore(PluginSchema schema, string path, string? bot) {
		Schema = schema;
		Path = path;
		Bot = bot;
	}

	// What applies a saved value (SaveResult.Restart)
	internal string Restart => Schema.InPluginFile
		? Schema.Reload == PluginSchema.LiveReload ? SaveResult.NoRestart : SaveResult.AsfRestart
		: Bot != null ? SaveResult.BotRestart : SaveResult.AsfRestart;

	// For asf-json, ASF's own config watcher restarts it, or the bot; nothing watches config/plugins, so the host must
	internal bool HostRestarts => Schema.InPluginFile && (Schema.Reload == PluginSchema.RestartReload);

	// null when the plugin's file path is refused (PluginFile.PathOf)
	internal static ValueStore? Global(PluginSchema schema, string configDirectory) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentException.ThrowIfNullOrEmpty(configDirectory);

		return schema.InPluginFile
			? PluginFile.PathOf(configDirectory, schema.Assembly) is { } path ? new ValueStore(schema, path, null) : null
			: new ValueStore(schema, System.IO.Path.Combine(configDirectory, "ASF.json"), null);
	}

	// botPath comes from BotFiles.PathOf, so only a bot with a config file has a value, whatever the storage
	internal static ValueStore? OfBot(PluginSchema schema, string configDirectory, string botPath) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentException.ThrowIfNullOrEmpty(configDirectory);
		ArgumentException.ThrowIfNullOrEmpty(botPath);

		string bot = System.IO.Path.GetFileNameWithoutExtension(botPath);

		return schema.InPluginFile
			? PluginFile.PathOf(configDirectory, schema.Assembly) is { } path ? new ValueStore(schema, path, bot) : null
			: new ValueStore(schema, botPath, bot);
	}

	internal ConfigFile.Snapshot Read() => Schema.InPluginFile ? PluginFile.Read(Path, Bot) : ConfigFile.Read(Path, Schema.ConfigKey);

	internal (ConfigFile.Outcome Outcome, string? Version) Write(JsonElement schema, JsonElement? value, string expectedVersion) =>
		Schema.InPluginFile ? PluginFile.Write(Path, Bot, schema, value, expectedVersion) : ConfigFile.Write(Path, Schema.ConfigKey, schema, value, expectedVersion);
}
