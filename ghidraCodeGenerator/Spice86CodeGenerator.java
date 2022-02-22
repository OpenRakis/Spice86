import com.google.gson.Gson;
import com.google.gson.reflect.TypeToken;
import com.google.gson.stream.JsonReader;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Program;
import ghidra.program.model.mem.MemoryAccessException;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolType;

import java.io.Closeable;
import java.io.FileReader;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.lang.reflect.Type;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collection;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

//Generates CSharp code to run on with the Spice86 emulator as a backend (https://github.com/OpenRakis/Spice86/)
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86CodeGenerator extends GhidraScript {
  // https://class.malware.re/2021/03/21/ghidra-scripting-feature-extraction.html
  private Log log;
  private PrintWriter printWriterCode;
  private Program program;
  private Listing listing;
  private JumpsAndCalls jumpsAndCalls;

  public void run() throws Exception {
    String baseFolder = "C:/tmp/dune/";
    jumpsAndCalls =
        readJumpMapFromFile(baseFolder + "spice86dumpjumps.json");
    try (Log log = new Log(this, baseFolder + "ghidrascriptout.txt", false)) {
      this.log = log;
      printWriterCode = new PrintWriter(new FileWriter(baseFolder + "ghidrascriptoutcode.cs"));
      program = getCurrentProgram();
      listing = program.getListing();
      //Address entry = toAddr(0x1ED0);
      //Address entry = toAddr(0x263A);
      //handleFunction(listing.getFunctionAt(entry));
      FunctionIterator functionIterator = listing.getFunctions(true);
      for (Function function : functionIterator) {
        printWriterCode.println(new FunctionGenerator(log, this, jumpsAndCalls).outputCSharp(function));
      }
      printWriterCode.close();
    }
  }

  private JumpsAndCalls readJumpMapFromFile(String filePath) throws IOException {
    try (FileReader fileReader = new FileReader(filePath); JsonReader reader = new JsonReader(fileReader)) {
      Type type = new TypeToken<JumpsAndCalls>() {
      }.getType();
      JumpsAndCalls res = new Gson().fromJson(reader, type);
      res.init();
      reader.close();
      return res;
    }
  }
}

class JumpsAndCalls {
  public Map<Long, List<Long>> CallsFromTo;
  public Map<Long, List<Long>> JumpsFromTo;
  public Map<Long, List<Long>> RetsFromTo;

  private Map<Long, List<Long>> callsJumpsFromTo;
  private Set<Long> jumpTargets;

  public void init() {
    callsJumpsFromTo = new HashMap<>();
    callsJumpsFromTo.putAll(CallsFromTo);
    callsJumpsFromTo.putAll(JumpsFromTo);
    jumpTargets = JumpsFromTo.values().stream().flatMap(Collection::stream).collect(Collectors.toSet());
  }

  public Map<Long, List<Long>> getCallsJumpsFromTo() {
    return callsJumpsFromTo;
  }

  public Set<Long> getJumpTargets() {
    return jumpTargets;
  }
}

class FunctionGenerator {
  private Log log;
  private GhidraScript ghidraScript;
  private JumpsAndCalls jumpsAndCalls;

  public FunctionGenerator(Log log, GhidraScript ghidraScript, JumpsAndCalls jumpsAndCalls) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.jumpsAndCalls = jumpsAndCalls;
  }

  public String outputCSharp(Function function) throws Exception {
    StringBuilder res = new StringBuilder();
    String name = function.getName();
    Integer cs = extractCS(name);
    res.append("public Action " + name + "() {\n");
    List<Instruction> instructionsBeforeEntry = new ArrayList<>();
    List<Instruction> instructionsAfterEntry = new ArrayList<>();
    boolean success = dispatchInstructions(function, instructionsBeforeEntry, instructionsAfterEntry);
    if (!success) {
      res.append("// Warning, could not read all the instructions!!\n");
    }
    if (!instructionsBeforeEntry.isEmpty()) {
      res.append("  if(false) {\n");
      writeInstructions(res, instructionsBeforeEntry, 4, cs, false);
      res.append("  }\n");
    }
    writeInstructions(res, instructionsAfterEntry, 2, cs, true);
    res.append("}\n");
    return res.toString();
  }

  private void writeInstructions(StringBuilder stringBuilder, List<Instruction> instructions, int indent, Integer cs,
      boolean returnExpected) throws Exception {
    Iterator<Instruction> instructionIterator = instructions.iterator();
    while (instructionIterator.hasNext()) {
      Instruction instruction = instructionIterator.next();
      boolean isLast = !instructionIterator.hasNext();
      InstructionGenerator instructionGenerator =
          new InstructionGenerator(log, ghidraScript, jumpsAndCalls, instruction, cs, isLast);
      stringBuilder.append(instructionGenerator.convertInstructionToSpice86(indent));
      if (isLast && returnExpected && !instructionGenerator.isFunctionReturn()) {
        // Last instruction should have been a return but it is not. It means the ASM code will continue to the next function. Generate a function call if possible.
        Instruction next = instruction.getNext();
        stringBuilder.append(
            Utils.indent(generateMissingReturn(next, instructionGenerator.getJumpCallTranslator()), indent) + "\n");
      }
    }
  }

  private String generateMissingReturn(Instruction next, JumpCallTranslator jumpCallTranslator) {
    if (next == null) {
      return "// Function does not end with return and no instruction after the body ...\nTHIS_CANNOT_WORK";
    }
    Address address = next.getAddress();
    return "// Function call generated as ASM continues to next function without return\n"
        + jumpCallTranslator.functionToString(ghidraScript.getFunctionAt(address), address.toString(), true);
  }

  /**
   * Dispatches the instruction of the given function to 2 lists, one for the instructions before the entry point and one for those after
   *
   * @param function
   * @param instructionsBeforeEntry
   * @param instructionsAfterEntry
   * @return
   */
  private boolean dispatchInstructions(Function function, List<Instruction> instructionsBeforeEntry,
      List<Instruction> instructionsAfterEntry) {
    Address entry = function.getEntryPoint();
    AddressSetView body = function.getBody();
    // Functions can be split accross the exe, they are divided in ranges and typically the code will jump accross ranges.
    // Let's get a list of all the instructions of the function split between instructions that are before the entry and after the entry.
    for (AddressRange addressRange : body) {
      Address min = addressRange.getMinAddress();
      Address max = addressRange.getMaxAddress();
      Instruction instruction = ghidraScript.getInstructionAt(min);
      if (instruction == null) {
        log.log("Warning, instruction at " + min + " is null");
        return false;
      }
      Instruction before = null;
      do {
        dispatchInstruction(instruction, entry, instructionsBeforeEntry, instructionsAfterEntry);
        before = instruction;
        instruction = instruction.getNext();
        if (instruction == null) {
          if (before != null) {
            log.log("Warning, instruction after " + before.getAddress() + " is null");
          }
          return false;
        }
      } while (instruction.getAddress().compareTo(max) <= 0);
    }
    return true;
  }

  private void dispatchInstruction(Instruction instruction, Address entry, List<Instruction> instructionsBeforeEntry,
      List<Instruction> instructionsAfterEntry) {
    Address instructionAddress = instruction.getAddress();
    if (instructionAddress.compareTo(entry) < 0) {
      instructionsBeforeEntry.add(instruction);
    } else {
      instructionsAfterEntry.add(instruction);
    }
  }

  private Integer extractCS(String functionName) {
    if (functionName.startsWith("FUN")) {
      return null;
    }
    //unknown_01ED_D72B_0F5FB => CS is 01ED
    String[] split = functionName.split("_");
    if (split.length < 4) {
      return null;
    }
    String csString = split[split.length - 3];
    try {
      return Utils.parseHex(csString);
    } catch (NumberFormatException nfe) {
      log.log("Could not parse " + csString + " to hex");
      return null;
    }
  }
}

