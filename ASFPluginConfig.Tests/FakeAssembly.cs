using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace ASFPluginConfig.Tests;

// An assembly that carries only a config schema resource, so invalid schemas need no compiled fixture
internal sealed class FakeAssembly(string name, string schemaJson) : Assembly {
	private readonly string ResourceName = name + SchemaCatalog.ResourceSuffix;

	public override string FullName => name;

	public override AssemblyName GetName() => new(name);

	public override string[] GetManifestResourceNames() => [ResourceName];

	public override Stream? GetManifestResourceStream(string name) => name == ResourceName ? new MemoryStream(Encoding.UTF8.GetBytes(schemaJson)) : null;
}

internal sealed class ThrowingAssembly : Assembly {
	public override string FullName => "Throwing";

	public override AssemblyName GetName() => new("Throwing");

	public override string[] GetManifestResourceNames() => throw new BadImageFormatException("broken image");
}
