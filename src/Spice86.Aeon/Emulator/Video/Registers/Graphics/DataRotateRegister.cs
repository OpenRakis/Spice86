namespace Spice86.Aeon.Emulator.Video.Registers.Graphics;

public class DataRotateRegister : VgaRegisterBase {
    /// <summary>
    /// This field allows data from the CPU bus to be rotated up to seven bit positions prior to being altered by the SR logic. 
    /// </summary>
    public byte RotateCount {
        get => GetBits(2, 0);
        set => SetBits(2, 0, value);
    }

    /// <summary>
    /// This field controls the operation that takes place between the data in the latches and the data from the CPU or SR logic. The
    /// result of this operation is written into display memory. This field is used for  Write mode 0 only.
    /// </summary>
    public FunctionSelect FunctionSelect {
        get => (FunctionSelect)GetBits(4, 3);
        set => SetBits(4, 3, (byte)value);
    }
}

public enum FunctionSelect {
    None,
    And,
    Or,
    Xor
}