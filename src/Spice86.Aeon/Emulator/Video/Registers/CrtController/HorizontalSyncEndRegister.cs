namespace Spice86.Aeon.Emulator.Video.Registers.CrtController;

public class HorizontalSyncEndRegister : VgaRegisterBase {
    /// <summary>
    /// This bit extends the Horizontal Blanking End value by one bit. Refer to register CR3 for an explanation of the
    /// Horizontal Blanking End Value.
    /// </summary>
    public byte HorizontalBlankingEnd5 {
        get => (byte)(Value & 1 << 7);
        set => SetBit(7, value != 0);
    }

    /// <summary>
    /// This two-bit field is used to delay the external Horizontal Sync pulse from the position implied in CR4. This
    /// is necessary in some modes to allow internal timing signals triggered from Horizontal Sync Start to begin prior
    /// to Display Enable. 
    /// </summary>
    public byte HorizontalSyncDelay {
        get => GetBits(6, 5);
        set => SetBits(6, 5, value);
    }

    /// <summary>
    /// This field determines the width of the Horizontal Sync pulse. The least-significant five bits of the Character
    /// Counter are compared with the contents of this field. When a match occurs, the Horizontal Sync pulse is ended.
    /// Note the Horizontal Sync pulse is limited to 31 character clock times. The value to be programmed into this
    /// register can be calculated by subtracting the desired sync width from the value programmed into CR4 (Horizontal
    /// Sync Start). Never program the sync pulse to extend past the Horizontal Total. In addition, HSYNC should always
    /// end during the Horizontal Blanking period.
    /// </summary>
    public byte HorizontalSyncEnd {
        get => GetBits(4, 0);
        set => SetBits(4, 0, value);
    }
}