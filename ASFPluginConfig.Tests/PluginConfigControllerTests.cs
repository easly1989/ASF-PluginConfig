using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
[DoNotParallelize]
internal sealed class PluginConfigControllerTests {
	private const string Token = "FAKE-token-0000";

	private const string Parts = """
		"global": { "type": "object", "properties": { "Url": { "type": "string" }, "Token": { "type": "string", "writeOnly": true } } },
		"bot": { "type": "object", "properties": { "Enabled": { "type": "boolean" } } }
		""";

	private static readonly SchemaCatalog Catalog = SchemaCatalog.Load(
		[
			new FakeAssembly("FilePlugin", $$"""{ "configKey": "FileDemo", "storage": "file", "formatVersion": 1, {{Parts}} }"""),
			new FakeAssembly("LivePlugin", $$"""{ "configKey": "LiveDemo", "storage": "file", "formatVersion": 1, "reload": "live", {{Parts}} }"""),
			new FakeAssembly("LegacyPlugin", $$"""{ "configKey": "LegacyDemo", {{Parts}} }""")
		], static _ => { }
	);

	private string ConfigDirectory = "";
	private PluginConfigController Controller = null!;
	private int Restarts;

	private string AsfJson => Path.Combine(ConfigDirectory, "ASF.json");
	private string PluginsDirectory => Path.Combine(ConfigDirectory, "plugins");

	[TestInitialize]
	public void Init() {
		ConfigDirectory = Path.Combine(Path.GetTempPath(), $"pluginconfig-{Guid.NewGuid():N}", "config");
		Directory.CreateDirectory(ConfigDirectory);
		File.WriteAllText(AsfJson, """{ "IPC": true }""");
		File.WriteAllText(Path.Combine(ConfigDirectory, "Bot1.json"), """{ "Enabled": true }""");
		HostSettings.Load(HostSettingsTests.Settings("""{ "ASFPluginConfig": { "AllowWritesWithoutIPCPassword": true } }"""));
		Restarts = 0;
		Controller = new PluginConfigController(Catalog, ConfigDirectory, () => Restarts++);
	}

	[TestCleanup]
	public void Cleanup() {
		HostSettings.Load(null);
		Directory.Delete(Path.GetDirectoryName(ConfigDirectory)!, true);
	}

	[TestMethod]
	internal void ThePluginListSaysWhereEachPluginStoresItsValues() {
		IReadOnlyCollection<PluginSchema> schemas = ((GenericResponse<IReadOnlyCollection<PluginSchema>>) ((ObjectResult) Controller.PluginsGet().Result!).Value!).Result!;

		Assert.AreEqual("FileDemo:file:restart,LegacyDemo:asf-json:restart,LiveDemo:file:live", string.Join(",", schemas.Select(static schema => $"{schema.ConfigKey}:{schema.Storage}:{schema.Reload}").Order(StringComparer.Ordinal)));
	}

