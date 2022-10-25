namespace Spice86.Core.Emulator.Devices.Sound;
public struct AudioFrame {
    public AudioFrame(float left, float right) {
        Left = left;
        Right = right;
    }

    public float Left { get; set; }

    public float Right { get; set; }

    public float this[int i] {
        get { return int.IsEvenInteger(i) ? Left : Right; }
        set { if (int.IsEvenInteger(i)) { Left = value; } else { Right = value; } }
    }
}
