namespace Spice86.Core.Emulator.InterruptHandlers.Dos.Ems; 

public record EmmMapping {
    public const int Size = 4;
    private byte[] _data { get; init; } = new byte[4];
    
    public int Handle {
        get => (_data[0] & 0xFF) | ((_data[1] & 0xFF) << 8);
        set {
            _data[0] = (byte) (value & 0xFF);
            _data[1] = (byte) ((value >> 8) & 0xFF);
        }
    }

    public int Page {
        get => (_data[2] & 0xFF) | ((_data[3] & 0xFF) << 8);
        set {
            _data[2] = (byte) (value & 0xFF);
            _data[3] = (byte) ((value >> 8) & 0xFF);
        }
    }

    public void ToHandle(int val) {
        _data[0] = (byte)(val & 0xFF);
        _data[1] = (byte)((val >> 8) & 0xFF);
    }

    public void ToPage(int val) {
        _data[2] = (byte)(val & 0xFF);
        _data[3] = (byte)((val >> 8) & 0xFF);
    }
}