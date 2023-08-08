namespace Spice86.Core.Emulator.CPU;

public enum SegmentRegisterIndex {
    /// <summary>
    /// The index of the ES (extra segment) register.
    /// </summary>
    EsIndex = 0,

    /// <summary>
    /// The index of the CS (code segment) register.
    /// </summary>
    CsIndex = 1,

    /// <summary>
    /// The index of the SS (stack segment) register.
    /// </summary>
    SsIndex = 2,

    /// <summary>
    /// The index of the DS (data segment) register.
    /// </summary>
    DsIndex = 3,


    /// <summary>
    /// The index of the FS register.
    /// </summary>
    FsIndex = 4,

    /// <summary>
    /// The index of the GS register.
    /// </summary>
    GsIndex = 5
}