class ParameterTranslator {
  private Log log;
  private RegisterHandler registerHandler;
  private Set<String> missingRegisters;

  public ParameterTranslator(Log log, RegisterHandler registerHandler) {
    this.log = log;
    this.registerHandler = registerHandler;
  }

  public void setMissingRegisters(Set<String> missingRegisters) {
    this.missingRegisters = missingRegisters;
  }

  public String toSpice86Value(String param, Integer bits, int offset) {
    if (Utils.isNumber(param)) {
      // immediate value
      return Utils.litteralToUpperHex(param);
    }
    if (param.length() == 2) {
      // register
      return registerHandler.substituteRegister(param);
    }
    if (param.startsWith("byte ptr ")) {
      return toSpice86Pointer(param.replaceAll("byte ptr ", ""), 8, offset);
    }
    if (param.startsWith("word ptr ")) {
      return toSpice86Pointer(param.replaceAll("word ptr ", ""), 16, offset);
    }
    if (bits != null) {
      return toSpice86Pointer(param, bits, offset);
    }
    log.log("Warning: Could not translate value " + param);
    return null;
  }

  public String toSpice86Value(String param, Integer bits) {
    return toSpice86Value(param, bits, 0);
  }

  public String toSpice86Pointer(String param, int bits, int offset) {
    String[] split = param.split(":");
    if (split.length == 2) {
      return toSpice86Pointer(split[0], split[1], bits, offset);
    } else {
      String segmentRegister = getSegmentRegister(param);
      return toSpice86Pointer(segmentRegister, param, bits, offset);
    }
  }

  public String getSegmentRegister(String expression) {
    String[] split = expression.split(":");
    if (split.length == 2) {
      return split[0];
    }
    if (!missingRegisters.isEmpty()) {
      return missingRegisters.iterator().next();
    }
    log.log("Warning: cannot guess segment register for parameter " + expression);
    return "DS";
  }

  private String toSpice86Pointer(String segmentRegister, String offsetString, int bits, int offset) {
    String memoryAddressExpression = toSpice86MemoryAddressExpression(segmentRegister, offsetString, offset);
    return toSpice86Pointer(memoryAddressExpression, bits);
  }

  public String toCsIpPointerValueInMemory(String expression) {
    String ip = toSpice86Value(expression, 16, 0);
    String cs = toSpice86Value(expression, 16, 2);
    return cs + " * 0x10 + " + ip;
  }

  public String toIpPointerValueInMemory(String expression) {
    String ip = toSpice86Value(expression, 16, 0);
    return registerHandler.substituteRegister("CS") + " * 0x10 + " + ip;
  }

  public String toSpice86MemoryAddressExpression(String segmentRegister, String offsetString, int offset) {
    String offsetExpression = pointerExpressionToOffset(offsetString);
    if (offset != 0) {
      offsetExpression += " + " + offset;
    }
    return registerHandler.substituteRegister(segmentRegister) + " * 0x10 + " + offsetExpression;
  }

  public String toSpice86Pointer(String memoryAddressExpression, int bits) {
    return "memory.UInt" + bits + "[" + memoryAddressExpression + "]";
  }

  public String pointerExpressionToOffset(String pointerString) {
    String res = Utils.litteralToUpperHex(pointerString.replaceAll("\\[", "").replaceAll("\\]", ""));
    return registerHandler.substituteRegistersWithSpice86Expression(res);
  }
}

