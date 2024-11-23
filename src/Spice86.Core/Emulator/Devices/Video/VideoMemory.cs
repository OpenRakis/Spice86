namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;

using System.Diagnostics;

/// <summary>
///     A wrapper class for the video card that implements the IMemoryDevice interface.
/// </summary>
public class VideoMemory : IVideoMemory {
    private readonly byte[] _latches;
    private readonly IVideoState _state;

    /// <summary>
    ///     Create a new instance of the video memory.
    /// </summary>
    /// <param name="state">The interface that represents the state of the video card.</param>
    public VideoMemory(IVideoState state) {
        _state = state;
        Planes = new byte[4, 0x20000];
        _latches = new byte[4];
        Size = 0x20000;
    }

    /// <inheritdoc />
    public uint Size { get; }

    /// <inheritdoc />
    public byte Read(uint address) {
        (byte plane, uint offset) = DecodeReadAddress(address);
        _latches[0] = Planes[0, offset];
        _latches[1] = Planes[1, offset];
        _latches[2] = Planes[2, offset];
        _latches[3] = Planes[3, offset];
        byte result = 0;
        switch (_state.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode) {
            case ReadMode.ReadMode0:
                // result = (byte)(_latch >> (plane << 3));
                result = _latches[plane];
                break;
            case ReadMode.ReadMode1: {
                // Read mode 1 reads 8 pixels from the planes and compares each to a colorCompare register
                // If the color matches, the corresponding bit in the result is set
                // The colorDontCare bits indicate which bits in the colorCompare register to ignore.

                // We take the inverse of the colorDontCare register and OR both the colorCompare and
                // the extracted bits with them. This makes sure that the colorDontCare bits are always
                // considered a match.
                int colorDontCare = ~_state.GraphicsControllerRegisters.ColorDontCare;
                int colorCompare = _state.GraphicsControllerRegisters.ColorCompare | colorDontCare;
                // We loop through the 8 pixels in the latches, as well as the 8 bits in the result.
                for (int i = 0; i < 8; i++) {
                    // A pixel consists of 4 bits, one from each plane. We extract the bits from the
                    // latches and OR them together to get the pixel.
                    byte pixel = 0;
                    for (int j = 0; j < 4; j++) {
                        int bit = _latches[j] & 1 << i;
                        pixel |= (byte)(bit >> i - j);
                    }
                    // Then we compare the pixel to the colorCompare register, and set the corresponding
                    // bit in the result if they match.
                    if ((pixel | colorDontCare) == colorCompare) {
                        result |= (byte)(1 << i);
                    }
                }
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown readMode {_state.GraphicsControllerRegisters.GraphicsModeRegister.ReadMode}");
        }

        return result;
    }

    /// <inheritdoc />
    public void Write(uint address, byte value) {
        (byte planes, uint offset) = DecodeWriteAddress(address);
        bool[] writePlane = planes.ToBits();
        Register8 planeEnable = _state.SequencerRegisters.PlaneMaskRegister;
        Register8 setReset = _state.GraphicsControllerRegisters.SetReset;
        Register8 setResetEnable = _state.GraphicsControllerRegisters.EnableSetReset;
        switch (_state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode) {
            case WriteMode.WriteMode0:
                HandleWriteMode0(value, planeEnable, writePlane, setResetEnable, setReset, offset);
                break;
            case WriteMode.WriteMode1:
                HandleWriteMode1(planeEnable, writePlane, offset);
                break;
            case WriteMode.WriteMode2:
                HandleWriteMode2(value, planeEnable, writePlane, offset);
                break;
            case WriteMode.WriteMode3:
                HandleWriteMode3(value, planeEnable, writePlane, setReset, offset);
                break;
            default:
                throw new InvalidOperationException($"Unknown writeMode {_state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode}");
        }
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int address, int length) {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public byte[,] Planes { get; }

    private void HandleWriteMode3(byte value, Register8 planeEnable, bool[] writePlane, Register8 setReset, uint offset) {
        value.Ror(_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount);
        byte bitMask = (byte)(value & _state.GraphicsControllerRegisters.BitMask);
        for (int plane = 0; plane < 4; plane++) {
            if (!planeEnable[plane] || !writePlane[plane]) {
                continue;
            }
            // Apply set/reset logic.
            byte tempValue = (byte)(setReset[plane] ? 0xFF : 0x00);

            // Apply bitmask. (0 = use latch, 1 = use value)
            tempValue &= bitMask;
            tempValue |= (byte)(_latches[plane] & ~bitMask);
            // write the data
            Planes[plane, offset] = tempValue;
        }
    }

    private void HandleWriteMode2(byte value, Register8 planeEnable, bool[] writePlane, uint offset) {
        bool[] unpacked = value.ToBits();
        for (int plane = 0; plane < 4; plane++) {
            // Skip if plane is disabled or we're not writing to it.
            if (!planeEnable[plane] || !writePlane[plane]) {
                continue;
            }
            // Apply set/reset logic.
            byte tempValue = (byte)(unpacked[plane] ? 0xFF : 0x00);
            tempValue = ApplyAluFunction(tempValue, plane);
            // Apply bitmask. (0 = use latch, 1 = use value)
            tempValue &= _state.GraphicsControllerRegisters.BitMask;
            tempValue |= (byte)(_latches[plane] & ~_state.GraphicsControllerRegisters.BitMask);
            // write the data
            Planes[plane, offset] = tempValue;
        }
    }

    private void HandleWriteMode1(Register8 planeEnable, ReadOnlySpan<bool> writePlane, uint offset) {
        // Foreach plane
        for (int plane = 0; plane < 4; plane++) {
            if (planeEnable[plane] && writePlane[plane]) {
                Planes[plane, offset] = _latches[plane];
            }
        }
    }

    private void HandleWriteMode0(byte value, Register8 planeEnable,
        ReadOnlySpan<bool> writePlane, Register8 setResetEnable, Register8 setReset, uint offset) {
        Debug.Assert(offset < 0x10000);
        if (_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount != 0) {
            value.Ror(_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount);
        }
        // Foreach plane
        for (int plane = 0; plane < 4; plane++) {
            // Skip if plane is disabled or we're not writing to it.
            if (!planeEnable[plane] || !writePlane[plane]) {
                continue;
            }
            byte tempValue = value;

            // Apply set/reset logic.
            if (setResetEnable[plane]) {
                tempValue = (byte)(setReset[plane] ? 0xFF : 0x00);
            }
            tempValue = ApplyAluFunction(tempValue, plane);
            // Apply bitmask. (0 = use latch, 1 = use value)
            tempValue &= _state.GraphicsControllerRegisters.BitMask;
            tempValue |= (byte)(_latches[plane] & ~_state.GraphicsControllerRegisters.BitMask);
            // write the data
            Planes[plane, offset] = tempValue;
        }
    }

    private byte ApplyAluFunction(byte tempValue, int plane) {
        // Apply ALU function
        switch (_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect) {
            case FunctionSelect.None:
                break;
            case FunctionSelect.And:
                tempValue &= _latches[plane];
                break;
            case FunctionSelect.Or:
                tempValue |= _latches[plane];
                break;
            case FunctionSelect.Xor:
                tempValue ^= _latches[plane];
                break;
            default:
                throw new InvalidOperationException($"Unknown functionSelect {_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect}");
        }
        return tempValue;
    }

    private (byte plane, uint offset) DecodeReadAddress(uint address) {
        byte plane;
        uint offset = address - _state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;
        // read chain 4 memory
        if (_state.SequencerRegisters.MemoryModeRegister.Chain4Mode) {
            plane = (byte)(offset & 3);
            offset &= ~3u;
        }
        // read odd/even memory
        else if (_state.SequencerRegisters.MemoryModeRegister.OddEvenMode) {
            plane = (byte)(offset & 1);
            if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven) {
                int highOrderBitShift = _state.SequencerRegisters.MemoryModeRegister.ExtendedMemory ? 16 : 14;
                uint mask = (1u << highOrderBitShift) - 2u;
                uint highOrderBit = offset >> highOrderBitShift & 1u;
                offset &= mask + highOrderBit;
            } else if (_state.GeneralRegisters.MiscellaneousOutput.EvenPageSelect) {
                offset &= 0xFFFE;
            } else {
                offset |= 1;
            }
        }
        // read planar memory
        else {
            plane = _state.GraphicsControllerRegisters.ReadMapSelectRegister.PlaneSelect;
        }
        return (plane, offset);
    }

    private (byte planes, uint offset) DecodeWriteAddress(uint address) {
        byte planes;
        uint offset = address - _state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.BaseAddress;
        // chain 4 memory
        if (_state.SequencerRegisters.MemoryModeRegister.Chain4Mode) {
            // write to which plane?
            planes = (byte)(1 << (int)(offset & 3));
            offset &= ~3u;
        }
        // odd/even memory
        else if (_state.SequencerRegisters.MemoryModeRegister.OddEvenMode) {
            // Select odd or even planes
            planes = (byte)((offset & 1) == 0 ? 0b0101 : 0b1010);
            if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven) {
                int highOrderBitShift = _state.SequencerRegisters.MemoryModeRegister.ExtendedMemory ? 16 : 14;
                uint mask = (1u << highOrderBitShift) - 2u;
                uint highOrderBit = offset >> highOrderBitShift & 1u;
                offset &= mask + highOrderBit;
            } else if (_state.GeneralRegisters.MiscellaneousOutput.EvenPageSelect) {
                offset &= 0xFFFE;
            } else {
                offset |= 1;
            }
        }
        // planar memory
        else {
            // write to all planes
            planes = 0b1111;
        }
        return (planes, offset);
    }
}