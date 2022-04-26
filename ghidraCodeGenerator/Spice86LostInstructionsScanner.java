import ghidra.app.script.GhidraScript;

//Finds instructions which were not tied to a function by Ghidra
//Should not happen, but...
//Use it to then tell Ghidra that it is part of a function
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86LostInstructionsScanner extends GhidraScript {
  //params, modify them before running this script.
  private int minAddress = 0x0;
  private int maxAddress = 0xFFFFF;

  @Override
  protected void run() throws Exception {
    //TODO ASAP
    }
  }
