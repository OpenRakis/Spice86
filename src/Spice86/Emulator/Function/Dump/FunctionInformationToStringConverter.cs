using Spice86.Emulator.Memory;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Spice86.Emulator.Function.Dump
{
    /// <summary>
    /// Base class for FunctionInformation to String conversion. Each subclass could implement a custom format.
    /// </summary>
    public abstract class FunctionInformationToStringConverter
    {
        /// <summary>
        /// Generates the header for the file.
        /// </summary>
        /// <param name="allGlobals">List of addresses encountered during the execution. Hacky to pass it there, but used by Java stub generator.</param>
        /// <param name="whiteListOfSegmentForOffset">Set of segmented addresses for which nothing should be displayed if the offset matches but not the segmen</param>
        /// <returns></returns>
        public virtual string GetFileHeader(IEnumerable<SegmentRegisterBasedAddress> allGlobals, IEnumerable<SegmentedAddress> whiteListOfSegmentForOffset)
        {
            return "";
        }

        /// <summary>
        /// Generates the footer of the file.
        /// </summary>
        /// <returns></returns>
        public virtual string GetFileFooter()
        {
            return "";
        }

        /// <summary>
        /// Called once per functionInformation.
        /// </summary>
        /// <param name="functionInformation"></param>
        /// <param name="allFunctions"></param>
        /// <returns></returns>
        public abstract string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions);
        protected virtual int ApproximateSize(FunctionInformation functionInformation)
        {
            List<SegmentedAddress> boundaries = GetBoundaries(functionInformation);
            SegmentedAddress first = boundaries[0];
            SegmentedAddress last = boundaries[^1];
            return Math.Abs(first.ToPhysical() - last.ToPhysical());
        }

        protected virtual List<SegmentedAddress> GetBoundaries(FunctionInformation functionInformation)
        {
            List<SegmentedAddress> boundaries = new();
            boundaries.AddRange(functionInformation.GetReturns().Keys.ToDictionary(x => x.GetAddress()).Select(x => x.Key));
            boundaries.Add(functionInformation.GetAddress());
            return boundaries.OrderBy(x => x).ToList();
        }

        protected virtual bool ContainsNonOverride(IEnumerable<FunctionInformation> calls)
        {
            return calls.Any((function) => !function.HasOverride());
        }

        protected virtual bool IsOverridable(IEnumerable<FunctionInformation> calls)
        {
            return calls.Any() == false || !ContainsNonOverride(calls);
        }

        protected virtual List<FunctionInformation> GetCallers(FunctionInformation functionInformation)
        {
            return Sort(functionInformation.GetCallers()).ToList();
        }

        protected virtual IEnumerable<FunctionInformation> GetCalls(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions)
        {

            // calls made by this function is the list of functions that get called by it
            return Sort(allFunctions.Where((callee) => callee.GetCallers().Contains(functionInformation)));
        }

        protected virtual ICollection<T> Sort<T>(ICollection<T> collection)
        {
            return collection.OrderBy(x => x).ToList();
        }

        protected virtual IEnumerable<T> Sort<T>(IEnumerable<T> enumerable)
        {
            return enumerable.OrderBy(x => x).ToList();
        }

        protected virtual IDictionary<K, V> Sort<K, V>(IDictionary<K, V> map)
        {
            var ordered = map.OrderBy(x => x);
            Dictionary<K, V> result = new();
            foreach (var kv in ordered)
            {
                result.Add(kv.Key, kv.Value);
            }
            return result;
        }

        protected virtual string JoinNewLine(IEnumerable<string> enumerable)
        {
            var sb = new StringBuilder();
            foreach (var item in enumerable)
            {
                sb.Append(item);
            }
            sb.Append(Environment.NewLine);
            return sb.ToString();
        }

        protected virtual string ToCSharpName(FunctionInformation functionInformation, bool dots)
        {
            string? nameToUse = functionInformation.GetName();
            if (!dots)
            {
                nameToUse = RemoveDotsFromFunctionName(nameToUse);
            }

            return $"{nameToUse}_{ConvertUtils.ToCSharpStringWithPhysical(functionInformation.GetAddress())}";
        }

        protected virtual string? RemoveDotsFromFunctionName(string? name)
        {
            if(name is null)
            {
                return null;
            }
            String[] functionNameSplit = name.Split(".");
            if (functionNameSplit.Length > 1)
            {
                return functionNameSplit[^1];
            }

            return name;
        }
    }
}