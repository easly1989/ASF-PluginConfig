using System.Collections.Generic;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class SchemaCatalogTests {
	[TestMethod]
	internal void FindsTheEmbeddedSchemaAndSkipsAssembliesWithout() {
		List<string> errors = [];

		SchemaCatalog catalog = SchemaCatalog.Load([typeof(SchemaCatalogTests).Assembly, typeof(object).Assembly], errors.Add);

		Assert.IsEmpty(errors);
		Assert.HasCount(1, catalog.Schemas);
		Assert.AreEqual("ASFPluginConfig.Tests", catalog.Schemas[0].Assembly);
		Assert.AreEqual("TestFixture", catalog.Schemas[0].ConfigKey);
	}

	[TestMethod]
	internal void LogsAnInvalidSchemaNamingTheAssembly() {
		List<string> errors = [];

		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Broken", "{ \"configKey\": \"X\" }")], errors.Add);

		Assert.IsEmpty(catalog.Schemas);
		Assert.HasCount(1, errors);
		Assert.Contains("Broken", errors[0]);
		Assert.Contains("global or bot", errors[0]);
	}

	[TestMethod]
	internal void AnAssemblyThatThrowsIsLoggedAndSkipped() {
		List<string> errors = [];

		SchemaCatalog catalog = SchemaCatalog.Load([new ThrowingAssembly(), typeof(SchemaCatalogTests).Assembly], errors.Add);

		Assert.HasCount(1, catalog.Schemas);
		Assert.HasCount(1, errors);
		Assert.Contains("BadImageFormatException", errors[0]);
	}

	[TestMethod]
	internal void ARejectedSchemaStillNamesItsKeyAsOffLimits() {
		const string json = """{ "configKey": "Secretive", "global": { "type": "object", "properties": { "Token": { "type": "string", "writeOnly": true, "minimum": 1 } } } }""";

		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Rejected", json)], static _ => { });

		Assert.IsEmpty(catalog.Schemas);
		Assert.IsTrue(catalog.RejectedKeys.Contains("secretive"), "the key is kept, whatever its case");
		Assert.IsFalse(catalog.Incomplete, "the key is known, so the other keys stay editable");
	}

	[TestMethod]
	[DataRow("{ not json")]
	[DataRow("""{ "global": { "type": "object" } }""")]
	[DataRow("""{ "configKey": "", "global": { "type": "object" } }""")]
	[DataRow("""{ "configKey": 7, "global": { "type": "object" } }""")]
	[DataRow("""{ "configKey": " Foo", "global": { "type": "object" } }""")]
	[DataRow("""{ "configKey": "Foo ", "global": { "type": "object" } }""")]
	[DataRow("""{ "configKey": "   ", "global": { "type": "object" } }""")]
	internal void ASchemaWithoutAReadableKeyMakesTheCatalogIncomplete(string json) {
		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Nameless", json)], static _ => { });

		Assert.IsEmpty(catalog.Schemas);
		Assert.IsEmpty(catalog.RejectedKeys);
		Assert.IsTrue(catalog.Incomplete, "its secrets may sit under any key");
	}

	[TestMethod]
	internal void AnAssemblyThatThrowsMakesTheCatalogIncomplete() => Assert.IsTrue(SchemaCatalog.Load([new ThrowingAssembly()], static _ => { }).Incomplete);

	[TestMethod]
	internal void AResourceThatCannotBeOpenedMakesTheCatalogIncomplete() {
		List<string> errors = [];

		SchemaCatalog catalog = SchemaCatalog.Load([new NoStreamAssembly("Damaged")], errors.Add);

		Assert.IsTrue(catalog.Incomplete);
		Assert.HasCount(1, errors);
		Assert.Contains("Damaged", errors[0]);
	}

	[TestMethod]
	internal void ASpacedKeyDoesNotHideTheRealOne() {
		// " Foo" never becomes a rejected key, and the catalog turns incomplete, so Foo itself is not left editable
		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Spaced", """{ "configKey": " Foo", "global": { "type": "object" } }""")], static _ => { });

		Assert.IsNotNull(RawKeys.Refusal("Foo", catalog));
		Assert.IsFalse(catalog.RejectedKeys.Contains(" Foo"));
	}

	[TestMethod]
	internal void AGoodScanIsComplete() {
		Assert.IsFalse(SchemaCatalog.Load([typeof(SchemaCatalogTests).Assembly], static _ => { }).Incomplete);
		Assert.IsFalse(SchemaCatalog.Empty.Incomplete);
		Assert.IsTrue(SchemaCatalog.Failed.Incomplete);
	}

	[TestMethod]
	internal void AnAssemblyThatLoadsLaterMayCarryASchemaOnlyWhenItListsOne() {
		Assert.IsTrue(SchemaCatalog.MayCarrySchema(new FakeAssembly("Late", "{ }")));
		Assert.IsTrue(SchemaCatalog.MayCarrySchema(new ThrowingAssembly()), "an assembly that cannot be inspected fails closed");
		Assert.IsFalse(SchemaCatalog.MayCarrySchema(typeof(object).Assembly));
	}

	[TestMethod]
	internal void OnlyAnAssemblyNewToTheScanCountsAsLate() {
		FakeAssembly scanned = new("Scanned", "{ }");
		FakeAssembly late = new("Late", "{ }");
		HashSet<Assembly> known = [scanned];

		Assert.IsFalse(SchemaCatalog.AnyNewSchemaCarrier([scanned, typeof(object).Assembly], known), "the scan listed the first, and the second holds no schema");
		Assert.IsTrue(SchemaCatalog.AnyNewSchemaCarrier([scanned, late], known));
		Assert.IsFalse(SchemaCatalog.AnyNewSchemaCarrier([scanned, late], known), "each new assembly is judged once");
		Assert.HasCount(3, known);
	}

	[TestMethod]
	internal void ACatalogMarkedIncompleteKeepsItsSchemasAndRefusesEveryKey() {
		SchemaCatalog scanned = SchemaCatalog.Load([new FakeAssembly("Demo", """{ "configKey": "DemoNotifier", "global": { "type": "object" } }""")], static _ => { });

		SchemaCatalog late = scanned.AsIncomplete();

		Assert.IsFalse(scanned.Incomplete);
		Assert.IsTrue(late.Incomplete);
		Assert.HasCount(1, late.Schemas);
		Assert.IsNotNull(RawKeys.Refusal("Other", late));
		Assert.AreSame(late, late.AsIncomplete());
	}

	[TestMethod]
	internal void KeepsTheFirstOfTwoSchemasForTheSameKey() {
		List<string> errors = [];
		const string json = """{ "configKey": "Same", "global": { "type": "object" } }""";

		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("First", json), new FakeAssembly("Second", json)], errors.Add);

		Assert.HasCount(1, catalog.Schemas);
		Assert.AreEqual("First", catalog.Schemas[0].Assembly);
		Assert.HasCount(1, errors);
		Assert.Contains("Second", errors[0]);
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
