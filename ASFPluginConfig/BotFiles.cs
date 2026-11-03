using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ASFPluginConfig;

// The bot config files in ASF's config directory, named the way ASF names bots: <bot>.json, never ASF.json, never a
// name starting with a dot
internal static class BotFiles {
	internal static IReadOnlyList<string> List(string configDirectory) {
		ArgumentException.ThrowIfNullOrEmpty(configDirectory);

		if (!Directory.Exists(configDirectory)) {
			return [];
		}

		return [
			.. Directory.EnumerateFiles(configDirectory, "*.json")
				.Select(Path.GetFileNameWithoutExtension)
				.OfType<string>()
				.Where(static name => (name.Length > 0) && (name[0] != '.') && !name.Equals("ASF", StringComparison.OrdinalIgnoreCase))
				.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
		];
	}

	// Only a name from the listing resolves, so a request can never point outside the config directory
	internal static string? PathOf(string configDirectory, string bot) {
		ArgumentException.ThrowIfNullOrEmpty(configDirectory);

		return List(configDirectory).FirstOrDefault(name => name == bot) is { } found ? Path.Combine(configDirectory, found + ".json") : null;
	}
}
