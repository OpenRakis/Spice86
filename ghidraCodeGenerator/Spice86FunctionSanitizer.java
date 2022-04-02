import com.google.gson.annotations.SerializedName;
import ghidra.app.script.GhidraScript;
import ghidra.program.database.function.OverlappingFunctionException;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.SourceType;
import ghidra.util.exception.DuplicateNameException;
import ghidra.util.exception.InvalidInputException;
import org.apache.commons.collections4.IteratorUtils;

import java.io.Closeable;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Iterator;
import java.util.List;

//Splits functions
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86FunctionSanitizer extends GhidraScript {
  @Override
  protected void run() throws Exception {
    String baseFolder = System.getenv("SPICE86_DUMPS_FOLDER");
    try(Log log = new Log(this, baseFolder+"sanitizerLogs.txt", false)) {
      Program program = getCurrentProgram();
      Listing listing = program.getListing();
      List<Function> functions = IteratorUtils.toList(listing.getFunctions(true));
      int created = sanitizeFunctions(log, functions);
      this.println("Created " + created + " functions.");
    }
  }

  private int sanitizeFunctions(Log log, List<Function> functions)
      throws InvalidInputException, OverlappingFunctionException {
    Program program = getCurrentProgram();
    Listing listing = program.getListing();
    int numberOfCreated = 0;
    for (Function function : functions) {
      AddressSetView body = function.getBody();
      log.log("Checking " + function.getName());
      List<AddressRange> addressRangeList = IteratorUtils.toList(body.iterator());
      if (addressRangeList.size() <= 1) {
        // Nothing to split
        continue;
      }
      Address entryPoint = function.getEntryPoint();
      listing.removeFunction(entryPoint);
      for (AddressRange addressRange : addressRangeList) {
        Address start = addressRange.getMinAddress();
        AddressSetView newBody = new AddressSet(addressRange);
        String newName = "FUN_split_" + start.getUnsignedOffset();
        if (start.equals(entryPoint)) {
          // If entry point matched, recreate it
          newName = function.getName();
        } else {
          numberOfCreated++;
        }
        listing.createFunction(newName, start, newBody, SourceType.IMPORTED);
      }
    }
    return numberOfCreated;
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
