using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Spice86.Aeon.Emulator.Video
{
    /// <summary>
    /// A 4-bit register value that can be expanded to a 4-byte mask.
    /// </summary>
    public readonly struct MaskValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MaskValue"/> struct.
        /// </summary>
        /// <param name="packed">The packed 4-bit value. Only the low four bits are considered.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MaskValue(byte packed) => Expanded = Unpack(packed) * 0xFFu;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator MaskValue(byte v) => new(v);

        /// <summary>
        /// Gets the value expanded out to four bytes.
        /// </summary>
        public uint Expanded { get; }
        /// <summary>
        /// Gets the original packed value.
        /// </summary>
        public byte Packed => (byte)Pack(Expanded);

        public override string ToString() => $"0x{Packed:x2}";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Unpack(uint v) {
            if (Bmi2.IsSupported)
                return Bmi2.ParallelBitDeposit(v, 0x01010101);
            return (v | (v << 7) | (v << 14) | (v << 21)) & 0x01010101u;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Pack(uint v) {
            if (Bmi2.IsSupported)
                return Bmi2.ParallelBitExtract(v, 0x01010101);
            return v & 0x1 | (v & 0x100) >> 7 | (v & 0x10000) >> 14 | (v & 0x1000000) >> 21;
        }
    }
}