class RegisterHandler {
  private static Set<String> REGISTER_NAMES_16_BITS =
      new HashSet<>(Arrays.asList("AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"));
  private static Set<String> REGISTER_NAMES_8_BITS =
      new HashSet<>(Arrays.asList("AL", "AH", "CL", "CH", "DL", "DH", "BL", "BH"));
  private static Set<String> REGULAR_REGISTER_NAMES = new HashSet<>();
  private static Set<String> SEGMENT_REGISTER_NAMES = new HashSet<>(Arrays.asList("ES", "CS", "SS", "DS", "FS", "GS"));
  private static Set<String> ALL_REGISTER_NAMES = new HashSet<>();

  static {
    REGULAR_REGISTER_NAMES.addAll(REGISTER_NAMES_16_BITS);
    REGULAR_REGISTER_NAMES.addAll(REGISTER_NAMES_8_BITS);
    ALL_REGISTER_NAMES.addAll(REGULAR_REGISTER_NAMES);
    ALL_REGISTER_NAMES.addAll(SEGMENT_REGISTER_NAMES);
  }

  private Log log;
  private Integer cs;

  public RegisterHandler(Log log, Integer cs) {
    this.log = log;
    this.cs = cs;
  }

  public String substituteRegister(String registerName) {
    if ("CS".equals(registerName)) {
      if (cs != null) {
        return "/* CS */" + Utils.toHexWith0X(cs);
      }
      return "/* WARNING, CS value could not be evaluated, CS will not have a correct value */ state.CS";
    }
    return "state." + registerName;
  }

  public String substituteRegistersWithSpice86Expression(String input) {
    String res = input;
    for (String registerName : ALL_REGISTER_NAMES) {
      res = res.replaceAll(registerName, substituteRegister(registerName));
    }
    return res.replaceAll(" \\+ 0x0", "");
  }

  public Set<String> computeSegmentRegistersInInstructionRepresentation(String[] params) {
    Set<String> res = new HashSet<>();
    for (String registerName : SEGMENT_REGISTER_NAMES) {
      for (String param : params) {
        if (param.contains(registerName)) {
          res.add(registerName);
        }
      }
    }
    return res;
  }

  public Set<String> computeSegmentRegistersInInstruction(Object[] inputObjects) {
    Set<String> res = new HashSet<>();
    for (Object inputObject : inputObjects) {
      if (inputObject instanceof ghidra.program.model.lang.Register) {
        String registerName = inputObject.toString();
        if (SEGMENT_REGISTER_NAMES.contains(registerName)) {
          res.add(inputObject.toString());
        }
      }
    }
    return res;
  }

  public Set<String> computeMissingRegisters(String mnemonic, String[] params, Object[] inputObjects) {
    Set<String> registersInRepresentation = computeSegmentRegistersInInstructionRepresentation(params);
    Set<String> registersInInstruction = computeSegmentRegistersInInstruction(inputObjects);
    Set<String> res = new HashSet<>(registersInInstruction);
    res.removeAll(registersInRepresentation);
    boolean usesCs =
        "CALL".equals(mnemonic) || "CALLF".equals(mnemonic) || "RET".equals(mnemonic) || "RETF".equals(mnemonic);
    if (usesCs) {
      // Implicitely touched, but we don't care for address calculation
      res.remove("CS");
    }
    if (usesCs || "PUSH".equals(mnemonic) || "POP".equals(mnemonic) || "PUSHA".equals(mnemonic) || "POPA".equals(
        mnemonic)) {
      // Implicitely touched, but we don't care for address calculation
      res.remove("SS");
      res.remove("SP");
    }
    if (res.size() > 1) {
      log.log("Warning, found more than one missing segment register in instruction. Segment registers in instruction: "
          + registersInRepresentation + " Segment registers according to ghidra: " + registersInInstruction + " delta:"
          + res);
    }
    return res;
  }
}

class InstructionAnalyzer {
  private static Set<String> STRING_OPERATIONS_CHECKING_ZERO_FLAG =
      new HashSet<>(Arrays.asList("CMPSB", "CMPSW", "SCASB", "SCASW"));

  private static Set<Integer> OPCODES_ON_8_BITS = new HashSet<>(Arrays.asList(
      0x00, 0x02, 0x04, 0x08, 0x0A, 0x0C, 0x10, 0x12, 0x14, 0x18, 0x1A, 0x1C, 0x20, 0x22, 0x24, 0x27,
      0x28, 0x2A, 0x2C, 0x2F, 0x30, 0x32, 0x34, 0x37, 0x38, 0x3A, 0x3C, 0x3F, 0x6B, 0x6C, 0x6E, 0x80, 0x82, 0x83,
      0x84, 0x86, 0x88, 0x8A, 0xA0, 0xA2, 0xA4, 0xA6, 0xA8, 0xAA, 0xAC, 0xAE, 0x6C, 0x6E, 0xB0, 0xB1, 0xB2, 0xB3,
      0xB4, 0xB5, 0xB6, 0xB7, 0xC0, 0xC6, 0xD0, 0xD2, 0xD4, 0xD5, 0xD7, 0xE4, 0xE6, 0xEC, 0xEE, 0xF6, 0xFE));
  private static Set<Integer> OPCODES_ON_16_BITS = new HashSet<>(Arrays.asList(
      0x01, 0x03, 0x05, 0x06, 0x07, 0x09, 0x0B, 0x0D, 0x0E, 0x11, 0x13, 0x15, 0x16, 0x17, 0x19, 0x1B, 0x1D, 0x1E, 0x1F,
      0x21, 0x23, 0x25, 0x29, 0x2B, 0x2D, 0x31, 0x33, 0x35, 0x39, 0x3B, 0x3D, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46,
      0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
      0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x61, 0x68, 0x69, 0x6D, 0x6F, 0x81, 0x85, 0x87, 0x89, 0x8B, 0x8C,
      0x8D, 0x8E, 0x8F, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x9C, 0x9D, 0xA1, 0xA3, 0xA9, 0xB8, 0xB9, 0xBA, 0xBB,
      0xBC, 0xBD, 0xBE, 0xBF, 0xC1, 0xC7, 0xD1, 0xD3, 0xE5, 0xE7, 0xED, 0xEF, 0xF7, 0xFF));
  private static final Set<Integer> PREFIXES_OPCODES = new HashSet<>(
      Arrays.asList(0x26, 0x2E, 0x36, 0x3E, 0x64, 0x65, 0xF0, 0xF2, 0xF3));
  private Log log;

