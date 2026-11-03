using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ASFPluginConfig.Tests;

#pragma warning disable CA1812 // MSTest uses this class during runtime
[TestClass]
internal sealed class PluginTests {
	[TestMethod]
	internal void PluginIdentifiesItself() {
		ASFPluginConfig plugin = new();

		Assert.AreEqual("ASFPluginConfig", plugin.Name);
		Assert.AreEqual("easly1989/ASF-PluginConfig", plugin.RepositoryName);
		Assert.AreEqual("/plugin-config", plugin.WebPath);
		Assert.AreEqual(plugin.Version.ToString(), HostInfo.Current.Version);
	}
}
#pragma warning restore CA1812 // MSTest uses this class during runtime
