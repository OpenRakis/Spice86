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
      Arrays.asList(0x56453, 0x56465, 0x56459, 0x564E9, 0x564E3, 0x564EF);

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
