import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;
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
import org.apache.commons.collections4.CollectionUtils;

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
import java.util.Objects;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Collectors;
import java.util.stream.StreamSupport;

//Generates CSharp code to run on with the Spice86 emulator as a backend (https://github.com/OpenRakis/Spice86/)
//Please define the folder with the dump data via system environment variable SPICE86_DUMPS_FOLDER
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86CodeGenerator extends GhidraScript {
  public void run() throws Exception {
    String baseFolder = System.getenv("SPICE86_DUMPS_FOLDER");
    run(baseFolder + "spice86dumpJumps.json", baseFolder + "ghidrascriptout.txt", baseFolder + "GeneratedCode.cs",
        "Cryogenic.Generated");
  }

  private void run(String jumpsAndCallsFile, String logFile, String generatedCodeFile, String namespace)
      throws Exception {
    JumpsAndCalls jumpsAndCalls =
        readJumpMapFromFile(jumpsAndCallsFile);
    try (Log log = new Log(this, logFile, false)) {
      Program program = getCurrentProgram();
      Listing listing = program.getListing();
      FunctionIterator functionIterator = listing.getFunctions(true);
      List<ParsedFunction> parsedFunctions = StreamSupport.stream(functionIterator.spliterator(), false)
          .map(f -> ParsedFunction.createParsedFunction(log, this, f)).filter(Objects::nonNull).toList();
      ParsedProgram parsedProgram = ParsedProgram.createParsedProgram(log, parsedFunctions, jumpsAndCalls);
      log.info("Finished parsing.");
      generateProgram(log, parsedProgram, generatedCodeFile, namespace);
    }
  }

  private void generateProgram(Log log, ParsedProgram parsedProgram, String generatedCodeFile, String namespace)
      throws IOException {
    PrintWriter printWriterCode = new PrintWriter(new FileWriter(generatedCodeFile));
    printWriterCode.print(new ProgramGenerator(log, this, parsedProgram, namespace).outputCSharp());
    printWriterCode.close();
  }

  private JumpsAndCalls readJumpMapFromFile(String filePath) throws IOException {
    try (FileReader fileReader = new FileReader(filePath); JsonReader reader = new JsonReader(fileReader)) {
      Type type = new TypeToken<JumpsAndCalls>() {
      }.getType();
      JumpsAndCalls res = new Gson().fromJson(reader, type);
      res.init();
      return res;
    }
  }
}

class ProgramGenerator {
  private final Log log;
  private final GhidraScript ghidraScript;
  private final ParsedProgram parsedProgram;
  private final String namespace;

  public ProgramGenerator(Log log, GhidraScript ghidraScript, ParsedProgram parsedProgram, String namespace) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.parsedProgram = parsedProgram;
    this.namespace = namespace;
  }

  public String outputCSharp() {
    StringBuilder res = new StringBuilder("namespace " + namespace + ";\n\n ");
    res.append("""
        using Spice86.Emulator.Function;
        using Spice86.Emulator.Memory;
        using Spice86.Emulator.ReverseEngineer;
        using Spice86.Emulator.VM;

        using System;
        using System.Collections.Generic;
                
        """);
    res.append("public partial class Overrides : CSharpOverrideHelper {\n");
    res.append(Utils.indent(generateSegmentStorage(), 2));
    res.append('\n');
    Collection<ParsedFunction> parsedFunctions = parsedProgram.getEntryPoints().values();
    res.append(Utils.indent(generateConstructor(parsedFunctions), 2));
    res.append('\n');
    generateFunctions(parsedFunctions, res);
    res.append("}\n");
    return res.toString();
  }

  private String generateConstructor(Collection<ParsedFunction> functions) {
    StringBuilder res = new StringBuilder(
        "public GeneratedCode(Dictionary<SegmentedAddress, FunctionInformation> functionInformations, ushort entrySegment, Machine machine) : base(functionInformations, \"generatedCode\", machine) {\n");
    res.append(Utils.indent(generateSegmentConstructorAssignment(), 2));
    res.append('\n');
    int lastSegment = 0;
    for (ParsedFunction parsedFunction : functions) {
      String name = parsedFunction.getName();
      SegmentedAddress address = parsedFunction.getEntrySegmentedAddress();
      int currentSegment = address.getSegment();
      if (currentSegment != lastSegment) {
        lastSegment = currentSegment;
        res.append("  // " + Utils.toHexWith0X(currentSegment) + "\n");
      }
      res.append(
          "  DefineFunction(" + parsedProgram.getCodeSegmentVariables().get(currentSegment) + ", " + Utils.toHexWith0X(
              address.getOffset())
              + ", " + name + ", false);");
      res.append('\n');
    }
    res.append(generateCodeRewriteDetector());
    res.append("}\n");
    return res.toString();
  }

  private String generateSegmentConstructorAssignment() {
    return generateSegmentVars(e -> "this." + e.getValue() + " = (ushort)(entrySegment + " + Utils.toHexWith0X(
        e.getKey() - parsedProgram.getEntryAddress() / 0x10)
        + ");\n");
  }

  private String generateSegmentStorage() {
    return generateSegmentVars(v -> "private ushort " + v.getValue() + "; // " + Utils.toHexWith0X(v.getKey()) + "\n");
  }

  private String generateSegmentVars(java.util.function.Function<Map.Entry<Integer, String>, String> mapper) {
    return parsedProgram.getCodeSegmentVariables()
        .entrySet()
        .stream()
        .map(mapper)
        .collect(Collectors.joining(""));
  }

  private void generateFunctions(Collection<ParsedFunction> functions, StringBuilder res) {
    for (ParsedFunction parsedFunction : functions) {
      res.append(
          Utils.indent(new FunctionGenerator(log, ghidraScript, parsedProgram, parsedFunction).outputCSharp(), 2));
      res.append('\n');
    }
  }

  private String generateCodeRewriteDetector() {
    List<Integer> codeAddresses = parsedProgram.getInstructionAddresses().stream().sorted().toList();
    if (codeAddresses.isEmpty()) {
      return "";
    }
    String res = "";
    int rangeStart = codeAddresses.get(0);
    for (int i = 0; i < codeAddresses.size(); i++) {
      int currentAddress = codeAddresses.get(i);
      int currentInstructionLength = parsedProgram.getInstructionAtAddress(currentAddress).getInstructionLength();
      if (i == codeAddresses.size() - 1) {
        // Last instruction
        res += generateFailWhenWriteInRange(rangeStart, currentAddress + currentInstructionLength - 1);
      } else {
        int actualNextAddress = codeAddresses.get(i + 1);
        int expectedNextAddress = currentAddress + currentInstructionLength;
        if (expectedNextAddress != actualNextAddress) {
          // end of range
          res += generateFailWhenWriteInRange(rangeStart, expectedNextAddress - 1);
          rangeStart = actualNextAddress;
        }
      }
    }
    return res;
  }

  private String generateFailWhenWriteInRange(int rangeStart, int rangeEnd) {
    return "  FailWhenWriteInRange(" + Utils.toHexWith0X(rangeStart) + ", " + Utils.toHexWith0X(rangeEnd) + ");\n";
  }
}

class FunctionGenerator {
  private final Log log;
  private final GhidraScript ghidraScript;
  private final ParsedProgram parsedProgram;
  private final ParsedFunction parsedFunction;

