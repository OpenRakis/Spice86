import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;
import com.google.gson.reflect.TypeToken;
import com.google.gson.stream.JsonReader;
import ghidra.app.cmd.disassemble.DisassembleCommand;
import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.script.GhidraScript;
import ghidra.framework.cmd.Command;
import ghidra.program.database.function.OverlappingFunctionException;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSet;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.RefType;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.program.model.symbol.SourceType;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolType;
import ghidra.util.exception.DuplicateNameException;
import ghidra.util.exception.InvalidInputException;
import org.apache.commons.collections4.IteratorUtils;
import org.apache.commons.lang3.builder.HashCodeBuilder;

import java.io.Closeable;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.lang.reflect.Type;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

//Imports data into ghidra from spice86 and reorganizes the code in a way the generator can work on it without too many errors
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86OneClickImport extends GhidraScript {

  private final static Map<Integer, Integer> SEGMENTS = Map.of(
      0x1000, 0x335AF - 0x10000,
      0x334B, 0x56350 - 0x334B0,
      0x5635, 0x564DE - 0x56350,
      0x563E, 0x1000);

  @Override
  protected void run() throws Exception {
    String baseFolder = System.getenv("SPICE86_DUMPS_FOLDER");
    Log log = new Log(this, baseFolder + "spice86ImportLog.txt", true);
    FunctionCreator functionCreator = new FunctionCreator(this, log);

    log.info("Reading function symbols");
    Map<SegmentedAddress, String> functions =
        new SymbolsFileReader().readFunctionFile(baseFolder + "spice86dumpGhidraSymbols.txt");

    log.info("Importing function symbols");
    new FunctionImporter(this, functionCreator).importFunctions(functions);

    log.info("Reading execution flow");
    ExecutionFlow executionFlow =
        readJumpMapFromFile(baseFolder + "spice86dumpExecutionFlow.json");

    log.info("Importing execution flow");
    ReferencesImporter referencesImporter = new ReferencesImporter(this, log);
    referencesImporter.importReferences(executionFlow);

    log.info("Renaming functions guessed by ghidra");
    SegmentedAddressGuesser segmentedAddressGuesser = new SegmentedAddressGuesser(log, SEGMENTS);
    FunctionRenamer functionRenamer = new FunctionRenamer(this, log, segmentedAddressGuesser);
    functionRenamer.renameAll();

    log.info("Splitting jump functions");
    FunctionSplitter functionSplitter = new FunctionSplitter(this, log, segmentedAddressGuesser, functionCreator);
    functionSplitter.splitAllFunctions();
  }

  private ExecutionFlow readJumpMapFromFile(String filePath) throws IOException {
    try (FileReader fileReader = new FileReader(filePath); JsonReader reader = new JsonReader(fileReader)) {
      Type type = new TypeToken<ExecutionFlow>() {
      }.getType();
      ExecutionFlow res = new Gson().fromJson(reader, type);
      res.init();
      return res;
    }
  }

  static class SymbolsFileReader {
    public Map<SegmentedAddress, String> readFunctionFile(String filePath) throws IOException {
      Map<SegmentedAddress, String> res = new HashMap<>();
      List<String> lines = Files.readAllLines(Paths.get(filePath));
      for (String line : lines) {
        parseLine(res, line);
      }
      return res;
    }

    private void parseLine(Map<SegmentedAddress, String> res, String line) {
      String[] split = line.split(" ");
      if (split.length != 3) {
        // Not a function line
        return;
      }
      String type = split[2];
      if (!"f".equals(type)) {
        // Not a function line
        return;
      }
      String name = split[0];
      String[] nameSplit = name.split("_");
      if (nameSplit.length < 4) {
        // Format is not correct, we can't use this line
        return;
      }
      try {
        int segment = Utils.parseHex(nameSplit[nameSplit.length - 3]);
        int offset = Utils.parseHex(nameSplit[nameSplit.length - 2]);
        SegmentedAddress address = new SegmentedAddress(segment, offset);
        res.put(address, name);
      } catch (NumberFormatException nfe) {
        return;
      }
    }
  }

  static class ReferencesImporter {
    private GhidraScript ghidraScript;
    private EntryPointDisassembler entryPointDisassembler;

    public ReferencesImporter(GhidraScript ghidraScript, Log log) {
      this.ghidraScript = ghidraScript;
      this.entryPointDisassembler = new EntryPointDisassembler(ghidraScript, log);
    }

    public void importReferences(ExecutionFlow executionFlow) throws Exception {
      importReferences(executionFlow.getJumpsFromTo(), RefType.COMPUTED_JUMP, "jump_target");
      importReferences(executionFlow.getCallsFromTo(), RefType.COMPUTED_CALL, "call_target");
      //importReferences(executionFlow.getRetsFromTo(), RefType.COMPUTED_JUMP, "ret_target");
    }

    private void importReferences(Map<Integer, List<SegmentedAddress>> fromTo, RefType refType, String labelPrefix)
        throws Exception {
      ReferenceManager referenceManager = ghidraScript.getCurrentProgram().getReferenceManager();
      for (Map.Entry<Integer, List<SegmentedAddress>> e : fromTo.entrySet()) {
        Address from = ghidraScript.toAddr(e.getKey());
        if (referenceManager.hasReferencesFrom(from)) {
          referenceManager.removeAllReferencesFrom(from);
        }
        List<SegmentedAddress> toSegmentedAddresses = e.getValue();
        int index = 0;
        for (SegmentedAddress toSegmentedAddress : toSegmentedAddresses) {
          Address to = ghidraScript.toAddr(toSegmentedAddress.toPhysical());
          referenceManager.addMemoryReference(from, to, refType, SourceType.USER_DEFINED, index);
          index++;
          Symbol symbol = ghidraScript.getSymbolAt(to);
          if (shouldCreateLabel(symbol)) {
            String name =
                "spice86_imported_label_" + labelPrefix + "_" + Utils.toHexSegmentOffsetPhysical(toSegmentedAddress);
            ghidraScript.createLabel(to, name, true, SourceType.USER_DEFINED);
          }
          entryPointDisassembler.disassembleEntryPoint(to);
        }
      }
    }

    private boolean shouldCreateLabel(Symbol existingSymbol) {
      if (existingSymbol == null) {
        return true;
      }
      if (Utils.extractSpice86Address(existingSymbol.getName()) == null) {
        return true;
      }
      if (existingSymbol.getSymbolType() == SymbolType.FUNCTION) {
        return false;
      }
      return existingSymbol.getSymbolType() != SymbolType.LABEL;
    }
  }

  static class FunctionImporter {
    private GhidraScript ghidraScript;
    private FunctionCreator functionCreator;

    public FunctionImporter(GhidraScript ghidraScript, FunctionCreator functionCreator) {
      this.ghidraScript = ghidraScript;
      this.functionCreator = functionCreator;
    }

    public void importFunctions(Map<SegmentedAddress, String> functions) {
      for (Map.Entry<SegmentedAddress, String> functionEntry : functions.entrySet()) {
        SegmentedAddress segmentedAddress = functionEntry.getKey();
        String name = functionEntry.getValue();

        Address entry = ghidraScript.toAddr(segmentedAddress.toPhysical());
        functionCreator.removeSymbolAt(entry);
        functionCreator.createOrUpdateFunction(entry, name);
      }
    }

  }

  static class FunctionCreator {
    private GhidraScript ghidraScript;
    private Log log;
    private EntryPointDisassembler entryPointDisassembler;

    public FunctionCreator(GhidraScript ghidraScript, Log log) {
      this.ghidraScript = ghidraScript;
      this.log = log;
      entryPointDisassembler = new EntryPointDisassembler(ghidraScript, log);
    }

    public void removeSymbolAt(Address address) {
      Symbol symbol = ghidraScript.getSymbolAt(address);
      if (symbol != null) {
        log.info("Found symbol " + symbol.getName() + " at address " + address + ". Deleting it.");
        ghidraScript.removeSymbol(address, symbol.getName());
      }
      Function function = ghidraScript.getFunctionAt(address);
      if (function != null) {
        log.info("Found function " + function.getName() + " at address " + address + ". Deleting it.");
        ghidraScript.removeFunction(function);
      }
    }

    public void createOrUpdateFunction(Address address, String name) {
      entryPointDisassembler.createFunction(name, address);
    }
  }

  static class FunctionRenamer {
    private GhidraScript ghidraScript;
    private Log log;
    private SegmentedAddressGuesser segmentedAddressGuesser;

    public FunctionRenamer(GhidraScript ghidraScript, Log log, SegmentedAddressGuesser segmentedAddressGuesser) {
      this.ghidraScript = ghidraScript;
      this.log = log;
      this.segmentedAddressGuesser = segmentedAddressGuesser;
    }

    protected void renameAll() throws Exception {
      FunctionIterator functionIterator = Utils.getFunctionIterator(ghidraScript);
      int renamed = 0;
      while (functionIterator.hasNext()) {
        if (renameFunction(functionIterator.next())) {
          renamed++;
        }
      }
      log.info("Renamed " + renamed + " functions");
    }

    private boolean renameFunction(Function function) throws InvalidInputException, DuplicateNameException {
      String functionName = function.getName();
      if (Utils.extractSpice86Address(functionName) != null) {
        // Nothing to do
        return false;
      }
      String prefix = "ghidra_guess_";
      log.info("processing " + functionName + " at address " + Utils.toHexWith0X(
          (int)function.getEntryPoint().getUnsignedOffset()));
      SegmentedAddress address = getAddress(function);
      String name = prefix + Utils.toHexSegmentOffsetPhysical(address);
      function.setName(name, SourceType.USER_DEFINED);
      return true;
    }

    private SegmentedAddress getAddress(Function function) {
      int entryPointAddress = (int)function.getEntryPoint().getUnsignedOffset();
      return segmentedAddressGuesser.guessSegmentedAddress(entryPointAddress);
    }
  }

  static class EntryPointDisassembler {
    private GhidraScript ghidraScript;
    private Log log;

    public EntryPointDisassembler(GhidraScript ghidraScript, Log log) {
      this.ghidraScript = ghidraScript;
      this.log = log;
    }

    public void recreateFunction(Function function) {
      String name = function.getName();
      createFunction(name, function.getEntryPoint());
    }

    public void createFunction(String name, Address entryPoint) {
      CreateFunctionCmd cmd =
          new CreateFunctionCmd("Re-create " + name, entryPoint, null, SourceType.USER_DEFINED, true,
              true);
      boolean result = Utils.executeCommand(ghidraScript, cmd);
      log.info("Creation attempt status for " + name + ": " + (result ? "success" : "failure"));
      disassembleEntryPoint(entryPoint);
    }

    public void disassembleEntryPoint(Address address) {
      if (ghidraScript.getInstructionAt(address) != null) {
        // Already disassembled
        return;
      }
      DisassembleCommand disassembleCommand = new DisassembleCommand(address, null, true);
      boolean result = Utils.executeCommand(ghidraScript, disassembleCommand);
      log.info("Disassembly status for " + address + ": " + (result ? "success" : "failure"));
    }
  }

  static class FunctionSplitter {
    private GhidraScript ghidraScript;
    private Log log;
    private SegmentedAddressGuesser segmentedAddressGuesser;
    private FunctionCreator functionCreator;

    public FunctionSplitter(GhidraScript ghidraScript, Log log, SegmentedAddressGuesser segmentedAddressGuesser,
        FunctionCreator functionCreator) {
      this.ghidraScript = ghidraScript;
      this.log = log;
      this.segmentedAddressGuesser = segmentedAddressGuesser;
      this.functionCreator = functionCreator;
    }

    private int splitAllFunctions()
        throws InvalidInputException, OverlappingFunctionException, DuplicateNameException {
      List<Function> functions = Utils.getAllFunctions(ghidraScript);
      int numberOfCreated = 0;

      for (Function function : functions) {
        numberOfCreated += splitOneFunction(function);
      }
      return numberOfCreated;
    }

    private int splitOneFunction(Function function)
        throws InvalidInputException, OverlappingFunctionException, DuplicateNameException {
      int numberOfCreated = 0;
      AddressSetView body = function.getBody();
      log.log("Checking " + function.getName());
      List<AddressRange> addressRangeList = IteratorUtils.toList(body.iterator());
      if (addressRangeList.size() <= 1) {
        // Nothing to split
        return 0;
      }
      Program program = ghidraScript.getCurrentProgram();
      Listing listing = program.getListing();
      Address entryPoint = function.getEntryPoint();
      for (AddressRange addressRange : addressRangeList) {
        Address start = addressRange.getMinAddress();
        AddressSetView newBody = new AddressSet(addressRange);
        String newName = generateSplitName(function, start);
        if (!start.equals(entryPoint)) {
          numberOfCreated++;
        }
        // Calling this first to take care of eventual symbol removal
        functionCreator.removeSymbolAt(start);
        listing.createFunction(newName, start, newBody, SourceType.USER_DEFINED);
      }
      return numberOfCreated;
    }

    private String generateSplitName(Function function, Address start) {
      Address entryPoint = function.getEntryPoint();
      String name = function.getName();
      if (start.equals(entryPoint)) {
        // If entry point matched, recreate it
        return name;
      }
      SegmentedAddress segmentedAddress = segmentedAddressGuesser.guessSegmentedAddress((int)start.getUnsignedOffset());
      // Do not include original name in the new name as it is often unrelated
      return "split_" + Utils.toHexSegmentOffsetPhysical(segmentedAddress);
    }
  }

  static class SegmentedAddressGuesser {
    private Log log;
    private Map<Integer, Integer> segmentLengths;

    public SegmentedAddressGuesser(Log log, Map<Integer, Integer> segmentLengths) {
      this.log = log;
      this.segmentLengths = segmentLengths;
    }

    public SegmentedAddress guessSegmentedAddress(int entryPointAddress) {
      int segment = guessSegment(entryPointAddress);
      int offset = entryPointAddress - segment * 0x10;
      return new SegmentedAddress(segment, offset);
    }

    private int guessSegment(int entryPointAddress) {
      int foundSegment = 0;
      for (Map.Entry<Integer, Integer> segmentInformation : segmentLengths.entrySet()) {
        int segment = segmentInformation.getKey();
        int segmentStart = segment * 0x10;
        int segmentEnd = segmentStart + segmentInformation.getValue();
        if (entryPointAddress >= segmentStart && entryPointAddress < segmentEnd) {
          foundSegment = segment;
        }
      }
      log.info("Address " + Utils.toHexWith0X(entryPointAddress) + " corresponds to segment " + Utils.toHexWith0X(
          foundSegment));
      return foundSegment;
    }
  }

  static class ExecutionFlow {
    @SerializedName("CallsFromTo") private Map<Integer, List<SegmentedAddress>> callsFromTo;
    @SerializedName("JumpsFromTo") private Map<Integer, List<SegmentedAddress>> jumpsFromTo;
    @SerializedName("RetsFromTo") private Map<Integer, List<SegmentedAddress>> retsFromTo;
    @SerializedName("ExecutableAddressWrittenBy") private Map<Integer, Map<Integer, Set<ByteModificationRecord>>>
        executableAddressWrittenBy;

    private Map<Integer, List<SegmentedAddress>> callsJumpsFromTo = new HashMap<>();
    private Set<SegmentedAddress> jumpTargets;

    public void init() {
      callsJumpsFromTo.putAll(callsFromTo);
      callsJumpsFromTo.putAll(jumpsFromTo);
      jumpTargets = jumpsFromTo.values().stream().flatMap(Collection::stream).collect(Collectors.toSet());
    }

    public Map<Integer, List<SegmentedAddress>> getCallsFromTo() {
      return callsFromTo;
    }

    public Map<Integer, List<SegmentedAddress>> getJumpsFromTo() {
      return jumpsFromTo;
    }

    public Map<Integer, List<SegmentedAddress>> getRetsFromTo() {
      return retsFromTo;
    }

    public Map<Integer, List<SegmentedAddress>> getCallsJumpsFromTo() {
      return callsJumpsFromTo;
    }

    public Set<SegmentedAddress> getJumpTargets() {
      return jumpTargets;
    }

    public Map<Integer, Map<Integer, Set<ByteModificationRecord>>> getExecutableAddressWrittenBy() {
      return executableAddressWrittenBy;
    }
  }

  static class ByteModificationRecord {
    @SerializedName("OldValue") private int oldValue;

    @SerializedName("NewValue") private int newValue;

    public int getOldValue() {
      return oldValue;
    }

    public int getNewValue() {
      return newValue;
    }

    @Override public boolean equals(Object o) {
      return this == o
          || o instanceof ByteModificationRecord that && this.oldValue == that.oldValue
          && this.newValue == that.newValue;
    }

    @Override public int hashCode() {
      return new HashCodeBuilder().append(oldValue).append(newValue).toHashCode();
    }
  }

  static class SegmentedAddress implements Comparable<SegmentedAddress> {
    @SerializedName("Segment")
    private final int segment;
    @SerializedName("Offset")
    private final int offset;

    public SegmentedAddress(int segment, int offset) {
      this.segment = Utils.uint16(segment);
      this.offset = Utils.uint16(offset);
    }

    public int getSegment() {
      return segment;
    }

    public int getOffset() {
      return offset;
    }

    public int toPhysical() {
      return segment * 0x10 + offset;
    }

    @Override
    public int hashCode() {
      return toPhysical();
    }

    @Override
    public boolean equals(Object obj) {
      if (this == obj) {
        return true;
      }

      return (obj instanceof SegmentedAddress other)
          && toPhysical() == other.toPhysical();
    }

    @Override
    public int compareTo(SegmentedAddress other) {
      return Integer.compare(this.toPhysical(), other.toPhysical());
    }

    @Override
    public String toString() {
      return Utils.toHexSegmentOffset(this) + " / " + Utils.toHexWith0X(this.toPhysical());
    }
  }

  static class Log implements Closeable {
    private final GhidraScript ghidraScript;
    private final PrintWriter printWriterLogs;
    private final boolean consoleOutput;

    public Log(GhidraScript ghidraScript, String logFile, boolean consoleOutput) throws IOException {
      this.ghidraScript = ghidraScript;
      printWriterLogs = new PrintWriter(new FileWriter(logFile));
      this.consoleOutput = consoleOutput;
    }

    public void info(String line) {
      log("Info: " + line);
    }

    public void warning(String line) {
      log("Warning: " + line);
    }

    public void error(String line) {
      log("Error: " + line);
    }

    private void log(String line) {
      printWriterLogs.println(line);
      if (consoleOutput) {
        ghidraScript.println(line);
      }
    }

    @Override public void close() {
      printWriterLogs.close();
    }
  }

  static class Utils {
    private static final int SEGMENT_SIZE = 0x10000;

    public static String joinLines(List<String> res) {
      return String.join("\n", res);
    }

    public static String indent(String input, int indent) {
      if (input.isEmpty()) {
        return "";
      }
      String indentString = " ".repeat(indent);
      return indentString + input.replaceAll("\n", "\n" + indentString);
    }

    public static String getType(Integer bits) {
      if (bits == null) {
        return "unknown";
      }
      if (bits == 8) {
        return "byte";
      }
      if (bits == 16) {
        return "ushort";
      }
      if (bits == 32) {
        return "uint";
      }
      return "unknown";
    }

    public static String litteralToUpperHex(String litteralString) {
      return litteralString.toUpperCase().replaceAll("0X", "0x");
    }

    public static String toHexWith0X(long addressLong) {
      return String.format("0x%X", addressLong);
    }

    public static String toHexWithout0X(long addressLong) {
      return String.format("%X", addressLong);
    }

    public static String toHexSegmentOffset(SegmentedAddress address) {
      return String.format("%04X_%04X", address.getSegment(), address.getOffset());
    }

    public static String toHexSegmentOffsetPhysical(SegmentedAddress address) {
      return String.format("%04X_%04X_%05X", address.getSegment(), address.getOffset(), address.toPhysical());
    }

    public static int parseHex(String value) {
      return Integer.parseInt(value.replaceAll("0x", ""), 16);
    }

    public static boolean isNumber(String value) {
      try {
        parseHex(value);
        return true;
      } catch (NumberFormatException nfe) {
        return false;
      }
    }

    public static int uint(int value, int bits) {
      return switch (bits) {
        case 8 -> uint8(value);
        case 16 -> uint16(value);
        default -> throw new RuntimeException("Unsupported bits number " + bits);
      };
    }

    public static int uint8(int value) {
      return value & 0xFF;
    }

    public static int uint16(int value) {
      return value & 0xFFFF;
    }

    /**
     * Sign extend value considering it is a 8 bit value
     */
    public static int int8(int value) {
      return (byte)value;
    }

    /**
     * Sign extend value considering it is a 16 bit value
     */
    public static int int16(int value) {
      return (short)value;
    }

    public static int getUint8(byte[] memory, int address) {
      return uint8(memory[address]);
    }

    public static int getUint16(byte[] memory, int address) {
      return uint16(uint8(memory[address]) | (uint8(memory[address + 1]) << 8));
    }

    public static int toAbsoluteSegment(int physicalAddress) {
      return ((physicalAddress / SEGMENT_SIZE) * SEGMENT_SIZE) >>> 4;
    }

    public static int toAbsoluteOffset(int physicalAddress) {
      return physicalAddress - (physicalAddress / SEGMENT_SIZE) * SEGMENT_SIZE;
    }

    public static SegmentedAddress extractSpice86Address(String name) {
      String[] split = name.split("_");
      if (split.length < 4) {
        return null;
      }
      try {
        return new SegmentedAddress(Utils.parseHex(split[split.length - 3]),
            Utils.parseHex(split[split.length - 2]));
      } catch (NumberFormatException nfe) {
        return null;
      }
    }

    public static String stripSegmentedAddress(String name) {
      SegmentedAddress nameSegmentedAddress = extractSpice86Address(name);
      if (nameSegmentedAddress != null) {
        return name.replaceAll("_" + Utils.toHexSegmentOffsetPhysical(nameSegmentedAddress), "");
      }
      return name;
    }

    public static List<Function> getAllFunctions(GhidraScript ghidraScript) {
      return IteratorUtils.toList(getFunctionIterator(ghidraScript));
    }

    public static FunctionIterator getFunctionIterator(GhidraScript ghidraScript) {
      Program program = ghidraScript.getCurrentProgram();
      Listing listing = program.getListing();
      return listing.getFunctions(true);
    }

    public static boolean executeCommand(GhidraScript ghidraScript, Command cmd) {
      return ghidraScript.getState().getTool().execute(cmd, ghidraScript.getCurrentProgram());
    }
  }
}
