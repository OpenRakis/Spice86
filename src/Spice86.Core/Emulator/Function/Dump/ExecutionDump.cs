namespace Spice86.Core.Emulator.Function.Dump;

using Spice86.Shared.Emulator.Memory;

public class ExecutionDump {
    /// <summary>
    /// Gets a dictionary of calls from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> CallsFromTo { get; set; } = new Dictionary<uint, HashSet<SegmentedAddress>>();

    /// <summary>
    /// Gets a dictionary of jumps from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> JumpsFromTo { get; set; } = new Dictionary<uint, HashSet<SegmentedAddress>>();

    /// <summary>
    /// Gets a dictionary of returns from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> RetsFromTo { get; set; } = new Dictionary<uint, HashSet<SegmentedAddress>>();

    /// <summary>
    /// Gets a dictionary of unaligned returns from one address to another.
    /// </summary>
    public IDictionary<uint, HashSet<SegmentedAddress>> UnalignedRetsFromTo { get; set; } = new Dictionary<uint, HashSet<SegmentedAddress>>();

    /// <summary>
    /// Gets the set of executed instructions.
    /// </summary>
    public HashSet<SegmentedAddress> ExecutedInstructions { get; set; } = new();

    /// <summary>
    /// Gets a dictionary of executable addresses written by modifying instructions.
    /// The key of the outer dictionary is the modified byte address.
    /// The value of the outer dictionary is a dictionary of modifying instructions, where the key is the instruction address and the value is a set of possible changes that the instruction did.
    /// </summary>
    public IDictionary<uint, IDictionary<uint, HashSet<ByteModificationRecord>>> ExecutableAddressWrittenBy { get; } = new Dictionary<uint, IDictionary<uint, HashSet<ByteModificationRecord>>>();
}