  public InstructionAnalyzer(Log log) {
    this.log = log;
  }

  private Integer getOpcode(Instruction instruction) {
    try {
      int opcode = instruction.getUnsignedByte(0);
      if (PREFIXES_OPCODES.contains(opcode)) {
        return instruction.getUnsignedByte(1);
      }
      return opcode;
    } catch (MemoryAccessException e) {
      log.log("Warning: could not get opcode for instruction. Exception is " + e.toString());
    }
    return null;
  }

  private ModRM getModRm(Instruction instruction) {
    int modRmIndex = 1;
    try {
      int opcode = instruction.getUnsignedByte(0);
      if (PREFIXES_OPCODES.contains(opcode)) {
        modRmIndex++;
      }
      return new ModRM(instruction.getUnsignedByte(modRmIndex));
    } catch (MemoryAccessException e) {
      log.log("Warning: could not get ModRM for instruction. Exception is " + e.toString());
    }
    return null;
  }

  public Integer guessBitLength(Instruction instruction) {
    Integer opcode = getOpcode(instruction);
    if (OPCODES_ON_8_BITS.contains(opcode)) {
      return 8;
    }
    if (OPCODES_ON_16_BITS.contains(opcode)) {
      return 16;
    }
    return null;
  }

  public boolean isStringCheckingZeroFlag(String mnemonic) {
    return STRING_OPERATIONS_CHECKING_ZERO_FLAG.contains(mnemonic);
  }
}

class InstructionGenerator {
  private Log log;
  private GhidraScript ghidraScript;
  private ParameterTranslator parameterTranslator;
  private RegisterHandler registerHandler;
  private InstructionAnalyzer instructionAnalyzer;
  private JumpCallTranslator jumpCallTranslator;
  private Integer cs;
  private Instruction instruction;
  private long instructionAddress;
  private boolean isFunctionReturn;
  private boolean showInstructionComment = true;

  public JumpCallTranslator getJumpCallTranslator() {
    return jumpCallTranslator;
  }

  public boolean isFunctionReturn() {
    return isFunctionReturn;
  }

