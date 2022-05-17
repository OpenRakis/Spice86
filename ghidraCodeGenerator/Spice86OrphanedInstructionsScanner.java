import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.AddressRange;
import ghidra.program.model.address.AddressSetView;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Instruction;

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
    for(Instruction instruction:orphans) {
      long address = instruction.getAddress().getUnsignedOffset();
      println(Utils.toHexWith0X(address));
    }
    println("Found " + orphans.size() + " orphaned instructions:");
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

