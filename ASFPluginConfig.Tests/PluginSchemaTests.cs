using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class PluginSchemaTests {
	private const string Valid = """
		{
			"$schema": "https://json-schema.org/draft/2020-12/schema",
			"title": "Demo",
			"description": "A demo plugin",
			"configKey": "Demo",
			"global": {
				"type": "object",
				"required": ["Url"],
				"properties": {
					"Enabled": { "type": "boolean", "default": true },
					"Url": { "type": "string", "format": "uri", "title": "Server URL" },
					"Token": { "type": "string", "writeOnly": true },
					"Minutes": { "type": "integer", "minimum": 1, "maximum": 1440, "default": 5 },
					"Mode": { "type": "string", "enum": ["a", "b"] },
					"Tags": { "type": "array", "items": { "type": "string" } },
					"Events": { "type": "object", "properties": { "Stopped": { "type": "boolean" } } }
				}
			},
			"bot": {
				"type": "object",
				"properties": { "Enabled": { "type": "boolean" } }
			}
		}
		""";

	[TestMethod]
	internal void ParsesTheSupportedSubset() {
		PluginSchema? schema = PluginSchema.Parse("DemoPlugin", Valid, out string? error);

		Assert.IsNull(error);
		Assert.IsNotNull(schema);
		Assert.AreEqual("DemoPlugin", schema.Assembly);
		Assert.AreEqual("Demo", schema.ConfigKey);
		Assert.AreEqual("Demo", schema.Title);
		Assert.AreEqual("A demo plugin", schema.Description);
		Assert.IsTrue(schema.Global.HasValue);
		Assert.IsTrue(schema.Bot.HasValue);
	}

	[TestMethod]
	internal void TitleFallsBackToTheConfigKey() {
		PluginSchema? schema = PluginSchema.Parse("DemoPlugin", """{ "configKey": "Demo", "bot": { "type": "object" } }""", out string? error);

		Assert.IsNull(error);
		Assert.IsNotNull(schema);
		Assert.AreEqual("Demo", schema.Title);
		Assert.IsFalse(schema.Global.HasValue);
	}

	public static IEnumerable<object[]> InvalidSchemas => new (string Json, string ErrorPart)[] {
		("{ not json", "not valid JSON"),
		("[]", "must be a JSON object"),
		("""{ "global": { "type": "object" } }""", "configKey"),
		("""{ "configKey": "", "global": { "type": "object" } }""", "configKey"),
		("""{ "configKey": "Demo" }""", "global or bot"),
		("""{ "configKey": "Demo", "extra": 1, "global": { "type": "object" } }""", "unsupported keyword 'extra'"),
		("""{ "configKey": "Demo", "global": { "type": "string" } }""", "global: must be an object schema"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "pattern": "x" } } } }""", "global.properties.A: unsupported keyword 'pattern'"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { } } } }""", "global.properties.A: missing type"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "null" } } } }""", "unsupported type 'null'"),
		("""{ "configKey": "Demo", "global": { "type": "object", "required": ["B"], "properties": { "A": { "type": "string" } } } }""", "required 'B'"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "minimum": 1 } } } }""", "minimum"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "format": "email" } } } }""", "format"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "boolean", "writeOnly": true } } } }""", "writeOnly"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "array", "items": { "type": "object" } } } } }""", "items"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "enum": [] } } } }""", "enum")
	}.Select(static test => new object[] { test.Json, test.ErrorPart });

	[TestMethod]
	[DynamicData(nameof(InvalidSchemas))]
	internal void RejectsWhatTheContractDoesNotSupport(string json, string errorPart) {
		PluginSchema? schema = PluginSchema.Parse("DemoPlugin", json, out string? error);

		Assert.IsNull(schema);
		Assert.IsNotNull(error);
		Assert.Contains(errorPart, error);
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
