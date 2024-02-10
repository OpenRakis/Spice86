namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// Represents a component that defines a BLASTER variable environnement to DOS, for Sound Blaster compatibility and auto-detection
/// </summary>
public interface IBlasterEnvVarProvider {
    /// <summary>
    /// The BLASTER string which specifies parameters such as the address, low IRQ, high IRQ, and DMA. <br/>
    /// For example: 'A220 I5 D1 H5 P330 T6'
    /// </summary>
    string BlasterString { get; }
}