namespace Spice86.Core.Emulator.Function.Dump;

using Errors;

using Newtonsoft.Json;

using Serilog;

using Spice86.Core.Emulator.Function;
using Spice86.Logging;

using System;
using System.IO;

public class ExecutionFlowDumper {
    private static readonly ILogger _logger = new Serilogger().Logger.ForContext<ExecutionFlowDumper>();

    public void Dump(ExecutionFlowRecorder executionFlowRecorder, string destinationFilePath) {
        using var printWriter = new StreamWriter(destinationFilePath);
        string jsonString = JsonConvert.SerializeObject(executionFlowRecorder);
        printWriter.WriteLine(jsonString);
    }

    public ExecutionFlowRecorder ReadFromFileOrCreate(string filePath) {
        if (!File.Exists(filePath)) {
            _logger.Information("File doesn't exists");
            return new ExecutionFlowRecorder();
        }
        try {
            if (string.IsNullOrWhiteSpace(filePath) == false && File.Exists(filePath)) {
                return JsonConvert.DeserializeObject<ExecutionFlowRecorder>(File.ReadAllText(filePath)) ?? new();
            } else {
                return new();
            }
        } catch (JsonException e) {
            throw new UnrecoverableException($"File {filePath} is not valid", e);
        }
    }
}