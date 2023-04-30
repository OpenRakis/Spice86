namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Devices.Video.Registers.Graphics;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
///     A wrapper class for the video card that implements the IMemoryDevice interface.
/// </summary>
public class VideoMemory : IMemoryDevice {
    private readonly uint _baseAddress;
    private readonly byte[] _latches;
    private readonly byte[][] _planes;
    private readonly IVideoState _state;
    private readonly IVideoCard _videoCard;

    public VideoMemory(uint size, IVideoCard videoCard, uint baseAddress, IVideoState state) {
        _videoCard = videoCard;
        _baseAddress = baseAddress;
        _state = state;
        _planes = new byte[4][];
        _planes[0] = new byte[0x10000];
        _planes[1] = new byte[0x10000];
        _planes[2] = new byte[0x10000];
        _planes[3] = new byte[0x10000];
        _latches = new byte[4];
        Size = 0x40000;
    }

    public uint Size { get; }

    public byte Read(uint address) {
        (byte plane, uint offset) = DecodeReadAddress(address);

        _latches[0] = _planes[0][offset];
        _latches[1] = _planes[1][offset];
        _latches[2] = _planes[2][offset];
        _latches[3] = _planes[3][offset];
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

    public void Write(uint address, byte value) {
        (byte planes, uint offset) = DecodeWriteAddress(address);
        bool[] writePlane = planes.ToBits();
        Register8 planeEnable = _state.SequencerRegisters.PlaneMaskRegister;
        Register8 setReset = _state.GraphicsControllerRegisters.SetReset;
        Register8 setResetEnable = _state.GraphicsControllerRegisters.EnableSetReset;
        switch (_state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode) {
            case WriteMode.WriteMode0:
                if (_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount != 0) {
                    value.Ror(_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount);
                }
                // Foreach plane
                for (int i = 0; i < 4; i++) {
                    // Skip if plane is disabled or we're not writing to it.
                    if (!planeEnable[i]) {
                        continue;
                    }
                    if (!writePlane[i]) {
                        continue;
                    }
                    // Apply set/reset logic.
                    if (setResetEnable[i]) {
                        value = (byte)(setReset[i] ? 0xFF : 0x00);
                    }
                    // Apply ALU function
                    switch (_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect) {
                        case FunctionSelect.None:
                            break;
                        case FunctionSelect.And:
                            value &= _latches[i];
                            break;
                        case FunctionSelect.Or:
                            value |= _latches[i];
                            break;
                        case FunctionSelect.Xor:
                            value ^= _latches[i];
                            break;
                        default:
                            throw new InvalidOperationException($"Unknown functionSelect {_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect}");
                    }
                    // Apply bitmask. (0 = use latch, 1 = use value)
                    value &= _state.GraphicsControllerRegisters.BitMask;
                    value |= (byte)(_latches[i] & ~_state.GraphicsControllerRegisters.BitMask);
                    // write the data
                    _planes[i][offset] = value;
                }
                break;
            case WriteMode.WriteMode1:
                // Foreach plane
                for (int i = 0; i < 4; i++) {
                    // Skip if plane is disabled or we're not writing to it.
                    if (!planeEnable[i] || !writePlane[i]) {
                        continue;
                    }
                    _planes[i][offset] = _latches[i];
                }
                break;
            case WriteMode.WriteMode2:
                // Foreach plane
                bool[] unpacked = value.ToBits();
                for (int i = 0; i < 4; i++) {
                    // Skip if plane is disabled or we're not writing to it.
                    if (!planeEnable[i] || !writePlane[i]) {
                        continue;
                    }
                    // Apply set/reset logic.
                    value = (byte)(unpacked[i] ? 0xFF : 0x00);
                    // Apply ALU function
                    switch (_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect) {
                        case FunctionSelect.None:
                            break;
                        case FunctionSelect.And:
                            value &= _latches[i];
                            break;
                        case FunctionSelect.Or:
                            value |= _latches[i];
                            break;
                        case FunctionSelect.Xor:
                            value ^= _latches[i];
                            break;
                        default:
                            throw new InvalidOperationException($"Unknown functionSelect {_state.GraphicsControllerRegisters.DataRotateRegister.FunctionSelect}");
                    }
                    // Apply bitmask. (0 = use latch, 1 = use value)
                    value &= _state.GraphicsControllerRegisters.BitMask;
                    value |= (byte)(_latches[i] & ~_state.GraphicsControllerRegisters.BitMask);
                    // write the data
                    _planes[i][offset] = value;
                }
                break;
            case WriteMode.WriteMode3:
                value.Ror(_state.GraphicsControllerRegisters.DataRotateRegister.RotateCount);
                byte bitMask = (byte)(value & _state.GraphicsControllerRegisters.BitMask);
                // Foreach plane
                for (int i = 0; i < 4; i++) {
                    // Skip if plane is disabled or we're not writing to it.
                    if (!planeEnable[i] || !writePlane[i]) {
                        continue;
                    }
                    // Apply set/reset logic.
                    value = (byte)(setReset[i] ? 0xFF : 0x00);

                    // Apply bitmask. (0 = use latch, 1 = use value)
                    value &= bitMask;
                    value |= (byte)(_latches[i] & ~bitMask);
                    // write the data
                    _planes[i][offset] = value;
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown writeMode {_state.GraphicsControllerRegisters.GraphicsModeRegister.WriteMode}");
        }
    }

    public Span<byte> GetSpan(int address, int length) {
        throw new NotSupportedException();
    }

    private (byte plane, uint offset) DecodeReadAddress(uint address) {
        byte plane;
        uint offset = address - _baseAddress;
        // read chain 4 memory
        if (_state.SequencerRegisters.MemoryModeRegister.Chain4Mode) {
            plane = (byte)(offset & 3);
            offset &= ~3u;
        }
        // read odd/even memory
        else if (_state.SequencerRegisters.MemoryModeRegister.OddEvenMode) {
            if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven) {
                offset &= 0xFFFE; // Make address even
                if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.MemoryMap == 0) {
                    if (_state.SequencerRegisters.MemoryModeRegister.ExtendedMemory) {
                        offset |= offset >> 16 & 1; //Bit 16 becomes bit 0
                    } else {
                        offset |= offset >> 14 & 1; //Bit 14 becomes bit 0
                    }
                } else if (_state.GeneralRegisters.MiscellaneousOutput.OddPageSelect) {
                    offset |= 1; // Make address odd
                }
            }
            plane = (byte)(offset & 1);
        }
        // read planar memory
        else {
            plane = _state.GraphicsControllerRegisters.ReadMapSelectRegister.PlaneSelect;
        }
        return (plane, offset);
    }

