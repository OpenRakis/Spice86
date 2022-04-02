import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Bookmark;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionManager;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.Symbol;

import java.io.Closeable;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.util.Arrays;
import java.util.List;
import java.util.stream.StreamSupport;

//Clears all instructions between 2 ranges of addresses
//Useful when replacing chunks of bytes
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86InstructionDeleter extends GhidraScript {
  private int minAddress = 0x564f2;
  private int maxAddress = 0x57000;

  @Override
  protected void run() throws Exception {
    for (int address = minAddress; address <= maxAddress; address++) {
      Address ghidraAddress = this.toAddr(address);
      Bookmark[] bookmarks = this.currentProgram.getBookmarkManager().getBookmarks(ghidraAddress);
      if (bookmarks != null) {
        for (Bookmark bookmark : bookmarks) {
          this.removeBookmark(bookmark);
        }
      }
      this.removeInstructionAt(ghidraAddress);
    }

  }
}
