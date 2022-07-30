namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Base class for FunctionInformation to String conversion. Each subclass could implement a custom format.
/// </summary>
public abstract class FunctionInformationToStringConverter {

    /// <summary>
    /// Called once per functionInformation.
    /// </summary>
    /// <param name="functionInformation"></param>
    /// <param name="allFunctions"></param>
    /// <returns></returns>
    public abstract string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions);

    /// <summary>
    /// Generates the footer of the file.
    /// </summary>
    /// <returns></returns>
    public virtual string GetFileFooter() {
        return "";
    }

    /// <summary>
    /// Generates the header for the file.
    /// </summary>
    /// <param name="allGlobals">List of addresses encountered during the execution. Hacky to pass it there, but used by Java stub generator.</param>
    /// <param name="whiteListOfSegmentForOffset">Set of segmented addresses for which nothing should be displayed if the offset matches but not the segmen</param>
    /// <returns></returns>
    public virtual string GetFileHeader(List<SegmentRegisterBasedAddress> allGlobals, HashSet<SegmentedAddress> whiteListOfSegmentForOffset) {
        return "";
    }

    protected long ApproximateSize(FunctionInformation functionInformation) {
        List<SegmentedAddress> boundaries = GetBoundaries(functionInformation);
        SegmentedAddress first = boundaries[0];
        SegmentedAddress last = boundaries[^1];
        return Math.Abs(first.ToPhysical() - (long)last.ToPhysical());
    }

    protected bool ContainsNonOverride(IEnumerable<FunctionInformation> calls) {
        return calls.Any((function) => !function.HasOverride);
    }

    protected List<SegmentedAddress> GetBoundaries(FunctionInformation functionInformation) {
        List<SegmentedAddress> boundaries = new();
        boundaries.AddRange(functionInformation.Returns.Keys.ToDictionary(x => x.Address).Select(x => x.Key));
        boundaries.Add(functionInformation.Address);
        return boundaries.OrderBy(x => x).ToList();
    }

    protected List<FunctionInformation> GetCallers(FunctionInformation functionInformation) {
        return Sort(functionInformation.Callers).ToList();
    }

    protected IEnumerable<FunctionInformation> GetCalls(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        // calls made by this function is the list of functions that get called by it
        return Sort(allFunctions.Where((callee) => callee.Callers.Contains(functionInformation)));
    }

    protected bool IsOverridable(IEnumerable<FunctionInformation> calls) {
        return calls.Any() == false || !ContainsNonOverride(calls);
    }

    protected string JoinNewLine(IEnumerable<string> enumerable) {
        var sb = new StringBuilder();
        foreach (string? item in enumerable) {
            sb.Append(item);
        }
        sb.Append('\n');
        return sb.ToString();
    }

    protected string? RemoveDotsFromFunctionName(string? name) {
        if (name is null) {
            return null;
        }
        string[] functionNameSplit = name.Split(".");
        if (functionNameSplit.Length > 1) {
            return functionNameSplit[^1];
        }

        return name;
    }

    protected ICollection<T> Sort<T>(ICollection<T> collection) {
        return collection.OrderBy(x => x).ToList();
    }

    protected IEnumerable<T> Sort<T>(IEnumerable<T> enumerable) {
        return enumerable.OrderBy(x => x).ToList();
    }

    protected Dictionary<K, V> Sort<K, V>(IDictionary<K, V> map) where K : notnull {
        IOrderedEnumerable<KeyValuePair<K, V>>? ordered = map.OrderBy(x => x.Key);
        Dictionary<K, V> result = new();
        foreach (KeyValuePair<K, V> kv in ordered) {
            result.Add(kv.Key, kv.Value);
        }
        return result;
    }

    protected string ToCSharpName(FunctionInformation functionInformation, bool dots) {
        string? nameToUse = functionInformation.Name;
        if (!dots) {
            nameToUse = RemoveDotsFromFunctionName(nameToUse);
        }

        return $"{nameToUse}_{ConvertUtils.ToCSharpStringWithPhysical(functionInformation.Address)}";
    }
}
