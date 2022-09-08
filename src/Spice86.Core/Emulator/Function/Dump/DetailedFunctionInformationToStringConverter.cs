namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Converts FunctionInformation to custom human readable format with details
/// </summary>
public class DetailedFunctionInformationToStringConverter : FunctionInformationToStringConverter {

    public override string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        StringBuilder res = new StringBuilder();
        Dictionary<FunctionReturn, ISet<SegmentedAddress>> returns = Sort(functionInformation.Returns);
        Dictionary<FunctionReturn, ISet<SegmentedAddress>> unalignedReturns = Sort(functionInformation.UnalignedReturns);
        List<FunctionInformation> callers = GetCallers(functionInformation);
        IEnumerable<FunctionInformation> calls = GetCalls(functionInformation, allFunctions);
        long approximateSize = ApproximateSize(functionInformation);
        StringBuilder header = new($"function {functionInformation}");
        header
            .Append(" returns:")
            .Append(returns.Count)
            .Append(" callers:")
            .Append("callers.Count}")
            .Append(" called: ")
            .Append(functionInformation.CalledCount)
            .Append(" calls:")
            .Append(calls.Count())
            .Append(" approximateSize:")
            .Append(approximateSize);
        if (IsOverridable(calls)) {
            header.Append(" overridable");
        }

        res.Append(header).Append('\n');
        res.Append(DumpReturns(returns, "returns"));
        res.Append(DumpReturns(unalignedReturns, "unaligned returns"));
        for (int i = 0; i < callers.Count; i++) {
            FunctionInformation caller = callers[i];
            res.Append(" - caller: ").Append(caller).Append('\n');
        }

        foreach (FunctionInformation call in calls) {
            res.Append(" - call: ").Append(call).Append('\n');
        }

        return res.ToString();
    }

    private string DumpReturns(Dictionary<FunctionReturn, ISet<SegmentedAddress>> returns, string prefix) {
        StringBuilder res = new StringBuilder();
        foreach (KeyValuePair<FunctionReturn, ISet<SegmentedAddress>> entry in returns) {
            FunctionReturn oneReturn = entry.Key;
            res.Append(" - ");
            res.Append(prefix);
            res.Append(": ");
            res.Append(oneReturn);
            res.Append('\n');
            foreach (SegmentedAddress target in entry.Value) {
                res.Append("   - target: ");
                res.Append(target);
                res.Append('\n');
            }
        }

        return res.ToString();
    }
}