  public InstructionGenerator(Log log, GhidraScript ghidraScript, JumpsAndCalls jumpsAndCalls, Instruction instruction,
      Integer cs, boolean isLast) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.registerHandler = new RegisterHandler(log, cs);
    this.parameterTranslator = new ParameterTranslator(log, registerHandler);
    this.instructionAnalyzer = new InstructionAnalyzer(log);
    this.cs = cs;
    this.instruction = instruction;
    this.instructionAddress = instruction.getAddress().getUnsignedOffset();
    this.jumpCallTranslator =
        new JumpCallTranslator(log, ghidraScript, jumpsAndCalls, parameterTranslator, instruction.getAddress(), isLast);
  }

  public String convertInstructionToSpice86(int indent) throws Exception {
    log.log("Processing instruction " + instruction + " at address " + instruction.getAddress());
    String mnemonicWithPrefix = instruction.getMnemonicString();
    String[] mnemonicSplit = mnemonicWithPrefix.split("\\.");
    String mnemonic = mnemonicSplit[0];
    String prefix = "";
    if (mnemonicSplit.length > 1) {
      prefix = mnemonicSplit[1];
    }
    String representation = instruction.toString();
    String[] params = representation.replaceAll(mnemonicWithPrefix, "").trim().split(",");
    Object[] inputObjects = instruction.getInputObjects();
    Set<String> missingRegisters = registerHandler.computeMissingRegisters(mnemonic, params, inputObjects);
    parameterTranslator.setMissingRegisters(missingRegisters);
    Integer bits = instructionAnalyzer.guessBitLength(instruction);
    String label = jumpCallTranslator.getLabel();
    String instructionString = convertInstructionWithPrefix(mnemonic, prefix, params, bits);
    isFunctionReturn = instructionString.contains("return ");
    return Utils.indent(label + instructionString, indent) + "\n";
  }

  private String convertInstructionWithPrefix(String mnemonic, String prefix, String[] params, Integer bits)
      throws Exception {
    if (prefix.isEmpty()) {
      return convertInstructionWithoutPrefix(mnemonic, params, bits);
    }
    String ret = "while (state.CX-- != 0) {\n";
    ret += Utils.indent(convertInstructionWithoutPrefix(mnemonic, params, bits), 2) + "\n";
    if (instructionAnalyzer.isStringCheckingZeroFlag(mnemonic)) {
      boolean continueZeroFlagValue = prefix.equals("REPE") || prefix.equals("REP");
      ret += "  if(state.ZeroFlag == " + continueZeroFlagValue + ") {\n";
      ret += "    break;\n";
      ret += "  }\n";
    }
    ret += "}";
    return ret;
  }

  private String generateAssignmentWith1Parameter(String operation, String[] parameters,
      Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    return dest + " = " + operation + bits + "(" + dest + ");";
  }

  private String generateAssignmentWith2ParametersOnlyOneOperand(String operation, String[] parameters, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], bits);
    return dest + " = " + operation + bits + "(" + operand + ");";
  }

  private String generateXor(String[] parameters, Integer bits) {
    if (parameters[0].equals(parameters[1])) {
      // this is a set to 0
      String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
      return dest + " = 0;";
    }
    return generateAssignmentWith2Parameters("alu.Xor", parameters, bits);
  }

  private String generateAssignmentWith2Parameters(String operation, String[] parameters, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], bits);
    return dest + " = " + operation + bits + "(" + dest + ", " + operand + ");";
  }

  private String generateNoAssignmentWith2Parameters(String operation, String[] parameters, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], bits);
    return operation + bits + "(" + dest + ", " + operand + ");";
  }

  private String generateIns(String[] parameters, int bits) {
    String destination = getDestination(parameters, bits);
    String operation = destination + " = cpu.in" + bits + "(state.DX);";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateOuts(String[] parameters, int bits) {
    String source = getSource(parameters, bits);
    String operation = "cpu.out" + bits + "(state.DX, " + source + ");";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateScas(String[] parameters, int bits) {
    String param1 = getAXOrAL(bits);
    String param2 = getDestination(parameters, bits);
    String operation = "alu.Sub" + bits + "(state." + param1 + ", " + param2 + ");";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateStos(String[] parameters, int bits) {
    String source = getAXOrAL(bits);
    String destination = getDestination(parameters, bits);
    String operation = source + " = " + destination + ";";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateLods(String[] parameters, int bits) {
    String source = getSource(parameters, bits);
    String destination = getAXOrAL(bits);
    String operation = source + " = " + destination + ";";
    return generateStringOperation(operation, true, false, bits);
  }

  private String generateCmps(String[] parameters, int bits) {
    String param1 = getSource(parameters, bits);
    String param2 = getDestination(parameters, bits);
    String operation = "alu.Sub" + bits + "(" + param1 + ", " + param2 + ");";
    return generateStringOperation(operation, true, true, bits);
  }

  private String generateMovs(String[] parameters, int bits) {
    String destination = getDestination(parameters, bits);
    String source = getSource(parameters, bits);
    String operation =
        parameterTranslator.toSpice86Pointer(destination, bits) + " = " + parameterTranslator.toSpice86Pointer(
            source, bits) + ";";
    return generateStringOperation(operation, true, true, bits);
  }

  private String getSource(String[] parameters, int bits) {
    return parameterTranslator.toSpice86Pointer(parameters[getSIParamIndex(parameters)], bits, 0);
  }

  private String getDestination(String[] parameters, int bits) {
    return parameterTranslator.toSpice86Pointer(parameters[getDIParamIndex(parameters)], bits, 0);
  }

  private String generateStringOperation(String operation, boolean changeSI, boolean changeDI, int bits) {
    List<String> res = new ArrayList<>();
    res.add(operation);
    if (changeSI) {
      res.add(advanceRegister("SI", bits));
    }
    if (changeDI) {
      res.add(advanceRegister("DI", bits));
    }
    return Utils.joinLines(res);
  }

  private int getSIParamIndex(String[] parameters) {
    // Parameters are reversed in ghidra listing so we need to check which one is source and which one is destination ...
    return parameters[0].contains("SI") ? 0 : 1;
  }

  private int getDIParamIndex(String[] parameters) {
    // Parameters are reversed in ghidra listing so we need to check which one is source and which one is destination ...
    return parameters[0].contains("DI") ? 0 : 1;
  }

  private String advanceRegister(String register, int bits) {
    String advance = bits == 8 ? "1" : "2";
    return "state." + register + " = (ushort)(state." + register + " + (state.DirectionFlag?-" + advance + ":" + advance
        + "));";
  }

  private String generateNot(String[] parameters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], bits);
    return parameter + " = (" + Utils.getType(bits) + ")~" + parameter + ";";
  }

  private String generateNeg(String[] parameters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], bits);
    return parameter + " = alu.Sub" + bits + "(0, " + parameter + ");";
  }

  private String generateLXS(String register, String[] parameters) {
    String destination1 = parameterTranslator.toSpice86Value(parameters[0], 16);
    String destination2 = "state." + register;
    String value1 = parameterTranslator.toSpice86Value(parameters[1], 16, 0);
    String value2 = parameterTranslator.toSpice86Value(parameters[1], 16, 2);
    return destination1 + " = " + value1 + ";\n"
        + destination2 + " = " + value2 + ";";
  }

  private String generateLoop(String condition, String param) throws Exception {
    String loopCondition = "state.CX-- != 0";
    if (!condition.isEmpty()) {
      if ("NZ".equals(condition)) {
        loopCondition += " && !state.ZeroFlag";
      } else if ("Z".equals(condition)) {
        loopCondition += " && state.ZeroFlag";
      }
    }
    String res = "if(" + loopCondition + ") {\n";
    res += "  " + jumpCallTranslator.generateJump(param, false) + "\n";
    res += "}\n";
    return res;
  }

  private String getAXOrAL(int bits) {
    return bits == 8 ? "AL" : "AX";
  }

  private String generateXlat(String[] parameters, Integer bits) {
    return "state.AL = " + parameterTranslator.toSpice86Value(parameters[0], bits) + " + state.AL";
  }

  private String generateMul(String[] parameters, Integer bits) {
    return "cpu.Mul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateIMul(String[] parameters, Integer bits) {
    return "cpu.IMul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateDiv(String[] parameters, Integer bits) {
    return "cpu.Div" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateIDiv(String[] parameters, Integer bits) {
    return "cpu.IDiv" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateLea(String[] parameters) {
    String offset = parameterTranslator.pointerExpressionToOffset(parameters[1]);
    String destination = parameterTranslator.toSpice86Value(parameters[0], 16);
    return destination + " = " + offset + ";";
  }

  private String generateTempVar(String prefix) {
    return prefix + this.instructionAddress;
  }

  private String generateTempVar() {
    return generateTempVar("tmp");
  }

  private String convertInstructionWithoutPrefix(String mnemonic, String[] params, Integer bits) throws Exception {
    String instruction = convertInstructionWithoutPrefixAndComment(mnemonic, params, bits);
    if (instruction == null) {
      instruction = "UNIMPLEMENTED";
      showInstructionComment = true;
    }
    if (this.showInstructionComment) {
      String instuctionASM = mnemonic + " " + Arrays.stream(params).collect(Collectors.joining(","));
      return "// " + instuctionASM + "\n" + instruction;
    }
    return instruction;
  }

  private String convertInstructionWithoutPrefixAndComment(String mnemonic, String[] params, Integer bits)
      throws Exception {
    log.log("Params are " + Arrays.stream(params).collect(Collectors.joining(",")));
    switch (mnemonic) {
      case "AAM":
        return "cpu.Aam(" + parameterTranslator.toSpice86Value(params[0], bits) + ");";
      case "ADC":
        return generateAssignmentWith2Parameters("alu.Adc", params, bits);
      case "ADD":
        return generateAssignmentWith2Parameters("alu.Add", params, bits);
      case "AND":
        return generateAssignmentWith2Parameters("alu.And", params, bits);
      case "CALL":
        return jumpCallTranslator.generateCall(params[0], false);
      case "CALLF":
        return jumpCallTranslator.generateCall(params[0], true);
      case "CBW":
        return "state.AX = (ushort)((short)((sbyte)state.AL));";
      case "CLC":
        return "state.CarryFlag = false;";
      case "CLD":
        return "state.DirectionFlag = false;";
      case "CLI":
        return "state.InterruptFlag = false;";
      case "CMC":
        return "state.CarryFlag = !state.CarryFlag;";
      case "CMP":
        return generateNoAssignmentWith2Parameters("alu.Sub", params, bits);
      case "CMPSB":
        return generateCmps(params, 8);
      case "CMPSW":
        return generateCmps(params, 16);
      case "CWD":
        return "state.DX = state.AX>=0x8000?0xFFFF:0;";
      case "DEC":
        return generateAssignmentWith1Parameter("alu.Dec", params, bits);
      case "DIV":
        return generateDiv(params, bits);
      case "IDIV":
        return generateIDiv(params, bits);
      case "IMUL":
        return generateIMul(params, bits);
      case "IN":
        return generateAssignmentWith2ParametersOnlyOneOperand("cpu.In", params, bits);
      case "INC":
        return generateAssignmentWith1Parameter("alu.Inc", params, bits);
      case "INSB":
      case "INSW":
        return generateIns(params, bits);

      case "INT":
        return "//TODO: cpu.Interrupt(" + params[0] + ", false);";
      case "IRET":
        return "return InterruptRet();";
      case "JA":
        return jumpCallTranslator.generateJump("A", params[0], false);
      case "JBE":
        return jumpCallTranslator.generateJump("BE", params[0], false);
      case "JC":
        return jumpCallTranslator.generateJump("C", params[0], false);
      case "JCXZ":
        return jumpCallTranslator.generateJump("CXZ", params[0], false);
      case "JG":
        return jumpCallTranslator.generateJump("G", params[0], false);
      case "JGE":
        return jumpCallTranslator.generateJump("GE", params[0], false);
      case "JL":
        return jumpCallTranslator.generateJump("L", params[0], false);
      case "JLE":
        return jumpCallTranslator.generateJump("LE", params[0], false);
      case "JMP":
        return jumpCallTranslator.generateJump("", params[0], false);
      case "JMPF":
        return jumpCallTranslator.generateJump("", params[0], true);
      case "JNC":
        return jumpCallTranslator.generateJump("NC", params[0], false);
      case "JNS":
        return jumpCallTranslator.generateJump("NS", params[0], false);
      case "JNZ":
        return jumpCallTranslator.generateJump("NZ", params[0], false);
      case "JO":
        return jumpCallTranslator.generateJump("O", params[0], false);
      case "JS":
        return jumpCallTranslator.generateJump("S", params[0], false);
      case "JZ":
        return jumpCallTranslator.generateJump("Z", params[0], false);
      case "LAHF":
        return "state.AH = (byte)state.Flags.FlagRegisters";
      case "LDS":
        return generateLXS("DS", params);
      case "LEA":
        return generateLea(params);
      case "LES":
        return generateLXS("ES", params);
      case "LOCK":
        return "";
      case "LODSB":
        return generateLods(params, 8);
      case "LODSW":
        return generateLods(params, 16);
      case "LOOP":
        return generateLoop("", params[0]);
      case "LOOPNZ":
        return generateLoop("NZ", params[0]);
      case "LOOPZ":
        return generateLoop("Z", params[0]);
      case "MOV": {
        String dest = parameterTranslator.toSpice86Value(params[0], bits);
        String source = parameterTranslator.toSpice86Value(params[1], bits);
        return dest + " = " + source + ";";
      }
      case "MOVSB":
        return generateMovs(params, 8);
      case "MOVSW":
        return generateMovs(params, 16);
      case "MUL":
        return generateMul(params, bits);
      case "NEG":
        return generateNeg(params, bits);
      case "NOP":
        return "";
      case "NOT":
        return generateNot(params, bits);
      case "OR":
        return generateAssignmentWith2Parameters("alu.Or", params, bits);
      case "OUT":
        return generateNoAssignmentWith2Parameters("cpu.Out", params, bits);
      case "OUTSB":
      case "OUTSW":
        return generateOuts(params, bits);
      case "POP":
        return "state." + params[0] + " = stack.pop();";
      case "POPA":
        return "state.DI = stack.pop();\n"
            + "state.SI = stack.pop();\n"
            + "state.BP =stack.pop();\n"
            + "// not restoring SP\n"
            + "stack.pop();\n"
            + "state.BX = stack.pop();\n"
            + "state.DX = stack.pop();\n"
            + "state.CX = stack.pop();\n"
            + "state.AX = stack.pop();";
      case "POPF":
        return "state.Flags = stack.pop();";
      case "PUSH":
        return "stack.push(" + registerHandler.substituteRegister(params[0]) + ");";
      case "PUSHA": {
        String spTempVar = generateTempVar("sp");
        return "int " + spTempVar + " = state.SP;\n"
            + "stack.push(state.AX);\n"
            + "stack.push(state.CX);\n"
            + "stack.push(state.DX);\n"
            + "stack.push(state.BX);\n"
            + "stack.push(" + spTempVar + ");\n"
            + "stack.push(state.BP);\n"
            + "stack.push(state.SI);\n"
            + "stack.push(state.DI);";
      }
      case "PUSHF":
        return "stack.push(state.Flags);";
      case "RCL":
        return generateAssignmentWith2Parameters("alu.Rcl", params, bits);
      case "RCR":
        return generateAssignmentWith2Parameters("alu.Rcr", params, bits);
      case "RET":
        return "return NearRet();";
      case "RETF":
        return "return FarRet();";
      case "ROL":
        return generateAssignmentWith2Parameters("alu.Rol", params, bits);
      case "ROR":
        return generateAssignmentWith2Parameters("alu.Ror", params, bits);
      case "SAR":
        return generateAssignmentWith2Parameters("alu.Sar", params, bits);
      case "SBB":
        return generateAssignmentWith2Parameters("alu.Sbb", params, bits);
      case "SCASB":
        return generateScas(params, 8);
      case "SCASW":
        return generateScas(params, 16);
      case "SHL":
        return generateAssignmentWith2Parameters("alu.Shl", params, bits);
      case "SHR":
        return generateAssignmentWith2Parameters("alu.Shr", params, bits);
      case "STC":
        return "state.CarryFlag = true;";
      case "STD":
        return "state.DirectiontFlag = true;";
      case "STI":
        return "state.InterruptFlag = true;";
      case "STOSB":
        return generateStos(params, 8);
      case "STOSW":
        return generateStos(params, 16);
      case "SUB":
        return generateAssignmentWith2Parameters("alu.Sub", params, bits);
      case "TEST":
        return generateNoAssignmentWith2Parameters("alu.Test", params, bits);
      case "XCHG": {
        String tempVarName = generateTempVar();
        String var1 = parameterTranslator.toSpice86Value(params[0], bits);
        String var2 = parameterTranslator.toSpice86Value(params[1], bits);
        String res =
            Utils.getType(bits) + " " + tempVarName + " = " + var1 + ";\n";
        res += "" + var1 + " = " + var2 + ";\n";
        res += "" + var2 + " = " + tempVarName + ";";
        return res;
      }
      case "XLAT":
        return generateXlat(params, bits);
      case "XOR":
        return generateXor(params, bits);
    }
    return null;
  }
}

class JumpCallTranslator {
  private Log log;
  private GhidraScript ghidraScript;
  private JumpsAndCalls jumpsAndCalls;
  private ParameterTranslator parameterTranslator;
  private Address address;
  private long instructionAddress;
  private boolean isLast;

  public JumpCallTranslator(Log log, GhidraScript ghidraScript, JumpsAndCalls jumpsAndCalls,
      ParameterTranslator parameterTranslator, Address address,
      boolean isLast) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.jumpsAndCalls = jumpsAndCalls;
    this.parameterTranslator = parameterTranslator;
    this.address = address;
    this.instructionAddress = address.getUnsignedOffset();
    this.isLast = isLast;
  }

  public String getLabel() {
    if (hasGhidraLabel() || this.jumpsAndCalls.getJumpTargets().contains(this.instructionAddress)) {
      return getLabelToAddress(this.instructionAddress, true) + "\n";
    }
    return "";
  }

  private boolean hasGhidraLabel() {
    //Should look like this LAB_1000_020d
    Symbol label = ghidraScript.getSymbolAt(address);
    if (label == null || label.getSymbolType() != SymbolType.LABEL) {
      return false;
    }
    String[] split = label.getName().split("_");
    if (split.length != 3) {
      log.log("Warning: cannot parse ghidra label " + label);
      return false;
    }
    return true;
  }

  private String getLabelToAddress(long address, boolean colon) {
    return "label_" + Utils.toHexWithout0X(address) + (colon ? ":" : "");
  }

  public String generateJumpCondition(String condition) {
    switch (condition) {
      case "A":
        return "!state.CarryFlag && !state.ZeroFlag";
      case "BE":
        return "state.CarryFlag || state.ZeroFlag";
      case "C":
        return "state.CarryFlag";
      case "CXZ":
        return "state.CX == 0";
      case "G":
        return "!state.ZeroFlag && state.SignFlag == state.OverflowFlag";
      case "GE":
        return "state.SignFlag == state.OverflowFlag";
      case "L":
        return "state.SignFlag != state.OverflowFlag";
      case "LE":
        return "state.ZeroFlag || state.SignFlag != state.OverflowFlag";
      case "NC":
        return "!state.CarryFlag";
      case "NO":
        return "!state.OverflowFlag";
      case "NS":
        return "!state.SignFlag";
      case "NZ":
        return "!state.ZeroFlag";
      case "O":
        return "state.OverflowFlag";
      case "S":
        return "state.SignFlag";
      case "Z":
        return "state.ZeroFlag";
    }
    return "UNHANDLED CONDITION " + condition;
  }

  public String generateJump(String condition, String param, boolean far)
      throws Exception {
    if (!condition.isEmpty()) {
      String res = "// J" + condition + "\n";
      res += "if(" + generateJumpCondition(condition) + ") {\n";
      res += "  " + generateJump(param, far) + "\n";
      res += "}";
      return res;
    }
    return generateJump(param, far);
  }

  private Function getFunctionAtLongAddress(long addressLong) {
    Address address = ghidraScript.getAddressFactory().getConstantAddress(addressLong);
    return ghidraScript.getFunctionAt(address);
  }

  private String addressToFunctionCall(long addressLong) {
    Function function = getFunctionAtLongAddress(addressLong);
    return functionToString(function, Utils.toHexWith0X(addressLong), false);
  }

  private List<Long> getTargetsOfJumpCall() {
    return this.jumpsAndCalls.getCallsJumpsFromTo().get(this.instructionAddress);
  }

  public String generateJump(String param, boolean far) throws Exception {
    if (!param.startsWith("0x")) {
      // Indirect address ...
      List<Long> targets = getTargetsOfJumpCall();
      List<String> res = new ArrayList<>();
      res.add("// Indirect jump to " + param + ", generating possible targets from emulator records");
      res.add(generateSwitchToIndirectTarget(param, far, targets,
          "Error: Jump not registered at address ", address -> generateGoto(address)));
      return Utils.joinLines(res);
    }
    Long labelAddress = parseGotoAddress(param);
    return generateGoto(labelAddress);
  }

  private String jumpToCaseBody(long address) {
    if (getFunctionAtAddress(address) != null) {
      return this.functionToCaseBody(address);
    }
    return generateGoto(address);
  }

  private long parseGotoAddress(String param) throws Exception {
    String[] split = param.split(":");
    if (split.length != 2) {
      throw new Exception("Cannot parse " + param + " as goto label, only one colon is expected.");
    }
    try {
      int segment = Utils.parseHex(split[0]) * 0x10;
      int offset = Utils.parseHex(split[1]);
      return segment + offset;
    } catch (NumberFormatException nfe) {
      throw new Exception("Cannot parse " + param + " as goto label");
    }
  }

  private String generateGoto(Long target) {
    Function function = getFunctionAtAddress(target);
    if (function != null) {
      return functionToString(function, Utils.toHexWith0X(target), true);
    }
    return "goto " + getLabelToAddress(target, false) + ";";
  }

  public String generateCall(String param, boolean far) {
    if (param.contains("[")) {
      // Indirect address ...
      List<Long> targets = getTargetsOfJumpCall();
      List<String> res = new ArrayList<>();
      res.add("// Indirect call to " + param + ", generating possible targets from emulator records");
      res.add(generateSwitchToIndirectTarget(param, far, targets,
          "Error: Function not registered at address ", address -> functionToCaseBody(address)));
      return Utils.joinLines(res);
    }
    Function function = getFunctionAtAddress(param);
    return functionToString(function, param, false);
  }

  private String functionToCaseBody(long address) {
    Function function = getFunctionAtAddress(address);
    return functionToString(function, Utils.toHexWith0X(address), false) + "\n    break;";
  }

  private Function getFunctionAtAddress(String param) {
    Address address = ghidraScript.getAddressFactory().getAddress(param);
    if (address == null) {
      return null;
    }
    return ghidraScript.getFunctionAt(address);
  }

  private Function getFunctionAtAddress(long addressLong) {
    Address address = ghidraScript.getAddressFactory().getDefaultAddressSpace().getAddress(addressLong);
    if (address == null) {
      return null;
    }
    return ghidraScript.getFunctionAt(address);
  }

  public String functionToString(Function function, String addressString, boolean withReturn) {
    if (function != null) {
      String prefix = withReturn || isLast ? "return " : "";
      return prefix + function.getName() + "();";
    }
    return "failAsUntested(\"Could not find a function at address " + addressString + "\");";
  }

  private String generateSwitchToIndirectTarget(String expression, boolean far, List<Long> targets,
      String errorInCaseNotFound, java.util.function.Function<Long, String> toCSharp) {
    String target = far ?
        parameterTranslator.toCsIpPointerValueInMemory(expression) :
        parameterTranslator.toIpPointerValueInMemory(expression);
    String res = "switch(" + target + ") {\n";
    if (targets != null) {
      for (Long targetFromRecord : targets) {
        res += "  case " + Utils.toHexWith0X(targetFromRecord) + " : " + toCSharp.apply(targetFromRecord) + "\n";
      }
    }
    res += "  default: failAsUntested(\"" + errorInCaseNotFound + "\" + (" + target + "));\n"
        + "}";
    return res;
  }
}

