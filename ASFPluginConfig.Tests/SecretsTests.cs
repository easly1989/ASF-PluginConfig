using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class SecretsTests {
	private const string Token = "FAKE-token-0000";

	private static readonly JsonElement Schema = SchemaValidatorTests.Parse("""
		{
			"type": "object",
			"required": ["Token"],
			"properties": {
				"Url": { "type": "string" },
				"Token": { "type": "string", "writeOnly": true },
				"Matrix": { "type": "object", "properties": { "Room": { "type": "string" }, "Password": { "type": "string", "writeOnly": true } } }
			}
		}
		""");

	private static readonly JsonElement Stored = SchemaValidatorTests.Parse($$"""{ "Url": "https://e.org", "Token": "{{Token}}", "Matrix": { "Room": "!r", "Password": "{{Token}}-2" }, "Legacy": 1 }""");

	[TestMethod]
	internal void MaskDropsEverySecretAndNamesWhichAreSet() {
		(JsonElement? masked, List<string> set) = Secrets.Mask(Schema, Stored);

		Assert.IsNotNull(masked);
		Assert.DoesNotContain(Token, masked.Value.GetRawText());
		Assert.AreEqual("Token,Matrix.Password", string.Join(",", set));
		Assert.AreEqual("https://e.org", JsonUtil.String(masked.Value, "Url"));
		Assert.AreEqual("!r", JsonUtil.String(JsonUtil.Property(masked.Value, "Matrix")!.Value, "Room"));
		Assert.IsNull(JsonUtil.Property(masked.Value, "Legacy"));
	}

	[TestMethod]
	internal void MaskOfNothingIsNothing() {
		(JsonElement? masked, List<string> set) = Secrets.Mask(Schema, null);

		Assert.IsNull(masked);
		Assert.IsEmpty(set);
	}

	[TestMethod]
	internal void AnOmittedSecretIsKept() {
		JsonElement merged = Secrets.Merge(Schema, Stored, SchemaValidatorTests.Parse("""{ "Url": "https://new.example", "Matrix": { "Room": "!s" } }"""), []);

		Assert.AreEqual(Token, JsonUtil.String(merged, "Token"));
		Assert.AreEqual(Token + "-2", JsonUtil.String(JsonUtil.Property(merged, "Matrix")!.Value, "Password"));
		Assert.AreEqual("!s", JsonUtil.String(JsonUtil.Property(merged, "Matrix")!.Value, "Room"));
		Assert.IsEmpty(SchemaValidator.Validate(Schema, merged), "a kept secret satisfies required");
	}

	[TestMethod]
	internal void ANewSecretReplacesTheStoredOne() {
		JsonElement merged = Secrets.Merge(Schema, Stored, SchemaValidatorTests.Parse("""{ "Token": "FAKE-new" }"""), []);

		Assert.AreEqual("FAKE-new", JsonUtil.String(merged, "Token"));
	}

	[TestMethod]
	internal void AClearedSecretIsRemoved() {
		JsonElement merged = Secrets.Merge(Schema, Stored, SchemaValidatorTests.Parse("""{ "Matrix": { } }"""), ["Token", "Matrix.Password"]);

		Assert.IsNull(JsonUtil.Property(merged, "Token"));
		Assert.IsNull(JsonUtil.Property(JsonUtil.Property(merged, "Matrix")!.Value, "Password"));
		Assert.HasCount(1, SchemaValidator.Validate(Schema, merged), "a cleared required secret is reported");
	}

	[TestMethod]
	internal void AStoredNullIsNotASecret() {
		JsonElement stored = SchemaValidatorTests.Parse("""{ "Url": "https://e.org", "Token": null }""");

		(_, List<string> set) = Secrets.Mask(Schema, stored);
		JsonElement merged = Secrets.Merge(Schema, stored, SchemaValidatorTests.Parse("""{ "Url": "https://new.example" }"""), []);

		Assert.IsEmpty(set);
		Assert.IsNull(JsonUtil.Property(merged, "Token"), "a kept null would fail validation as a non-string");
	}

	[TestMethod]
	internal void AnOmittedGroupKeepsItsSecrets() {
		JsonElement merged = Secrets.Merge(Schema, Stored, SchemaValidatorTests.Parse("""{ "Url": "https://new.example" }"""), []);
		JsonElement? matrix = JsonUtil.Property(merged, "Matrix");

		Assert.IsNotNull(matrix);
		Assert.AreEqual(Token + "-2", JsonUtil.String(matrix.Value, "Password"));
		Assert.IsNull(JsonUtil.Property(matrix.Value, "Room"), "only the secret is carried over");
	}

	[TestMethod]
	internal void UnknownStoredKeysAreLeftToTheWriter() {
		JsonElement merged = Secrets.Merge(Schema, Stored, SchemaValidatorTests.Parse("""{ }"""), []);

		Assert.IsNull(JsonUtil.Property(merged, "Legacy"), "ConfigFile.Write carries unknown keys over; the merged value stays validatable");
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
