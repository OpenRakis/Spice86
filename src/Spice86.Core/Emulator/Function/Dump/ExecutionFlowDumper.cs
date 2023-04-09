using Spice86.Shared.Interfaces;

namespace Spice86.Core.Emulator.Function.Dump;

using Errors;

using Newtonsoft.Json;

using Serilog;

using Spice86.Core.Emulator.Function;
using Spice86.Logging;

using System;
using System.Diagnostics;
using System.IO;

public class ExecutionFlowDumper {
    private readonly ILoggerService _loggerService;

    public ExecutionFlowDumper(ILoggerService loggerService) {
        _loggerService = loggerService;
    }

    public void Dump(ExecutionFlowRecorder executionFlowRecorder, string destinationFilePath) {
        using StreamWriter printWriter = new StreamWriter(destinationFilePath);
        string jsonString = JsonConvert.SerializeObject(executionFlowRecorder);
        printWriter.WriteLine(jsonString);
    }

    public ExecutionFlowRecorder ReadFromFileOrCreate(string filePath) {
        if (!File.Exists(filePath)) {
            _loggerService.Verbose("File doesn't exists");
            return new ExecutionFlowRecorder();
        }
        try {
            if (string.IsNullOrWhiteSpace(filePath) == false && File.Exists(filePath)) {
                return JsonConvert.DeserializeObject<ExecutionFlowRecorder>(File.ReadAllText(filePath)) ?? new();
            } else {
                return new();
            }
        } catch (JsonException e) {
            e.Demystify();
            throw new UnrecoverableException($"File {filePath} is not valid", e);
        }
    }
}