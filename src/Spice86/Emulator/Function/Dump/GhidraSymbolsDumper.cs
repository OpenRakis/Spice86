namespace Spice86.Emulator.Function.Dump;

using Errors;

using Memory;

using Newtonsoft.Json;

using Serilog;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Utils;

using VM;

public class GhidraSymbolsDumper {
    private static readonly ILogger _logger = Log.Logger.ForContext<GhidraSymbolsDumper>();

    public void Dump(Machine machine, string destinationFilePath) {
        List<FunctionInformation> functionInformations = FunctionInformationDumper.MergeFunctionHandlers(machine.Cpu.FunctionHandler, machine.Cpu.FunctionHandlerInExternalInterrupt).ToList();
        List<string> lines = new();
        dumpFunctionInformations(lines, functionInformations);
        dumpLabels(lines, machine.Cpu.JumpHandler);
        using var printWriter = new StreamWriter(destinationFilePath);
        lines.ForEach(line => printWriter.WriteLine(line));
    }

    private void dumpLabels(List<string> lines, JumpHandler jumpHandler) {
        jumpHandler.JumpsFromTo
            .SelectMany(x => x.Value)
            .Select(address => dumpLabel(address))
            .ToList()
            .ForEach(line => lines.Add(line));
    }

    private string dumpLabel(uint address) {
        return toGhidraSymbol($"spice86_label_{ConvertUtils.ToHex32WithoutX(address)}", address, "l");
    }

    private void dumpFunctionInformations(List<string> lines, List<FunctionInformation> functionInformations) {
        foreach (FunctionInformation functionInformation in functionInformations) {
            lines.Add(dumpFunctionInformation(functionInformation));
        }
    }

    private string dumpFunctionInformation(FunctionInformation functionInformation) {
        return toGhidraSymbol(functionInformation.Address, functionInformation.Name, "f");
    }

    private string toGhidraSymbol(SegmentedAddress address, string name, string type) {
        string addressString = $"{ConvertUtils.ToHex16WithoutX(address.Segment)}_{ConvertUtils.ToHex16WithoutX(address.Offset)}_{ConvertUtils.ToHex32WithoutX(address.ToPhysical())}";
        return toGhidraSymbol($"{name}_{addressString}", address.ToPhysical(), type);
    }

    private string toGhidraSymbol(string name, uint address, string type) {
        return $"{name} {ConvertUtils.ToHex(address)} {type}";
    }

    public JumpHandler ReadFromFileOrCreate(string? filePath) {
        if (String.IsNullOrEmpty(filePath)) {
            _logger.Information("No file specified");
            return new JumpHandler();
        }
        if (!File.Exists(filePath)) {
            _logger.Information("File doesn't exists");
            return new JumpHandler();
        }
        try {
            return JsonConvert.DeserializeObject<JumpHandler>(File.ReadAllText(filePath));
        } catch (JsonException e) {
            throw new UnrecoverableException($"File {filePath} is not valid", e);
        }
    }
}