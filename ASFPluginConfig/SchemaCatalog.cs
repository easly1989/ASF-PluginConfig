using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ASFPluginConfig;

// The schemas plugins embed as <AssemblyName>.config.schema.json manifest resources
internal sealed class SchemaCatalog {
	internal const string ResourceSuffix = ".config.schema.json";

	// A scan that found nothing
	internal static SchemaCatalog Empty { get; } = new([], ImmutableHashSet<string>.Empty, false);

	// No scan, or one that threw: nothing is known about which keys carry secrets
	internal static SchemaCatalog Failed { get; } = new([], ImmutableHashSet<string>.Empty, true);

	internal ImmutableArray<PluginSchema> Schemas { get; }

	// Config keys of schemas that were rejected as invalid: their plugins still hold secrets under those keys, so a
	// key here is as off limits to the raw editor as a key with a working schema
	internal ImmutableHashSet<string> RejectedKeys { get; }

	// Some schema or assembly could not be read and its key is unknown, so no key can be called free of secrets
	internal bool Incomplete { get; }

	private SchemaCatalog(ImmutableArray<PluginSchema> schemas, ImmutableHashSet<string> rejectedKeys, bool incomplete) {
		Schemas = schemas;
		RejectedKeys = rejectedKeys;
		Incomplete = incomplete;
	}

	internal PluginSchema? Find(string configKey) => Schemas.FirstOrDefault(schema => schema.ConfigKey == configKey);

	internal SchemaCatalog AsIncomplete() => Incomplete ? this : new SchemaCatalog(Schemas, RejectedKeys, true);

	// Whether any of the loaded assemblies is new to known and may hold a schema. Every new one is added to known
	internal static bool AnyNewSchemaCarrier(IEnumerable<Assembly> loaded, ISet<Assembly> known) {
		ArgumentNullException.ThrowIfNull(loaded);
		ArgumentNullException.ThrowIfNull(known);

		bool carrier = false;

		foreach (Assembly assembly in loaded) {
			if (known.Add(assembly) && MayCarrySchema(assembly)) {
				carrier = true;
			}
		}

		return carrier;
	}

	// Whether an assembly that loaded after a scan may hold a schema: it lists a schema resource, or it cannot be inspected
	internal static bool MayCarrySchema(Assembly assembly) {
		ArgumentNullException.ThrowIfNull(assembly);

		if (assembly.IsDynamic) {
			return false;
		}

		try {
			return assembly.GetName().Name is { Length: > 0 } name && assembly.GetManifestResourceNames().Contains(name + ResourceSuffix, StringComparer.Ordinal);
		} catch (Exception) {
			return true;
		}
	}

	internal static SchemaCatalog Load(IEnumerable<Assembly> assemblies, Action<string> logError) {
		ArgumentNullException.ThrowIfNull(assemblies);
		ArgumentNullException.ThrowIfNull(logError);

		List<PluginSchema> schemas = [];
		HashSet<string> rejected = new(StringComparer.OrdinalIgnoreCase);
		bool incomplete = false;

		// Sorted, so which of two plugins claiming one key wins does not depend on load order
		foreach (Assembly assembly in assemblies.Where(static assembly => !assembly.IsDynamic).OrderBy(static assembly => assembly.FullName, StringComparer.Ordinal)) {
			string name = assembly.FullName ?? "?";

			// One odd assembly or schema must never take the host down with it
			try {
				PluginSchema? schema = LoadOne(assembly, out name, logError, rejected, ref incomplete);

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

				// It may have held a schema that guards secrets; there is no telling which key
				incomplete = true;
			}
		}

		return new SchemaCatalog([.. schemas.OrderBy(static schema => schema.Title, StringComparer.OrdinalIgnoreCase)], rejected.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), incomplete);
	}

	// The configKey of a schema that failed to parse, when its text still names one the way a valid schema must: non-blank
	// and without surrounding spaces. Anything else could be a mistyped form of a key that is in use, which a plugin
	// reads as typed or trimmed; such a schema names no key we can refuse, so the caller treats it as unreadable
	private static string? KeyOf(string json) {
		try {
			using JsonDocument document = JsonDocument.Parse(json);

			return JsonUtil.String(document.RootElement, "configKey") is { } key && !string.IsNullOrWhiteSpace(key) && (key.Trim() == key) ? key : null;
		} catch (JsonException) {
			return null;
		}
	}

	private static PluginSchema? LoadOne(Assembly assembly, out string name, Action<string> logError, HashSet<string> rejected, ref bool incomplete) {
		name = assembly.GetName().Name ?? "";

		if (name.Length == 0) {
			return null;
		}

		string resource = name + ResourceSuffix;

		if (!assembly.GetManifestResourceNames().Contains(resource, StringComparer.Ordinal)) {
			return null;
		}

		using Stream? stream = assembly.GetManifestResourceStream(resource);

		// The resource is listed but cannot be opened: whatever it guards stays unknown
		if (stream == null) {
			logError($"Config schema of {name} cannot be read: its resource has no stream");
			incomplete = true;

			return null;
		}

		using StreamReader reader = new(stream);
		string json = reader.ReadToEnd();
		PluginSchema? schema = PluginSchema.Parse(name, json, out string? error);

		if (schema == null) {
			logError($"Config schema of {name} is invalid and ignored: {error}");

			if (KeyOf(json) is { } key) {
				rejected.Add(key);
			} else {
				incomplete = true;
			}
		}

		return schema;
	}
}
