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

// https://class.malware.re/2021/03/21/ghidra-scripting-feature-extraction.html
// quand il y a un jump remplacer en call c# si le target est une fonction? mais il va quand meme dumper les autres instructions.
// pour les goto generer des labels en faisant une 1ere passe sur tous les jumps?
public class Spice86 extends GhidraScript {
  private Log log;
  private PrintWriter printWriterCode;
  private Program program;
  private Listing listing;
  private JumpsAndCalls jumpsAndCalls;

  public void run() throws Exception {
    String baseFolder = "E:/Development/Spice86C/src/Spice86/bin/Release/net6.0//";//C:/tmp/dune/
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

  public String outputCSharp(Function function) {
    StringBuilder res = new StringBuilder();
    res.append("public Action " + function.getName() + "() {\n");
    List<Instruction> instructionsBeforeEntry = new ArrayList<>();
    List<Instruction> instructionsAfterEntry = new ArrayList<>();
    boolean success = dispatchInstructions(function, instructionsBeforeEntry, instructionsAfterEntry);
    if (!success) {
      res.append("// Warning, could not read all the instructions!!\n");
    }
    if (!instructionsBeforeEntry.isEmpty()) {
      res.append("  if(false) {\n");
      writeInstructions(res, instructionsBeforeEntry, 4, false);
      res.append("  }\n");
    }
    writeInstructions(res, instructionsAfterEntry, 2, true);
    res.append("}\n");
    return res.toString();
  }

  private void writeInstructions(StringBuilder stringBuilder, List<Instruction> instructions, int indent,
      boolean returnExpected) {
    Iterator<Instruction> instructionIterator = instructions.iterator();
    while (instructionIterator.hasNext()) {
      Instruction instruction = instructionIterator.next();
      boolean isLast = !instructionIterator.hasNext();
      InstructionGenerator instructionGenerator =
          new InstructionGenerator(log, ghidraScript, jumpsAndCalls, instruction, isLast);
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
        + jumpCallTranslator.functionToString(ghidraScript.getFunctionAt(address), address.toString());
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
}

class ParameterTranslator {
  private Log log;
  private RegisterHandler registerHandler;

  public ParameterTranslator(Log log) {
    this.log = log;
    this.registerHandler = new RegisterHandler(log);
  }

  public String toSpice86Value(String param, Set<String> missingRegisters, Integer bits, int offset) {
    if (param.startsWith("0x")) {
      // immediate value
      return litteralToUpperHex(param);
    }
    if (param.length() == 2) {
      // register
      return "state." + param;
    }
    if (param.startsWith("byte ptr ")) {
      return toSpice86Pointer(param.replaceAll("byte ptr ", ""), missingRegisters, 8, offset);
    }
    if (param.startsWith("word ptr ")) {
      return toSpice86Pointer(param.replaceAll("word ptr ", ""), missingRegisters, 16, offset);
    }
    if (bits != null) {
      return toSpice86Pointer(param, missingRegisters, bits, offset);
    }
    log.log("Warning: Could not translate value " + param);
    return null;
  }

  public String toSpice86Value(String param, Set<String> missingRegisters, Integer bits) {
    return toSpice86Value(param, missingRegisters, bits, 0);
  }

  public String toSpice86Pointer(String param, Set<String> missingRegisters, int bits, int offset) {
    String[] split = param.split(":");
    if (split.length == 2) {
      return toSpice86Pointer(split[0], split[1], bits, offset);
    } else {
      String segmentRegister = getSegmentRegister(param, missingRegisters);
      return toSpice86Pointer(segmentRegister, param, bits, offset);
    }
  }

  public String getSegmentRegister(String expression, Set<String> missingRegisters) {
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

  public String toSpice86MemoryAddressExpression(String segmentRegister, String offsetString, int offset) {
    String offsetExpression = pointerExpressionToOffset(offsetString);
    if (offset != 0) {
      offsetExpression += " + " + offset;
    }
    return "state." + segmentRegister + " * 0x10 + " + offsetExpression;
  }

  public String toSpice86Pointer(String memoryAddressExpression, int bits) {
    return "memory.UInt" + bits + "[" + memoryAddressExpression + "]";
  }

  private String pointerExpressionToOffset(String pointerString) {
    String res = litteralToUpperHex(pointerString.replaceAll("\\[", "").replaceAll("\\]", ""));
    return registerHandler.substituteRegistersWithSpice86Expression(res);
  }

  private String litteralToUpperHex(String litteralString) {
    return litteralString.toUpperCase().replaceAll("X", "x");
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

  public RegisterHandler(Log log) {
    this.log = log;
  }

  public String substituteRegistersWithSpice86Expression(String input) {
    String res = input;
    for (String registerName : ALL_REGISTER_NAMES) {
      res = res.replaceAll(registerName, "state." + registerName);
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

  public Set<String> computeMissingSegmentRegisters(String[] params, Object[] inputObjects) {
    Set<String> registersInRepresentation = computeSegmentRegistersInInstructionRepresentation(params);
    Set<String> registersInInstruction = computeSegmentRegistersInInstruction(inputObjects);
    Set<String> res = new HashSet<>(registersInInstruction);
    res.removeAll(registersInRepresentation);
    if (res.size() > 1) {
      log.log("Warning, found more than one missing segment register in instruction. Segment registers in instruction: "
          + registersInRepresentation + " Segment registers according to ghidra: " + registersInInstruction + " delta:"
          + res);
    }
    return res;
  }

  public Set<String> computeMissingRegisters(String mnemonic, String[] params, Object[] inputObjects) {
    Set<String> missingRegisters;
    if ("CALL".equals(mnemonic) || "CALLF".equals(mnemonic) || "RET".equals(mnemonic) || "RETF".equals(mnemonic)
        || "PUSH".equals(mnemonic) || "POP".equals(mnemonic) || "PUSHA".equals(mnemonic) || "POPA".equals(mnemonic)) {
      // Do not compute it for instructions with implicit register access that are fixed and known not to pollute the logs
      missingRegisters = new HashSet<>();
    } else {
      missingRegisters = computeMissingSegmentRegisters(params, inputObjects);
    }
    return missingRegisters;
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
  private Instruction instruction;
  private long instructionAddress;
  private boolean isFunctionReturn;
  private boolean showInstructionComment = false;

  public JumpCallTranslator getJumpCallTranslator() {
    return jumpCallTranslator;
  }

  public boolean isFunctionReturn() {
    return isFunctionReturn;
  }

  public InstructionGenerator(Log log, GhidraScript ghidraScript, JumpsAndCalls jumpsAndCalls, Instruction instruction,
      boolean isLast) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.parameterTranslator = new ParameterTranslator(log);
    this.registerHandler = new RegisterHandler(log);
    this.instructionAnalyzer = new InstructionAnalyzer(log);
    this.instruction = instruction;
    this.instructionAddress = instruction.getAddress().getUnsignedOffset();
    this.jumpCallTranslator =
        new JumpCallTranslator(ghidraScript, jumpsAndCalls, instructionAddress, isLast);
  }

  public String convertInstructionToSpice86(int indent) {
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
    Integer bits = instructionAnalyzer.guessBitLength(instruction);
    String label = jumpCallTranslator.getLabel();
    String instructionString = convertInstructionWithPrefix(mnemonic, prefix, params, missingRegisters, bits);
    isFunctionReturn = instructionString.contains("return ");
    return Utils.indent(label + instructionString, indent) + "\n";
  }

  private String convertInstructionWithPrefix(String mnemonic, String prefix, String[] params,
      Set<String> missingRegisters, Integer bits) {
    if (prefix.isEmpty()) {
      return convertInstructionWithoutPrefix(mnemonic, params, missingRegisters, bits);
    }
    String ret = "while (state.CX-- != 0) {\n";
    ret += Utils.indent(convertInstructionWithoutPrefix(mnemonic, params, missingRegisters, bits), 2) + "\n";
    if (instructionAnalyzer.isStringCheckingZeroFlag(mnemonic)) {
      boolean continueZeroFlagValue = prefix.equals("REPE") || prefix.equals("REP");
      ret += "  if(state.ZeroFlag == " + continueZeroFlagValue + ") {\n";
      ret += "    break;\n";
      ret += "  }\n";
    }
    ret += "}";
    return ret;
  }

  private String toAssignmentWith1Parameter(String operation, String[] parameters, Set<String> missingRegisters,
      Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    return dest + " = " + operation + bits + "(" + dest + ");";
  }

  private String toAssignmentWith2ParametersOnlyOneOperand(String operation, String[] parameters,
      Set<String> missingRegisters,
      Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], missingRegisters, bits);
    return dest + " = " + operation + bits + "(" + operand + ");";
  }

  private String toXor(String[] parameters, Set<String> missingRegisters, Integer bits) {
    if (parameters[0].equals(parameters[1])) {
      // this is a set to 0
      String dest = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
      return dest + " = 0;";
    }
    return toAssignmentWith2Parameters("alu.Xor", parameters, missingRegisters, bits);
  }

  private String toAssignmentWith2Parameters(String operation, String[] parameters, Set<String> missingRegisters,
      Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], missingRegisters, bits);
    return dest + " = " + operation + bits + "(" + dest + ", " + operand + ");";
  }

  private String toNoAssignmentWith2Parameters(String operation, String[] parameters, Set<String> missingRegisters,
      Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], missingRegisters, bits);
    return operation + bits + "(" + dest + ", " + operand + ");";
  }

  private String toScas(String[] parameters, Set<String> missingRegisters, int bits) {
    String param1 = getAXOrAL(bits);
    String param2 = getDestination(parameters, missingRegisters, bits);
    String operation = "alu.Sub" + bits + "(state." + param1 + ", " + param2 + ");";
    return toStringOperation(operation, false, true);
  }

  private String toStos(String[] parameters, Set<String> missingRegisters, int bits) {
    String source = getAXOrAL(bits);
    String destination = getDestination(parameters, missingRegisters, bits);
    String operation = source + " = " + destination + ";";
    return toStringOperation(operation, false, true);
  }

  private String toLods(String[] parameters, Set<String> missingRegisters, int bits) {
    String source = getSource(parameters, missingRegisters, bits);
    String destination = getAXOrAL(bits);
    String operation = source + " = " + destination + ";";
    return toStringOperation(operation, true, false);
  }

  private String getAXOrAL(int bits) {
    return bits == 8 ? "AL" : "AX";
  }

  private String toCmps(String[] parameters, Set<String> missingRegisters, int bits) {
    String param1 = getSource(parameters, missingRegisters, bits);
    String param2 = getDestination(parameters, missingRegisters, bits);
    String operation = "alu.Sub" + bits + "(" + param1 + ", " + param2 + ");";
    return toStringOperation(operation, true, true);
  }

  private String toMovs(String[] parameters, Set<String> missingRegisters, int bits) {
    String destination = getDestination(parameters, missingRegisters, bits);
    String source = getSource(parameters, missingRegisters, bits);
    String operation =
        parameterTranslator.toSpice86Pointer(destination, bits) + " = " + parameterTranslator.toSpice86Pointer(
            source, bits) + ";";
    return toStringOperation(operation, true, true);
  }

  private String getSource(String[] parameters, Set<String> missingRegisters, int bits) {
    return parameterTranslator.toSpice86Pointer(parameters[getSIParamIndex(parameters)], missingRegisters, bits, 0);
  }

  private String getDestination(String[] parameters, Set<String> missingRegisters, int bits) {
    return parameterTranslator.toSpice86Pointer(parameters[getDIParamIndex(parameters)], missingRegisters, bits, 0);
  }

  private String toStringOperation(String operation, boolean changeSI, boolean changeDI) {
    List<String> res = new ArrayList<>();
    res.add(operation);
    if (changeSI) {
      res.add(advanceRegister("SI"));
    }
    if (changeDI) {
      res.add(advanceRegister("DI"));
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

  private String advanceRegister(String register) {
    return "state." + register + " = (ushort)(state." + register + " + (state.DirectionFlag?-1:1));";
  }

  private String toNot(String[] parameters, Set<String> missingRegisters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    return parameter + " = (" + Utils.getType(bits) + ")~" + parameter + ";";
  }

  private String toNeg(String[] parameters, Set<String> missingRegisters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits);
    return parameter + " = alu.Sub" + bits + "(0, " + parameter + ");";
  }

  private String generateLXS(String register, String[] parameters, Set<String> missingRegisters) {
    String destination1 = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, 16);
    String destination2 = "state." + register;
    String value1 = parameterTranslator.toSpice86Value(parameters[1], missingRegisters, 16, 0);
    String value2 = parameterTranslator.toSpice86Value(parameters[1], missingRegisters, 16, 2);
    return destination1 + " = " + value1 + ";\n"
        + destination2 + " = " + value2 + ";";
  }

  private String generateLoop(String condition, String param) {
    String loopCondition = "state.CX-- != 0";
    if (!condition.isEmpty()) {
      if ("NZ".equals(condition)) {
        loopCondition += " && !state.ZeroFlag";
      } else if ("Z".equals(condition)) {
        loopCondition += " && state.ZeroFlag";
      }
    }
    String res = "if(" + loopCondition + ") {\n";
    res += "  " + jumpCallTranslator.generateJump(param) + "\n";
    res += "}\n";
    return res;
  }

  private String toXlat(String[] parameters, Set<String> missingRegisters, Integer bits) {
    return "state.AL = " + parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits) + " + state.AL";
  }

