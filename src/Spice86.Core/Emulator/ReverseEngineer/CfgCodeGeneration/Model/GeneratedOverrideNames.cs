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

    /// <summary>The dump file (without directory) holding the fields and constructor of the override class.</summary>
    public const string DumpFileSuffix = OverrideClassName + ".cs";

    /// <summary>The file (without directory) holding the generated override supplier class.</summary>
    public const string SupplierFileName = SupplierClassName + ".cs";

    /// <summary>The instance field holding the <c>Func&lt;int, Action&gt;</c> a single-entry
    /// delegate is converted to once in the generated constructor, so every site (registrations,
    /// call helpers, jump dispatch) shares the same delegate identity.</summary>
    public static string EntryFieldNameOf(string delegateName) => delegateName + "Entry";
}
