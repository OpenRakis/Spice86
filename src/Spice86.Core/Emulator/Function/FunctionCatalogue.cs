namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;

public class FunctionCatalogue {
    public FunctionCatalogue() : this(new Dictionary<SegmentedAddress, FunctionInformation>()) {
    }

    public FunctionCatalogue(IDictionary<SegmentedAddress, FunctionInformation> functionInformations) {
        FunctionInformations = functionInformations;
    }

    public IDictionary<SegmentedAddress, FunctionInformation> FunctionInformations { get; }

    public FunctionInformation GetOrCreateFunctionInformation(SegmentedAddress entryAddress, string? name) {
        if (!FunctionInformations.TryGetValue(entryAddress, out FunctionInformation? res)) {
            res = new FunctionInformation(entryAddress, string.IsNullOrWhiteSpace(name) ? "unknown" : name);
            FunctionInformations.Add(entryAddress, res);
        }
        return res;
    }

    public FunctionInformation? GetFunctionInformation(FunctionCall? functionCall) {
        if (functionCall == null) {
            return null;
        }
        return FunctionInformations.TryGetValue(functionCall.Value.EntryPointAddress, out FunctionInformation? value) ? value : null;
    }
    
}