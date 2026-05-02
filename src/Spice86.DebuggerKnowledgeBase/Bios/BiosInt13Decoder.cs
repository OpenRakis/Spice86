namespace Spice86.DebuggerKnowledgeBase.Bios;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes BIOS INT 13h (disk services) calls. Mirrors <c>SystemBiosInt13Handler</c>.
/// </summary>
public sealed class BiosInt13Decoder : IInterruptDecoder {
    private const string Subsystem = "BIOS INT 13h";

    private static readonly IReadOnlyDictionary<byte, BiosFunctionEntry> ByAh = new Dictionary<byte, BiosFunctionEntry> {
        [0x00] = new BiosFunctionEntry("Reset Disk System", "Reset disk controller for drive DL."),
        [0x01] = new BiosFunctionEntry("Get Disk Status", "Return last operation status for drive DL in AH."),
        [0x02] = new BiosFunctionEntry("Read Sectors", "Read AL sectors at CHS into ES:BX."),
        [0x03] = new BiosFunctionEntry("Write Sectors", "Write AL sectors at CHS from ES:BX."),
        [0x04] = new BiosFunctionEntry("Verify Sectors", "Verify AL sectors at CHS on drive DL."),
        [0x05] = new BiosFunctionEntry("Format Track", "Format track CH on drive DL."),
        [0x08] = new BiosFunctionEntry("Get Drive Parameters", "Return geometry for drive DL."),
        [0x15] = new BiosFunctionEntry("Get Disk Type", "Return diskette/HDD type for drive DL.")
    };

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x13;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        BiosFunctionEntry entry;
        if (ByAh.TryGetValue(ah, out BiosFunctionEntry? known)) {
            entry = known;
        } else {
            entry = new BiosFunctionEntry($"AH={ah:X2}h (unknown)", "Unknown BIOS INT 13h sub-function.");
        }
        IReadOnlyList<DecodedParameter> parameters = DecodeParameters(ah, state);
        return new DecodedCall(Subsystem, $"AH={ah:X2}h {entry.Name}", entry.Description, parameters, []);
    }

    private static IReadOnlyList<DecodedParameter> DecodeParameters(byte ah, State state) {
        if (ah == 0x00 || ah == 0x01 || ah == 0x08 || ah == 0x15) {
            return [BiosParameter.Drive("drive", "DL", state.DL)];
        }
        if (ah == 0x02 || ah == 0x03 || ah == 0x04) {
            return [
                BiosParameter.Drive("drive", "DL", state.DL),
                BiosParameter.Decimal("sectors", "AL", state.AL),
                BiosParameter.Decimal("cylinder", "CH", state.CH),
                BiosParameter.Decimal("sector", "CL", state.CL),
                BiosParameter.Decimal("head", "DH", state.DH),
                BiosParameter.SegmentedPointer("buffer", "ES:BX", state.ES, state.BX)
            ];
        }
        if (ah == 0x05) {
            return [
                BiosParameter.Drive("drive", "DL", state.DL),
                BiosParameter.Decimal("track", "CH", state.CH),
                BiosParameter.Decimal("head", "DH", state.DH)
            ];
        }
        return [];
    }
}
