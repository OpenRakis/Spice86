namespace Spice86.Core.Emulator.Function.Dump;

using System.IO;
using System.Linq;

using Serilog.Events;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

/// <summary>
/// Provides functionality for dumping Ghidra symbols and labels to a file.
/// </summary>
public class GhidraSymbolsDumper {
    private readonly ILoggerService _loggerService;
    
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    public GhidraSymbolsDumper(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    /// <summary>
    /// Dumps function information and labels to a file.
    /// </summary>
    /// <param name="machine">The machine instance to extract the function information and labels from.</param>
    /// <param name="destinationFilePath">The path of the file to write the dumped information to.</param>
    public void Dump(Machine machine, string destinationFilePath) {
        ICollection<FunctionInformation> functionInformations = machine.Cpu.FunctionHandler.FunctionInformations.Values;
        List<string> lines = new();
        // keep addresses in a set in order not to write a label where a function was, ghidra will otherwise overwrite functions with labels and this is not cool.
        ISet<SegmentedAddress> dumpedAddresses = new HashSet<SegmentedAddress>();
        DumpFunctionInformations(lines, dumpedAddresses, functionInformations);
        DumpLabels(lines, dumpedAddresses, machine.Cpu.ExecutionFlowRecorder);
        using StreamWriter printWriter = new StreamWriter(destinationFilePath);
        lines.ForEach(line => printWriter.WriteLine(line));
    }

    private void DumpLabels(List<string> lines, ISet<SegmentedAddress> dumpedAddresses, ExecutionFlowRecorder executionFlowRecorder) {
        executionFlowRecorder.JumpsFromTo
            .SelectMany(x => x.Value)
            .Where(address => !dumpedAddresses.Contains(address))
            .OrderBy(x => x)
            .Distinct()
            .OrderBy(x => x)
            .ToList()
            .ForEach(address => lines.Add(DumpLabel(address)));
    }

    private string DumpLabel(SegmentedAddress address) {
        return ToGhidraSymbol("spice86_label", address, "l");
    }

    private void DumpFunctionInformations(ICollection<string> lines, ISet<SegmentedAddress> dumpedAddresses, ICollection<FunctionInformation> functionInformations) {
        functionInformations
            .OrderBy(functionInformation => functionInformation.Address)
            .Select(functionInformation => DumpFunctionInformation(dumpedAddresses, functionInformation))
            .ToList()
            .ForEach(line => lines.Add(line));
    }

    private string DumpFunctionInformation(ISet<SegmentedAddress> dumpedAddresses, FunctionInformation functionInformation) {
        dumpedAddresses.Add(functionInformation.Address);
        return ToGhidraSymbol(functionInformation.Name, functionInformation.Address, "f");
    }

    private string ToGhidraSymbol(string name, SegmentedAddress address, string type) {
        return $"{name}_{ToString(address)} {ConvertUtils.ToHex(address.ToPhysical())} {type}";
    }

    private string ToString(SegmentedAddress address) {
        return $"{ConvertUtils.ToHex16WithoutX(address.Segment)}_{ConvertUtils.ToHex16WithoutX(address.Offset)}_{ConvertUtils.ToHex32WithoutX(address.ToPhysical())}";
    }

    /// <summary>
    /// Reads the symbols and labels from the specified file or creates an empty dictionary if the file does not exist.
    /// </summary>
    /// <param name="filePath">The path of the file to read the symbols and labels from.</param>
    /// <returns>A dictionary containing the symbols and labels.</returns>
    public IDictionary<SegmentedAddress, FunctionInformation> ReadFromFileOrCreate(string filePath) {
        if (!File.Exists(filePath)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("File doesn't exist");
            }
            return new Dictionary<SegmentedAddress, FunctionInformation>();
        }
        return File.ReadLines(filePath)
            .Select(line => ToFunctionInformation(line))
            .OfType<FunctionInformation>()
            .Distinct()
            .ToDictionary(functionInformation => functionInformation.Address, functionInformation => functionInformation);
    }

    private FunctionInformation? ToFunctionInformation(string line) {
        string[] split = line.Split(" ");
        if (split.Length != 3) {
            _loggerService.Debug("Cannot parse line {Line} into a function, only lines with 3 arguments can represent functions", line);
            // Not a function line
            return null;
        }
        string type = split[2];
        if (type == "f") {
            return NameToFunctionInformation(_loggerService, split[0]);
        }

        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("Cannot parse line {Line} into a function, type is not f", line);
        }

        // Not a function line
        return null;
    }

    /// <summary>
    /// Parses a function name with an associated address string into a <see cref="FunctionInformation"/> instance. <br/>
    /// Example of a valid function name: 'IncDialogueCount47A8_0x1ED_0xA1E8_0xC0B8'
    /// </summary>
    /// <param name="loggerService">The logger service to use for logging errors during parsing.</param>
    /// <param name="nameWithAddress">The function name with address to parse.</param>
    /// <returns>A <see cref="FunctionInformation"/> instance representing the parsed function, or <c>null</c> if parsing failed.</returns>
    public static FunctionInformation? NameToFunctionInformation(ILoggerService loggerService, string nameWithAddress) {
        string[] nameSplit = nameWithAddress.Split("_");
        if (nameSplit.Length < 4) {
            // Format is not correct, we can't use this line
            if (loggerService.IsEnabled(LogEventLevel.Debug)) {
                loggerService.Debug("Cannot parse function name {NameWithAddress} into a function, segmented address missing", nameWithAddress);
            }
            return null;
        }
        SegmentedAddress address;
        try {
            ushort segment = ConvertUtils.ParseHex16(nameSplit[^3]);
            ushort offset = ConvertUtils.ParseHex16(nameSplit[^2]);
            address = new SegmentedAddress(segment, offset);
        } catch (FormatException) {
            if (loggerService.IsEnabled(LogEventLevel.Debug)) {
                loggerService.Debug(
                    "Cannot parse function name {NameWithAddress} into a function, the last 3 underscore segments of the name are not hexadecimal values",
                    nameWithAddress);
            }
            return null;
        }
        string nameWithoutAddress = string.Join("_", nameSplit.Take(nameSplit.Length - 3));
        return new FunctionInformation(address, nameWithoutAddress);
    }
}