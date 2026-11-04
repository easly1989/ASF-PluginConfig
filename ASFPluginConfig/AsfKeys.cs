using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;

namespace ASFPluginConfig;

// ASF's own top-level setting names in ASF.json and config/<bot>.json; no plugin may claim one. ASF serializes both
// configs by reflection, so their public properties survive trimming
internal static class AsfKeys {
	internal static FrozenSet<string> Owned { get; } = Names(typeof(GlobalConfig)).Concat(Names(typeof(BotConfig))).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	// Every property name whatever its visibility, since ASF also reads internal [JsonInclude] ones (WebProxyPassword),
	// plus the JSON names it writes on some (s_SteamOwnerID); refusing a name ASF never reads costs nothing
	private static IEnumerable<string> Names(Type config) => config.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
		.SelectMany(static property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } json ? [property.Name, json.Name] : new[] { property.Name });
}