  private String toMul(String[] parameters, Set<String> missingRegisters, Integer bits) {
    return "cpu.Mul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits) + ");";
  }

  private String toIMul(String[] parameters, Set<String> missingRegisters, Integer bits) {
    return "cpu.IMul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits) + ");";
  }

  private String toDiv(String[] parameters, Set<String> missingRegisters, Integer bits) {
    return "cpu.Div" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits) + ");";
  }

  private String toIDiv(String[] parameters, Set<String> missingRegisters, Integer bits) {
    return "cpu.IDiv" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], missingRegisters, bits) + ");";
  }

  private String generateLea(String[] parameters, Set<String> missingRegisters) {
    String memoryExpression = parameters[1];
    String segmentRegister = parameterTranslator.getSegmentRegister(memoryExpression, missingRegisters);
    String destination = parameterTranslator.toSpice86Value(parameters[0], missingRegisters, 16);
    String offset = parameterTranslator.toSpice86MemoryAddressExpression(segmentRegister, memoryExpression, 0);
    return destination + " = " + offset + ";";
  }

  private String generateTempVar() {
    return "tmp" + this.instructionAddress;
  }

  private String convertInstructionWithoutPrefix(String mnemonic, String[] params, Set<String> missingRegisters,
      Integer bits) {
    String instruction = convertInstructionWithoutPrefixAndComment(mnemonic, params, missingRegisters, bits);
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

  private String convertInstructionWithoutPrefixAndComment(String mnemonic, String[] params,
      Set<String> missingRegisters, Integer bits) {
    log.log("Params are " + Arrays.stream(params).collect(Collectors.joining(",")));
    switch (mnemonic) {
      case "AAM":
        return "cpu.Aam(" + parameterTranslator.toSpice86Value(params[0], missingRegisters, bits) + ");";
      case "ADC":
        return toAssignmentWith2Parameters("alu.Adc", params, missingRegisters, bits);
      case "ADD":
        return toAssignmentWith2Parameters("alu.Add", params, missingRegisters, bits);
      case "AND":
        return toAssignmentWith2Parameters("alu.And", params, missingRegisters, bits);
      case "CALL":
      case "CALLF":
        return jumpCallTranslator.generateCall(params[0]);
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
        return toNoAssignmentWith2Parameters("alu.Sub", params, missingRegisters, bits);
      case "CMPSB":
        return toCmps(params, missingRegisters, 8);
      case "CMPSW":
        return toCmps(params, missingRegisters, 16);
      case "CWD":
        return "state.DX = state.AX>=0x8000?0xFFFF:0;";
      case "DEC":
        return toAssignmentWith1Parameter("alu.Dec", params, missingRegisters, bits);
      case "DIV":
        return toDiv(params, missingRegisters, bits);
      case "IDIV":
        return toIDiv(params, missingRegisters, bits);
      case "IMUL":
        return toIMul(params, missingRegisters, bits);
      case "IN":
        return toAssignmentWith2ParametersOnlyOneOperand("cpu.In", params, missingRegisters, bits);
      case "INC":
        return toAssignmentWith1Parameter("alu.Inc", params, missingRegisters, bits);
      case "INT":
        return "//TODO: cpu.Interrupt(" + params[0] + ", false);";
      case "IRET":
        return "return InterruptRet();";
      case "JA":
        return jumpCallTranslator.generateJump("A", params[0]);
      case "JBE":
        return jumpCallTranslator.generateJump("BE", params[0]);
      case "JC":
        return jumpCallTranslator.generateJump("C", params[0]);
      case "JCXZ":
        return jumpCallTranslator.generateJump("CXZ", params[0]);
      case "JG":
        return jumpCallTranslator.generateJump("G", params[0]);
      case "JGE":
        return jumpCallTranslator.generateJump("GE", params[0]);
      case "JL":
        return jumpCallTranslator.generateJump("L", params[0]);
      case "JLE":
        return jumpCallTranslator.generateJump("LE", params[0]);
      case "JMP":
      case "JMPF":
        return jumpCallTranslator.generateJump("", params[0]);
      case "JNC":
        return jumpCallTranslator.generateJump("NC", params[0]);
      case "JNS":
        return jumpCallTranslator.generateJump("NS", params[0]);
      case "JNZ":
        return jumpCallTranslator.generateJump("NZ", params[0]);
      case "JS":
        return jumpCallTranslator.generateJump("S", params[0]);
      case "JZ":
        return jumpCallTranslator.generateJump("Z", params[0]);
      case "LAHF":
        return "state.AH = (byte)state.Flags.FlagRegisters";
      case "LDS":
        return generateLXS("DS", params, missingRegisters);
      case "LEA":
        return generateLea(params, missingRegisters);
      case "LES":
        return generateLXS("ES", params, missingRegisters);
      case "LODSB":
        return toLods(params, missingRegisters, 8);
      case "LODSW":
        return toLods(params, missingRegisters, 16);
      case "LOOP":
        return generateLoop("", params[0]);
      case "LOOPNZ":
        return generateLoop("NZ", params[0]);
      case "LOOPZ":
        return generateLoop("Z", params[0]);
      case "MOV": {
        String dest = parameterTranslator.toSpice86Value(params[0], missingRegisters, bits);
        String source = parameterTranslator.toSpice86Value(params[1], missingRegisters, bits);
        return dest + " = " + source + ";";
      }
      case "MOVSB":
        return toMovs(params, missingRegisters, 8);
      case "MOVSW":
        return toMovs(params, missingRegisters, 16);
      case "MUL":
        return toMul(params, missingRegisters, bits);
      case "NEG":
        return toNeg(params, missingRegisters, bits);
      case "NOP":
        return "";
      case "NOT":
        return toNot(params, missingRegisters, bits);
      case "OR":
        return toAssignmentWith2Parameters("alu.Or", params, missingRegisters, bits);
      case "OUT":
        return toNoAssignmentWith2Parameters("cpu.Out", params, missingRegisters, bits);
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
        return "stack.push(state." + params[0] + ");";
      case "PUSHF":
        return "stack.push(state.Flags);";
      case "RCL":
        return toAssignmentWith2Parameters("alu.Rcl", params, missingRegisters, bits);
      case "RCR":
        return toAssignmentWith2Parameters("alu.Rcr", params, missingRegisters, bits);
      case "RET":
        return "return NearRet();";
      case "RETF":
        return "return FarRet();";
      case "ROL":
        return toAssignmentWith2Parameters("alu.Rol", params, missingRegisters, bits);
      case "ROR":
        return toAssignmentWith2Parameters("alu.Ror", params, missingRegisters, bits);
      case "SAR":
        return toAssignmentWith2Parameters("alu.Sar", params, missingRegisters, bits);
      case "SBB":
        return toAssignmentWith2Parameters("alu.Sbb", params, missingRegisters, bits);
      case "SCASB":
        return toScas(params, missingRegisters, 8);
      case "SCASW":
        return toScas(params, missingRegisters, 16);
      case "SHL":
        return toAssignmentWith2Parameters("alu.Shl", params, missingRegisters, bits);
      case "SHR":
        return toAssignmentWith2Parameters("alu.Shr", params, missingRegisters, bits);
      case "STC":
        return "state.CarryFlag = true;";
      case "STD":
        return "state.DirectiontFlag = true;";
      case "STI":
        return "state.InterruptFlag = true;";
      case "STOSB":
        return toStos(params, missingRegisters, 8);
      case "STOSW":
        return toStos(params, missingRegisters, 16);
      case "SUB":
        return toAssignmentWith2Parameters("alu.Sub", params, missingRegisters, bits);
      case "TEST":
        return toNoAssignmentWith2Parameters("alu.Test", params, missingRegisters, bits);
      case "XCHG": {
        String tempVarName = generateTempVar();
        String var1 = parameterTranslator.toSpice86Value(params[0], missingRegisters, bits);
        String var2 = parameterTranslator.toSpice86Value(params[1], missingRegisters, bits);
        String res =
            Utils.getType(bits) + " " + tempVarName + " = " + var1 + ";\n";
        res += "" + var1 + " = " + var2 + ";\n";
        res += "" + var2 + " = " + tempVarName + ";";
        return res;
      }
      case "XLAT":
        return toXlat(params, missingRegisters, bits);
      case "XOR":
        return toXor(params, missingRegisters, bits);
    }
    return null;
  }
}

