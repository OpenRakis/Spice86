namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using System.Collections.Generic;

internal interface IDosBatchExecutionHost {
    string? TryGetEnvironmentVariable(string variableName);
    IReadOnlyList<KeyValuePair<string, string>> GetEnvironmentVariablesSnapshot();
    bool TrySetEnvironmentVariable(string variableName, string value);
}