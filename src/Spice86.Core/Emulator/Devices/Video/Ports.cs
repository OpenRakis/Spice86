namespace Spice86.Core.Emulator.Devices.Video;

/// <summary>
/// Contains the port addresses for the VGA video card. <br/>
/// The vast majority of VGA registers are not mapped in the I/O address space, and are instead indexed.
/// </summary>
public static class Ports {
    /// <summary>
    /// The CRT Controller Index Register for the <see cref="CrtControllerData"/> register.
    /// </summary>
    public const int CrtControllerAddress = 0x03B4;

    /// <summary>
    /// Register used to write to the CRT Controller.
    /// </summary>
    public const int CrtControllerData = 0x03B5;

    /// <summary>
    /// Input Status #1 Register (Port 0x03BA for monochrome, 0x03DA for color)
    /// <list type="table">
    /// <listheader>
    /// <term>Bit</term>
    /// <term>Description</term>
    /// </listheader>
    /// <item>
    /// <term>5, 4</term>
    /// <term>DIA Diagnostic (EGA)</term>
    /// <description>Reports the status of two of the six color outputs. The values set into the VSM field of the Color Plane Enable Register determine which colors are input to these two diagnostic pins.
    /// <list type="table">
    /// <listheader>
    /// <term>Bit 5</term>
    /// <term>Bit 4</term>
    /// <term>Color</term>
    /// </listheader>
    /// <item>
    /// <term>0</term>
    /// <term>0</term>
    /// <term>Red</term>
    /// </item>
    /// <item>
    /// <term>1</term>
    /// <term>0</term>
    /// <term>Blue</term>
    /// </item>
    /// <item>
    /// <term>0</term>
    /// <term>1</term>
    /// <term>Green</term>
    /// </item>
    /// </list>
    /// </description>
    /// </item>
    /// <item>
    /// <term>3</term>
    /// <term>VR Vertical Retrace (EGA, VGA)</term>
    /// <description>Reports the status of the display regarding whether the display is in a display mode or a vertical retrace mode. 0 = Display is in the display mode. 1 = Display is in the vertical retrace mode.</description>
    /// </item>
    /// <item>
    /// <term>2</term>
    /// <term>LSW Light Pen Switch (EGA)</term>
    /// <description>Monitors the status of the light pen switch. 0 = Light pen switch is pushed in (closed). 1 = Light pen switch is not pushed (open).</description>
    /// </item>
    /// <item>
    /// <term>1</term>
    /// <term>LST Light Pen Strobe (EGA)</term>
    /// <description>Monitors the status of the light pen strobe. 0 = Light pen has not been triggered. 1 = Light pen is triggered (electron beam is at the light pen position).</description>
    /// </item>
    /// <item>
    /// <term>0</term>
    /// <term>DE Display Enable NOT (EGA, VGA)</term>
    /// <description>Monitors the status of the display. 0 = The display is in the display mode. 1 = The display is not in the display mode. Either the horizontal or vertical retrace period is active.</description>
    /// </item>
    /// </list>
    /// </summary>
    public const int InputStatus1Read = 0x03BA;

    /// <summary>
    /// The address of the Feature Control Register, for VGA (it's at 0x3DA for EGA).
    /// </summary>
    public const int FeatureControlWrite = 0x03BA;

    /// <summary>
    /// Specifies the index of the attribute register to be accessed.
    /// <para>
    /// The Attribute Address Register is structured as follows:
    /// </para>
    /// <list type="table">
    /// <listheader>
    /// <term>Bit</term>
    /// <description>Description</description>
    /// </listheader>
    /// <item>
    /// <term>7</term>
    /// <description>Palette Address Source (PAS): This bit is set to 0 to load color values to the registers in the internal palette. It is set to 1 for normal operation of the attribute controller. Note: Do not access the internal palette while this bit is set to 1. While this bit is 1, the Type 1 video subsystem disables accesses to the palette; however, the Type 2 does not, and the actual color value addressed cannot be ensured.</description>
    /// </item>
    /// <item>
    /// <term>6-0</term>
    /// <description>Attribute Address: This field specifies the index value of the attribute register to be read or written.</description>
    /// </item>
    /// </list>
    /// </summary>
    public const int AttributeAddress = 0x03C0;

