namespace Spice86.Emulator.Function.Dump;

using Spice86.Emulator.Memory;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Dumps collected function informations to a file
/// </summary>
public class FunctionInformationDumper {

    public void DumpFunctionHandlers(string destinationFilePath, FunctionInformationToStringConverter converter, StaticAddressesRecorder staticAddressesRecorder, FunctionHandler functionHandler) {
        ICollection<FunctionInformation> functionInformations = functionHandler.FunctionInformations.Values;

        // Set for search purposes
        HashSet<FunctionInformation> functionInformationsSet = new(functionInformations);
        List<SegmentRegisterBasedAddress> allGlobals = new(staticAddressesRecorder.GetSegmentRegisterBasedAddress());
        HashSet<SegmentedAddress> whiteListOfSegmentForOffset = new(staticAddressesRecorder.GetWhiteListOfSegmentForOffset());
        StreamWriter? printWriter = null;
        try {
            printWriter = new StreamWriter(destinationFilePath);
            string header = converter.GetFileHeader(allGlobals, whiteListOfSegmentForOffset);
            if (string.IsNullOrWhiteSpace(header) == false) {
                printWriter.WriteLine(header);
            }

            foreach (FunctionInformation functionInformation in functionInformations) {
                if (functionInformation.CalledCount == 0) {
                    continue;
                }

                string res = converter.Convert(functionInformation, functionInformationsSet);
                if (string.IsNullOrWhiteSpace(res) == false) {
                    printWriter.WriteLine(res);
                }
            }

            string footer = converter.GetFileFooter();
            if (string.IsNullOrWhiteSpace(footer) == false) {
                printWriter.WriteLine(footer);
            }
        } finally {
            printWriter?.Dispose();
        }
    }
}