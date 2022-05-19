import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.SourceType;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;

//Finds instructions which were not tied to a function by Ghidra
//Should not happen, but...
//Use it to then tell Ghidra that it is part of a function
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86OrphanedInstructionsScanner extends GhidraScript {
  private long minAddress = 0x0;
  private long maxAddress = 0xFFFFF;

  @Override
  protected void run() throws Exception {
    Map<Long, Long> rangeMap = new TreeMap<>();
    FunctionIterator functionIterator = this.getCurrentProgram().getListing().getFunctions(true);
    for (Function function : functionIterator) {
      AddressSetView body = function.getBody();
      for (AddressRange range : body) {
        rangeMap.put(range.getMinAddress().getUnsignedOffset(), range.getMaxAddress().getUnsignedOffset());
      }
    }
    List<Instruction> orphans = new ArrayList<>();
    for (long address = minAddress; address < maxAddress; ) {
      Long rangeEnd = rangeMap.get(address);
      if (rangeEnd != null) {
        println("Jumped over range " + Utils.toHexWith0X(address));
        address = rangeEnd + 1;
      } else {
        Instruction instruction = getInstructionAt(address);
        if (instruction != null) {
          orphans.add(instruction);
          println("Found instruction " + Utils.toHexWith0X(address));
          address += instruction.getLength();
        } else {
          //println("In the void " + address);
          address++;
        }
      }
    }
    Map<Integer, Integer> ranges = new HashMap<>();
    for (int i = 0; i < orphans.size(); ) {
      int rangeIndexStart = i;
      int rangeIndexEnd = i;
      while (isNextOrphanNextInstruction(orphans, ++i)) {
        rangeIndexEnd = i;
      }
      ranges.put(rangeIndexStart, rangeIndexEnd);
    }
    for (Map.Entry<Integer, Integer> range : ranges.entrySet()) {
      int rangeIndexStart = range.getKey();
      Instruction start = orphans.get(rangeIndexStart);
      int rangeIndexEnd = range.getValue();
      Instruction end = orphans.get(rangeIndexEnd);
      String rangeDescription = toInstructionAddress(start) + " -> " + toInstructionAddress(end);
      Function function = findFirstFunctionBeforeInstruction(start);

      if (function == null) {
        println("Did not find any function for range " + rangeDescription);
        continue;
      }
      String functionName = function.getName();
      println("Function " + functionName + " found for range " + rangeDescription+". Attempting to re-create it.");

      CreateFunctionCmd cmd = new CreateFunctionCmd("Re-create "+functionName, function.getEntryPoint(), null, SourceType.USER_DEFINED, true,
          true);
      boolean result = this.state.getTool().execute(cmd, this.getCurrentProgram());
      println("Re-creation attempt status: " + (result ? "success" : "failure"));
    }
    println("Found " + orphans.size() + " orphaned instructions spanning over " + ranges.size() + " ranges.");
  }

  private Function findFirstFunctionBeforeInstruction(Instruction instruction) {
    Instruction previous = instruction;
    while (previous != null) {
      Address address = previous.getAddress();
      Function res = this.getFunctionAt(address);
      if (res != null) {
        return res;
      }
      previous = previous.getPrevious();
    }
    return null;
  }

  private String toInstructionAddress(Instruction instruction) {
    return Utils.toHexWith0X(instruction.getAddress().getUnsignedOffset());
  }

  private boolean isNextOrphanNextInstruction(List<Instruction> orphans, int index) {
    if (index + 1 >= orphans.size()) {
      return false;
    }
    Instruction instruction = orphans.get(index);
    Instruction next = instruction.getNext();
    if (next == null) {
      return false;
    }
    Instruction nextOrphan = orphans.get(index + 1);

    return next.getAddress().getUnsignedOffset() == nextOrphan.getAddress().getUnsignedOffset();
  }

  Instruction getInstructionAt(Long address) {
    return this.getInstructionAt(this.toAddr(address));
  }

  class Utils {
    public static String toHexWith0X(long addressLong) {
      return String.format("0x%X", addressLong);
    }
  }
}

