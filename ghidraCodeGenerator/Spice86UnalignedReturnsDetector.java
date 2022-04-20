import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

//Attempts to detect and output return destinations that don't target instructions after a call.
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86UnalignedReturnsDetector extends GhidraScript {
  @Override
  protected void run() throws Exception {
    SymbolIterator it = this.currentProgram.getSymbolTable().getAllSymbols(true);
    int symbolsSeen = 0;
    int retTargetsSeen = 0;
    int unalignmentDetected = 0;
    for (Symbol symbol : it) {
      symbolsSeen++;
      String name = symbol.getName();
      if (name.contains("ret_target")) {
        retTargetsSeen++;
        if (processRetTargetAddress(symbol)) {
          unalignmentDetected++;
        }
      }
    }
    this.println("Saw " + symbolsSeen + " symbols, " + retTargetsSeen + " ret targets, " + unalignmentDetected
        + " unaligned returns");
  }

  private boolean processRetTargetAddress(Symbol symbol) {
    Address address = symbol.getAddress();
    String name = symbol.getName();
    Function function = this.getFunctionAt(address);
    if (function != null) {
      // return to function entry point, handled in spice86, no need to do anything
      return false;
    }
    Instruction instruction = this.getInstructionAt(address);
    if (instruction == null) {
      println("Warning: Instruction at " + name + " is null.");
      return false;
    }
    Instruction previous = instruction.getPrevious();
    if (previous == null || !previous.getMnemonicString().contains("CALL")) {
      println("Return target " + name + " looks like an unaligned return");
      return true;
    }
    return false;
  }
}
