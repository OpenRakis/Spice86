namespace Spice86.Core.Emulator.Function;

using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Defines the contract for providing C# function overrides to replace assembly code during emulation.
/// </summary>
/// <remarks>
/// This interface is central to Spice86's reverse engineering workflow. Implementations provide mappings
/// from segmented addresses in the original program to C# reimplementations of those functions.
/// <para>
/// <b>Usage in reverse engineering:</b>
/// <list type="number">
/// <item>Run the DOS program in Spice86 with <c>--DumpDataOnExit true</c></item>
/// <item>Load the memory dump in Ghidra using the spice86-ghidra-plugin</item>
/// <item>Decompile functions and convert them to C# using CSharpOverrideHelper base class</item>
/// <item>Implement this interface to register your C# overrides</item>
/// <item>Run with <c>--OverrideSupplierClassName YourClass --UseCodeOverride true</c></item>
/// </list>
/// </para>
/// <para>
/// This allows incremental rewriting of assembly functions into C# while maintaining a working program,
/// making it easier to understand complex DOS binaries without complete source code.
/// </para>
/// </remarks>
public interface IOverrideSupplier {

    /// <summary>
    /// Generates function information overrides for the given target program.
    /// </summary>
    /// <param name="loggerService">The logger.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="programStartAddress">The start address of the program.</param>
    /// <param name="machine">The emulator machine.</param>
    /// <returns>A dictionary containing the generated function information overrides.</returns>
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService,
        Configuration configuration,
        ushort programStartAddress,
        Machine machine);
}