class JumpCallTranslator {

  private GhidraScript ghidraScript;
  private JumpsAndCalls jumpsAndCalls;
  private long instructionAddress;
  private boolean isLast;

  public JumpCallTranslator(GhidraScript ghidraScript, JumpsAndCalls jumpsAndCalls, long instructionAddress,
      boolean isLast) {
    this.ghidraScript = ghidraScript;
    this.jumpsAndCalls = jumpsAndCalls;
    this.instructionAddress = instructionAddress;
    this.isLast = isLast;
  }

  public String getLabel() {
    if (this.jumpsAndCalls.getJumpTargets().contains(this.instructionAddress)) {
      return getLabelToAddress(this.instructionAddress, true) + "\n";
    }
    return "";
  }

  private String getLabelToAddress(long address, boolean colon) {
    return "label_" + String.format("%X", address) + (colon ? ":" : "");
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
      case "NS":
        return "!state.SignFlag";
      case "NZ":
        return "!state.ZeroFlag";
      case "S":
        return "state.SignFlag";
      case "Z":
        return "state.ZeroFlag";
    }
    return "UNHANDLED CONDITION " + condition;
  }

  public String generateJump(String condition, String param) {
    if (!condition.isEmpty()) {
      String res = "// J" + condition + "\n";
      res += "if(" + generateJumpCondition(condition) + ") {\n";
      res += "  " + generateJump(param) + "\n";
      res += "}";
      return res;
    }
    return generateJump(param);
  }

  public String generateJump(String param) {
    List<String> res = new ArrayList<>();
    if (param.contains("[")) {
      res.add("// Warning, indirect jump!");
    }
    List<Long> targets = this.getTargetsOfJumpCall();
    Function function = getFunctionAtAddress(param);
    if (function == null) {
      // Try indirect call
      if (targets != null && targets.size() == 1) {
        function = getFunctionAtLongAddress(targets.get(0));
      }
    }
    if (function != null) {
      res.add("// JMP converted to function call");
      res.add(generateCall(param));
    } else {
      String label = param;
      if (targets != null) {
        if (targets.size() == 1) {
          label = getLabelToAddress(targets.get(0), false);
        } else {
          res.add("LINES BELOW WILL NOT WORK, SHOW COMPILE ERROR, INDIRECT JUMP WITH MULTIPLE TARGETS");
          res.addAll(
              targets.stream()
                  .map(address -> "// " + "goto " + getLabelToAddress(address, false) + ";")
                  .collect(Collectors.toList()));
        }
      } else {
        res.add("SHOW COMPILE ERROR, NO TARGET REGISTERED AT RUN TIME");
      }
      res.add("goto " + label + ";");
    }
    return Utils.joinLines(res);
  }

  private Function getFunctionAtLongAddress(long addressLong) {
    Address address = ghidraScript.getAddressFactory().getConstantAddress(addressLong);
    return ghidraScript.getFunctionAt(address);
  }

  private String addressToFunctionCall(long addressLong) {
    Function function = getFunctionAtLongAddress(addressLong);
    return functionToString(function, String.format("0x%X", addressLong));
  }

  private List<Long> getTargetsOfJumpCall() {
    return this.jumpsAndCalls.getCallsJumpsFromTo().get(this.instructionAddress);
  }

  public String generateCall(String param) {
    if (param.contains("[")) {
      // Indirect address ...
      List<Long> targets = getTargetsOfJumpCall();
      List<String> res = new ArrayList<>();
      if (targets != null) {
        if (targets.size() == 1) {
          res.add("// Warning, indirect call, generated function call may not be accurate.");
          res.add(addressToFunctionCall(targets.get(0)));
        } else {
          res.add("// Warning, indirect call, several possible addresses destinations.");
          res.add("LINES BELOW WILL NOT WORK, SHOW COMPILE ERROR, INDIRECT FUNCTION CALL WITH MULTIPLE TARGETS");
          res.addAll(
              targets.stream().map(address -> "// " + addressToFunctionCall(address)).collect(Collectors.toList()));
        }
      } else {
        res.add("SHOW COMPILE ERROR, INDIRECT FUNCTION CALL BUT NO TARGET REGISTERED AT RUN TIME");
      }
      return Utils.joinLines(res);
    }

    Function function = getFunctionAtAddress(param);
    return functionToString(function, param);
  }

  public String functionToString(Function function, String addressString) {
    if (function != null) {
      String prefix = isLast ? "return " : "";
      return prefix + function.getName() + "();";
    }
    return "// Warning, could not find a function at address " + addressString;
  }

  private Function getFunctionAtAddress(String param) {
    Address address = ghidraScript.getAddressFactory().getAddress(param);
    if (address == null) {
      return null;
    }
    return ghidraScript.getFunctionAt(address);
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
}