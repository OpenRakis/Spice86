namespace Spice86.Tests;

using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Collectible load context used to host a single compiled generated-override assembly so it can be
/// unloaded once the test that produced it completes, instead of permanently growing the default context.
/// </summary>
internal sealed class CollectibleAssemblyLoadContext : AssemblyLoadContext {
    public CollectibleAssemblyLoadContext() : base(isCollectible: true) {
    }

    protected override Assembly? Load(AssemblyName assemblyName) {
        return null;
    }
}
