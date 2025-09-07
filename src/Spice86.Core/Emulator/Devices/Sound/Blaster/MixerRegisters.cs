namespace Spice86.Core.Emulator.Devices.Sound.Blaster;

/// <summary>
/// This class contains the register addresses for the SoundBlaster mixer.
/// </summary>
public static class MixerRegisters {
    /// <summary>
    /// Reset mixer register.
    /// </summary>
    public const byte Reset = 0x00;

    /// <summary>
    /// Interrupt status register.
    /// </summary>
    public const byte InterruptStatus = 0x82;

    /// <summary>
    /// IRQ select register.
    /// </summary>
    public const byte IRQ = 0x80;

    /// <summary>
    /// DMA select register.
    /// </summary>
    public const byte DMA = 0x81;

    /// <summary>
    /// Master Volume register (SB Pro).
    /// </summary>
    public const byte MasterVolume = 0x22;

    /// <summary>
    /// DAC/Voice Volume register (SB Pro).
    /// </summary>
    public const byte DacVolume = 0x04;

    /// <summary>
    /// FM Volume register (SB Pro).
    /// </summary>
    public const byte FmVolume = 0x26;

    /// <summary>
    /// CD Audio Volume register (SB Pro).
    /// </summary>
    public const byte CdVolume = 0x28;

    /// <summary>
    /// Line-in Volume register (SB Pro).
    /// </summary>
    public const byte LineVolume = 0x2E;

    /// <summary>
    /// Mic Volume register (SB Pro).
    /// </summary>
    public const byte MicVolume = 0x0A;

    /// <summary>
    /// Output/Stereo Select register.
    /// </summary>
    public const byte OutputStereoSelect = 0x0E;

    // SB16-specific registers
    /// <summary>
    /// Master Volume Left register (SB16).
    /// </summary>
    public const byte MasterVolumeLeft = 0x30;

    /// <summary>
    /// Master Volume Right register (SB16).
    /// </summary>
    public const byte MasterVolumeRight = 0x31;

    /// <summary>
    /// DAC Volume Left register (SB16).
    /// </summary>
    public const byte DacVolumeLeft = 0x32;

    /// <summary>
    /// DAC Volume Right register (SB16).
    /// </summary>
    public const byte DacVolumeRight = 0x33;

    /// <summary>
    /// FM Volume Left register (SB16).
    /// </summary>
    public const byte FmVolumeLeft = 0x34;

    /// <summary>
    /// FM Volume Right register (SB16).
    /// </summary>
    public const byte FmVolumeRight = 0x35;

    /// <summary>
    /// CD Volume Left register (SB16).
    /// </summary>
    public const byte CdVolumeLeft = 0x36;

    /// <summary>
    /// CD Volume Right register (SB16).
    /// </summary>
    public const byte CdVolumeRight = 0x37;

    /// <summary>
    /// Line-in Volume Left register (SB16).
    /// </summary>
    public const byte LineVolumeLeft = 0x38;

    /// <summary>
    /// Line-in Volume Right register (SB16).
    /// </summary>
    public const byte LineVolumeRight = 0x39;

    /// <summary>
    /// Mic Volume register (SB16).
    /// </summary>
    public const byte MicVolumeSb16 = 0x3A;

    /// <summary>
    /// SB16 Advanced register 0x3B - PCM Level.
    /// </summary>
    public const byte Sb16PcmLevel = 0x3B;
    
    /// <summary>
    /// SB16 Advanced register 0x3C - Recording Monitor.
    /// </summary>
    public const byte Sb16RecordingMonitor = 0x3C;
    
    /// <summary>
    /// SB16 Advanced register 0x3D - Recording Source.
    /// </summary>
    public const byte Sb16RecordingSource = 0x3D;
    
    /// <summary>
    /// SB16 Advanced register 0x3E - Recording Gain.
    /// </summary>
    public const byte Sb16RecordingGain = 0x3E;
    
    /// <summary>
    /// SB16 Advanced register 0x3F - Recording Gain Left.
    /// </summary>
    public const byte Sb16RecordingGainLeft = 0x3F;
    
    /// <summary>
    /// SB16 Advanced register 0x40 - Recording Gain Right.
    /// </summary>
    public const byte Sb16RecordingGainRight = 0x40;
    
    /// <summary>
    /// SB16 Advanced register 0x41 - Output Filter.
    /// </summary>
    public const byte Sb16OutputFilter = 0x41;
    
    /// <summary>
    /// SB16 Advanced register 0x42 - Input Filter.
    /// </summary>
    public const byte Sb16InputFilter = 0x42;
    
    /// <summary>
    /// SB16 Advanced register 0x43 - 3D Effects Control.
    /// </summary>
    public const byte Sb16Effects3D = 0x43;
    
    /// <summary>
    /// SB16 Advanced register 0x44 - Alt Feature Enable 1.
    /// </summary>
    public const byte Sb16AltFeatureEnable1 = 0x44;
    
    /// <summary>
    /// SB16 Advanced register 0x45 - Alt Feature Enable 2.
    /// </summary>
    public const byte Sb16AltFeatureEnable2 = 0x45;
    
    /// <summary>
    /// SB16 Advanced register 0x46 - Alt Feature Status.
    /// </summary>
    public const byte Sb16AltFeatureStatus = 0x46;
    
    /// <summary>
    /// SB16 Advanced register 0x47 - Game Port Control.
    /// </summary>
    public const byte Sb16GamePortControl = 0x47;
    
    /// <summary>
    /// SB16 Advanced register 0x48 - Volume Control Mode.
    /// </summary>
    public const byte Sb16VolumeControlMode = 0x48;
    
    /// <summary>
    /// SB16 Advanced register 0x49 - Reserved.
    /// </summary>
    public const byte Sb16Reserved = 0x49;
}