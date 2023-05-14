namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;

public interface IVideoMemory : IMemoryDevice {
    byte[,] Planes { get; }
}