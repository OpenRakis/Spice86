namespace Spice86.Core.Emulator.OperatingSystem;

public abstract class VirtualDeviceBase : IVirtualDevice {
    protected VirtualDeviceBase(DeviceAttributes attributes, ushort strategy = 0, ushort interrupt = 0) {
        Attributes = attributes;
        StrategyEntryPoint = strategy;
        InterruptEntryPoint = interrupt;
    }

    public ushort Segment { get; set; }
    public ushort Offset { get; set; }
    public DeviceAttributes Attributes { get; set; }
    public ushort StrategyEntryPoint { get; set; }
    public ushort InterruptEntryPoint { get; set; }
}