  public FunctionGenerator(Log log, GhidraScript ghidraScript, ParsedProgram parsedProgram,
      ParsedFunction parsedFunction) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.parsedProgram = parsedProgram;
    this.parsedFunction = parsedFunction;
  }

  public String outputCSharp() {
    StringBuilder res = new StringBuilder();
    String name = parsedFunction.getName();
    log.info("Generating C# code for function " + name);
    res.append("public Action " + name + "(int gotoTarget) {\n");
    List<ParsedInstruction> instructionsBeforeEntry = parsedFunction.getInstructionsBeforeEntry();
    List<ParsedInstruction> instructionsAfterEntry = parsedFunction.getInstructionsAfterEntry();
    Set<String> generatedTempVars = new HashSet<>();
    SegmentedAddress firstInstructionOfBeforeSectionAddress = null;
    if (!instructionsBeforeEntry.isEmpty()) {
      firstInstructionOfBeforeSectionAddress = instructionsBeforeEntry.get(0).getInstructionSegmentedAddress();
    }
    String gotosFromOutsideHandlingSection =
        generateGotoFromOutsideAndInstructionSkip(firstInstructionOfBeforeSectionAddress);
    if (!gotosFromOutsideHandlingSection.isEmpty()) {
      res.append(Utils.indent(gotosFromOutsideHandlingSection, 2) + "\n");
    }
    if (!instructionsBeforeEntry.isEmpty()) {
      writeInstructions(res, instructionsBeforeEntry, 2, generatedTempVars, false);
      res.append("  entry:\n");
    }
    writeInstructions(res, instructionsAfterEntry, 2, generatedTempVars, true);
    res.append("}\n");
    return res.toString();
  }

  private String generateGotoFromOutsideAndInstructionSkip(SegmentedAddress firstInstructionOfBeforeSectionAddress) {
    Collection<SegmentedAddress> jumpTargets = CollectionUtils.emptyIfNull(
        parsedProgram.getJumpsFromOutsideForFunction(parsedFunction.getEntrySegmentedAddress()));
    if (firstInstructionOfBeforeSectionAddress == null && jumpTargets.isEmpty()) {
      return "";
    }
    StringBuilder res = new StringBuilder("switch(gotoTarget) {\n");
    for (SegmentedAddress target : jumpTargets) {
      if (target.equals(firstInstructionOfBeforeSectionAddress)) {
        // Handled separately, do not do double case
        continue;
      }
      res.append(
          "  case " + Utils.toHexWith0X(target.toPhysical() - parsedProgram.getEntryAddress()) + ": goto "
              + JumpCallTranslator.getLabelToAddress(target,
              false) + ";break;\n");
    }
    if (firstInstructionOfBeforeSectionAddress != null) {
      // instructions before entry point are just after this switch
      res.append("  case " + Utils.toHexWith0X(
          firstInstructionOfBeforeSectionAddress.toPhysical() - parsedProgram.getEntryAddress()) + ": break;\n");
      // default address 0 is for entry point, goto there as there are instructions between
      res.append("  case 0: goto entry; break; //\n");
    } else {
      // default address 0 is for entry point, instructions are just after the switch
      res.append("  case 0: break;\n");
    }
    if (!jumpTargets.isEmpty()) {
      // Only makes sense to generate this when there are labels accessible from outside
      res.append("  default: " + InstructionGenerator.generateFailAsUntested(
          "\"Could not find any label from outside with address \" + gotoTarget", false) + "\n");
    }
    res.append("}");
    return res.toString();
  }

  private void writeInstructions(StringBuilder stringBuilder, List<ParsedInstruction> instructions, int indent,
      Set<String> generatedTempVars, boolean returnExpected) {
    Iterator<ParsedInstruction> instructionIterator = instructions.iterator();
    while (instructionIterator.hasNext()) {
      ParsedInstruction parsedInstruction = instructionIterator.next();
      InstructionGenerator instructionGenerator =
          new InstructionGenerator(log, ghidraScript, parsedProgram, parsedFunction, parsedInstruction,
              generatedTempVars);
      stringBuilder.append(Utils.indent(instructionGenerator.convertInstructionToSpice86(true), indent) + "\n");
      boolean isLast = !instructionIterator.hasNext();
      if (isLast && returnExpected && !instructionGenerator.isFunctionReturn()) {
        // Last instruction should have been a return, but it is not.
        // It means the ASM code will continue to the next function. Generate a function call if possible.
        Instruction next = parsedInstruction.getInstruction().getNext();
        stringBuilder.append(
            Utils.indent(generateMissingReturn(next, instructionGenerator.getJumpCallTranslator()), indent) + "\n");
      }
    }
  }

  private String generateMissingReturn(Instruction next, JumpCallTranslator jumpCallTranslator) {
    if (next == null) {
      return InstructionGenerator.generateFailAsUntested(
          "Function does not end with return and no instruction after the body ...", true);
    }
    Address address = next.getAddress();
    ParsedFunction function = parsedProgram.getFunctionAtGhidraAddressEntryPoint(address);
    if (function != null) {
      return "// Function call generated as ASM continues to next function entry point without return\n"
          + jumpCallTranslator.functionToDirectCSharpCallWithReturn(function, null);
    } else {
      function = parsedProgram.getFunctionAtGhidraAddressAny(address);
      if (function != null) {
        return "// Function call generated as ASM continues to next function body without return\n"
            + jumpCallTranslator.functionToDirectCSharpCallWithReturn(function, (int)address.getUnsignedOffset());
      }
    }
    return InstructionGenerator.generateFailAsUntested(
        "Function does not end with return and no other function found after the body at address "
            + Utils.toHexWith0X(address.getUnsignedOffset()), true);
  }
}

class ParameterTranslator {
  private final Log log;
  private final ParsedInstruction parsedInstruction;
  private final RegisterHandler registerHandler;
  private Set<String> missingRegisters;
  // variables must be unique accross a function
  private Set<String> generatedTempVars;
  private Map<Integer, String> codeSegmentVariables;

  public ParameterTranslator(Log log, ParsedInstruction parsedInstruction, RegisterHandler registerHandler,
      Map<Integer, String> codeSegmentVariables, Set<String> generatedTempVars) {
    this.log = log;
    this.parsedInstruction = parsedInstruction;
    this.registerHandler = registerHandler;
    this.codeSegmentVariables = codeSegmentVariables;
    this.generatedTempVars = generatedTempVars;
  }

  public Set<String> getGeneratedTempVars() {
    return generatedTempVars;
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
    log.warning("Could not translate value " + param);
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
    if (parsedInstruction.getSegment() != null) {
      return parsedInstruction.getSegment();
    }
    if (!missingRegisters.isEmpty()) {
      String source = " ";
      if (parsedInstruction.getModRmByte() != null) {
        source = "from modrm";
      }
      if (!parsedInstruction.getPrefixes().isEmpty()) {
        source = "from prefixes";
      }
      if (missingRegisters.size() > 1) {
        log.warning("More than one missing registers, heuristic will probably not work!!!");
      }
      String res = missingRegisters.iterator().next();
      log.info("Cannot guess segment register " + source + "for parameter " + expression
          + " defaulting to missing registers heuristic. Found " + res);
      return res;
    }
    log.warning("Cannot guess segment register for parameter " + expression);
    return "DS";
  }

  private String toSpice86Pointer(String segmentRegister, String offsetString, int bits, int offset) {
    String offsetExpression = toOffsetExpression(offsetString, offset);
    return toSpice86Pointer(registerHandler.substituteRegister(segmentRegister), offsetExpression, bits);
  }

  public String toCsIpPointerValueInMemory(String expression) {
    String ip = toSpice86Value(expression, 16, 0);
    String cs = toSpice86Value(expression, 16, 2);
    return toPhysicalAddress(cs, ip);
  }

  public String toIpPointerValueInMemory(String expression) {
    String ip = toSpice86Value(expression, 16, 0);
    return toPhysicalAddress("CS", ip);
  }

  private String toPhysicalAddress(String segmentExpression, String offsetExpression) {
    String segmentVariable = segmentExpression;
    if ("CS".equals(segmentExpression)) {
      segmentVariable = codeSegmentVariables.get(parsedInstruction.getInstructionSegmentedAddress().getSegment());
    }
    return segmentVariable + " * 0x10 + " + offsetExpression;
  }

  public String toOffsetExpression(String offsetString, int offset) {
    String offsetExpression = pointerExpressionToOffset(offsetString);
    if (offset != 0) {
      if (isDirectValue(offsetExpression)) {
        // Do the addition directly at generation time
        int value = Utils.parseHex(offsetExpression) + offset;
        offsetExpression = Utils.toHexWith0X(value);
      } else {
        offsetExpression += " + " + offset;
      }
    }
    return castToUInt16(offsetExpression);
  }

  public String signExtendToUInt16(String expression) {
    if (isDirectValue(expression)) {
      // Do the sign extension directly here to avoid confusing C#
      int value = Utils.uint16(Utils.int16(Utils.parseHex(expression)));
      return Utils.toHexWith0X(value);
    }
    return castToUInt16("(short)((sbyte)" + expression + ")");
  }

  public String castToUInt16(String expression) {
    if (expression.contains("+") || expression.contains("-") || expression.contains("(") || expression.contains("?")
        || expression.contains(":")) {
      // Complex expression, cast...
      return "(ushort)(" + expression + ")";
    }
    return expression;
  }

  public boolean isDirectValue(String expression) {
    if (expression.startsWith("0x") || expression.startsWith("-0x")) {
      try {
        Utils.parseHex(expression);
        return true;
      } catch (NumberFormatException nfe) {
        return false;
      }
    }
    return false;
  }

  public String toSpice86Pointer(String segment, String offset, int bits) {
    return "UInt" + bits + "[" + segment + ", " + offset + "]";
  }

  public String pointerExpressionToOffset(String pointerString) {
    String res = Utils.litteralToUpperHex(pointerString
        .replaceAll("\\[", "")
        .replaceAll("]", "")
        .replaceAll(" \\+ 0x0", "")
        .replaceAll(" \\+ -", " - "));
    return registerHandler.substituteRegistersWithSpice86Expression(res);
  }

  private String generateNonUniqueTempVar(String prefix) {
    return prefix + Utils.toHexSegmentOffset(this.parsedInstruction.getInstructionSegmentedAddress());
  }

  public String generateTempVar(String prefix) {
    String res = generateNonUniqueTempVar(prefix);
    int index = 1;
    while (generatedTempVars.contains(res)) {
      log.info("Variable already in scope of function, adding index " + res);
      res = generateNonUniqueTempVar(prefix + index + "_");
    }
    generatedTempVars.add(res);
    return res;
  }

  public String generateTempVar() {
    return generateTempVar("tmp_");
  }
}

class RegisterHandler {
  private static final Set<String> REGISTER_NAMES_16_BITS =
      new HashSet<>(Arrays.asList("AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"));
  private static final Set<String> REGISTER_NAMES_8_BITS =
      new HashSet<>(Arrays.asList("AL", "AH", "CL", "CH", "DL", "DH", "BL", "BH"));
  private static final Set<String> REGULAR_REGISTER_NAMES = new HashSet<>();
  private static final Set<String> SEGMENT_REGISTER_NAMES =
      new HashSet<>(Arrays.asList("ES", "CS", "SS", "DS", "FS", "GS"));
  private static final Set<String> ALL_REGISTER_NAMES = new HashSet<>();

  static {
    REGULAR_REGISTER_NAMES.addAll(REGISTER_NAMES_16_BITS);
    REGULAR_REGISTER_NAMES.addAll(REGISTER_NAMES_8_BITS);
    ALL_REGISTER_NAMES.addAll(REGULAR_REGISTER_NAMES);
    ALL_REGISTER_NAMES.addAll(SEGMENT_REGISTER_NAMES);
  }

  private final Log log;
  private final String csVariable;

  public RegisterHandler(Log log, String csVariable) {
    this.log = log;
    this.csVariable = csVariable;
  }

