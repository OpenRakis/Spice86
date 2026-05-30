namespace Spice86.Tests;

using Spice86.Core.Emulator.Function;

internal sealed class CompiledGeneratedOverride(IOverrideSupplier supplier) {
    public IOverrideSupplier Supplier { get; } = supplier;
}
