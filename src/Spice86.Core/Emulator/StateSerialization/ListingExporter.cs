namespace Spice86.Core.Emulator.StateSerialization;

using Serilog.Events;

using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Interfaces;

public class ListingExporter(CfgCpu cpu, ILoggerService loggerService, NodeToString nodeToString) {
    private readonly ListingExtractor _listingExtractor = new(nodeToString);

    
    /// <summary>
    /// Dumps an assembly listing of instructions encountered so far to the file system.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    public void Write(string path) {
        if (loggerService.IsEnabled(LogEventLevel.Information)) {
            loggerService.Information("Saving executed listing in file {Path}", path);
        }
        
        File.WriteAllLines(path, _listingExtractor.ToAssemblyListing(cpu));
    }
}