    /// <summary>
    /// Represents the Attribute Controller Data Register in the VGA specification, located at port 0x03C1.
    /// <para>
    /// This register is used to read data from the attribute controller. The index of the attribute register to be read is specified by the Attribute Address Register.
    /// </para>
    /// </summary>
    public const int AttributeData = 0x03C1;

    /// <summary>
    /// Input Status #0 Register (Port 0x03C2)
    /// <list type="table">
    /// <listheader>
    /// <term>Bit</term>
    /// <term>Description</term>
    /// </listheader>
    /// <item>
    /// <term>7</term>
    /// <term>VRI Vertical Retrace Interrupt (EGA)</term>
    /// <term></term>
    /// <description>Reports the status of the Vertical Interrupt. 0 = Vertical retrace is occurring. 1 = Vertical retrace is not occurring. Video is being displayed.</description>
    /// </item>
    /// <item>
    /// <term>6</term>
    /// <term>FS1 Feature Status 1 (EGA)</term>
    /// <term></term>
    /// <description>Reports the status of the feature 1 (FEAT1) on pin 17 of the feature connector. 0 = Feat1 = 0 (logical low level). 1 = Feat1 = 1 (logical high level).</description>
    /// </item>
    /// <item>
    /// <term>5</term>
    /// <term>FS0 Feature Status 0 (EGA)</term>
    /// <term></term>
    /// <description>Reports the status of the feature 0 (FEAT0) on pin 19 of the feature connector. 0 = Feat0 = 0 (logical low level). 1 = Feat0 = 1 (logical high level).</description>
    /// </item>
    /// <item>
    /// <term>4</term>
    /// <term>SS Switch Sense (EGA, VGA)</term>
    /// <description>Reports the status from one of the four sense switches as determined by the CS field of the Misc. Output Register. 0 = Selected sense switch= 0 (off). 1 = Selected sense switch= 1 (on).</description>
    /// </item>
    /// </list>
    /// </summary>
    public const int InputStatus0Read = 0x03C2;
    public const int MiscOutputWrite = 0x03C2;

    /// <summary>
    /// The Sequencer Index Register is used to specify the index of the <see cref="SequencerData"/> to be accessed. From 0 to 4.
    /// <para>
    /// Reading or writing to this Sequencer Data Registers will repeatedly access the register selected by
    /// the index value until the Sequencer Address Register is modified.
    /// </para>
    /// </summary>
    public const int SequencerAddress = 0x03C4;

