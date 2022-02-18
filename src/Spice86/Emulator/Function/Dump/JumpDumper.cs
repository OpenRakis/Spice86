namespace Spice86.Emulator.Function.Dump;

using Errors;

using Memory;

using Newtonsoft.Json;

using Serilog;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using JsonSerializer = System.Text.Json.JsonSerializer;

public class JumpDumper {
    private static readonly ILogger _logger = Log.Logger.ForContext<JumpDumper>();

    public void Dump(JumpHandler jumpHandler, string destinationFilePath) {
        using StreamWriter printWriter = new StreamWriter(destinationFilePath);
        string jsonString = JsonConvert.SerializeObject(jumpHandler);
        printWriter.WriteLine(jsonString);
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
            throw new UnrecoverableException("File " + filePath + " is not valid", e);
        }
    }
}