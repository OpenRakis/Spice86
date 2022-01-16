namespace Spice86.Emulator.Function.Dump;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Converts FunctionInformation to custom human readable format with details
/// </summary>
public class DetailedFunctionInformationToStringConverter : FunctionInformationToStringConverter {

    public override string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        StringBuilder res = new StringBuilder();
        Dictionary<FunctionReturn, List<SegmentedAddress>> returns = Sort(functionInformation.GetReturns());
        Dictionary<FunctionReturn, List<SegmentedAddress>> unalignedReturns = Sort(functionInformation.GetUnalignedReturns());
        List<FunctionInformation> callers = GetCallers(functionInformation);
        IEnumerable<FunctionInformation> calls = GetCalls(functionInformation, allFunctions);
        int approximateSize = ApproximateSize(functionInformation);
        string header = $"function {functionInformation}";
        header += $" returns:{returns.Count}";
        header += $" callers:{callers.Count}";
        header += $" called: {functionInformation.GetCalledCount()}";
        header += $" calls:{calls.Count()}";
        header += $" approximateSize:{approximateSize}";
        if (IsOverridable(calls)) {
            header += " overridable";
        }

        res.Append($"{header}\n");
        res.Append(DumpReturns(returns, "returns"));
        res.Append(DumpReturns(unalignedReturns, "unaligned returns"));
        foreach (FunctionInformation caller in callers) {
            res.Append($" - caller: {caller}\n");
        }

        foreach (FunctionInformation call in calls) {
            res.Append($" - call: {call}\n");
        }

        return res.ToString();
    }

    private string DumpReturns(Dictionary<FunctionReturn, List<SegmentedAddress>> returns, string prefix) {
        StringBuilder res = new StringBuilder();
        foreach (KeyValuePair<FunctionReturn, List<SegmentedAddress>> entry in returns) {
            FunctionReturn oneReturn = entry.Key;
            res.Append(" - ");
            res.Append(prefix);
            res.Append(": ");
            res.Append(oneReturn.ToString());
            res.Append('\n');
            List<SegmentedAddress>? targets = entry.Value;
            foreach (SegmentedAddress target in targets) {
                res.Append("   - target: ");
                res.Append(target);
                res.Append('\n');
            }
        }

        return res.ToString();
    }
}