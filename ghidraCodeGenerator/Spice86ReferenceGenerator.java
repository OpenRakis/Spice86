import com.google.gson.Gson;
import com.google.gson.annotations.SerializedName;
import com.google.gson.reflect.TypeToken;
import com.google.gson.stream.JsonReader;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.symbol.RefType;
import ghidra.program.model.symbol.ReferenceManager;
import ghidra.program.model.symbol.SourceType;

import java.io.FileReader;
import java.io.IOException;
import java.lang.reflect.Type;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;

//Imports indirect jumps and calls destinations from spice86 dump file (https://github.com/OpenRakis/Spice86/)
//@author Kevin Ferrare kevinferrare@gmail.com
//@category Assembly
//@keybinding
//@menupath
//@toolbar
public class Spice86ReferenceGenerator extends GhidraScript {
  private final String baseFolder = "C:/tmp/dune/c/Cryogenic/src/Cryogenic/bin/Debug/net6.0/";

  @Override protected void run() throws Exception {
    JumpsAndCalls jumpsAndCalls =
        readJumpMapFromFile(baseFolder + "spice86dumpjumps.json");

    importReferences(jumpsAndCalls.getJumpsFromTo(), RefType.COMPUTED_JUMP);
    importReferences(jumpsAndCalls.getCallsFromTo(), RefType.COMPUTED_CALL);
  }

  private void importReferences(Map<Integer, List<SegmentedAddress>> fromTo, RefType refType) {
    ReferenceManager referenceManager = getCurrentProgram().getReferenceManager();
    fromTo.entrySet().stream().forEach(e -> {
          Address from = this.toAddr(e.getKey());
          if (referenceManager.hasReferencesFrom(from)) {
            // Do not overwrite
            return;
          }
          List<SegmentedAddress> toSegmentedAddresses = e.getValue();
          int index = 0;
          for (SegmentedAddress toSegmentedAddress : toSegmentedAddresses) {
            Address to = this.toAddr(toSegmentedAddress.toPhysical());
            referenceManager.addMemoryReference(from, to, refType, SourceType.IMPORTED, index);
            index++;
          }
        }
    );
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

  class JumpsAndCalls {
    @SerializedName("CallsFromTo")
    private Map<Integer, List<SegmentedAddress>> callsFromTo;
    @SerializedName("JumpsFromTo")
    private Map<Integer, List<SegmentedAddress>> jumpsFromTo;
    @SerializedName("RetsFromTo")
    private Map<Integer, List<SegmentedAddress>> retsFromTo;

    private Map<Integer, List<SegmentedAddress>> callsJumpsFromTo;
    private Set<SegmentedAddress> jumpTargets;

    public void init() {
      callsJumpsFromTo = new HashMap<>();
      callsJumpsFromTo.putAll(callsFromTo);
      callsJumpsFromTo.putAll(jumpsFromTo);
      jumpTargets = jumpsFromTo.values().stream().flatMap(Collection::stream).collect(Collectors.toSet());
    }

    public Map<Integer, List<SegmentedAddress>> getCallsJumpsFromTo() {
      return callsJumpsFromTo;
    }

    public Set<SegmentedAddress> getJumpTargets() {
      return jumpTargets;
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


