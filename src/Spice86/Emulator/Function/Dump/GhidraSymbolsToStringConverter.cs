namespace Spice86.Emulator.Function.Dump;

using System.Collections.Generic;

using Utils;

public class GhidraSymbolsToStringConverter : FunctionInformationToStringConverter {

    public override string Convert(FunctionInformation functionInformation, IEnumerable<FunctionInformation> allFunctions) {
        return $"{ToCSharpName(functionInformation, true)} {ConvertUtils.ToHex(functionInformation.GetAddress().ToPhysical())} f";
    }
}