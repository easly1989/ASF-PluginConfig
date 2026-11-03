using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class BotFilesTests {
	private string Directory = "";

	[TestInitialize]
	public void Init() {
		Directory = Path.Combine(Path.GetTempPath(), $"pluginconfig-bots-{Guid.NewGuid():N}");
		System.IO.Directory.CreateDirectory(Directory);

		foreach (string file in (string[]) ["ASF.json", "main.json", "Alt.json", ".hidden.json", "main.db", "IPC.config", "ASF.json.pluginconfig.tmp", "dupe.json", "DUPE.json"]) {
			File.WriteAllText(Path.Combine(Directory, file), "{}");
		}
	}

	[TestCleanup]
	public void Cleanup() => System.IO.Directory.Delete(Directory, true);

	[TestMethod]
	internal void ListsOnlyBotConfigs() {
		// On a case-insensitive file system DUPE.json overwrote dupe.json and one bot remains; otherwise both are hidden
		bool oneDupe = System.IO.Directory.GetFiles(Directory).Count(static file => Path.GetFileName(file).Equals("dupe.json", StringComparison.OrdinalIgnoreCase)) == 1;
		string expected = oneDupe ? $"Alt,{Path.GetFileNameWithoutExtension(System.IO.Directory.GetFiles(Directory).Single(static file => Path.GetFileName(file).Equals("dupe.json", StringComparison.OrdinalIgnoreCase)))},main" : "Alt,main";

		Assert.AreEqual(expected, string.Join(",", BotFiles.List(Directory)));
	}

	[TestMethod]
	internal void AMissingDirectoryListsNothing() => Assert.IsEmpty(BotFiles.List(Path.Combine(Directory, "absent")));

	[TestMethod]
	internal void ResolvesAListedBot() => Assert.AreEqual(Path.Combine(Directory, "main.json"), BotFiles.PathOf(Directory, "main"));

	[TestMethod]
	[DataRow("ASF")]
	[DataRow("asf")]
	[DataRow(".hidden")]
	[DataRow("../main")]
	[DataRow("..\\main")]
	[DataRow("MAIN")]
	[DataRow("missing")]
	internal void RefusesAnythingElse(string bot) => Assert.IsNull(BotFiles.PathOf(Directory, bot));
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
