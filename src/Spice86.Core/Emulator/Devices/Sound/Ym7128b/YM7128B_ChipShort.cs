namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;
public struct YM7128B_ChipShort {
    public YM7128B_ChipShort() {
        Regs_ = new byte[(int)YM7128B_Reg.YM7128B_Reg_Count];
        Gains_ = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Taps_ = new ushort[(int)YM7128B_DatasheetSpecs.YM7128B_Tap_Count];
        Buffer_ = new short[(int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length];
    }

    public byte[] Regs_ { get; set; }

    public short[] Gains_ { get; set; }

    public ushort[] Taps_ { get; set;  }

    public short T0_d_ { get; set;  }

    public ushort Tail_ { get; set; }

    public short[] Buffer_ { get; set; }

    public int Length_ => Buffer_.Length;

    public ushort Sample_Rate_ { get; set; }
}