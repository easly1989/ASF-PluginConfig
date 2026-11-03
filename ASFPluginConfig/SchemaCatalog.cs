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

		foreach (Assembly assembly in assemblies) {
			if (assembly.IsDynamic || (assembly.GetName().Name is not { Length: > 0 } name)) {
				continue;
			}

			string resource = name + ResourceSuffix;

			if (!assembly.GetManifestResourceNames().Contains(resource, StringComparer.Ordinal)) {
				continue;
			}

			string json;

			try {
				using Stream? stream = assembly.GetManifestResourceStream(resource);

				if (stream == null) {
					continue;
				}

				using StreamReader reader = new(stream);
				json = reader.ReadToEnd();
			} catch (IOException e) {
				logError($"Config schema of {name} cannot be read: {e.Message}");

				continue;
			}

			PluginSchema? schema = PluginSchema.Parse(name, json, out string? error);

			if (schema == null) {
				logError($"Config schema of {name} is invalid and ignored: {error}");

				continue;
			}

			if (schemas.Any(existing => existing.ConfigKey == schema.ConfigKey)) {
				logError($"Config schema of {name} is ignored: another plugin already claims the config key {schema.ConfigKey}");

				continue;
			}

			schemas.Add(schema);
		}

		return new SchemaCatalog([.. schemas.OrderBy(static schema => schema.Title, StringComparer.OrdinalIgnoreCase)]);
	}
}
