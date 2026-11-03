using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class ConfigFileTests {
	private static readonly JsonElement Schema = SchemaValidatorTests.Parse("""
		{ "type": "object", "properties": { "Url": { "type": "string" }, "Events": { "type": "object", "properties": { "Stopped": { "type": "boolean" } } } } }
		""");

	private string Directory = "";
	private string Path = "";

	[TestInitialize]
	public void Init() {
		Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pluginconfig-{Guid.NewGuid():N}");
		System.IO.Directory.CreateDirectory(Directory);
		Path = System.IO.Path.Combine(Directory, "ASF.json");
	}

	[TestCleanup]
	public void Cleanup() => System.IO.Directory.Delete(Directory, true);

	[TestMethod]
	internal void ReadsTheKeyAndAVersion() {
		File.WriteAllText(Path, """{ "IPC": true, "Demo": { "Url": "https://e.org" } }""");

		ConfigFile.Snapshot snapshot = ConfigFile.Read(Path, "Demo");

		Assert.AreEqual(ConfigFile.Outcome.Written, snapshot.Outcome);
		Assert.AreEqual("""{ "Url": "https://e.org" }""", snapshot.Value?.GetRawText());
		Assert.AreEqual(ConfigFile.Version(File.ReadAllBytes(Path)), snapshot.Version);
	}

	[TestMethod]
	internal void AMissingKeyReadsAsNoValue() {
		File.WriteAllText(Path, """{ "IPC": true }""");

		ConfigFile.Snapshot snapshot = ConfigFile.Read(Path, "Demo");

		Assert.AreEqual(ConfigFile.Outcome.Written, snapshot.Outcome);
		Assert.IsNull(snapshot.Value);
		Assert.IsNotNull(snapshot.Version);
	}

	[TestMethod]
	internal void AMissingFileIsReported() {
		Assert.AreEqual(ConfigFile.Outcome.Missing, ConfigFile.Read(Path, "Demo").Outcome);
		Assert.AreEqual(ConfigFile.Outcome.Missing, ConfigFile.Write(Path, "Demo", Schema, Value("""{ }"""), "x").Outcome);
	}

	[TestMethod]
	internal void AWriteChangesOnlyThePluginKey() {
		File.WriteAllText(Path, """{ "IPC": true, "IPCPassword": "FAKE", "Demo": { "Url": "https://old.example" }, "Other": [1, 2] }""");
		string version = ConfigFile.Read(Path, "Demo").Version!;

		(ConfigFile.Outcome outcome, string? newVersion) = ConfigFile.Write(Path, "Demo", Schema, Value("""{ "Url": "https://new.example" }"""), version);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.AreEqual(ConfigFile.Version(File.ReadAllBytes(Path)), newVersion);

		using JsonDocument written = JsonDocument.Parse(File.ReadAllBytes(Path));
		Assert.AreEqual(4, written.RootElement.EnumerateObject().Count());
		Assert.AreEqual("true", JsonUtil.Property(written.RootElement, "IPC")?.GetRawText());
		Assert.AreEqual("FAKE", JsonUtil.String(written.RootElement, "IPCPassword"));
		Assert.AreEqual(2, JsonUtil.Property(written.RootElement, "Other")?.GetArrayLength());
		Assert.AreEqual("https://new.example", JsonUtil.String(JsonUtil.Property(written.RootElement, "Demo")!.Value, "Url"));
	}

	[TestMethod]
	internal void AWriteAddsAMissingKeyAndNullRemovesIt() {
		File.WriteAllText(Path, """{ "IPC": true }""");

		(_, string? version) = ConfigFile.Write(Path, "Demo", Schema, Value("""{ "Url": "https://e.org" }"""), ConfigFile.Read(Path, "Demo").Version!);
		Assert.IsNotNull(ConfigFile.Read(Path, "Demo").Value);

		(ConfigFile.Outcome outcome, _) = ConfigFile.Write(Path, "Demo", Schema, null, version!);
		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.IsNull(ConfigFile.Read(Path, "Demo").Value);
		Assert.IsNotNull(JsonUtil.Property(JsonDocument.Parse(File.ReadAllBytes(Path)).RootElement, "IPC"));
	}

	[TestMethod]
	internal void AStaleVersionWritesNothing() {
		File.WriteAllText(Path, """{ "Demo": { "Url": "https://e.org" } }""");
		string version = ConfigFile.Read(Path, "Demo").Version!;
		File.WriteAllText(Path, """{ "Demo": { "Url": "https://changed-elsewhere.example" } }""");
		byte[] before = File.ReadAllBytes(Path);

		(ConfigFile.Outcome outcome, string? newVersion) = ConfigFile.Write(Path, "Demo", Schema, Value("""{ "Url": "https://mine.example" }"""), version);

		Assert.AreEqual(ConfigFile.Outcome.Stale, outcome);
		Assert.IsNull(newVersion);
		CollectionAssert.AreEqual(before, File.ReadAllBytes(Path));
	}

	[TestMethod]
	internal void KeysTheSchemaDoesNotDefineSurviveAWrite() {
		File.WriteAllText(Path, """{ "Demo": { "Url": "https://e.org", "Legacy": 1, "Events": { "Stopped": true, "Future": "x" } } }""");

		ConfigFile.Write(Path, "Demo", Schema, Value("""{ "Events": { "Stopped": false } }"""), ConfigFile.Read(Path, "Demo").Version!);

		JsonElement demo = ConfigFile.Read(Path, "Demo").Value!.Value;
		Assert.IsNull(JsonUtil.Property(demo, "Url"), "a defined key the form left out is removed");
		Assert.AreEqual("1", JsonUtil.Property(demo, "Legacy")?.GetRawText());
		Assert.AreEqual("false", JsonUtil.Property(JsonUtil.Property(demo, "Events")!.Value, "Stopped")?.GetRawText());
		Assert.AreEqual("x", JsonUtil.String(JsonUtil.Property(demo, "Events")!.Value, "Future"));
	}

	[TestMethod]
	internal void CommentsTrailingCommasAndAByteOrderMarkAreAcceptedAndNoTempFileRemains() {
		File.WriteAllText(Path, "{\n\t// ASF tolerates comments\n\t\"IPC\": true,\n}", Encoding.UTF8);

		(ConfigFile.Outcome outcome, _) = ConfigFile.Write(Path, "Demo", Schema, Value("""{ }"""), ConfigFile.Read(Path, "Demo").Version!);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.AreEqual("ASF.json", System.IO.Path.GetFileName(System.IO.Directory.GetFiles(Directory).Single()));
		Assert.IsFalse(ConfigFile.TempSuffix.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
	}

	[TestMethod]
	internal void AFileThatIsNotAJsonObjectIsUnreadableAndUntouched() {
		File.WriteAllText(Path, "{ broken");
		string version = ConfigFile.Read(Path, "Demo").Version!;

		Assert.AreEqual(ConfigFile.Outcome.Unreadable, ConfigFile.Read(Path, "Demo").Outcome);
		Assert.AreEqual(ConfigFile.Outcome.Unreadable, ConfigFile.Write(Path, "Demo", Schema, Value("""{ }"""), version).Outcome);
		Assert.AreEqual("{ broken", File.ReadAllText(Path));
	}

	private static JsonElement Value(string json) => SchemaValidatorTests.Parse(json);
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
