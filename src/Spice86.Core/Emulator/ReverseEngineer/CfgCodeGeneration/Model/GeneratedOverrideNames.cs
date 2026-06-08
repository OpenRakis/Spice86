namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

/// <summary>
/// Single source of truth for the names the C# generator bakes into its output: the generated namespace,
/// the supplier class, the override class, and the dump file name. Centralized so the emitter, the dumper,
/// and the state-dump file naming cannot drift out of sync.
/// </summary>
internal static class GeneratedOverrideNames {
    public const string GeneratedNamespace = "Spice86.Generated";
    public const string SupplierClassName = "CfgGeneratedOverrideSupplier";
    public const string OverrideClassName = "CfgGeneratedOverrides";

    /// <summary>The dump file (without directory) the generated source is written to.</summary>
    public const string DumpFileSuffix = OverrideClassName + ".cs";
}
