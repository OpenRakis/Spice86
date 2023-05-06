namespace Spice86.Core.Emulator.Devices.Video;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

public class Renderer : IVgaRenderer {
    private readonly IVideoMemory _memory;
    private readonly IVideoState _state;
    private static bool _rendering;
    private readonly byte[] _frameBuffer = new byte[2000000];
    private readonly ILoggerService _logger;

    public Renderer(IVideoState state, IVideoMemory memory, ILoggerService loggerService) {
        _state = state;
        _memory = memory;
        _logger = loggerService.WithLogLevel(LogEventLevel.Verbose);
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public int BitsPerPixel { get; set; }
    public int Stride => BitsPerPixel * Width;
    public int Size => Stride * Height;

    public void Render(IntPtr bufferAddress, int size) {
        if (_rendering) {
            return;
        }
        _rendering = true;

        int characterWidth = _state.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        int horizontalDisplayEnd = _state.CrtControllerRegisters.HorizontalDisplayEnd + 1;
        int horizontalBlankStart = _state.CrtControllerRegisters.HorizontalBlankingStart;
        int horizontalRetraceStart = _state.CrtControllerRegisters.HorizontalSyncStart;
        int horizontalRetraceEnd = _state.CrtControllerRegisters.HorizontalSyncEndRegister.HorizontalSyncEnd;
        int horizontalBlankEnd = _state.CrtControllerRegisters.HorizontalBlankingEndValue;
        int totalWidth = _state.CrtControllerRegisters.HorizontalTotal + 4;
        int verticalDisplayEnd = _state.CrtControllerRegisters.VerticalDisplayEndValue;
        int verticalBlankStart = _state.CrtControllerRegisters.VerticalBlankingStartValue;
        int verticalSyncStart = _state.CrtControllerRegisters.VerticalSyncStartValue;
        int verticalSyncEnd = _state.CrtControllerRegisters.VerticalSyncEndRegister.VerticalSyncEnd;
        int verticalBlankEnd = _state.CrtControllerRegisters.VerticalBlankingEnd - 1;
        int totalHeight = _state.CrtControllerRegisters.VerticalTotalValue + 2;
        int verticalSize = _state.GeneralRegisters.MiscellaneousOutput.VerticalSize;

        // Skew controls the delay of enabling the display at the start of the line.
        int skew = _state.CrtControllerRegisters.HorizontalBlankingEndRegister.DisplayEnableSkew;

        _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
        _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = true;
        int startAddress = _state.CrtControllerRegisters.ScreenStartAddress;
        int destinationAddress = 0;

        int memoryAddressCounterIsCharacterClockDividedBy = _state.CrtControllerRegisters.CrtModeControlRegister.CountByTwo ? 2 : 1;
        bool horizontalBlanking = false;
        bool verticalBlanking = false;
        bool horizontalRetrace = false;
        bool overscan = false;
        int memoryAddress = startAddress;
        int previousRowMemoryAddress = memoryAddress;

        MemoryWidth memoryWidthMode;
        int offsetShift;
        if (_state.CrtControllerRegisters.UnderlineRowScanlineRegister.DoubleWordMode) {
            memoryWidthMode = MemoryWidth.DoubleWord;
            offsetShift = 4;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.ByteWordMode == ByteWordMode.Byte) {
            memoryWidthMode = MemoryWidth.Byte;
            offsetShift = 1;
        } else if (_state.CrtControllerRegisters.CrtModeControlRegister.AddressWrap) {
            memoryWidthMode = MemoryWidth.Word15;
            offsetShift = 2;
        } else {
            memoryWidthMode = MemoryWidth.Word13;
            offsetShift = 2;
        }

        /////////////////////////
        // Start Vertical loop //
        /////////////////////////
        for (uint rowScanCounter = 0; rowScanCounter < totalHeight; rowScanCounter++) {
            ///////////////////////////
            // Start Horizontal loop //
            ///////////////////////////
            for (int characterCounter = 0; characterCounter < totalWidth; characterCounter++) {
                if (characterCounter == skew) {
                    overscan = false;
                }
                if (characterCounter == horizontalDisplayEnd + skew) {
                    overscan = true;
                }
                if (characterCounter == horizontalBlankStart) {
                    horizontalBlanking = true;
                }
                if (characterCounter == horizontalRetraceStart) {
                    horizontalRetrace = true;
                }
                if (horizontalRetrace && horizontalRetraceEnd == (characterCounter & 0b11111)) {
                    horizontalRetrace = false;
                }
                if (horizontalBlanking && horizontalBlankEnd == (characterCounter & 0b111111)) {
                    horizontalBlanking = false;
                }
                _state.GeneralRegisters.InputStatusRegister1.DisplayDisabled = horizontalBlanking || verticalBlanking;
                if (rowScanCounter < verticalDisplayEnd && characterCounter < horizontalDisplayEnd) {
                    // draw
                    // Convert logical address to physical address.
                    ushort physicalAddress = memoryWidthMode switch {
                        MemoryWidth.Byte => (ushort)memoryAddress,
                        MemoryWidth.Word13 => (ushort)(memoryAddress << 1 & 0x1FFF8 | memoryAddress >> 13 & 0x4 | memoryAddress & ~0x1FFFF),
                        MemoryWidth.Word15 => (ushort)(memoryAddress << 1 & 0x1FFF8 | memoryAddress >> 15 & 0x4 | memoryAddress & ~0x1FFFF),
                        MemoryWidth.DoubleWord => (ushort)(memoryAddress << 2 & 0x3FFF0 | memoryAddress >> 14 & 0xC | memoryAddress & ~0x3FFFF),
                        _ => throw new InvalidOperationException($"Unknown memory width mode: {memoryWidthMode}")
                    };
                    if (!_state.CrtControllerRegisters.CrtModeControlRegister.CompatibilityModeSupport) {
                        // Use the row scan counter rather than the memory address counter for bit 13.
                        physicalAddress = (ushort)(physicalAddress & ~0x8000 | ((rowScanCounter & 1) != 0 ? 0x8000 : 0));
                    }
                    if (!_state.CrtControllerRegisters.CrtModeControlRegister.SelectRowScanCounter) {
                        // Use the row scan counter rather than the memory address counter for bit 14.
                        physicalAddress = (ushort)(physicalAddress & ~0x10000 | ((rowScanCounter & 2) != 0 ? 0x10000 : 0));
                    }
                    
                    // if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    //     _logger.Verbose("[{X:D3},{Y:D3}] Reading logical address 0x{LogicalAddress:X4} at physical address 0x{PhysicalAddress:X4}",
                    //         characterCounter, rowScanCounter, memoryAddress, physicalAddress);
                    // }

                    // Read 1 byte from each plane.
                    byte d0 = _memory.Planes[physicalAddress, 0];
                    byte d1 = _memory.Planes[physicalAddress, 1];
                    byte d2 = _memory.Planes[physicalAddress, 2];
                    byte d3 = _memory.Planes[physicalAddress, 3];
                    // if (_logger.IsEnabled(LogEventLevel.Verbose)) {
                    //     _logger.Verbose("[{X:D3},{Y:D3}]  0x{PhysicalAddress:X4} Data from memory: 0x{D0:X2} 0x{D1:X2} 0x{D2:X2} 0x{D3:X2}",
                    //         characterCounter, rowScanCounter,physicalAddress, d0, d1, d2,d3);
                    // }
                    // Loop through that byte and create an index from the 4 planes for each bit.
                    for (int i = 7; i >= 0; i--) {
                        int p0 = d0 >> i & 1;
                        int p1 = d1 >> i & 1;
                        int p2 = d2 >> i & 1;
                        int p3 = d3 >> i & 1;
                        int index = p3 << 3 | p2 << 2 | p1 << 1 | p0;
                        // Lookup that index in the palette.
                        var color = Color.FromArgb((int)_state.DacRegisters.ArgbPalette[index]);
                        // if (index != 0)
                        //     // Debugger.Break();
                        // Write the color to the frame buffer.
                        _frameBuffer[destinationAddress++] = color.R;
                        _frameBuffer[destinationAddress++] = color.G;
                        _frameBuffer[destinationAddress++] = color.B;
                        _frameBuffer[destinationAddress++] = color.A;
                    }
                    memoryAddress++;
                }
            } // End of X loop
            if (rowScanCounter == verticalDisplayEnd) {
                overscan = true;
            }
            if (rowScanCounter == verticalBlankStart) {
                verticalBlanking = true;
            }
            if (rowScanCounter == verticalSyncStart) {
                _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = true;
            }
            if (verticalSyncEnd == (rowScanCounter & 0b1111)) {
                _state.GeneralRegisters.InputStatusRegister1.VerticalRetrace = false;
            }
            if (verticalBlankEnd == (rowScanCounter & 0b11111111)) {
                verticalBlanking = false;
            }

            memoryAddress = previousRowMemoryAddress + (_state.CrtControllerRegisters.Offset << offsetShift);
            previousRowMemoryAddress = memoryAddress;
        } // End of Y loop

        //
        // Span<byte> vram = _memory.GetSpan((int)address, size);
        // int vramIndex = 0;
        // int pixelsIndex = 0;
        // for (int y = 0; y < Height; y++) {
        //     for (int x = 0; x < Width; x++) {
        //         byte colorIndex = vram[vramIndex++];
        //         var color = _state.GraphicsControllerRegisters.ReadPalette(colorIndex);
        //         frameBuffer[pixelsIndex++] = color.R;
        //         frameBuffer[pixelsIndex++] = color.G;
        //         frameBuffer[pixelsIndex++] = color.B;
        //     }
        // }
        Marshal.Copy(_frameBuffer, 0, bufferAddress, size);
        _rendering = false;
    }
}

public enum MemoryWidth {
    Byte,
    Word13,
    Word15,
    DoubleWord
}

public interface IVideoMemory : IMemoryDevice {
    byte[,] Planes { get; }
}