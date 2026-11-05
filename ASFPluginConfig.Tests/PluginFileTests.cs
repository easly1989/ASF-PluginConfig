using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class PluginFileTests {
	private static readonly JsonElement Schema = SchemaValidatorTests.Parse("""
		{ "type": "object", "properties": { "Url": { "type": "string" }, "Events": { "type": "object", "properties": { "Stopped": { "type": "boolean" } } } } }
		""");

	private string ConfigDirectory = "";
	private string Outside = "";
	private string Path = "";

	[TestInitialize]
	public void Init() {
		string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pluginconfig-{Guid.NewGuid():N}");
		ConfigDirectory = System.IO.Path.Combine(root, "config");
		Outside = System.IO.Path.Combine(root, "outside");
		Directory.CreateDirectory(ConfigDirectory);
		Directory.CreateDirectory(Outside);
		Path = PluginFile.PathOf(ConfigDirectory, "DemoPlugin")!;
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(System.IO.Path.GetDirectoryName(ConfigDirectory)!, true);

	[TestMethod]
	internal void ThePathIsTheAssemblyNameUnderConfigPlugins() => Assert.AreEqual(System.IO.Path.Combine(System.IO.Path.GetFullPath(ConfigDirectory), "plugins", "DemoPlugin.json"), Path);

	[TestMethod]
	[DataRow("../Evil")]
	[DataRow("..")]
	[DataRow("a/b")]
	[DataRow("")]
	internal void AnUnsafeAssemblyNameHasNoPath(string assembly) => Assert.IsNull(PluginFile.PathOf(ConfigDirectory, assembly));

	[TestMethod]
	internal void AMissingFileReadsAsNoValueAndTheFirstSaveCreatesIt() {
		ConfigFile.Snapshot snapshot = PluginFile.Read(Path, null);

		Assert.AreEqual(ConfigFile.Outcome.Written, snapshot.Outcome);
		Assert.IsNull(snapshot.Value);
		Assert.AreEqual(ConfigFile.Version([]), snapshot.Version);

		(ConfigFile.Outcome outcome, string? version) = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://e.org" }"""), snapshot.Version!);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.AreEqual(ConfigFile.Version(File.ReadAllBytes(Path)), version);

		using JsonDocument written = JsonDocument.Parse(File.ReadAllBytes(Path));
		Assert.AreEqual("formatVersion,global", string.Join(",", written.RootElement.EnumerateObject().Select(static property => property.Name)));
		Assert.AreEqual("1", JsonUtil.Property(written.RootElement, "formatVersion")?.GetRawText());
		Assert.AreEqual("https://e.org", JsonUtil.String(PluginFile.Read(Path, null).Value!.Value, "Url"));
		Assert.IsFalse(File.Exists(Path + ConfigFile.TempSuffix), "the temp file is not left behind");
	}

	[TestMethod]
	internal void AnEmptyFileIsAFileWithNoValuesYet() {
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		File.WriteAllBytes(Path, []);

		(ConfigFile.Outcome outcome, _) = PluginFile.Write(Path, "Bot1", Schema, Value("""{ "Url": "https://e.org" }"""), PluginFile.Read(Path, "Bot1").Version!);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.AreEqual("https://e.org", JsonUtil.String(PluginFile.Read(Path, "Bot1").Value!.Value, "Url"));
	}

	[TestMethod]
	internal void GlobalAndBotValuesShareTheFileAndEverythingElseIsCarriedOver() {
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		File.WriteAllText(Path, """
			{
				// written by hand
				"formatVersion": 1,
				"note": "kept",
				"global": { "Url": "https://old.example", "Legacy": 1 },
				"bots": { "Gone": { "Url": "https://gone.example" }, "Bot1": { "Events": { "Stopped": true, "Future": "x" } } },
			}
			""");

		PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://new.example" }"""), PluginFile.Read(Path, null).Version!);
		(ConfigFile.Outcome outcome, _) = PluginFile.Write(Path, "Bot1", Schema, Value("""{ "Events": { "Stopped": false } }"""), PluginFile.Read(Path, "Bot1").Version!);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);

		using JsonDocument written = JsonDocument.Parse(File.ReadAllBytes(Path));
		JsonElement root = written.RootElement;
		Assert.AreEqual("kept", JsonUtil.String(root, "note"));
		Assert.AreEqual("https://new.example", JsonUtil.String(JsonUtil.Property(root, "global")!.Value, "Url"));
		Assert.AreEqual("1", JsonUtil.Property(JsonUtil.Property(root, "global")!.Value, "Legacy")?.GetRawText(), "a property the schema does not know is kept");

		JsonElement bots = JsonUtil.Property(root, "bots")!.Value;
		Assert.IsNotNull(JsonUtil.Property(bots, "Gone"), "a bot that no longer exists keeps its entry");
		JsonElement events = JsonUtil.Property(JsonUtil.Property(bots, "Bot1")!.Value, "Events")!.Value;
		Assert.AreEqual("false", JsonUtil.Property(events, "Stopped")?.GetRawText());
		Assert.AreEqual("x", JsonUtil.String(events, "Future"));
	}

	[TestMethod]
	internal void NullRemovesOnlyThatValue() {
		string version = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://e.org" }"""), ConfigFile.Version([])).Version!;
		version = PluginFile.Write(Path, "Bot1", Schema, Value("""{ "Url": "https://bot.example" }"""), version).Version!;

		(ConfigFile.Outcome outcome, _) = PluginFile.Write(Path, "Bot1", Schema, null, version);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.IsNull(PluginFile.Read(Path, "Bot1").Value);
		Assert.IsNotNull(PluginFile.Read(Path, null).Value);
	}

	[TestMethod]
	[DataRow("""{ "formatVersion": 2, "global": { "Url": "https://e.org" } }""")]
	[DataRow("""{ "global": { "Url": "https://e.org" } }""")]
	[DataRow("""{ "formatVersion": 1, "bots": [] }""")]
	[DataRow("[1]")]
	internal void AFileOfAnUnknownFormatIsRefusedAndNeverRewritten(string content) {
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		File.WriteAllText(Path, content);
		byte[] before = File.ReadAllBytes(Path);
		ConfigFile.Snapshot snapshot = PluginFile.Read(Path, null);

		Assert.AreEqual(ConfigFile.Outcome.Unreadable, snapshot.Outcome);
		Assert.IsNull(snapshot.Value);
		Assert.AreEqual(ConfigFile.Outcome.Unreadable, PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), snapshot.Version!).Outcome);
		CollectionAssert.AreEqual(before, File.ReadAllBytes(Path));
	}

	[TestMethod]
	internal void AStaleVersionWritesNothing() {
		string version = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://e.org" }"""), ConfigFile.Version([])).Version!;
		File.WriteAllText(Path, """{ "formatVersion": 1, "global": { "Url": "https://changed-elsewhere.example" } }""");
		byte[] before = File.ReadAllBytes(Path);

		Assert.AreEqual(ConfigFile.Outcome.Stale, PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), version).Outcome);
		CollectionAssert.AreEqual(before, File.ReadAllBytes(Path));
	}

	[TestMethod]
	internal void AnEditWhileOurWriteWasOnItsWayIsNotOverwritten() {
		string version = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://e.org" }"""), ConfigFile.Version([])).Version!;
		const string theirs = """{ "formatVersion": 1, "global": { "Url": "https://changed-elsewhere.example" } }""";

		(ConfigFile.Outcome outcome, string? newVersion) = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), version, () => File.WriteAllText(Path, theirs));

		Assert.AreEqual(ConfigFile.Outcome.Stale, outcome);
		Assert.IsNull(newVersion);
		Assert.AreEqual(theirs, File.ReadAllText(Path));
		Assert.IsFalse(File.Exists(Path + ConfigFile.TempSuffix), "the temp file is not left behind");
	}

	[TestMethod]
	internal void ACreateThatLosesTheRaceToAnotherCreatorIsStale() {
		const string theirs = """{ "formatVersion": 1, "global": { "Url": "https://created-elsewhere.example" } }""";

		(ConfigFile.Outcome outcome, string? version) = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), ConfigFile.Version([]), () => File.WriteAllText(Path, theirs));

		Assert.AreEqual(ConfigFile.Outcome.Stale, outcome);
		Assert.IsNull(version);
		Assert.AreEqual(theirs, File.ReadAllText(Path));
		Assert.IsFalse(File.Exists(Path + ConfigFile.TempSuffix), "the temp file is not left behind");
	}

	[TestMethod]
	internal void AWriteKeepsThePreCreatedFilesMode() {
		if (OperatingSystem.IsWindows()) {
			Assert.Inconclusive("Windows files have no Unix mode");

			return;
		}

		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		File.WriteAllBytes(Path, []);
		File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

		(ConfigFile.Outcome outcome, _) = PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://e.org" }"""), PluginFile.Read(Path, null).Version!);

		Assert.AreEqual(ConfigFile.Outcome.Written, outcome);
		Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path));
	}

	[TestMethod]
	internal void ALinkedFileIsNeitherReadNorWritten() {
		string target = System.IO.Path.Combine(Outside, "target.json");
		const string content = """{ "formatVersion": 1, "global": { "Url": "https://outside.example" } }""";
		File.WriteAllText(target, content);
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		Link(() => File.CreateSymbolicLink(Path, target));

		Assert.AreEqual(ConfigFile.Outcome.Unreadable, PluginFile.Read(Path, null).Outcome);
		Assert.AreEqual(ConfigFile.Outcome.Unwritable, PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), ConfigFile.Version(File.ReadAllBytes(target))).Outcome);
		Assert.AreEqual(content, File.ReadAllText(target));
	}

	[TestMethod]
	internal void ALinkedPluginsDirectoryIsNeitherReadNorWritten() {
		Link(() => Directory.CreateSymbolicLink(System.IO.Path.GetDirectoryName(Path)!, Outside));

		Assert.AreEqual(ConfigFile.Outcome.Unreadable, PluginFile.Read(Path, null).Outcome);
		Assert.AreEqual(ConfigFile.Outcome.Unwritable, PluginFile.Write(Path, null, Schema, Value("""{ "Url": "https://mine.example" }"""), ConfigFile.Version([])).Outcome);
		Assert.IsEmpty(Directory.GetFiles(Outside));
	}

	// Windows allows symbolic links only to administrators or in developer mode
	internal static void Link(Action create) {
		ArgumentNullException.ThrowIfNull(create);

		try {
			create();
		} catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			Assert.Inconclusive($"cannot create a symbolic link here: {e.Message}");
		}
	}

	private static JsonElement Value(string json) => SchemaValidatorTests.Parse(json);
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
