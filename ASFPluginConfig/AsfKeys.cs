using System;
using System.Collections.Frozen;
using System.Linq;
using System.Reflection;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;

namespace ASFPluginConfig;

// ASF's own top-level setting names in ASF.json and config/<bot>.json; no plugin may claim one. ASF serializes both
// configs by reflection, so their public properties survive trimming
internal static class AsfKeys {
	internal static FrozenSet<string> Owned { get; } = typeof(GlobalConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public)
		.Concat(typeof(BotConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public))
		.Select(static property => property.Name)
		.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
