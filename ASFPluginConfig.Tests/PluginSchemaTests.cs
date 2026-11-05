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
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "array", "items": { "type": "string", "writeOnly": true } } } } }""", "not supported on array items"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "array", "items": { "type": "object" } } } } }""", "items"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "enum": [] } } } }""", "enum"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string" }, "A": { "type": "integer" } } } }""", "duplicate property name"),
		("""{ "configKey": "Headless", "global": { "type": "object" } }""", "ASF's own settings"),
		("""{ "configKey": "steamlogin", "bot": { "type": "object" } }""", "ASF's own settings"),
		("""{ "configKey": "s_SteamOwnerID", "global": { "type": "object" } }""", "ASF's own settings"),
		("""{ "configKey": " Demo ", "global": { "type": "object" } }""", "surrounding spaces"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "integer", "default": "abc" } } } }""", "default must be integer"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "integer", "default": 1.5 } } } }""", "default must be integer"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "integer", "minimum": 1, "maximum": 5, "default": 50 } } } }""", "within minimum and maximum"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "boolean", "default": 5 } } } }""", "default must be boolean"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "enum": ["a", 5] } } } }""", "every enum value must be string"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "integer", "minimum": 10, "maximum": 1 } } } }""", "minimum is greater than maximum"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "format": null } } } }""", "format"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "integer", "minimum": null } } } }""", "minimum must be a number"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "string", "title": 5 } } } }""", "title must be a string"),
		("""{ "configKey": "Demo", "title": 5, "global": { "type": "object" } }""", "title must be a string"),
		("""{ "configKey": "Demo", "global": { "type": "object", "properties": { "A": { "type": "array" } } } }""", "an array needs items"),
		("""{ "configKey": "Demo", "storage": "disk", "global": { "type": "object" } }""", "storage must be"),
		("""{ "configKey": "Demo", "storage": null, "global": { "type": "object" } }""", "storage must be"),
		("""{ "configKey": "Demo", "storage": "file", "global": { "type": "object" } }""", "needs \"formatVersion\": 1"),
		("""{ "configKey": "Demo", "storage": "file", "formatVersion": "1", "global": { "type": "object" } }""", "needs \"formatVersion\": 1"),
		("""{ "configKey": "Demo", "storage": "file", "formatVersion": 2, "global": { "type": "object" } }""", "unknown formatVersion"),
		("""{ "configKey": "Demo", "storage": "file", "formatVersion": 1, "reload": "sometimes", "global": { "type": "object" } }""", "reload must be"),
		("""{ "configKey": "Demo", "formatVersion": 1, "global": { "type": "object" } }""", "formatVersion applies only"),
		("""{ "configKey": "Demo", "storage": "asf-json", "reload": "live", "global": { "type": "object" } }""", "reload applies only")
	}.Select(static test => new object[] { test.Json, test.ErrorPart });

	[TestMethod]
	internal void StorageDefaultsToAsfJsonWithRestart() {
		PluginSchema? schema = PluginSchema.Parse("DemoPlugin", Valid, out _);

		Assert.IsNotNull(schema);
		Assert.AreEqual("asf-json", schema.Storage);
		Assert.AreEqual("restart", schema.Reload);
	}

	[TestMethod]
	[DataRow("", "restart")]
	[DataRow(""", "reload": "restart" """, "restart")]
	[DataRow(""", "reload": "live" """, "live")]
	internal void ParsesFileStorage(string reloadKey, string reload) {
		PluginSchema? schema = PluginSchema.Parse("My.Plugin_2-x", $$"""{ "configKey": "Demo", "storage": "file", "formatVersion": 1{{reloadKey}}, "global": { "type": "object" } }""", out string? error);

		Assert.IsNull(error);
		Assert.IsNotNull(schema);
		Assert.AreEqual("file", schema.Storage);
		Assert.AreEqual(reload, schema.Reload);
	}

	// The file is named after the assembly, so file storage needs a name that is safe as one
	[TestMethod]
	[DataRow("../Evil")]
	[DataRow("..")]
	[DataRow("a/b")]
	[DataRow("a\\b")]
	[DataRow("C:Evil")]
	[DataRow(".hidden")]
	[DataRow("-dash")]
	[DataRow("Evil Plugin")]
	[DataRow("Evil\n")]
	[DataRow("Ünicode")]
	internal void FileStorageRefusesAnAssemblyNameThatIsNoSafeFileName(string assembly) {
		PluginSchema? schema = PluginSchema.Parse(assembly, """{ "configKey": "Demo", "storage": "file", "formatVersion": 1, "global": { "type": "object" } }""", out string? error);

		Assert.IsNull(schema);
		Assert.IsNotNull(error);
		Assert.Contains("cannot name a settings file", error);
		Assert.IsNotNull(PluginSchema.Parse(assembly, """{ "configKey": "Demo", "global": { "type": "object" } }""", out _), "asf-json storage never makes a path of the name");
	}

	[TestMethod]
	internal void AnAssemblyNameOfMoreThanAHundredCharactersIsRefused() {
		Assert.IsTrue(PluginFile.IsSafeName(new string('a', 100)));
		Assert.IsFalse(PluginFile.IsSafeName(new string('a', 101)));
	}

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
