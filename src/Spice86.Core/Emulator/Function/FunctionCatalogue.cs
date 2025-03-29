namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class FunctionCatalogue {
    public FunctionCatalogue() : this(new List<FunctionInformation>()) {
    }

    public FunctionCatalogue(IEnumerable<FunctionInformation> functionInformations) {
        FunctionInformations = functionInformations.ToDictionary(f => f.Address, f => f);
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