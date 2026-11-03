using System.Collections.Generic;
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