    private (byte planes, uint offset) DecodeWriteAddress(uint address) {
        byte planes;
        uint offset = address - _baseAddress;
        // chain 4 memory
        if (_state.SequencerRegisters.MemoryModeRegister.Chain4Mode) {
            // write to which plane?
            planes = (byte)(1 << (int)(offset & 3));
            offset &= ~3u;
        }
        // odd/even memory
        else if (_state.SequencerRegisters.MemoryModeRegister.OddEvenMode) {
            if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.ChainOddMapsToEven) {
                offset &= 0xFFFE; // Make address even
                if (_state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.MemoryMap == 0) {
                    if (_state.SequencerRegisters.MemoryModeRegister.ExtendedMemory) {
                        offset |= offset >> 16 & 1; //Bit 16 becomes bit 0
                    } else {
                        offset |= offset >> 14 & 1; //Bit 14 becomes bit 0
                    }
                } else if (_state.GeneralRegisters.MiscellaneousOutput.OddPageSelect) {
                    offset |= 1; // Make address odd
                }
            }
            // Select odd or even planes
            planes = (byte)((offset & 1) == 0 ? 0b0101 : 0b1010);
        }
        // planar memory
        else {
            // write to all planes
            planes = 0b1111;
        }
        return (planes, offset);
    }
}