  public String substituteRegister(String registerName) {
    if ("CS".equals(registerName)) {
      if (csVariable != null) {
        return csVariable;
      }
      return "/* WARNING, CS value could not be evaluated, CS will not have a correct value */ CS";
    }
    return registerName;
  }

  public String substituteRegistersWithSpice86Expression(String input) {
    String res = input;
    for (String registerName : ALL_REGISTER_NAMES) {
      res = res.replaceAll(registerName, substituteRegister(registerName));
    }
    return res;
  }

  private Set<String> computeSegmentRegistersInInstructionRepresentation(String[] params) {
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

  private Set<String> computeSegmentRegistersInInstruction(Object[] inputObjects) {
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
      log.warning("Found more than one missing segment register in instruction. Segment registers in instruction: "
          + registersInRepresentation + " Segment registers according to ghidra: " + registersInInstruction + " delta:"
          + res);
    }
    return res;
  }
}

class InstructionGenerator {
  private final Log log;
  private final ParameterTranslator parameterTranslator;
  private final RegisterHandler registerHandler;
  private final ParsedInstruction parsedInstruction;
  private final JumpCallTranslator jumpCallTranslator;
  private final Instruction instruction;
  private boolean isFunctionReturn;
  private boolean showInstructionComment = true;

  public JumpCallTranslator getJumpCallTranslator() {
    return jumpCallTranslator;
  }

  public boolean isFunctionReturn() {
    return isFunctionReturn;
  }

  public InstructionGenerator(Log log, GhidraScript ghidraScript, ParsedProgram parsedProgram,
      ParsedFunction currentFunction, ParsedInstruction parsedInstruction, Set<String> generatedTempVars) {
    SegmentedAddress instructionSegmentedAddress = parsedInstruction.getInstructionSegmentedAddress();
    Map<Integer, String> codeSegmentVariables = parsedProgram.getCodeSegmentVariables();
    this.log = log;
    this.registerHandler = new RegisterHandler(log, codeSegmentVariables.get(instructionSegmentedAddress.getSegment()));
    this.parameterTranslator =
        new ParameterTranslator(log, parsedInstruction, registerHandler, codeSegmentVariables,
            generatedTempVars);
    this.parsedInstruction = parsedInstruction;
    this.instruction = parsedInstruction.getInstruction();
    this.jumpCallTranslator =
        new JumpCallTranslator(log, ghidraScript, parameterTranslator, parsedProgram, currentFunction,
            parsedInstruction);
  }

  public String convertInstructionToSpice86(boolean generateLabel) {
    log.info("Processing instruction " + instruction + " at address " + instruction.getAddress());

    Object[] inputObjects = instruction.getInputObjects();
    Set<String> missingRegisters =
        registerHandler.computeMissingRegisters(parsedInstruction.getMnemonic(), parsedInstruction.getParameters(),
            inputObjects);
    parameterTranslator.setMissingRegisters(missingRegisters);
    String label = jumpCallTranslator.getLabel();
    String instructionString =
        convertInstructionWithPrefix(parsedInstruction.getMnemonic(), parsedInstruction.getPrefix(),
            parsedInstruction.getParameters());
    isFunctionReturn =
        instructionString.contains("return ") && !(instructionString.contains("if") || instructionString.contains(
            "switch"));
    if (generateLabel) {
      return label + instructionString;
    }
    return instructionString;
  }

  private String convertInstructionWithPrefix(String mnemonic, String prefix, String[] params) {
    if (prefix.isEmpty()) {
      return convertInstructionWithoutPrefix(mnemonic, params);
    }
    String ret = "while (CX-- != 0) {\n";
    ret += Utils.indent(convertInstructionWithoutPrefix(mnemonic, params), 2) + "\n";
    if (parsedInstruction.isStringCheckingZeroFlag()) {
      boolean continueZeroFlagValue = prefix.equals("REPE") || prefix.equals("REP");
      ret += "  if(ZeroFlag == " + continueZeroFlagValue + ") {\n";
      ret += "    break;\n";
      ret += "  }\n";
    }
    ret += "}";
    return ret;
  }

  private String generateAssignmentWith1Parameter(String operation, String[] parameters, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    return dest + " = " + operation + bits + "(" + dest + ");";
  }

  private String generateAssignmentWith2ParametersOnlyOneOperand(String operation, String[] parameters, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
    String operand = parameterTranslator.toSpice86Value(parameters[1], bits);
    return dest + " = " + operation + bits + "(" + operand + ");";
  }

  private String generateXor(String[] parameters) {
    if (parameters[0].equals(parameters[1])) {
      Integer bits = parsedInstruction.getParameter1BitLength();
      // this is a set to 0
      String dest = parameterTranslator.toSpice86Value(parameters[0], bits);
      return dest + " = 0;";
    }
    return generateAssignmentWith2Parameters("Alu.Xor", "^", parameters);
  }

  private String generateAssignmentWith2Parameters(String operation, String nativeOperation, String[] parameters) {
    Integer bitsParameter1 = parsedInstruction.getParameter1BitLength();
    String dest = parameterTranslator.toSpice86Value(parameters[0], bitsParameter1);
    String operand = signExtendParameter2IfNeeded(parameters);
    String res = "";
    if (nativeOperation != null) {
      res += "// " + dest + " " + nativeOperation + "= " + operand + ";\n";
    }
    res += dest + " = " + operation + bitsParameter1 + "(" + dest + ", " + operand + ");";
    return res;
  }

  private String generateNoAssignmentWith2Parameters(String operation, String[] parameters) {
    Integer bitsParameter1 = parsedInstruction.getParameter1BitLength();
    String dest = parameterTranslator.toSpice86Value(parameters[0], bitsParameter1);
    String operand = signExtendParameter2IfNeeded(parameters);
    return operation + bitsParameter1 + "(" + dest + ", " + operand + ");";
  }

  private String signExtendParameter2IfNeeded(String[] parameters) {
    Integer bitsParameter1 = parsedInstruction.getParameter1BitLength();
    Integer bitsParameter2 = parsedInstruction.getParameter2BitLength();
    String operand = parameterTranslator.toSpice86Value(parameters[1], bitsParameter2);
    if (bitsParameter1 != bitsParameter2) {
      log.info("Sign extending parameter 2");
      if (bitsParameter2 != 8) {
        log.warning("Parameter 1 length is " + bitsParameter1 + " and parameter 2 is " + bitsParameter2
            + " this is unsupported.");
      }
      return parameterTranslator.signExtendToUInt16(operand);
    }
    return operand;
  }

