namespace Spice86.Audio.Filters;

using Spice86.Audio.Common;

using System;

public sealed class AudioFrameBuffer {
    private AudioFrame[] _buffer;

    public AudioFrameBuffer(int initialCapacity) {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        _buffer = new AudioFrame[initialCapacity];
    }

    public int Count { get; private set; }

    public AudioFrame this[int index] {
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

    private void EnsureCapacity(int capacity) {
        if (capacity <= _buffer.Length) {
            return;
        }
        int newSize = _buffer.Length == 0 ? 4 : _buffer.Length * 2;
        if (newSize < capacity) {
            newSize = capacity;
        }
        Array.Resize(ref _buffer, newSize);
    }

    public void Add(AudioFrame frame) {
        EnsureCapacity(Count + 1);
        _buffer[Count] = frame;
        Count++;
    }

    public void AddRange(ReadOnlySpan<AudioFrame> frames) {
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

    public Span<AudioFrame> AsSpan() {
        return _buffer.AsSpan(0, Count);
    }

    public Span<AudioFrame> AsSpan(int start, int length) {
        return _buffer.AsSpan(start, length);
    }
}
