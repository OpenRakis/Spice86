namespace Spice86.Core.Emulator.StateSerialization;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

public class ListingExporter(CfgCpu cpu, NodeToString nodeToString) {
    private readonly ListingExtractor _listingExtractor = new(nodeToString);

    
    /// <summary>
    /// Dumps an assembly listing of instructions encountered so far to the file system.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    public void Write(string path) {
        File.WriteAllLines(path, _listingExtractor.ToAssemblyListing(cpu));
    }
}