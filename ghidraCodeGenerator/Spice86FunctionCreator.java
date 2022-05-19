import ghidra.app.script.GhidraScript;
import ghidra.program.database.function.OverlappingFunctionException;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.util.exception.InvalidInputException;
import org.apache.commons.collections4.IteratorUtils;

import java.io.Closeable;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.util.Arrays;
import java.util.List;

//Create functions at given hardcoded addresses, for when generated code complains no function exists because ghidra didnt create them
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86FunctionCreator extends GhidraScript {
  private List<Integer> addresses =
      Arrays.asList(0x188AF, 0x1181E, 0x1998E, 0x11771, 0x1181E, 0x1C868, 0x11BB2, 0x11BB2, 0x178E9, 0x11EDA, 0x129EE, 0x12017, 0x184A6, 0x18357, 0x183FD, 0x171B2, 0x16144, 0x124D2, 0x121FA, 0x1221D, 0x124D2, 0x1272F, 0x129EE, 0x128B5, 0x128E1, 0x12DD3, 0x12AD8, 0x11071, 0x12C92, 0x19EF1, 0x10ACD, 0x1488A, 0x1301A, 0x12318, 0x133AD, 0x133AD, 0x12B00, 0x12566, 0x19556, 0x19655, 0x141CC, 0x149D4, 0x1181E, 0x198AF, 0x1491C, 0x1181E, 0x13A73, 0x149D4, 0x14D6C, 0x14D57, 0x14BDF, 0x14D57, 0x149D9, 0x15098, 0x16144, 0x1557B, 0x1557B, 0x15584, 0x1B69A, 0x158E4, 0x15605, 0x1563E, 0x162F2, 0x1813E, 0x1B69A, 0x15AD9, 0x1557B, 0x15BB0, 0x15406, 0x15692, 0x15746, 0x179DE, 0x1605C, 0x160AC, 0x15098, 0x163C7, 0x163C7, 0x16A89, 0x18308, 0x166B1, 0x16DBB, 0x16F93, 0x129F0, 0x16EDD, 0x171BC, 0x129F0, 0x16E02, 0x15098, 0x171B2, 0x16F56, 0x1765E, 0x1758D, 0x1668F, 0x15098, 0x174B6, 0x179DE, 0x18FD1, 0x188AF, 0x179DE, 0x19556, 0x19655, 0x1998E, 0x199DA, 0x1998E, 0x19F1C, 0x11243, 0x188AF, 0x12EBF, 0x1A49C, 0x1A49C, 0x1A672, 0x1A69F, 0x1D617, 0x1A814, 0x1A82E, 0x1A814, 0x1A82E, 0x1AB92, 0x1ACBF, 0x1181E, 0x109F5, 0x33607, 0x3360A, 0x3360A, 0x335D4, 0x1A9F4, 0x1CDF7, 0x1CDF7, 0x1B270, 0x10D8E, 0x1D962, 0x11707, 0x1599F, 0x1DD10, 0x1E159, 0x1E159, 0x1E068, 0x1E0A2, 0x1E0DB, 0x1E11C, 0x1E243, 0x1E243, 0x1ED4C, 0x1EB74, 0x1EBAA, 0x1EBE3, 0x1F204, 0x34018, 0x34018, 0x351B7, 0x354D5, 0x354D5, 0x354D5, 0x354D5, 0x354D5, 0x34018, 0x34018, 0x36771, 0x56939, 0x566C0, 0x10D45, 0x10E66, 0x12090, 0x11258, 0x11E24, 0x111CB, 0x15344, 0x1235F, 0x12524, 0x12806, 0x12795, 0x126AC, 0x1215F, 0x12773, 0x19945, 0x1CE4B, 0x1C8FB, 0x14937, 0x149A0, 0x14B5F, 0x14C45, 0x14DA0, 0x155C0, 0x156ED, 0x157B2, 0x1578B, 0x157E5, 0x1E295, 0x1617A, 0x160F8, 0x16FB0, 0x1858C, 0x17085, 0x1719C, 0x16447, 0x181D7, 0x18347, 0x183BC, 0x16AC5, 0x1851F, 0x185CC, 0x17F5F, 0x16AD4, 0x18604, 0x16827, 0x1848F, 0x16EBF, 0x17F75, 0x199B2, 0x1A685, 0x1A6B2, 0x19D2D, 0x1C8C1);

  @Override
  protected void run() throws Exception {
    String baseFolder = System.getenv("SPICE86_DUMPS_FOLDER");
    try (Log log = new Log(this, baseFolder + "functionCreator.txt", false)) {
      Program program = getCurrentProgram();
      FunctionManager functionManager = program.getFunctionManager();
      int created = 0;
      for (Integer address : addresses) {
        String addressHex = String.format("0x%X", address);
        Address ghidraAddress = this.toAddr(address);
        Function function = functionManager.getFunctionAt(ghidraAddress);
        if (function != null) {
          log.info(
              "No function created at offset " + addressHex + " because " + function.getName() + " is already there");
          continue;
        }
        String name = "FUN_" + addressHex;
        Symbol symbol = getSymbolAt(ghidraAddress);
        if (symbol != null) {
          String symbolName = symbol.getName();
          log.info("symbol is " + symbolName);
          Integer spice86Address = extractSpice86Address(symbolName);
          if (address.equals(spice86Address)) {
            name = symbolName;
          } else {
            if (spice86Address != null) {
              log.info(
                  "spice86 address represented by symbol " + symbolName + " differs from actual address " + addressHex);
            }
          }
        }
        created++;
        createFunction(ghidraAddress, name);
      }
      this.println("Created " + created + " functions.");
    }
  }

  private static Integer extractSpice86Address(String name) {
    String[] split = name.split("_");
    if (split.length < 4) {
      return null;
    }
    try {
      int segment = parseHex(split[split.length - 3]);
      int offset = parseHex(split[split.length - 2]);
      return segment * 0x10 + offset;
    } catch (NumberFormatException nfe) {
      return null;
    }
  }

  public static int parseHex(String value) {
    return Integer.parseInt(value.replaceAll("0x", ""), 16);
  }

  class Log implements Closeable {
    private final GhidraScript ghidraScript;
    private final PrintWriter printWriterLogs;
    private final boolean consoleOutput;

    public Log(GhidraScript ghidraScript, String logFile, boolean consoleOutput) throws IOException {
      this.ghidraScript = ghidraScript;
      printWriterLogs = new PrintWriter(new FileWriter(logFile));
      this.consoleOutput = consoleOutput;
    }

    public void warning(String line) {
      log("Warning: " + line);
    }

    public void info(String line) {
      log("Info: " + line);
    }

    private void log(String line) {
      printWriterLogs.println(line);
      if (consoleOutput) {
        ghidraScript.println(line);
      }
    }

    @Override
    public void close() {
      printWriterLogs.close();
    }
  }
}
