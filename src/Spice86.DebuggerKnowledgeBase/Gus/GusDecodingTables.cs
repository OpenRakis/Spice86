namespace Spice86.DebuggerKnowledgeBase.Gus;

using System.Collections.Generic;

/// <summary>
/// Static lookup tables for the Gravis Ultrasound (GF1 / Interwave) knowledge base.
/// Names mirror the AMD/Gravis GF1 datasheet, the Gravis Ultrasound SDK headers and
/// dosbox-staging's <c>gus.cpp</c>.
/// </summary>
internal static class GusDecodingTables {
    /// <summary>
    /// Standard GUS card base I/O addresses. The card is configurable; software
    /// probes the well-known ones in this list. All offsets in
    /// <see cref="ClassifyOffset(int)"/> are relative to one of these bases.
    /// </summary>
    public static readonly IReadOnlyList<ushort> StandardBases = new ushort[] {
        0x210, 0x220, 0x230, 0x240, 0x250, 0x260, 0x270
    };

    /// <summary>
    /// Returns the symbolic name for a GF1 register selected via 2X3 / 3X3
    /// (the "Register Select" port). 0x00..0x0F are write/read; 0x80..0x8F are
    /// the read-only mirrors of 0x00..0x0F. 0x40..0x4F cover the GF1 control
    /// block (DMA, timer, sampling, reset).
    /// </summary>
    public static string LookupGf1Register(byte index) {
        switch (index) {
            // Per-voice registers (selected together with the page register at 2X2 / 3X2).
            case 0x00: return "Voice Control";
            case 0x01: return "Frequency Control";
            case 0x02: return "Start Address (high)";
            case 0x03: return "Start Address (low)";
            case 0x04: return "End Address (high)";
            case 0x05: return "End Address (low)";
            case 0x06: return "Volume Ramp Rate";
            case 0x07: return "Volume Ramp Start";
            case 0x08: return "Volume Ramp End";
            case 0x09: return "Current Volume";
            case 0x0A: return "Current Address (high)";
            case 0x0B: return "Current Address (low)";
            case 0x0C: return "Pan Position";
            case 0x0D: return "Volume Ramp Control";
            case 0x0E: return "Active Voices";
            case 0x0F: return "IRQ Source Register";

            // Global GF1 control block.
            case 0x41: return "DMA Control";
            case 0x42: return "DMA Start Address";
            case 0x43: return "DRAM I/O Address (low 16 bits)";
            case 0x44: return "DRAM I/O Address (high 4 bits)";
            case 0x45: return "Timer Control";
            case 0x46: return "Timer 1 Counter";
            case 0x47: return "Timer 2 Counter";
            case 0x49: return "Sampling Control";
            case 0x4B: return "Sampling Frequency";
            case 0x4C: return "Reset / Master IRQ Enable";

            // Read-only mirrors of 0x00..0x0F.
            case 0x80: return "Voice Control (read)";
            case 0x81: return "Frequency Control (read)";
            case 0x82: return "Start Address high (read)";
            case 0x83: return "Start Address low (read)";
            case 0x84: return "End Address high (read)";
            case 0x85: return "End Address low (read)";
            case 0x86: return "Volume Ramp Rate (read)";
            case 0x87: return "Volume Ramp Start (read)";
            case 0x88: return "Volume Ramp End (read)";
            case 0x89: return "Current Volume (read)";
            case 0x8A: return "Current Address high (read)";
            case 0x8B: return "Current Address low (read)";
            case 0x8C: return "Pan Position (read)";
            case 0x8D: return "Volume Ramp Control (read)";
            case 0x8E: return "Active Voices (read)";
            case 0x8F: return "IRQ Source (read, latched)";
        }
        return $"Unknown / Reserved (0x{index:X2})";
    }

    /// <summary>
    /// GUS port-offset classification. Each constant is the offset relative to the
    /// configured card base (e.g. 0x240). The "low window" 0x000..0x00F holds the
    /// mixer / IRQ-DMA / AdLib-compat block; the "GF1 window" 0x100..0x107 holds the
    /// MIDI UART, page/register select and DRAM I/O.
    /// </summary>
    public enum GusOffset {
        /// <summary>Not a known GUS offset.</summary>
        Unknown,
        /// <summary>2X0 — Mix Control Register (write).</summary>
        MixControl,
        /// <summary>2X6 — IRQ Status Register (read).</summary>
        IrqStatus,
        /// <summary>2X8 — AdLib-compatibility Timer Command/Data.</summary>
        AdlibTimerCommand,
        /// <summary>2X9 — AdLib-compatibility Timer Data.</summary>
        AdlibTimerData,
        /// <summary>2XA — AdLib-compatibility Command Register.</summary>
        AdlibCommand,
        /// <summary>2XB — IRQ/DMA Control Set (latched via 2XF).</summary>
        IrqDmaControlSet,
        /// <summary>2XF — Register Controls Select (which 2XB latch is targeted).</summary>
        RegisterControlsSelect,
        /// <summary>3X0 — MIDI Control Register.</summary>
        MidiControl,
        /// <summary>3X1 — MIDI Data Register.</summary>
        MidiData,
        /// <summary>3X2 — GF1 Page Register (selects active voice).</summary>
        Gf1Page,
        /// <summary>3X3 — GF1 Register Select.</summary>
        Gf1RegisterSelect,
        /// <summary>3X4 — GF1 Data Port low byte (16-bit data window).</summary>
        Gf1DataLow,
        /// <summary>3X5 — GF1 Data Port high byte.</summary>
        Gf1DataHigh,
        /// <summary>3X6 — DRAM I/O Address low byte.</summary>
        DramAddressLow,
        /// <summary>3X7 — DRAM I/O Address high byte (also DRAM data when sampling).</summary>
        DramAddressHigh
    }

    /// <summary>
    /// Returns the meaningful GUS offset for the given <paramref name="offset"/> from a
    /// card base, or <see cref="GusOffset.Unknown"/> if the offset is not a documented
    /// GUS register.
    /// </summary>
    public static GusOffset ClassifyOffset(int offset) {
        switch (offset) {
            case 0x000: return GusOffset.MixControl;
            case 0x006: return GusOffset.IrqStatus;
            case 0x008: return GusOffset.AdlibTimerCommand;
            case 0x009: return GusOffset.AdlibTimerData;
            case 0x00A: return GusOffset.AdlibCommand;
            case 0x00B: return GusOffset.IrqDmaControlSet;
            case 0x00F: return GusOffset.RegisterControlsSelect;
            case 0x100: return GusOffset.MidiControl;
            case 0x101: return GusOffset.MidiData;
            case 0x102: return GusOffset.Gf1Page;
            case 0x103: return GusOffset.Gf1RegisterSelect;
            case 0x104: return GusOffset.Gf1DataLow;
            case 0x105: return GusOffset.Gf1DataHigh;
            case 0x106: return GusOffset.DramAddressLow;
            case 0x107: return GusOffset.DramAddressHigh;
        }
        return GusOffset.Unknown;
    }
}
