using System;
using System.Collections.Generic;
using System.Linq;

namespace ASFPluginConfig;

// Which top-level keys the raw editor may touch: neither ASF's own settings nor a key whose plugin ships a schema, so
// a schema's writeOnly secrets are only ever reached through the masking form
internal static class RawKeys {
	internal static string? Refusal(string key, SchemaCatalog catalog) {
		ArgumentNullException.ThrowIfNull(catalog);

		if (string.IsNullOrWhiteSpace(key)) {
			return "A config key needs a name";
		}

		if (AsfKeys.Owned.Contains(key)) {
			return $"{key} is one of ASF's own settings";
		}

		// Named explicitly too: the catalog is empty when the schema scan fails, and the opt-in must not be raw-editable
		if (string.Equals(key, HostSettings.ConfigKey, StringComparison.OrdinalIgnoreCase)) {
			return $"{key} holds this plugin's own settings, which have their own form";
		}

		return catalog.Schemas.FirstOrDefault(schema => string.Equals(schema.ConfigKey, key, StringComparison.OrdinalIgnoreCase)) is { } owner
			? $"{key} belongs to {owner.Title}, which has its own form"
			: null;
	}

	internal static IReadOnlyList<string> Editable(IEnumerable<string> keys, SchemaCatalog catalog) => keys.Where(key => Refusal(key, catalog) == null).ToList();
}
