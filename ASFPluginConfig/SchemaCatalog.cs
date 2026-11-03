using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ASFPluginConfig;

// The schemas plugins embed as <AssemblyName>.config.schema.json manifest resources
internal sealed class SchemaCatalog {
	internal const string ResourceSuffix = ".config.schema.json";

	internal static SchemaCatalog Empty { get; } = new([]);

	internal ImmutableArray<PluginSchema> Schemas { get; }

	private SchemaCatalog(ImmutableArray<PluginSchema> schemas) => Schemas = schemas;

	internal PluginSchema? Find(string configKey) => Schemas.FirstOrDefault(schema => schema.ConfigKey == configKey);

	internal static SchemaCatalog Load(IEnumerable<Assembly> assemblies, Action<string> logError) {
		ArgumentNullException.ThrowIfNull(assemblies);
		ArgumentNullException.ThrowIfNull(logError);

		List<PluginSchema> schemas = [];

		// Sorted, so which of two plugins claiming one key wins does not depend on load order
		foreach (Assembly assembly in assemblies.Where(static assembly => !assembly.IsDynamic).OrderBy(static assembly => assembly.FullName, StringComparer.Ordinal)) {
			string name = assembly.FullName ?? "?";

			// One odd assembly or schema must never take the host down with it
			try {
				PluginSchema? schema = LoadOne(assembly, out name, logError);

				if (schema == null) {
					continue;
				}

				if (schemas.Any(existing => string.Equals(existing.ConfigKey, schema.ConfigKey, StringComparison.OrdinalIgnoreCase))) {
					logError($"Config schema of {name} is ignored: another plugin already claims the config key {schema.ConfigKey}");

					continue;
				}

				schemas.Add(schema);
			} catch (Exception e) {
				logError($"Config schema of {name} is ignored: {e.GetType().Name}: {e.Message}");
			}
		}

		return new SchemaCatalog([.. schemas.OrderBy(static schema => schema.Title, StringComparer.OrdinalIgnoreCase)]);
	}

	private static PluginSchema? LoadOne(Assembly assembly, out string name, Action<string> logError) {
		name = assembly.GetName().Name ?? "";

		if (name.Length == 0) {
			return null;
		}

		string resource = name + ResourceSuffix;

		if (!assembly.GetManifestResourceNames().Contains(resource, StringComparer.Ordinal)) {
			return null;
		}

		using Stream? stream = assembly.GetManifestResourceStream(resource);

		if (stream == null) {
			return null;
		}

		using StreamReader reader = new(stream);
		PluginSchema? schema = PluginSchema.Parse(name, reader.ReadToEnd(), out string? error);

		if (schema == null) {
			logError($"Config schema of {name} is invalid and ignored: {error}");
		}

		return schema;
	}
}
