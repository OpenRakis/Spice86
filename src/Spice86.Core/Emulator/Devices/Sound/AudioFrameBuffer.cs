namespace Spice86.Core.Emulator.Devices.Sound;

using System;

public sealed class AudioFrameBuffer {
    private Spice86.Libs.Sound.Common.AudioFrame[] _buffer;

    public AudioFrameBuffer(int initialCapacity) {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _buffer = new Spice86.Libs.Sound.Common.AudioFrame[initialCapacity];
    }

    public int Count { get; private set; }

    public Spice86.Libs.Sound.Common.AudioFrame this[int index] {
        get {
            return _buffer[index];
        }
        set {
            _buffer[index] = value;
        }
    }

    public void Clear() {
        Count = 0;
    }

    public void EnsureCapacity(int capacity) {
        if (capacity <= _buffer.Length) {
            return;
        }
        int newSize = _buffer.Length == 0 ? 4 : _buffer.Length * 2;
        if (newSize < capacity) {
            newSize = capacity;
        }
        Array.Resize(ref _buffer, newSize);
    }

    public void Add(Spice86.Libs.Sound.Common.AudioFrame frame) {
        EnsureCapacity(Count + 1);
        _buffer[Count] = frame;
        Count++;
    }

    public void AddRange(ReadOnlySpan<Spice86.Libs.Sound.Common.AudioFrame> frames) {
        if (frames.Length == 0) {
            return;
        }
        EnsureCapacity(Count + frames.Length);
        frames.CopyTo(_buffer.AsSpan(Count));
        Count += frames.Length;
    }

    public void Resize(int size) {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        EnsureCapacity(size);
        if (size > Count) {
            _buffer.AsSpan(Count, size - Count).Clear();
        }
        Count = size;
    }

    public void RemoveRange(int start, int count) {
        if (start < 0 || count < 0 || start + count > Count) {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        if (count == 0) {
            return;
        }
        int tailCount = Count - (start + count);
        if (tailCount > 0) {
            _buffer.AsSpan(start + count, tailCount).CopyTo(_buffer.AsSpan(start));
        }
        Count -= count;
    }

    public Span<Spice86.Libs.Sound.Common.AudioFrame> AsSpan() {
        return _buffer.AsSpan(0, Count);
    }

    public Span<Spice86.Libs.Sound.Common.AudioFrame> AsSpan(int start, int length) {
        return _buffer.AsSpan(start, length);
    }
}
