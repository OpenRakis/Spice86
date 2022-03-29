import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;
import com.google.gson.reflect.TypeToken;
import com.google.gson.stream.JsonReader;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.listing.FunctionIterator;
import ghidra.program.model.listing.Listing;
import ghidra.program.model.listing.Program;
import ghidra.program.model.symbol.RefType;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.program.model.symbol.SourceType;
import ghidra.util.exception.DuplicateNameException;
import ghidra.util.exception.InvalidInputException;

import java.io.FileReader;
import java.io.IOException;
import java.lang.reflect.Type;
import java.util.*;
import java.util.stream.Collectors;
import java.util.stream.StreamSupport;

//Imports indirect jumps and calls destinations from spice86 dump file (https://github.com/OpenRakis/Spice86/)
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86TentativeFunctionRenamer extends GhidraScript {
  //private final String baseFolder = "C:/tmp/dune/c/Cryogenic/src/Cryogenic/bin/Debug/net6.0/";
  private final String baseFolder = "C:/tmp/Cryogenic/src/Cryogenic/bin/Release/net6.0/";
  private final List<Integer> segments = Arrays.asList(0x1000, 0x334B, 0x5635, 0x563E);

  @Override
  protected void run() throws Exception {
    Program program = getCurrentProgram();
    Listing listing = program.getListing();
    FunctionIterator functionIterator = listing.getFunctions(true);
    while (functionIterator.hasNext()) {
      renameFunction(functionIterator.next());
    }
  }

  private void renameFunction(Function function) throws InvalidInputException, DuplicateNameException {
    String functionName = function.getName();
    if (!functionName.startsWith("FUN_")) {
      return;
    }
    println("processing " + functionName + " at address " + Utils.toHexWith0X(
        (int)function.getEntryPoint().getUnsignedOffset()));
    SegmentedAddress address = getAddress(function);
    String name = "not_observed_" + Utils.toHexSegmentOffsetPhysical(address);
    function.setName(name, SourceType.USER_DEFINED);
  }

  private SegmentedAddress getAddress(Function function) {
    int entryPointAddress = (int)function.getEntryPoint().getUnsignedOffset();
    int segment = guessSegment(entryPointAddress);
    int offset = entryPointAddress - segment * 0x10;
    return new SegmentedAddress(segment, offset);
  }

  private int guessSegment(int entryPointAddress) {
    int foundSegment = 0;
    for (int segment : segments) {
      if (entryPointAddress >= segment * 0x10) {
        println("OK for segment " + Utils.toHexWith0X(segment));
        foundSegment = segment;
      }
    }
    println("Found segment " + Utils.toHexWith0X(foundSegment));
    return foundSegment;
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
    public static String joinLines(List<String> res) {
      return String.join("\n", res);
    }

    public static String indent(String input, int indent) {
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
      return String.format("%04X_%04X_%06X", address.getSegment(), address.getOffset(), address.toPhysical());
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
  }
}


