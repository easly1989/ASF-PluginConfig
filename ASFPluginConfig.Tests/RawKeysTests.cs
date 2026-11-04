using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class RawKeysTests {
	// The host's own schema, under ASFPluginConfig, and a plugin's, under DemoNotifier
	private static readonly SchemaCatalog Catalog = SchemaCatalog.Load([typeof(HostSettings).Assembly, new FakeAssembly("Demo", """{ "configKey": "DemoNotifier", "global": { "type": "object" } }""")], static _ => { });

	[TestMethod]
	internal void TheHostKeyIsRefusedEvenWithAnEmptyCatalog() => Assert.IsNotNull(RawKeys.Refusal("ASFPluginConfig", SchemaCatalog.Empty));

	[TestMethod]
	[DataRow("IPCPassword", "ASF's own")]
	[DataRow("ipcpassword", "ASF's own")]
	[DataRow("SteamLogin", "ASF's own")]
	[DataRow("s_SteamOwnerID", "ASF's own")]
	[DataRow("WebProxyPassword", "ASF's own")]
	[DataRow("ASFPluginConfig", "own form")]
	[DataRow("asfpluginconfig", "own form")]
	[DataRow("DemoNotifier", "its own form")]
	[DataRow(" ", "needs a name")]
	internal void RefusesAsfKeysAndKeysWithASchema(string key, string reason) {
		string? refusal = RawKeys.Refusal(key, Catalog);

		Assert.IsNotNull(refusal);
		Assert.Contains(reason, refusal);
	}

	[TestMethod]
	internal void ARejectedSchemasKeyIsRefused() {
		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Rejected", """{ "configKey": "Secretive", "global": { "type": "object", "properties": { "Token": { "type": "string", "minimum": 1 } } } }""")], static _ => { });

		string? refusal = RawKeys.Refusal("secretive", catalog);

		Assert.IsNotNull(refusal);
		Assert.Contains("rejected", refusal);
		Assert.IsNull(RawKeys.Refusal("Other", catalog), "a catalog that knows every rejected key leaves the rest editable");
	}

	[TestMethod]
	internal void AnIncompleteCatalogRefusesEveryKey() {
		Assert.IsNotNull(RawKeys.Refusal("Other", SchemaCatalog.Failed));
		Assert.IsEmpty(RawKeys.Editable(["Other", "ASFNotifier"], SchemaCatalog.Failed));
	}

	[TestMethod]
	internal void AcceptsAPluginKeyWithoutASchema() => Assert.IsNull(RawKeys.Refusal("ASFNotifier", Catalog));

	[TestMethod]
	internal void EditableKeepsOnlyTheAcceptedKeys() {
		IReadOnlyList<string> editable = RawKeys.Editable(["IPC", "ASFNotifier", "ASFPluginConfig", "SteamOwnerID", "WebProxyPassword", "demonotifier", "Other"], Catalog);

		Assert.AreEqual("ASFNotifier,Other", string.Join(",", editable));
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