    /// <summary>
    /// The Sequencer Data Register is used to access various sequencer registers based on the index set in the Sequencer Index Register.
    /// <list type="table">
    /// <listheader>
    /// <term>Register Name</term>
    /// <term>Index</term>
    /// <term>Write (Hex) (EGA/VGA)</term>
    /// <term>Read (Hex) (VGA)</term>
    /// <term>Address</term>
    /// </listheader>
    /// <item>
    /// <term>Reset</term>
    /// <term>0</term>
    /// <term>3C5</term>
    /// <term>3C5</term>
    /// <term>3C4</term>
    /// </item>
    /// <item>
    /// <term>Clocking Mode</term>
    /// <term>1</term>
    /// <term>3C5</term>
    /// <term>3C5</term>
    /// <term>3C4</term>
    /// </item>
    /// <item>
    /// <term>Map Mask</term>
    /// <term>2</term>
    /// <term>3C5</term>
    /// <term>3C5</term>
    /// <term>3C4</term>
    /// </item>
    /// <item>
    /// <term>Character Map Select</term>
    /// <term>3</term>
    /// <term>3C5</term>
    /// <term>3C5</term>
    /// <term>3C4</term>
    /// </item>
    /// <item>
    /// <term>Memory Mode</term>
    /// <term>4</term>
    /// <term>3C5</term>
    /// <term>3C5</term>
    /// <term>3C4</term>
    /// </item>
    /// </list>
    /// The Reset register provides two ways to reset the processor. Either reset-SR or AR-will cause the sequencer to reset, thereby stopping the functioning of the EGA. Both resets must be off (logical 1) in order for the EGA to operate. Both cause a clear-and-halt condition to occur. All outputs are placed in a high-impedance state during a reset condition.
    /// <list type="bullet">
    /// <item>
    /// <term>SR Synchronous Reset (EGA, VGA Bit 1)</term>
    /// <description>Control the state of the sequencer by producing a synchronous reset. A synchronous reset preserves memory contents. It must be used before changing the Clocking Mode Register in order to preserve memory contents. If set to 0, it generates and holds the system in a reset condition. If set to 1, it releases the reset if bit 0 is in the inactive state.</description>
    /// </item>
    /// <item>
    /// <term>SR Synchronous Reset (No Asynchronous Reset on VGA, VGA Bit 0)</term>
    /// <description>Control the state of the sequencer by producing a synchronous reset. A synchronous reset preserves memory contents and must be used before changing the Clocking Mode Register to avoid loss of memory contents. If set to 0, it generates and holds the system in a reset condition. If set to 1, it releases the reset if bit 1 is in the inactive state.</description>
    /// </item>
    /// <item>
    /// <term>AR Asynchronous Reset (EGA Bit 0)</term>
    /// <description>Control the state of the sequencer by producing an asynchronous reset. An asynchronous reset may cause a loss of display memory contents. If set to 0, it generates and holds the system in a reset condition. If set to 1, it releases the reset if bit 1 is in the inactive state.</description>
    /// </item>
    /// </list>
    /// </summary>
    public const int SequencerData = 0x03C5;

    /// <summary>
    ///  DAC Pixel Mask Register
    /// </summary>
    public const int DacPelMask = 0x03C6;

    /// <summary>
    /// Returns either 00 (prepared to accept reads to the DacData register) or 11 (prepared to accept writes to the DacData register)
    /// </summary>
    public const int DacStateRead = 0x03C7;

    /// <summary>
    /// Index of the first DAC entry to be read.
    /// </summary>
    public const int DacAddressReadIndex = 0x03C7;

    /// <summary>
    /// Index of the first DAC entry to be written.
    /// </summary>
    public const int DacAddressWriteIndex = 0x03C8;

    /// <summary>
    /// This register is used for accessing the Digital-to-Analog Converter (DAC) memory. It operates in a sequence of three I/O operations, each corresponding to the intensity values of red, green, and blue, respectively. The specific DAC entry to be accessed is initially determined by either the DAC Address Read Mode Register or the DAC Address Write Mode Register, depending on the type of I/O operation being performed. After three I/O operations, the index automatically increments, allowing the next DAC entry to be accessed without the need to manually update the index. It's important to perform I/O operations to this port in groups of three to ensure consistent results, as the outcome can vary based on the DAC implementation if this is not adhered to.
    /// </summary>
    public const int DacData = 0x03C9;
    public const int FeatureControlRead = 0x03CA;
    public const int MiscOutputRead = 0x03CC;
    public const int GraphicsControllerAddress = 0x03CE;
    public const int GraphicsControllerData = 0x03CF;
    public const int CrtControllerAddressAltMirror1 = 0x03D0;
    public const int CrtControllerDataAltMirror1 = 0x03D1;
    public const int CrtControllerAddressAltMirror2 = 0x03D2;
    public const int CrtControllerDataAltMirror2 = 0x03D3;
    public const int CrtControllerAddressAlt = 0x03D4;
    public const int CrtControllerDataAlt = 0x03D5;

    public const int CgaModeControl = 0x03D8;
    public const int CgaColorSelect = 0x03D9;
    public const int InputStatus1ReadAlt = 0x03DA;
    public const int FeatureControlWriteAlt = 0x03DA;
}