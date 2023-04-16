namespace Spice86.Aeon.Emulator.Video.Registers.Sequencer;

public class MapMaskRegister {
    public byte Value { get; set; } = 0xF;

    public bool Plane0Enabled {
        get => (Value & 0x1) != 0;
        set => Value = (byte)(Value & 0xFE | (value ? 0x1 : 0));
    }
        
    public bool Plane1Enabled {
        get => (Value & 0x2) != 0;
        set => Value = (byte)(Value & 0xFD | (value ? 0x2 : 0));
    }
        
    public bool Plane2Enabled {
        get => (Value & 0x4) != 0;
        set => Value = (byte)(Value & 0xFB | (value ? 0x4 : 0));
    }
        
    public bool Plane3Enabled {
        get => (Value & 0x8) != 0;
        set => Value = (byte)(Value & 0xF7 | (value ? 0x8 : 0));
    }

    public MaskValue MaskValue {
        get => Value;
        set => Value = value.Packed;
    }
        
}