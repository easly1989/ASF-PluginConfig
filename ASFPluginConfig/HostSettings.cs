using System.Collections.Generic;
using System.Text.Json;
using ArchiSteamFarm.Core;

namespace ASFPluginConfig;

// The host's own settings, under its own key in ASF.json and described by its own embedded schema
internal static class HostSettings {
	internal const string ConfigKey = nameof(ASFPluginConfig);
	internal const string OptInName = $"{ConfigKey}.{nameof(AllowWritesWithoutIPCPassword)}";

	// Read once per ASF start in OnASFInit; ASF restarts after any ASF.json change
	internal static bool AllowWritesWithoutIPCPassword { get; private set; }

	internal static bool HasIpcPassword => !string.IsNullOrEmpty(ASF.GlobalConfig?.IPCPassword);

	internal static bool WritesAllowed => HasIpcPassword || AllowWritesWithoutIPCPassword;

	// Returns a warning to log when the value is present but not a boolean
	internal static string? Load(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
		AllowWritesWithoutIPCPassword = false;

		if ((additionalConfigProperties == null) || !additionalConfigProperties.TryGetValue(ConfigKey, out JsonElement settings)) {
			return null;
		}

		switch (JsonUtil.Property(settings, nameof(AllowWritesWithoutIPCPassword))?.ValueKind) {
			case null:
				return null;
			case JsonValueKind.True:
				AllowWritesWithoutIPCPassword = true;

				return null;
			case JsonValueKind.False:
				return null;
			default:
				return $"{OptInName} must be true or false; saving without IPCPassword stays off";
		}
	}
}
