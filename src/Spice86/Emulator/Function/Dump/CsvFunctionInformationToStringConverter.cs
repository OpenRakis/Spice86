namespace Spice86.Emulator.Function.Dump;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Converts FunctionInformation to CSV
/// </summary>
public class CsvFunctionInformationToStringConverter : FunctionInformationToStringConverter {

    public override string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        IEnumerable<FunctionInformation>? calls = GetCalls(functionInformation, allFunctions);
        return GenerateLine(ToCSharpName(functionInformation, true), Size(functionInformation.GetReturns()), Size(functionInformation.GetUnalignedReturns()), Size(GetCallers(functionInformation)), functionInformation.GetCalledCount().ToString(), Size(calls), ApproximateSize(functionInformation).ToString(), IsOverridable(calls).ToString(), functionInformation.HasOverride().ToString());
    }

    public override string GetFileHeader(List<SegmentRegisterBasedAddress> allGlobals, HashSet<SegmentedAddress> whiteListOfSegmentForOffset) {
        return GenerateLine("Name", "Returns", "UnalignedReturns", "Callers", "Called", "Calls", "ApproximateSize", "Overridable", "Overriden");
    }

    private static string GenerateLine(string name, string returns, string unalignedReturns, string callers, string called, string calls, string approximateSize, string overridable, string overridden) {
        StringBuilder res = new();
        res.Append(name);
        res.Append(',');
        res.Append(returns);
        res.Append(',');
        res.Append(unalignedReturns);
        res.Append(',');
        res.Append(callers);
        res.Append(',');
        res.Append(called);
        res.Append(',');
        res.Append(calls);
        res.Append(',');
        res.Append(approximateSize);
        res.Append(',');
        res.Append(overridable);
        res.Append(',');
        res.Append(overridden);
        return res.ToString();
    }

    private string Size<K, V>(IDictionary<K, V> map) {
        return map.Count.ToString();
    }

    private string Size<T>(IEnumerable<T> collection) {
        return collection.Count().ToString();
    }
}