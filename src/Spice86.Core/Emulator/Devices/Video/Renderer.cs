namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;

public class Renderer : IVgaRenderer {
    private readonly IMemoryDevice _memory;
    private readonly IVideoState _state;

    public Renderer(IVideoState state, IMemoryDevice memory) {
        _state = state;
        _memory = memory;
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public int BitsPerPixel { get; set; }
    public int Stride => BitsPerPixel * Width;
    public int Size => Stride * Height;

    public void Render(IntPtr bufferAddress, int size) {
        // int characterWidth = _state.SequencerRegisters.ClockingModeRegister.DotsPerClock;
        // int totalWidth = characterWidth * (_state.CrtControllerRegisters.HorizontalTotal + 5);
        // int horizontalDisplayEnd = characterWidth * (_state.CrtControllerRegisters.HorizontalDisplayEnd + 1);
        // int horizontalBlankStart = characterWidth * _state.CrtControllerRegisters.HorizontalBlankingStart;
        // // int horizontalBlankEnd = characterWidth * _state.CrtControllerRegisters.HorizontalBlankingEnd;
        // int horizontalRetraceStart = characterWidth * _state.CrtControllerRegisters.HorizontalSyncStart;
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
        // Marshal.Copy(frameBuffer, 0, bufferAddress, size);
    }
}