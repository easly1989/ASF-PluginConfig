using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
[DoNotParallelize]
internal sealed class HostSettingsTests {
	[TestCleanup]
	public void Cleanup() => HostSettings.Load(null);

	[TestMethod]
	internal void OffWhenAbsent() {
		Assert.IsNull(HostSettings.Load(null));
		Assert.IsFalse(HostSettings.AllowWritesWithoutIPCPassword);
		Assert.IsNull(HostSettings.Load(Settings("""{ "Other": true }""")));
		Assert.IsFalse(HostSettings.AllowWritesWithoutIPCPassword);
	}

	[TestMethod]
	internal void OnOnlyWhenTrue() {
		Assert.IsNull(HostSettings.Load(Settings("""{ "ASFPluginConfig": { "AllowWritesWithoutIPCPassword": true } }""")));
		Assert.IsTrue(HostSettings.AllowWritesWithoutIPCPassword);
		Assert.IsTrue(HostSettings.WritesAllowed, "no IPCPassword in tests, so only the opt-in allows writes");

		Assert.IsNull(HostSettings.Load(Settings("""{ "ASFPluginConfig": { "AllowWritesWithoutIPCPassword": false } }""")));
		Assert.IsFalse(HostSettings.AllowWritesWithoutIPCPassword);
		Assert.IsFalse(HostSettings.WritesAllowed);
	}

	[TestMethod]
	internal void AValueThatIsNotABooleanStaysOffAndWarns() {
		string? warning = HostSettings.Load(Settings("""{ "ASFPluginConfig": { "AllowWritesWithoutIPCPassword": "yes" } }"""));

		Assert.IsFalse(HostSettings.AllowWritesWithoutIPCPassword);
		Assert.IsNotNull(warning);
		Assert.Contains("AllowWritesWithoutIPCPassword", warning);
	}

	[TestMethod]
	internal void TheHostShipsAValidSchemaForItsOwnSetting() {
		List<string> errors = [];

		SchemaCatalog catalog = SchemaCatalog.Load([typeof(HostSettings).Assembly], errors.Add);

		Assert.IsEmpty(errors);
		Assert.AreEqual(HostSettings.ConfigKey, catalog.Schemas[0].ConfigKey);
	}

	private static Dictionary<string, JsonElement> Settings(string json) {
		Dictionary<string, JsonElement> settings = [];

		foreach (JsonProperty property in SchemaValidatorTests.Parse(json).EnumerateObject()) {
			settings[property.Name] = property.Value.Clone();
		}

		return settings;
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
