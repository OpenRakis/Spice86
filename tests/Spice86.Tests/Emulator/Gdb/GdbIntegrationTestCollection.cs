namespace Spice86.Tests.Emulator.Gdb;

using Xunit;

/// <summary>
/// Defines a test collection for GDB integration tests that must run sequentially.
/// This ensures that only one GDB server/client pair is active at a time,
/// avoiding port conflicts and race conditions.
/// </summary>
[CollectionDefinition("GDB Integration Tests", DisableParallelization = true)]
public class GdbIntegrationTestCollection {
    // This class is never instantiated. It exists only to define the collection.
}
