using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class SchemaValidatorTests {
	private static readonly JsonElement Schema = Parse("""
		{
			"type": "object",
			"required": ["Url"],
			"properties": {
				"Enabled": { "type": "boolean" },
				"Url": { "type": "string", "format": "uri" },
				"Minutes": { "type": "integer", "minimum": 1, "maximum": 1440 },
				"Ratio": { "type": "number" },
				"Mode": { "type": "string", "enum": ["a", "b"] },
				"Tags": { "type": "array", "items": { "type": "string" } },
				"Events": { "type": "object", "properties": { "Stopped": { "type": "boolean" } } }
			}
		}
		""");

	[TestMethod]
	internal void AcceptsAValidValue() {
		List<FieldError> errors = SchemaValidator.Validate(Schema, Parse("""{ "Enabled": true, "Url": "https://example.org", "Minutes": 5, "Ratio": 0.5, "Mode": "b", "Tags": ["x"], "Events": { "Stopped": false } }"""));

		Assert.IsEmpty(errors);
	}

	public static IEnumerable<object[]> Invalid => new (string Json, string Path, string MessagePart)[] {
		("""{ }""", "Url", "required"),
		("""[]""", "", "an object"),
		("""{ "Url": "https://e.org", "Enabled": "yes" }""", "Enabled", "a boolean"),
		("""{ "Url": "ftp://e.org" }""", "Url", "http"),
		("""{ "Url": "not a url" }""", "Url", "URL"),
		("""{ "Url": "https://e.org", "Minutes": 0 }""", "Minutes", "at least 1"),
		("""{ "Url": "https://e.org", "Minutes": 1441 }""", "Minutes", "at most 1440"),
		("""{ "Url": "https://e.org", "Minutes": 2.5 }""", "Minutes", "an integer"),
		("""{ "Url": "https://e.org", "Mode": "c" }""", "Mode", "one of"),
		("""{ "Url": "https://e.org", "Tags": ["x", 1] }""", "Tags[1]", "a string"),
		("""{ "Url": "https://e.org", "Events": { "Unknown": true } }""", "Events.Unknown", "not a setting"),
		("""{ "Url": "https://e.org", "Extra": 1 }""", "Extra", "not a setting")
	}.Select(static test => new object[] { test.Json, test.Path, test.MessagePart });

	[TestMethod]
	[DynamicData(nameof(Invalid))]
	internal void ReportsTheFieldAndTheReason(string json, string path, string messagePart) {
		List<FieldError> errors = SchemaValidator.Validate(Schema, Parse(json));

		Assert.HasCount(1, errors, string.Join("; ", errors));
		Assert.AreEqual(path, errors[0].Path);
		Assert.Contains(messagePart, errors[0].Message);
	}

	internal static JsonElement Parse(string json) {
		using JsonDocument document = JsonDocument.Parse(json);

		return document.RootElement.Clone();
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
