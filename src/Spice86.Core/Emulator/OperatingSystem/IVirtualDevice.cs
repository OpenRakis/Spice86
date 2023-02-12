namespace Spice86.Core.Emulator.OperatingSystem;

public interface IVirtualDevice {
    public ushort Segment { get; set; }
    public ushort Offset { get; set; }
    public DeviceAttributes Attributes { get; set; }
    public ushort StrategyEntryPoint { get; set; }
    public ushort InterruptEntryPoint { get; set; }
}