namespace Spice86.Core.Emulator.Devices.Structures;

public class BitView {
    private readonly Memory<byte> _memory;
    private readonly int _viewIndex;
    private readonly int _viewWidth;
    private readonly byte _mask;

    public BitView(Memory<byte> memory, int viewIndex, int viewWidth) {
        if (viewIndex < 0 || viewWidth <= 0 || viewIndex + viewWidth > 8) {
            throw new ArgumentOutOfRangeException();
        }

        _memory = memory;
        _viewIndex = viewIndex;
        _viewWidth = viewWidth;
        _mask = (byte)((1 << viewWidth) - 1);
    }

    public byte Data {
        get => _memory.Span[0];
        set => _memory.Span[0] = value;
    }

    public byte Value {
        get => (byte)((Data >> _viewIndex) & _mask);
        set {
            if (value >= (1 << _viewWidth)) {
                throw new ArgumentOutOfRangeException();
            }

            Data = (byte)((Data & ~(_mask << _viewIndex)) | ((value & _mask) << _viewIndex));
        }
    }

    public bool All => (Data & (_mask << _viewIndex)) == (_mask << _viewIndex);
    public bool Any => (Data & (_mask << _viewIndex)) != 0;
    public bool None => (Data & (_mask << _viewIndex)) == 0;

    public void Flip() => Data ^= (byte)(_mask << _viewIndex);
    public void Clear() => Data &= (byte)~(_mask << _viewIndex);
}