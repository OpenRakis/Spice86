namespace Spice86.Tests;

using Spice86.Core.Emulator.Function;

using System.Runtime.Loader;

internal sealed class CompiledGeneratedOverride : IDisposable {
    private readonly AssemblyLoadContext _loadContext;

    public CompiledGeneratedOverride(AssemblyLoadContext loadContext, IOverrideSupplier supplier) {
        _loadContext = loadContext;
        Supplier = supplier;
    }

    public IOverrideSupplier Supplier { get; }

    public void Dispose() {
        if (_loadContext.IsCollectible) {
            _loadContext.Unload();
        }
    }
}
