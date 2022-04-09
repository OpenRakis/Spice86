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
        var res = new StringBuilder();
        Dictionary<FunctionReturn, ISet<SegmentedAddress>> returns = Sort(functionInformation.Returns);
        Dictionary<FunctionReturn, ISet<SegmentedAddress>> unalignedReturns = Sort(functionInformation.UnalignedReturns);
        List<FunctionInformation> callers = GetCallers(functionInformation);
        IEnumerable<FunctionInformation> calls = GetCalls(functionInformation, allFunctions);
        long approximateSize = ApproximateSize(functionInformation);
        string header = $"function {functionInformation}";
        header += $" returns:{returns.Count}";
        header += $" callers:{callers.Count}";
        header += $" called: {functionInformation.CalledCount}";
        header += $" calls:{calls.Count()}";
        header += $" approximateSize:{approximateSize}";
        if (IsOverridable(calls)) {
            header += " overridable";
        }

        res.Append(header).Append('\n');
        res.Append(DumpReturns(returns, "returns"));
        res.Append(DumpReturns(unalignedReturns, "unaligned returns"));
        foreach (FunctionInformation caller in callers) {
            res.Append(" - caller: ").Append(caller).Append('\n');
        }

        foreach (FunctionInformation call in calls) {
            res.Append(" - call: ").Append(call).Append('\n');
        }

        return res.ToString();
    }

    private string DumpReturns(Dictionary<FunctionReturn, ISet<SegmentedAddress>> returns, string prefix) {
        var res = new StringBuilder();
        foreach (KeyValuePair<FunctionReturn, ISet<SegmentedAddress>> entry in returns) {
            FunctionReturn oneReturn = entry.Key;
            res.Append(" - ");
            res.Append(prefix);
            res.Append(": ");
            res.Append(oneReturn);
            res.Append('\n');
            ISet<SegmentedAddress> targets = entry.Value;
            foreach (SegmentedAddress target in targets) {
                res.Append("   - target: ");
                res.Append(target);
                res.Append('\n');
            }
        }

        return res.ToString();
    }
}