	[TestMethod]
	internal void AFilePluginsFirstSaveCreatesItsFileMasksTheSecretAndRestartsAsf() {
		byte[] asfJson = File.ReadAllBytes(AsfJson);
		ConfigValue empty = Value(Controller.GlobalGet("FileDemo"));
		Assert.IsNull(empty.Value);

		SaveResult saved = Saved(Controller.GlobalPost("FileDemo", Request($$"""{ "Url": "https://e.org", "Token": "{{Token}}" }""", empty.Version)));

		Assert.AreEqual("asf", saved.Restart);
		Assert.AreEqual(1, Restarts);
		Assert.Contains(Token, File.ReadAllText(Path.Combine(PluginsDirectory, "FilePlugin.json")));
		CollectionAssert.AreEqual(asfJson, File.ReadAllBytes(AsfJson), "ASF.json is not touched");

		ConfigValue shown = Value(Controller.GlobalGet("FileDemo"));
		Assert.AreEqual(saved.Result, shown.Version);
		Assert.DoesNotContain(Token, shown.Value!.Value.GetRawText());
		Assert.AreEqual("Token", string.Join(",", shown.SetSecrets));

		// A save that leaves the secret out keeps it
		Saved(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://new.example" }""", shown.Version)));
		Assert.Contains(Token, File.ReadAllText(Path.Combine(PluginsDirectory, "FilePlugin.json")));
	}

	[TestMethod]
	internal void ALivePluginsSaveRestartsNothing() {
		SaveResult saved = Saved(Controller.GlobalPost("LiveDemo", Request("""{ "Url": "https://e.org" }""", Value(Controller.GlobalGet("LiveDemo")).Version)));

		Assert.AreEqual("none", saved.Restart);
		Assert.AreEqual(0, Restarts);
		Assert.IsTrue(File.Exists(Path.Combine(PluginsDirectory, "LivePlugin.json")));
	}

	[TestMethod]
	internal void ABotValueGoesIntoThePluginsFileUnderBots() {
		byte[] botFile = File.ReadAllBytes(Path.Combine(ConfigDirectory, "Bot1.json"));

		SaveResult saved = Saved(Controller.BotPost("FileDemo", "Bot1", Request("""{ "Enabled": false }""", Value(Controller.BotGet("FileDemo", "Bot1")).Version)));

		Assert.AreEqual("asf", saved.Restart, "with reload restart a bot value restarts all of ASF");
		Assert.AreEqual(1, Restarts);
		Assert.AreEqual("false", JsonUtil.Property(Value(Controller.BotGet("FileDemo", "Bot1")).Value!.Value, "Enabled")?.GetRawText());
		CollectionAssert.AreEqual(botFile, File.ReadAllBytes(Path.Combine(ConfigDirectory, "Bot1.json")), "the bot's config file is not touched");
		Assert.AreEqual(404, Status(Controller.BotGet("FileDemo", "NoSuchBot")), "only a bot with a config file has a value");
	}

	[TestMethod]
	internal void ASaveAfterAnEditElsewhereIsAConflict() {
		string version = Saved(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://e.org" }""", Value(Controller.GlobalGet("FileDemo")).Version))).Result;
		File.WriteAllText(Path.Combine(PluginsDirectory, "FilePlugin.json"), """{ "formatVersion": 1, "global": { "Url": "https://changed-elsewhere.example" } }""");

		Assert.AreEqual(409, Status(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://mine.example" }""", version))));
		Assert.AreEqual(1, Restarts, "a refused save restarts nothing");
	}

	[TestMethod]
	internal void AFileOfAnUnknownFormatIsAnErrorAndUntouched() {
		Directory.CreateDirectory(PluginsDirectory);
		string path = Path.Combine(PluginsDirectory, "FilePlugin.json");
		File.WriteAllText(path, """{ "formatVersion": 2, "global": { "Url": "https://newer-host.example" } }""");
		byte[] before = File.ReadAllBytes(path);

		Assert.AreEqual(500, Status(Controller.GlobalGet("FileDemo")));
		Assert.AreEqual(500, Status(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://mine.example" }""", ConfigFile.Version(before)))));
		CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
		Assert.AreEqual(0, Restarts);
	}

	[TestMethod]
	internal void ALegacyPluginStillUsesAsfJsonAndRestartsNothingItself() {
		SaveResult global = Saved(Controller.GlobalPost("LegacyDemo", Request("""{ "Url": "https://e.org" }""", Value(Controller.GlobalGet("LegacyDemo")).Version)));
		SaveResult bot = Saved(Controller.BotPost("LegacyDemo", "Bot1", Request("""{ "Enabled": false }""", Value(Controller.BotGet("LegacyDemo", "Bot1")).Version)));

		Assert.AreEqual("asf", global.Restart);
		Assert.AreEqual("bot", bot.Restart);
		Assert.AreEqual(0, Restarts, "ASF's own config watcher applies these");
		Assert.AreEqual("https://e.org", JsonUtil.String(ConfigFile.Read(AsfJson, "LegacyDemo").Value!.Value, "Url"));
		Assert.IsNotNull(ConfigFile.Read(Path.Combine(ConfigDirectory, "Bot1.json"), "LegacyDemo").Value);
		Assert.IsFalse(Directory.Exists(PluginsDirectory));
	}

	[TestMethod]
	internal void APluginWithAnUnsafeAssemblyNameGetsNoFileAndNoCard() {
		List<string> errors = [];
		SchemaCatalog catalog = SchemaCatalog.Load([new FakeAssembly("Evil Plugin", $$"""{ "configKey": "EvilDemo", "storage": "file", "formatVersion": 1, {{Parts}} }""")], errors.Add);
		PluginConfigController controller = new(catalog, ConfigDirectory, () => Restarts++);

		Assert.HasCount(1, errors);
		Assert.Contains("cannot name a settings file", errors[0]);
		Assert.AreEqual(404, Status(controller.GlobalGet("EvilDemo")));
		Assert.AreEqual(404, Status(controller.GlobalPost("EvilDemo", Request("""{ "Url": "https://e.org" }""", ConfigFile.Version([])))));
		Assert.IsFalse(Directory.Exists(PluginsDirectory));
	}

	[TestMethod]
	internal void ALinkedSettingsFileIsRefused() {
		string target = Path.Combine(Path.GetDirectoryName(ConfigDirectory)!, "target.json");
		const string content = """{ "formatVersion": 1, "global": { "Url": "https://outside.example" } }""";
		File.WriteAllText(target, content);
		Directory.CreateDirectory(PluginsDirectory);
		PluginFileTests.Link(() => File.CreateSymbolicLink(Path.Combine(PluginsDirectory, "FilePlugin.json"), target));

		Assert.AreEqual(500, Status(Controller.GlobalGet("FileDemo")));
		Assert.AreEqual(500, Status(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://mine.example" }""", ConfigFile.Version(File.ReadAllBytes(target))))));
		Assert.AreEqual(content, File.ReadAllText(target));
		Assert.AreEqual(0, Restarts);
	}

	[TestMethod]
	internal void SavingStaysRefusedWithoutPasswordOrOptIn() {
		HostSettings.Load(null);

		Assert.AreEqual(403, Status(Controller.GlobalPost("FileDemo", Request("""{ "Url": "https://e.org" }""", ConfigFile.Version([])))));
		Assert.IsFalse(Directory.Exists(PluginsDirectory));
	}

	private static ConfigWriteRequest Request(string valueJson, string version) => JsonSerializer.Deserialize<ConfigWriteRequest>($$"""{ "Value": {{valueJson}}, "Version": "{{version}}" }""")!;

	private static SaveResult Saved(ActionResult<GenericResponse> result) {
		Assert.AreEqual(200, Status(result));

		return (SaveResult) ((ObjectResult) result.Result!).Value!;
	}

	private static int Status(ActionResult<GenericResponse> result) => ((ObjectResult) result.Result!).StatusCode ?? 200;

	private static ConfigValue Value(ActionResult<GenericResponse> result) {
		Assert.AreEqual(200, Status(result));

		return ((GenericResponse<ConfigValue>) ((ObjectResult) result.Result!).Value!).Result!;
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