class ModRM {
  public int mode;
  public int registerIndex;
  public int registerMemoryIndex;

  public ModRM(int modRM) {
    mode = (modRM >> 6) & 0b11;
    registerIndex = ((modRM >> 3) & 0b111);
    registerMemoryIndex = (modRM & 0b111);
  }
}

class Log implements Closeable {
  private GhidraScript ghidraScript;
  private PrintWriter printWriterLogs;
  private boolean consoleOutput;

  public Log(GhidraScript ghidraScript, String logFile, boolean consoleOutput) throws IOException {
    this.ghidraScript = ghidraScript;
    printWriterLogs = new PrintWriter(new FileWriter(logFile));
    this.consoleOutput = consoleOutput;
  }

  public void log(String line) {
    printWriterLogs.println(line);
    if (consoleOutput) {
      ghidraScript.println(line);
    }
  }

  @Override public void close() throws IOException {
    printWriterLogs.close();
  }
}

class Utils {
  public static String joinLines(List<String> res) {
    return res.stream().collect(Collectors.joining("\n"));
  }

  public static String indent(String input, int indent) {
    String indentString = "";
    for (int i = 0; i < indent; i++) {
      indentString += " ";
    }
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

  public static String toHexWithout0X(long addressLong) {
    return String.format("%X", addressLong);
  }

  public static String toHexWith0X(long addressLong) {
    return String.format("0x%X", addressLong);
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
}