  private String generateIns(String[] parameters, int bits) {
    String destination = getDestination(parameters, bits);
    String operation = destination + " = Cpu.In" + bits + "(DX);";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateOuts(String[] parameters, int bits) {
    String source = getSource(parameters, bits);
    String operation = "Cpu.Out" + bits + "(DX, " + source + ");";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateScas(String[] parameters, int bits) {
    String param1 = getAXOrAL(bits);
    String param2 = getDestination(parameters, bits);
    String operation = "Alu.Sub" + bits + "(" + param1 + ", " + param2 + ");";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateStos(String[] parameters, int bits) {
    String source = getAXOrAL(bits);
    String destination = getDestination(parameters, bits);
    String operation = destination + " = " + source + ";";
    return generateStringOperation(operation, false, true, bits);
  }

  private String generateLods(String[] parameters, int bits) {
    String source = getSource(parameters, bits);
    String destination = getAXOrAL(bits);
    String operation = destination + " = " + source + ";";
    return generateStringOperation(operation, true, false, bits);
  }

  private String generateCmps(String[] parameters, int bits) {
    String param1 = getSource(parameters, bits);
    String param2 = getDestination(parameters, bits);
    String operation = "Alu.Sub" + bits + "(" + param1 + ", " + param2 + ");";
    return generateStringOperation(operation, true, true, bits);
  }

  private String generateMovs(String[] parameters, int bits) {
    String destination = getDestination(parameters, bits);
    String source = getSource(parameters, bits);
    String operation = destination + " = " + source + ";";
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
    String expression = parameterTranslator.castToUInt16("SI + (DirectionFlag ? -" + advance + " : " + advance + ")");
    return register + " = " + expression + ";";
  }

  private String generateNot(String[] parameters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], bits);
    return parameter + " = (" + Utils.getType(bits) + ")~" + parameter + ";";
  }

  private String generateNeg(String[] parameters, Integer bits) {
    String parameter = parameterTranslator.toSpice86Value(parameters[0], bits);
    return parameter + " = Alu.Sub" + bits + "(0, " + parameter + ");";
  }

  private String generateLXS(String register, String[] parameters) {
    String destination1 = parameterTranslator.toSpice86Value(parameters[0], 16);
    String destination2 = register;
    String value1 = parameterTranslator.toSpice86Value(parameters[1], 16, 0);
    String value2 = parameterTranslator.toSpice86Value(parameters[1], 16, 2);
    return destination1 + " = " + value1 + ";\n"
        + destination2 + " = " + value2 + ";";
  }

  private String generateLoop(String condition, String param) {
    String loopCondition = "--CX != 0";
    if (!condition.isEmpty()) {
      if ("NZ".equals(condition)) {
        loopCondition += " && !ZeroFlag";
      } else if ("Z".equals(condition)) {
        loopCondition += " && ZeroFlag";
      }
    }
    String res = "if(" + loopCondition + ") {\n";
    res += Utils.indent(jumpCallTranslator.generateJump(param, false), 2) + "\n";
    res += "}";
    return res;
  }

  private String generateInterrupt(String parameter) {
    return "Interrupt(" + parameter + ");";
  }

  private String getAXOrAL(int bits) {
    return (bits == 8 ? "AL" : "AX");
  }

  private String generateXlat(String[] parameters) {
    String pointer = parameterTranslator.toSpice86Pointer("BX + AL", 8, 0);
    return "AL = " + pointer + ";";
  }

  private String generateMul(String[] parameters, Integer bits) {
    return "Cpu.Mul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateIMul(String[] parameters, Integer bits) {
    return "Cpu.IMul" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateDiv(String[] parameters, Integer bits) {
    return "Cpu.Div" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateIDiv(String[] parameters, Integer bits) {
    return "Cpu.IDiv" + bits + "(" + parameterTranslator.toSpice86Value(parameters[0], bits) + ");";
  }

  private String generateLea(String[] parameters) {
    String offset = parameterTranslator.castToUInt16(parameterTranslator.pointerExpressionToOffset(parameters[1]));
    String destination = parameterTranslator.toSpice86Value(parameters[0], 16);
    return destination + " = " + offset + ";";
  }

  private String generateCwd() {
    String expression = parameterTranslator.castToUInt16("AX>=0x8000?0xFFFF:0");
    return "DX = " + expression + ";";
  }

  private String convertInstructionWithoutPrefix(String mnemonic, String[] params) {
    String instruction = convertInstructionWithoutPrefixAndComment(mnemonic, params);
    if (instruction == null) {
      instruction = InstructionGenerator.generateFailAsUntested("Unimplemented Instruction!", true);
    }
    if (this.showInstructionComment) {
      String instuctionAsm = mnemonic + " " + String.join(",", params);
      String address = parsedInstruction.getInstructionSegmentedAddress().toString();
      return "// " + instuctionAsm + " (" + address + ")\n" + instruction;
    }
    return instruction;
  }

  private String generateMov(String[] params, Integer bits) {
    String dest = parameterTranslator.toSpice86Value(params[0], bits);
    String source = parameterTranslator.toSpice86Value(params[1], bits);
    return dest + " = " + source + ";";
  }

  private String generatePushA() {
    String spTempVar = parameterTranslator.generateTempVar("sp");
    return "ushort " + spTempVar + " = SP;\n"
        + "Stack.Push(AX);\n"
        + "Stack.Push(CX);\n"
        + "Stack.Push(DX);\n"
        + "Stack.Push(BX);\n"
        + "Stack.Push(" + spTempVar + ");\n"
        + "Stack.Push(BP);\n"
        + "Stack.Push(SI);\n"
        + "Stack.Push(DI);";
  }

  private String generateXchg(String[] params, Integer bits) {
    String tempVarName = parameterTranslator.generateTempVar();
    String var1 = parameterTranslator.toSpice86Value(params[0], bits);
    String var2 = parameterTranslator.toSpice86Value(params[1], bits);
    String res =
        Utils.getType(bits) + " " + tempVarName + " = " + var1 + ";\n";
    res += "" + var1 + " = " + var2 + ";\n";
    res += "" + var2 + " = " + tempVarName + ";";
    return res;
  }

  private String generatePush(String[] params, Integer bits) {
    String value = parameterTranslator.toSpice86Value(params[0], bits);
    if (bits != 16) {
      value = parameterTranslator.signExtendToUInt16(value);
    }
    return "Stack.Push(" + value + ");";
  }

  private String generatePop(String[] params, Integer bits) {
    return parameterTranslator.toSpice86Value(params[0], bits) + " = Stack.Pop();";
  }

  private String convertInstructionWithoutPrefixAndComment(String mnemonic, String[] params) {
    log.info("Params are " + String.join(",", params));
    Integer parameter1Bits = parsedInstruction.getParameter1BitLength();
    return switch (mnemonic) {
      case "AAA" -> "Cpu.Aaa();";
      case "AAM" -> "Cpu.Aam(" + parameterTranslator.toSpice86Value(params[0], parameter1Bits) + ");";
      case "ADC" -> generateAssignmentWith2Parameters("Alu.Adc", null, params);
      case "ADD" -> generateAssignmentWith2Parameters("Alu.Add", "+", params);
      case "AND" -> generateAssignmentWith2Parameters("Alu.And", "&", params);
      case "CALL" -> jumpCallTranslator.generateCall(params[0], false);
      case "CALLF" -> jumpCallTranslator.generateCall(params[0], true);
      case "CBW" -> "AX = " + parameterTranslator.signExtendToUInt16("AL") + ";";
      case "CLC" -> "CarryFlag = false;";
      case "CLD" -> "DirectionFlag = false;";
      case "CLI" -> "InterruptFlag = false;";
      case "CMC" -> "CarryFlag = !CarryFlag;";
      case "CMP" -> generateNoAssignmentWith2Parameters("Alu.Sub", params);
      case "CMPSB" -> generateCmps(params, 8);
      case "CMPSW" -> generateCmps(params, 16);
      case "CWD" -> generateCwd();
      case "DAS" -> "Cpu.Das();";
      case "DEC" -> generateAssignmentWith1Parameter("Alu.Dec", params, parameter1Bits);
      case "DIV" -> generateDiv(params, parameter1Bits);
      case "HLT" -> "Hlt()";
      case "IDIV" -> generateIDiv(params, parameter1Bits);
      case "IMUL" -> generateIMul(params, parameter1Bits);
      case "IN" -> generateAssignmentWith2ParametersOnlyOneOperand("Cpu.In", params, parameter1Bits);
      case "INC" -> generateAssignmentWith1Parameter("Alu.Inc", params, parameter1Bits);
      case "INSB", "INSW" -> generateIns(params, parameter1Bits);
      case "INT" -> generateInterrupt(params[0]);
      case "INT3" -> generateInterrupt("3");
      case "IRET" -> "return InterruptRet();";
      case "JA" -> jumpCallTranslator.generateJump("A", params[0], false);
      case "JBE" -> jumpCallTranslator.generateJump("BE", params[0], false);
      case "JC" -> jumpCallTranslator.generateJump("C", params[0], false);
      case "JCXZ" -> jumpCallTranslator.generateJump("CXZ", params[0], false);
      case "JG" -> jumpCallTranslator.generateJump("G", params[0], false);
      case "JGE" -> jumpCallTranslator.generateJump("GE", params[0], false);
      case "JL" -> jumpCallTranslator.generateJump("L", params[0], false);
      case "JLE" -> jumpCallTranslator.generateJump("LE", params[0], false);
      case "JMP" -> jumpCallTranslator.generateJump("", params[0], false);
      case "JMPF" -> jumpCallTranslator.generateJump("", params[0], true);
      case "JNC" -> jumpCallTranslator.generateJump("NC", params[0], false);
      case "JNS" -> jumpCallTranslator.generateJump("NS", params[0], false);
      case "JNO" -> jumpCallTranslator.generateJump("NO", params[0], false);
      case "JNP" -> jumpCallTranslator.generateJump("NP", params[0], false);
      case "JNZ" -> jumpCallTranslator.generateJump("NZ", params[0], false);
      case "JO" -> jumpCallTranslator.generateJump("O", params[0], false);
      case "JS" -> jumpCallTranslator.generateJump("S", params[0], false);
      case "JP" -> jumpCallTranslator.generateJump("P", params[0], false);
      case "JZ" -> jumpCallTranslator.generateJump("Z", params[0], false);
      case "LAHF" -> "AH = (byte)FlagRegister;";
      case "LDS" -> generateLXS("DS", params);
      case "LEA" -> generateLea(params);
      case "LES" -> generateLXS("ES", params);
      case "LOCK", "NOP" -> "";
      case "LODSB" -> generateLods(params, 8);
      case "LODSW" -> generateLods(params, 16);
      case "LOOP" -> generateLoop("", params[0]);
      case "LOOPNZ" -> generateLoop("NZ", params[0]);
      case "LOOPZ" -> generateLoop("Z", params[0]);
      case "MOV" -> generateMov(params, parameter1Bits);
      case "MOVSB" -> generateMovs(params, 8);
      case "MOVSW" -> generateMovs(params, 16);
      case "MUL" -> generateMul(params, parameter1Bits);
      case "NEG" -> generateNeg(params, parameter1Bits);
      case "NOT" -> generateNot(params, parameter1Bits);
      case "OR" -> generateAssignmentWith2Parameters("Alu.Or", "|", params);
      case "OUT" -> generateNoAssignmentWith2Parameters("Cpu.Out", params);
      case "OUTSB", "OUTSW" -> generateOuts(params, parameter1Bits);
      case "POP" -> generatePop(params, parameter1Bits);
      case "POPA" -> """
          DI = Stack.Pop();
          SI = Stack.Pop();
          BP =Stack.Pop();
          // not restoring SP
          Stack.Pop();
          BX = Stack.Pop();
          DX = Stack.Pop();
          CX = Stack.Pop();
          AX = Stack.Pop();""";
      case "POPF" -> "FlagRegister = Stack.Pop();";
      case "PUSH" -> generatePush(params, parameter1Bits);
      case "PUSHA" -> generatePushA();
      case "PUSHF" -> "Stack.Push(FlagRegister);";
      case "RCL" -> generateAssignmentWith2Parameters("Alu.Rcl", null, params);
      case "RCR" -> generateAssignmentWith2Parameters("Alu.Rcr", null, params);
      case "RET" -> "return NearRet();";
      case "RETF" -> "return FarRet();";
      case "ROL" -> generateAssignmentWith2Parameters("Alu.Rol", null, params);
      case "ROR" -> generateAssignmentWith2Parameters("Alu.Ror", null, params);
      case "SAR" -> generateAssignmentWith2Parameters("Alu.Sar", null, params);
      case "SBB" -> generateAssignmentWith2Parameters("Alu.Sbb", null, params);
      case "SCASB" -> generateScas(params, 8);
      case "SCASW" -> generateScas(params, 16);
      case "SHL" -> generateAssignmentWith2Parameters("Alu.Shl", "<<", params);
      case "SHR" -> generateAssignmentWith2Parameters("Alu.Shr", ">>", params);
      case "STC" -> "CarryFlag = true;";
      case "STD" -> "DirectionFlag = true;";
      case "STI" -> "InterruptFlag = true;";
      case "STOSB" -> generateStos(params, 8);
      case "STOSW" -> generateStos(params, 16);
      case "SUB" -> generateAssignmentWith2Parameters("Alu.Sub", "-", params);
      case "TEST" -> generateNoAssignmentWith2Parameters("Alu.And", params);
      case "XCHG" -> generateXchg(params, parameter1Bits);
      case "XLAT" -> generateXlat(params);
      case "XOR" -> generateXor(params);
      default -> null;
    };
  }

  public static String generateFailAsUntested(String message, boolean quotes) {
    String quote = quotes ? "\"" : "";
    return "throw FailAsUntested(" + quote + message + quote + ");";
  }
}

class JumpCallTranslator {
  private final Log log;
  private final GhidraScript ghidraScript;
  private final ParsedProgram parsedProgram;
  private final ParameterTranslator parameterTranslator;
  private final ParsedInstruction parsedInstruction;
  private final SegmentedAddress instructionSegmentedAddress;
  private final ParsedFunction currentFunction;

  public JumpCallTranslator(Log log, GhidraScript ghidraScript, ParameterTranslator parameterTranslator,
      ParsedProgram parsedProgram, ParsedFunction currentFunction, ParsedInstruction parsedInstruction) {
    this.log = log;
    this.ghidraScript = ghidraScript;
    this.parsedProgram = parsedProgram;
    this.currentFunction = currentFunction;
    this.parameterTranslator = parameterTranslator;
    this.parsedInstruction = parsedInstruction;
    this.instructionSegmentedAddress = parsedInstruction.getInstructionSegmentedAddress();
  }

  public String getLabel() {
    if (hasGhidraLabel() || this.parsedProgram.getJumpsAndCalls()
        .getJumpTargets()
        .contains(this.instructionSegmentedAddress)) {
      return getLabelToAddress(this.instructionSegmentedAddress, true) + "\n";
    }
    return "";
  }

  private boolean hasGhidraLabel() {
    Symbol label = ghidraScript.getSymbolAt(ghidraScript.toAddr(this.instructionSegmentedAddress.toPhysical()));
    return label != null && (label.getSymbolType() == SymbolType.LABEL || label.getSymbolType() == SymbolType.FUNCTION);
  }

  public static String getLabelToAddress(SegmentedAddress address, boolean colon) {
    String colonString = colon ? ":" : "";
    return "label_" + Utils.toHexSegmentOffsetPhysical(address) + colonString;
  }

  private String generateJumpCondition(String condition) {
    return switch (condition) {
      case "A" -> "!CarryFlag && !ZeroFlag";
      case "BE" -> "CarryFlag || ZeroFlag";
      case "C" -> "CarryFlag";
      case "CXZ" -> "CX == 0";
      case "G" -> "!ZeroFlag && SignFlag == OverflowFlag";
      case "GE" -> "SignFlag == OverflowFlag";
      case "L" -> "SignFlag != OverflowFlag";
      case "LE" -> "ZeroFlag || SignFlag != OverflowFlag";
      case "NC" -> "!CarryFlag";
      case "NO" -> "!OverflowFlag";
      case "NS" -> "!SignFlag";
      case "NP" -> "!ParityFlag";
      case "NZ" -> "!ZeroFlag";
      case "O" -> "OverflowFlag";
      case "S" -> "SignFlag";
      case "P" -> "ParityFlag";
      case "Z" -> "ZeroFlag";
      default -> "UNHANDLED CONDITION " + condition;
    };
  }

  public String generateJump(String condition, String param, boolean far) {
    if (!condition.isEmpty()) {
      String res = "if(" + generateJumpCondition(condition) + ") {\n";
      res += Utils.indent(generateJump(param, far), 2) + "\n";
      res += "}";
      return res;
    }
    return generateJump(param, far);
  }

  private List<SegmentedAddress> getTargetsOfJumpCall() {
    return this.parsedProgram.getJumpsAndCalls()
        .getCallsJumpsFromTo()
        .get(this.instructionSegmentedAddress.toPhysical());
  }

  public String generateJump(String param, boolean far) {
    if (!param.startsWith("0x")) {
      // Indirect address ...
      List<SegmentedAddress> targets = getTargetsOfJumpCall();
      List<String> res = new ArrayList<>();
      res.add("// Indirect jump to " + param + ", generating possible targets from emulator records");
      res.add(generateSwitchToIndirectTarget(param, far, targets,
          "Error: Jump not registered at address ", this::jumpToCaseBody));
      return Utils.joinLines(res);
    }
    SegmentedAddress target = readJumpCallTargetFromInstruction(far);
    log.info("Jump target is " + target);
    return generateGoto(target);
  }

  private SegmentedAddress readJumpCallTargetFromInstruction(boolean far) {
    // Generating jump target from instruction bytes and not from ghidra listing as it doesnt work well for multiple segments.
    if (far) {
      return new SegmentedAddress(parsedInstruction.getParameter2(), parsedInstruction.getParameter1());
    } else {
      // instruction length needed because offset is from the next instruction
      int instructionLength = parsedInstruction.getInstructionLength();
      int targetSegment = instructionSegmentedAddress.getSegment();
      Integer signedOffset = parsedInstruction.getParameter1Signed();
      if (signedOffset == null) {
        log.info("Error: expected a signed offset for instruction " + parsedInstruction);
      }
      int targetOffset = Utils.uint16(
          instructionSegmentedAddress.getOffset() + instructionLength + signedOffset);
      return new SegmentedAddress(targetSegment, targetOffset);
    }
  }

  private String jumpToCaseBody(SegmentedAddress address) {
    String res = generateGoto(address);
    if (res.startsWith("goto label_")) {
      res += "\nbreak;";
    }
    return res;
  }

  private String attemptConvertJumpToFunctionCall(ParsedFunction function, Integer internalJumpAddress) {
    String nonEntry = internalJumpAddress == null ? "entry " : "non entry ";
    if (function == null) {
      log.info("Could not convert jump to " + nonEntry + "call because function at target is null");
    } else if (function.equals(this.currentFunction)) {
      log.info(
          "Could not convert jump to " + nonEntry + "call because function at target is the current function "
              + function.getName());
    } else {
      log.info("Converted jump to call");
      // If a function is found, change the jump to a function call
      return "// Jump converted to " + nonEntry + "function call\n" + functionToDirectCSharpCallWithReturn(function,
          internalJumpAddress);
    }
    return null;
  }

  private String inlineJumpOrRet(ParsedInstruction instruction) {
    String mnemonic = instruction.getMnemonic();
    if (instruction.isUnconditionalJump() || instruction.isRet()) {
      String comment = "// " + this.parsedInstruction.getMnemonic() + " target is " + mnemonic + ", inlining.\n";
      // Giving current function because the generated instruction is going to be generated in the function we currently are at
      InstructionGenerator generator =
          new InstructionGenerator(this.log, this.ghidraScript, this.parsedProgram, currentFunction, instruction,
              parameterTranslator.getGeneratedTempVars());
      return comment + generator.convertInstructionToSpice86(false);
    }
    return null;
  }

  private String generateGoto(SegmentedAddress target) {
    // check if target is a ret or another jump, and inline it if it is the case
    ParsedInstruction instruction = parsedProgram.getInstructionAtSegmentedAddress(target);
    if (instruction != null) {
      String inline = inlineJumpOrRet(instruction);
      if (inline != null) {
        return inline;
      }
    } else {
      String label = getLabelToAddress(target, false);
      return InstructionGenerator.generateFailAsUntested(
          "Would have been a goto but label " + label
              + " does not exist because no instruction was found there that belongs to a function.", true);
    }
    String convertedCall =
        attemptConvertJumpToFunctionCall(parsedProgram.getFunctionAtSegmentedAddressEntryPoint(target), null);
    if (convertedCall != null) {
      return convertedCall;
    }
    String convertedExtraCall =
        attemptConvertJumpToFunctionCall(parsedProgram.getFunctionAtSegmentedAddressAny(target), target.toPhysical());
    if (convertedExtraCall != null) {
      return convertedExtraCall;
    }
    log.info("Converting jump to regular goto");
    return "goto " + getLabelToAddress(target, false) + ";";
  }

  public String generateCall(String param, boolean far) {
    if (!param.startsWith("0x")) {
      // Indirect address ...
      List<SegmentedAddress> targets = getTargetsOfJumpCall();
      List<String> res = new ArrayList<>();
      res.add("// Indirect call to " + param + ", generating possible targets from emulator records");
      res.add(generateSwitchToIndirectTarget(param, far, targets,
          "Error: Function not registered at address ", address -> functionToCaseBody(address, far)));
      return Utils.joinLines(res);
    }
    SegmentedAddress target = readJumpCallTargetFromInstruction(far);
    log.info("Call target is " + target);
    ParsedFunction function = parsedProgram.getFunctionAtSegmentedAddressEntryPoint(target);
    if (function == null) {
      return noFunctionAtAddress(target);
    }
    return functionToString(function, far, false);
  }

  private String functionToCaseBody(SegmentedAddress address, boolean far) {
    ParsedFunction function = parsedProgram.getFunctionAtSegmentedAddressEntryPoint(address);
    if (function == null) {
      return noFunctionAtAddress(address);
    }
    return functionToString(function, far, false) + "\n    break;";
  }

  public String functionToString(ParsedFunction parsedFunction, boolean far, boolean withReturn) {
    String name = parsedFunction.getName();
    if (withReturn) {
      // Direct C# call, do not go through the Near/Far checks
      return functionToDirectCSharpCallWithReturn(parsedFunction, null);
    }
    String callType = far ? "Far" : "Near";
    int segment = this.parsedInstruction.getInstructionSegmentedAddress().getSegment();
    int offset = this.parsedInstruction.getInstructionSegmentedAddress().getOffset()
        + this.parsedInstruction.getInstructionLength();

    return callType + "Call(" + parsedProgram.getCodeSegmentVariables().get(segment) + ", " + Utils.toHexWith0X(offset)
        + ", " + name + ");";
  }

  public String functionToDirectCSharpCallWithReturn(ParsedFunction parsedFunction, Integer internalJumpAddress) {
    String internalJumpAddressString = "0";
    if (internalJumpAddress != null) {
      internalJumpAddressString = Utils.toHexWith0X(internalJumpAddress) + " - cs1 * 0x10";
    }
    return "return " + parsedFunction.getName() + "(" + internalJumpAddressString + ");";
  }

  private String noFunctionAtAddress(SegmentedAddress address) {
    return InstructionGenerator.generateFailAsUntested("Could not find a valid function at address " + address, true);
  }

  private String generateSwitchToIndirectTarget(String expression, boolean far, List<SegmentedAddress> targets,
      String errorInCaseNotFound, java.util.function.Function<SegmentedAddress, String> toCSharp) {
    String target = far ?
        parameterTranslator.toCsIpPointerValueInMemory(expression) :
        parameterTranslator.toIpPointerValueInMemory(expression);
    String tempVarName = parameterTranslator.generateTempVar("targetAddress_");
    if (target.contains("cs1")) {
      target = target.replaceAll("cs1 \\* 0x10 \\+ ", "");
    } else {
      target += " - cs1 * 0x10";
    }
    StringBuilder res = new StringBuilder("uint " + tempVarName + " = (uint)(" + target + ");\n");
    res.append("switch(" + tempVarName + ") {\n");
    if (targets != null) {
      for (SegmentedAddress targetFromRecord : targets) {
        res.append(
            "  case " + Utils.toHexWith0X(targetFromRecord.toPhysical() - parsedProgram.getEntryAddress()) + " : "
                + toCSharp.apply(targetFromRecord)
                + "\n");
      }
    }
    String failAsUntested =
        InstructionGenerator.generateFailAsUntested("\"" + errorInCaseNotFound + "\" + " + tempVarName, false);
    res.append("  default: " + failAsUntested + "\n" + "    break;\n" + "}");
    return res.toString();
  }
}

/**
 * Classes below represent an interpretation of what ghidra saw. They are used by the generator as a basis for code generation.
 */
class ParsedProgram {
  private JumpsAndCalls jumpsAndCalls;
  private Map<Integer, ParsedFunction> instructionAddressToFunction = new HashMap<>();
  private Map<Integer, ParsedInstruction> instructionAddressToInstruction = new HashMap<>();
  private Map<SegmentedAddress, Set<SegmentedAddress>> jumpsFromOutsidePerFunction = new HashMap<>();
  // Sorted by entry address
  private Map<Integer, ParsedFunction> entryPoints = new TreeMap<>();
  private Map<Integer, String> codeSegmentVariables = new TreeMap<>();
  private int entryAddress;

  public static ParsedProgram createParsedProgram(Log log, List<ParsedFunction> functions,
      JumpsAndCalls jumpsAndCalls) {
    ParsedProgram res = new ParsedProgram();
    res.jumpsAndCalls = jumpsAndCalls;
    res.entryPoints.putAll(
        functions.stream().collect(Collectors.toMap(f -> f.getEntrySegmentedAddress().toPhysical(), f -> f)));
    for (ParsedFunction parsedFunction : functions) {
      // Map address of instruction -> function
      res.instructionAddressToFunction.putAll(
          mapFunctionByInstructionAddress(parsedFunction, parsedFunction.getInstructionsBeforeEntry()));
      res.instructionAddressToFunction.putAll(
          mapFunctionByInstructionAddress(parsedFunction, parsedFunction.getInstructionsAfterEntry()));
      // Map address of instruction -> instruction
      res.instructionAddressToInstruction.putAll(
          mapInstructionByInstructionAddress(parsedFunction.getInstructionsBeforeEntry()));
      res.instructionAddressToInstruction.putAll(
          mapInstructionByInstructionAddress(parsedFunction.getInstructionsAfterEntry()));
      int cs = parsedFunction.getEntrySegmentedAddress().getSegment();
      if (!res.codeSegmentVariables.containsKey(cs)) {
        String varName = "cs" + (res.codeSegmentVariables.size() + 1);
        res.codeSegmentVariables.put(cs, varName);
      }
      res.entryAddress = res.codeSegmentVariables.keySet().iterator().next() * 0x10;
      log.info("Entry point address is " + res.entryAddress);
    }
    // Map address of function -> Set of externally reachable labels
    for (Map.Entry<Integer, List<SegmentedAddress>> jumpFromTo : jumpsAndCalls.getJumpsFromTo().entrySet()) {
      Integer fromAddress = jumpFromTo.getKey();
      ParsedFunction fromFunction = res.getFunctionAtAddressAny(fromAddress);
      if (fromFunction == null) {
        log.warning("No function found at address " + fromAddress);
        continue;
      }
      for (SegmentedAddress toAddress : jumpFromTo.getValue()) {
        ParsedFunction toFunction = res.getFunctionAtSegmentedAddressAny(toAddress);
        if (toFunction == null) {
          log.warning("No target function found at address " + toAddress);
          continue;
        }
        if (toFunction.equals(fromFunction)) {
          // filter out self
          continue;
        }
        // At this point toAddress belongs to toFunction which is different from fromFunction
        log.info("Found an externally accessible label at address " + toAddress + ". This address belongs to function "
            + toFunction.getName());
        // Let's register it as externally targeted label
        Set<SegmentedAddress> jumpsFromOutsideForFunction =
            res.jumpsFromOutsidePerFunction.computeIfAbsent(toFunction.getEntrySegmentedAddress(),
                a -> new HashSet<>());
        jumpsFromOutsideForFunction.add(toAddress);
      }
    }
    return res;
  }

  private static Map<Integer, ParsedInstruction> mapInstructionByInstructionAddress(List<ParsedInstruction> list) {
    return list.stream()
        .collect(Collectors.toMap(i -> i.getInstructionSegmentedAddress().toPhysical(), i -> i));
  }

  private static Map<Integer, ParsedFunction> mapFunctionByInstructionAddress(ParsedFunction parsedFunction,
      List<ParsedInstruction> list) {
    return list.stream()
        .collect(Collectors.toMap(i -> i.getInstructionSegmentedAddress().toPhysical(), i -> parsedFunction));
  }

  public JumpsAndCalls getJumpsAndCalls() {
    return jumpsAndCalls;
  }

  public Set<SegmentedAddress> getJumpsFromOutsideForFunction(SegmentedAddress address) {
    return jumpsFromOutsidePerFunction.get(address);
  }

  public Map<Integer, ParsedFunction> getEntryPoints() {
    return entryPoints;
  }

  public Set<Integer> getInstructionAddresses() {
    return instructionAddressToInstruction.keySet();
  }

  public ParsedInstruction getInstructionAtAddress(int address) {
    return instructionAddressToInstruction.get(address);
  }

  public ParsedInstruction getInstructionAtSegmentedAddress(SegmentedAddress address) {
    return getInstructionAtAddress(address.toPhysical());
  }

  public ParsedFunction getFunctionAtSegmentedAddressAny(SegmentedAddress address) {
    return getFunctionAtAddressAny(address.toPhysical());
  }

  public ParsedFunction getFunctionAtGhidraAddressAny(Address address) {
    return getFunctionAtAddressAny((int)address.getUnsignedOffset());
  }

  public ParsedFunction getFunctionAtAddressAny(int address) {
    return instructionAddressToFunction.get(address);
  }

  public ParsedFunction getFunctionAtSegmentedAddressEntryPoint(SegmentedAddress address) {
    return getFunctionAtAddressEntryPoint(address.toPhysical());
  }

  public ParsedFunction getFunctionAtGhidraAddressEntryPoint(Address address) {
    return getFunctionAtAddressEntryPoint((int)address.getUnsignedOffset());
  }

  private ParsedFunction getFunctionAtAddressEntryPoint(int address) {
    return entryPoints.get(address);
  }

  public Map<Integer, String> getCodeSegmentVariables() {
    return codeSegmentVariables;
  }

  public int getEntryAddress() {
    return entryAddress;
  }
}

class JumpsAndCalls {
  @SerializedName("CallsFromTo")
  private Map<Integer, List<SegmentedAddress>> callsFromTo;
  @SerializedName("JumpsFromTo")
  private Map<Integer, List<SegmentedAddress>> jumpsFromTo;
  @SerializedName("RetsFromTo")
  private Map<Integer, List<SegmentedAddress>> retsFromTo;

  private Map<Integer, List<SegmentedAddress>> callsJumpsFromTo = new HashMap<>();
  private Set<SegmentedAddress> jumpTargets;

  public void init() {
    callsJumpsFromTo.putAll(callsFromTo);
    callsJumpsFromTo.putAll(jumpsFromTo);
    jumpTargets = jumpsFromTo.values().stream().flatMap(Collection::stream).collect(Collectors.toSet());
  }

  public Map<Integer, List<SegmentedAddress>> getJumpsFromTo() {
    return jumpsFromTo;
  }

  public Map<Integer, List<SegmentedAddress>> getCallsJumpsFromTo() {
    return callsJumpsFromTo;
  }

  public Set<SegmentedAddress> getJumpTargets() {
    return jumpTargets;
  }
}

class ModRM {
  private int mode;
  private int registerIndex;
  private int registerMemoryIndex;
  private String defaultSegment;
  private String offset;

  public ModRM(int modRM, BytesReader bytesReader) {
    mode = (modRM >> 6) & 0b11;
    registerIndex = (modRM >>> 3) & 0b111;
    registerMemoryIndex = (modRM & 0b111);
    int disp = 0;
    if (mode == 1) {
      disp = bytesReader.nextUint8();
    } else if (mode == 2) {
      disp = bytesReader.nextUint16();
    }
    if (mode == 3) {
      // value at reg[memoryRegisterIndex] to be used instead of memoryAddress
      return;
    }
    boolean bpForRm6 = mode != 0;
    defaultSegment = computeDefaultSegment(bpForRm6);
    offset = computeOffset(bytesReader, bpForRm6, disp);
  }

  private String computeDefaultSegment(boolean bpForRm6) {
    // The default segment register is SS for the effective addresses containing a
    // BP index, DS for other effective addresses
    return switch (registerMemoryIndex) {
      case 0, 1, 4, 5, 7 -> "DS";
      case 2, 3 -> "SS";
      case 6 -> bpForRm6 ? "SS" : "DS";
      default -> null;
    };
  }

  private String computeOffset(BytesReader bytesReader, boolean bpForRm6, int disp) {
    String dispString = disp == 0 ? "" : " + " + Utils.toHexWith0X(disp);
    return switch (registerMemoryIndex) {
      case 0 -> "BX + SI" + dispString;
      case 1 -> "BX + DI" + dispString;
      case 2 -> "BP + SI" + dispString;
      case 3 -> "BP + DI" + dispString;
      case 4 -> "SI" + dispString;
      case 5 -> "DI" + dispString;
      case 6 -> bpForRm6 ? "BP" + dispString : Utils.toHexWith0X(bytesReader.nextUint16() + disp);
      case 7 -> "BX" + dispString;
      default -> null;
    };
  }

  public String getDefaultSegment() {
    return defaultSegment;
  }

  @Override
  public String toString() {
    return new Gson().toJson(this);
  }
}

class ParsedInstruction {
  private static final Set<Integer> STRING_OPCODES_CHECKING_ZERO_FLAG = new HashSet<>(
      Arrays.asList(0xA6, 0xA7, 0xAE, 0xAF));
  private static final Set<Integer> OPCODES_ON_8_BITS = new HashSet<>(Arrays.asList(
      0x00, 0x02, 0x04, 0x08, 0x0A, 0x0C, 0x10, 0x12, 0x14, 0x18, 0x1A, 0x1C, 0x20, 0x22, 0x24, 0x27, 0x28, 0x2A, 0x2C,
      0x2F, 0x30, 0x32, 0x34, 0x37, 0x38, 0x3A, 0x3C, 0x3F, 0x6A, 0x6B, 0x6C, 0x6E, 0x80, 0x82, 0x84, 0x86, 0x88, 0x8A,
      0xA0,
      0xA2, 0xA4, 0xA6, 0xA8, 0xAA, 0xAC, 0xAE, 0x6C, 0x6E, 0xB0, 0xB1, 0xB2, 0xB3,
      0xB4, 0xB5, 0xB6, 0xB7, 0xC0, 0xC6, 0xD0, 0xD2, 0xD4, 0xD5, 0xD7, 0xE4, 0xE6, 0xEC, 0xEE, 0xF6, 0xFE));
  private static final Set<Integer> OPCODES_ON_16_BITS = new HashSet<>(Arrays.asList(
      0x01, 0x03, 0x05, 0x06, 0x07, 0x09, 0x0B, 0x0D, 0x0E, 0x11, 0x13, 0x15, 0x16, 0x17, 0x19, 0x1B, 0x1D, 0x1E, 0x1F,
      0x21, 0x23, 0x25, 0x29, 0x2B, 0x2D, 0x31, 0x33, 0x35, 0x39, 0x3B, 0x3D, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46,
      0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
      0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x61, 0x68, 0x69, 0x6D, 0x6F, 0x81, 0x83, 0x85, 0x87, 0x89, 0x8B, 0x8C,
      0x8D, 0x8E, 0x8F, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x9C, 0x9D, 0xA1, 0xA3, 0xA9, 0xB8, 0xB9, 0xBA, 0xBB,
      0xBC, 0xBD, 0xBE, 0xBF, 0xC1, 0xC7, 0xD1, 0xD3, 0xE5, 0xE7, 0xED, 0xEF, 0xF7, 0xFF));
  private static final Set<Integer> PREFIXES_OPCODES = new HashSet<>(
      Arrays.asList(0x26, 0x2E, 0x36, 0x3E, 0x64, 0x65, 0xF0, 0xF2, 0xF3));
  private static final Map<Integer, String> SEGMENT_OVERRIDES = new HashMap<>();
  private static final Set<Integer> OPCODES_WITH_MODRM = new HashSet<>(
      Arrays.asList(0x00, 0x01, 0x02, 0x03, 0x08, 0x09, 0x0A, 0x0B, 0x10, 0x11, 0x12, 0x13, 0x18, 0x19, 0x1A, 0x1B,
          0x20, 0x21, 0x22, 0x23, 0x28, 0x29, 0x2A, 0x2B, 0x30, 0x31, 0x32, 0x33, 0x38, 0x39, 0x3A, 0x3B, 0x69, 0x6B,
          0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0xC0, 0xC1,
          0xC4, 0xC5, 0xC6, 0xC7, 0xD0, 0xD1, 0xD2, 0xD3, 0xD9, 0xDD, 0xF6, 0xF7, 0xFE, 0xFF));
  private static final Set<Integer> OPCODES_WITH_DIRECT_VALUE = new HashSet<>(
      Arrays.asList(0x04, 0x05, 0x0C, 0x0D, 0x14, 0x15, 0x1C, 0x1D, 0x24, 0x25, 0x2C, 0x2D, 0x34, 0x35, 0x3C, 0x3D,
          0x68, 0x69, 0x6A, 0x6B, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7D,
          0x7E, 0x7F, 0x80, 0x81, 0x82, 0x83, 0x9A, 0xA0, 0xA1, 0xA2, 0xA3, 0xA8, 0xA9, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4,
          0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xC1, 0xC2, 0xC6, 0xC7, 0xCA, 0xCD,
          0xD4, 0xD5, 0xDB, 0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xEB, 0xF6, 0xF7));

  static {
    SEGMENT_OVERRIDES.put(0x26, "ES");
    SEGMENT_OVERRIDES.put(0x2E, "CS");
    SEGMENT_OVERRIDES.put(0x36, "SS");
    SEGMENT_OVERRIDES.put(0x3E, "DS");
    SEGMENT_OVERRIDES.put(0x64, "FS");
    SEGMENT_OVERRIDES.put(0x65, "GS");
  }

  private transient Instruction instruction;
  private SegmentedAddress instructionSegmentedAddress;
  private Integer parameter1BitLength;
  private Integer parameter2BitLength;
  private List<Integer> prefixes = new ArrayList<>();
  private String segment;
  private int opCode;
  private Integer modRmByte;
  private ModRM modRM;
  private Integer parameter1;
  private Integer parameter1Signed;
  private Integer parameter2;
  private int instructionLength;
  private String prefix;
  private String mnemonic;
  private String[] parameters;

  public String getMnemonic() {
    return mnemonic;
  }

  public String getPrefix() {
    return prefix;
  }

  public String[] getParameters() {
    return parameters;
  }

  public Instruction getInstruction() {
    return instruction;
  }

  public SegmentedAddress getInstructionSegmentedAddress() {
    return instructionSegmentedAddress;
  }

  public Integer getParameter1BitLength() {
    return parameter1BitLength;
  }

  public Integer getParameter2BitLength() {
    return parameter2BitLength;
  }

  public List<Integer> getPrefixes() {
    return prefixes;
  }

  public String getSegment() {
    return segment;
  }

  public int getOpCode() {
    return opCode;
  }

  public Integer getModRmByte() {
    return modRmByte;
  }

  public ModRM getModRM() {
    return modRM;
  }

  public Integer getParameter1() {
    return parameter1;
  }

  public Integer getParameter1Signed() {
    return parameter1Signed;
  }

  public Integer getParameter2() {
    return parameter2;
  }

  public int getInstructionLength() {
    return instructionLength;
  }

  public boolean isStringCheckingZeroFlag() {
    return STRING_OPCODES_CHECKING_ZERO_FLAG.contains(opCode);
  }

  public boolean isUnconditionalJump() {
    return mnemonic.startsWith("JMP");
  }

  public boolean isRet() {
    return mnemonic.startsWith("RET");
  }

  public static ParsedInstruction parseInstruction(Log log, Instruction instruction,
      SegmentedAddress instructionAddress) {
    ParsedInstruction res = new ParsedInstruction();
    res.instruction = instruction;
    res.instructionSegmentedAddress = instructionAddress;

    String mnemonicWithPrefix = instruction.getMnemonicString();
    String[] mnemonicSplit = mnemonicWithPrefix.split("\\.");
    res.mnemonic = mnemonicSplit[0];
    res.prefix = "";
    if (mnemonicSplit.length > 1) {
      res.prefix = mnemonicSplit[1];
    }
    String representation = instruction.toString();
    res.parameters = representation.replaceAll(mnemonicWithPrefix, "").trim().split(",");

    byte[] bytes;
    try {
      bytes = instruction.getBytes();
      String s = "";
      for (int i = 0; i < bytes.length; i++) {
        s += String.format("%X", bytes[i]) + ", ";
      }
      log.info(
          "Instruction at address " + instructionAddress + " / " + Utils.toHexWith0X(
              instruction.getAddress().getUnsignedOffset()) + " bytes " + s);
    } catch (MemoryAccessException e) {
      log.info("Could not read instruction, caught " + e);
      return null;
    }
    res.instructionLength = bytes.length;
    BytesReader bytesReader = new BytesReader(bytes);
    int opCode = bytesReader.nextUint8();
    while (PREFIXES_OPCODES.contains(opCode)) {
      res.prefixes.add(opCode);
      if (SEGMENT_OVERRIDES.containsKey(opCode)) {
        res.segment = SEGMENT_OVERRIDES.get(opCode);
      }
      if (!bytesReader.hasNextUint8()) {
        log.warning("Instruction has prefix opcode but no opcode");
        return null;
      }
      opCode = bytesReader.nextUint8();

    }
    res.opCode = opCode;
    if (OPCODES_ON_8_BITS.contains(opCode)) {
      res.parameter1BitLength = 8;
    } else if (OPCODES_ON_16_BITS.contains(opCode)) {
      res.parameter1BitLength = 16;
    }
    if (opCode == 0x83) {
      //GRP1 operations with this opcode have param2 sign extended to match param1 which is 16 bits
      res.parameter2BitLength = 8;
    } else {
      res.parameter2BitLength = res.parameter1BitLength;
    }
    if (OPCODES_WITH_MODRM.contains(opCode)) {
      res.modRmByte = bytesReader.nextUint8();
      res.modRM = new ModRM(res.modRmByte, bytesReader);
      log.info("Instruction has modrm. modrm byte is " + res.modRmByte + " interpreted as " + res.modRM);
      if (res.segment == null) {
        // Only set it if not overriden by prefix
        res.segment = res.modRM.getDefaultSegment();
      }
    }
    int remainingLength = bytesReader.remaining();
    if (bytesReader.remaining() == 0) {
      return res;
    }
    if (!OPCODES_WITH_DIRECT_VALUE.contains(opCode)) {
      log.warning(
          "Opcode " + Utils.toHexWithout0X(opCode)
              + " is not supposed to have a direct value but instruction has trailing bytes.");
    }
    if (remainingLength == 1) {
      res.parameter1 = bytesReader.nextUint8();
      res.parameter1Signed = Utils.int8(res.parameter1);
    } else if (remainingLength == 2) {
      res.parameter1 = bytesReader.nextUint16();
      res.parameter1Signed = Utils.int16(res.parameter1);
    } else if (remainingLength == 4) {
      res.parameter1 = bytesReader.nextUint16();
      res.parameter2 = bytesReader.nextUint16();
    } else {
      log.warning("Found " + remainingLength + " trailing bytes, not supported.");
    }
    return res;
  }

  @Override
  public String toString() {
    return new Gson().toJson(this);
  }
}

class BytesReader {
  private final byte[] bytes;
  private int index;

  public BytesReader(byte[] bytes) {
    this.bytes = bytes;
  }

  int nextUint8() {
    return Utils.getUint8(bytes, index++);
  }

  int nextUint16() {
    int res = Utils.getUint16(bytes, index);
    index += 2;
    return res;
  }

  int remaining() {
    return bytes.length - index;
  }

  boolean hasNextUint8() {
    return remaining() >= 1;
  }

  boolean hasNextUint16() {
    return remaining() >= 2;
  }
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

class ParsedFunction {
  private final transient Function function;
  private final String name;
  private final SegmentedAddress entrySegmentedAddress;
  private final List<ParsedInstruction> instructionsBeforeEntry;
  private final List<ParsedInstruction> instructionsAfterEntry;

  public static ParsedFunction createParsedFunction(Log log, GhidraScript ghidraScript, Function function) {
    String name = function.getName();
    SegmentedAddress entrySegmentedAddress = extractAddress(name);
    long ghidraAddress = function.getEntryPoint().getUnsignedOffset();
    log.info(
        "Parsing function " + name + " at address " + entrySegmentedAddress + " / ghidra address " + Utils.toHexWith0X(
            ghidraAddress));
    if (entrySegmentedAddress == null) {
      log.warning("Could not determine segmented address for function entry point, aborting.");
      return null;
    }
    if (ghidraAddress != entrySegmentedAddress.toPhysical()) {
      log.warning(
          "Function entry point in ghidra is not the same as the one in spice86, this could be because ghidra created a function with the same name.");
      return null;
    }

    List<ParsedInstruction> instructionsBeforeEntry = new ArrayList<>();
    List<ParsedInstruction> instructionsAfterEntry = new ArrayList<>();
    boolean success = dispatchInstructions(log, ghidraScript, function, entrySegmentedAddress, instructionsBeforeEntry,
        instructionsAfterEntry);
    if (!success) {
      log.warning("Couldn't read the instructions for function " + name + ". Not generating code for it.");
      return null;
    }
    return new ParsedFunction(function, name, entrySegmentedAddress, instructionsBeforeEntry, instructionsAfterEntry);
  }

  private static SegmentedAddress extractAddress(String name) {
    String[] split = name.split("_");
    if (split.length < 4) {
      return null;
    }
    try {
      return new SegmentedAddress(Utils.parseHex(split[split.length - 3]), Utils.parseHex(split[split.length - 2]));
    } catch (NumberFormatException nfe) {
      return null;
    }
  }

  private static SegmentedAddress getInstructionAddress(Log log, Instruction instruction,
      SegmentedAddress entrySegmentedAddress) {
    long instructionAddress = instruction.getAddress().getUnsignedOffset();
    long entryAddress = entrySegmentedAddress.toPhysical();
    long delta = instructionAddress - entryAddress;
    long offset = entrySegmentedAddress.getOffset() + delta;
    if (offset < 0 || offset > 0xFFFF) {
      log.warning("Instruction outside of function segment. Function entry: " + entrySegmentedAddress
          + " / Instruction offset: " + offset);
      return null;
    }
    return new SegmentedAddress(entrySegmentedAddress.getSegment(), (int)offset);
  }

  /**
   * Dispatches the instruction of the given function to 2 lists, one for the instructions before the entry point and one for those after
   */
  private static boolean dispatchInstructions(Log log, GhidraScript ghidraScript, Function function,
      SegmentedAddress entrySegmentedAddress, List<ParsedInstruction> instructionsBeforeEntry,
      List<ParsedInstruction> instructionsAfterEntry) {
    AddressSetView body = function.getBody();
    // Functions can be split accross the exe, they are divided in ranges and typically the code will jump accross ranges.
    // Let's get a list of all the instructions of the function split between instructions that are before the entry and after the entry.
    for (AddressRange addressRange : body) {
      Address min = addressRange.getMinAddress();
      Address max = addressRange.getMaxAddress();
      Instruction instruction = ghidraScript.getInstructionAt(min);
      if (instruction == null) {
        log.warning(" Instruction at " + min + " is null");
        return false;
      }
      Instruction before;
      do {
        SegmentedAddress instructionAddress = getInstructionAddress(log, instruction, entrySegmentedAddress);
        if (instructionAddress == null) {
          return false;
        }
        ParsedInstruction parsedInstruction = ParsedInstruction.parseInstruction(log, instruction, instructionAddress);
        dispatchInstruction(parsedInstruction, entrySegmentedAddress, instructionsBeforeEntry, instructionsAfterEntry);
        log.info("Dispatched instruction " + parsedInstruction);
        before = instruction;
        instruction = instruction.getNext();
        if (instruction == null) {
          log.warning(" Instruction after " + before.getAddress() + " is null");
          return false;
        }
      } while (instruction.getAddress().compareTo(max) <= 0);
    }
    return true;
  }

  private static void dispatchInstruction(ParsedInstruction instruction, SegmentedAddress entry,
      List<ParsedInstruction> instructionsBeforeEntry,
      List<ParsedInstruction> instructionsAfterEntry) {
    if (instruction == null) {
      return;
    }
    SegmentedAddress instructionAddress = instruction.getInstructionSegmentedAddress();
    if (instructionAddress.compareTo(entry) < 0) {
      instructionsBeforeEntry.add(instruction);
    } else {
      instructionsAfterEntry.add(instruction);
    }
  }

  private ParsedFunction(Function function, String name, SegmentedAddress entrySegmentedAddress,
      List<ParsedInstruction> instructionsBeforeEntry, List<ParsedInstruction> instructionsAfterEntry) {
    this.function = function;
    this.name = name;
    this.entrySegmentedAddress = entrySegmentedAddress;
    this.instructionsBeforeEntry = instructionsBeforeEntry;
    this.instructionsAfterEntry = instructionsAfterEntry;
  }

  public String getName() {
    return name;
  }

  public SegmentedAddress getEntrySegmentedAddress() {
    return entrySegmentedAddress;
  }

  public List<ParsedInstruction> getInstructionsBeforeEntry() {
    return instructionsBeforeEntry;
  }

  public List<ParsedInstruction> getInstructionsAfterEntry() {
    return instructionsAfterEntry;
  }

  @Override
  public boolean equals(Object obj) {
    if (this == obj) {
      return true;
    }

    return (obj instanceof ParsedFunction other)
        && other.entrySegmentedAddress.equals(this.entrySegmentedAddress);
  }

  @Override
  public int hashCode() {
    return entrySegmentedAddress.hashCode();
  }

  @Override
  public String toString() {
    return new Gson().toJson(this);
  }
}

class SegmentedAddress implements Comparable<SegmentedAddress> {
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

class Utils {
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
}