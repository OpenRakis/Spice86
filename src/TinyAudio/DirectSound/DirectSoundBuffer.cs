namespace TinyAudio.DirectSound;

using System;
using System.Runtime.Versioning;

using TinyAudio.DirectSound.Interop;

/// <summary>
/// Represents a DirectSound buffer which can be played in the background.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DirectSoundBuffer : IDisposable
{
    private bool disposed;
    private bool isEmpty = true;
    private uint writePos;
    private readonly uint bufferSize;
    private readonly DirectSoundObject sound;
    private readonly unsafe DirectSoundBuffer8Inst* soundBuffer;

    /// <summary>
    /// Initializes a new instance of the DirectSoundBuffer class.
    /// </summary>
    /// <param name="dsb">Pointer to the native IDirectSoundBuffer8 instance.</param>
    /// <param name="sound">DirectSound instance used to create the buffer.</param>
    public unsafe DirectSoundBuffer(DirectSoundBuffer8Inst* dsb, DirectSoundObject sound)
    {
        this.soundBuffer = dsb;
        this.sound = sound;

        unsafe
        {
            var caps = new DSBCAPS { dwSize = (uint)sizeof(DSBCAPS) };
            uint res = dsb->Vtbl->GetCaps(dsb, &caps);
            this.bufferSize = caps.dwBufferBytes;
        }
    }
    ~DirectSoundBuffer()
    {
        this.Dispose(false);
    }

    /// <summary>
    /// Gets or sets the playback frequency of the buffer.
    /// </summary>
    public uint Frequency
    {
        get
        {
            if (this.disposed)
                return 0;

            unsafe
            {
                uint frequency = 0;
                this.soundBuffer->Vtbl->GetFrequency(this.soundBuffer, &frequency);
                return frequency;
            }
        }
        set
        {
            unsafe
            {
                if (!this.disposed)
                    this.soundBuffer->Vtbl->SetFrequency(this.soundBuffer, value);
            }
        }
    }
    /// <summary>
    /// Gets or sets the playback speaker pan of the buffer.
    /// </summary>
    public int Pan
    {
        get
        {
            if (this.disposed)
                return 0;

            unsafe
            {
                int pan = 0;
                this.soundBuffer->Vtbl->GetPan(this.soundBuffer, &pan);
                return pan;
            }
        }
        set
        {
            unsafe
            {
                if (!this.disposed)
                    this.soundBuffer->Vtbl->SetPan(this.soundBuffer, value);
            }
        }
    }
    /// <summary>
    /// Gets or sets the playback volume of the buffer.
    /// </summary>
    public int Volume
    {
        get
        {
            if (this.disposed)
                return 0;

            unsafe
            {
                int volume;
                this.soundBuffer->Vtbl->GetVolume(this.soundBuffer, &volume);
                return volume;
            }
        }
        set
        {
            unsafe
            {
                if (!this.disposed)
                    this.soundBuffer->Vtbl->SetVolume(this.soundBuffer, value);
            }
        }
    }
    /// <summary>
    /// Gets a value indicating whether the buffer is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            if (this.disposed)
                return false;

            unsafe
            {
                uint status;
                this.soundBuffer->Vtbl->GetStatus(this.soundBuffer, &status);
                return (status & 0x01u) != 0;
            }
        }
    }
    /// <summary>
    /// Gets the current playback position in bytes.
    /// </summary>
    public int Position
    {
        get
        {
            if (this.disposed)
                return 0;

            this.GetPosition(out uint playPos, out _);

            return (int)playPos;
        }
    }
    /// <summary>
    /// Gets the size of the buffer in bytes.
    /// </summary>
    public uint BufferSize => this.bufferSize;

    /// <summary>
    /// Begins playback of the sound buffer.
    /// </summary>
    /// <param name="playbackMode">Specifies the buffer playback behavior.</param>
    public void Play(PlaybackMode playbackMode)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(DirectSoundBuffer));

        unsafe
        {
            uint res = this.soundBuffer->Vtbl->Play(this.soundBuffer, 0, 0, (uint)playbackMode);
            if (res != 0)
                throw new InvalidOperationException("Unable to play DirectSound buffer.");
        }
    }
    /// <summary>
    /// Stops playback of the sound buffer.
    /// </summary>
    public void Stop()
    {
        unsafe
        {
            if (!this.disposed)
                this.soundBuffer->Vtbl->Stop(this.soundBuffer);
        }
    }

    public AcquiredBuffer Acquire(uint minLength)
    {
        this.GetPosition(out uint playPos, out _);

        uint maxBytes;
        if (this.isEmpty)
            maxBytes = bufferSize;
        else if (writePos > playPos)
            maxBytes = bufferSize - writePos + playPos;
        else if (writePos < playPos)
            maxBytes = playPos - writePos;
        else
            maxBytes = 0;

        if (maxBytes < minLength)
            return default;

        unsafe
        {
            void* ptr1;
            void* ptr2;
            uint length1;
            uint length2;

            uint res = this.soundBuffer->Vtbl->Lock(this.soundBuffer, this.writePos, maxBytes, &ptr1, &length1, &ptr2, &length2, 0);
            if (res != 0)
                throw new InvalidOperationException("Unable to lock DirectSound buffer.");

            if (ptr1 == null)
                throw new InvalidOperationException();

            return new(this, new IntPtr(ptr1), new IntPtr(ptr2), length1, length2);
        }
    }

    /// <summary>
    /// Returns the current playback position indicatiors.
    /// </summary>
    /// <param name="writePos">Current position of the write pointer.</param>
    /// <param name="playPos">Current position of the playback pointer.</param>
    public void GetPositions(out int writePos, out int playPos)
    {
        this.GetPosition(out uint tempPlayPos, out _);
        writePos = (int)this.writePos;
        playPos = (int)tempPlayPos;
    }
    /// <summary>
    /// Returns the maximum number of bytes that the buffer can currently accept.
    /// </summary>
    /// <returns>Maximum number of bytes that the buffer can currently accept.</returns>
    public uint GetFreeBytes()
    {
        this.GetPosition(out uint playPos, out _);

        uint maxBytes;
        if (this.isEmpty)
            maxBytes = bufferSize;
        else if (writePos > playPos)
            maxBytes = bufferSize - writePos + playPos;
        else if (writePos < playPos)
            maxBytes = playPos - writePos;
        else
            maxBytes = 0;

        return maxBytes;
    }
    /// <summary>
    /// Releases resources used by the buffer.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }
    public void Unlock(IntPtr ptr1, int length1, IntPtr ptr2, int length2)
    {
        this.isEmpty = false;

        this.writePos = (this.writePos + (uint)length1 + (uint)length2) % this.bufferSize;

        unsafe
        {
            this.soundBuffer->Vtbl->Unlock(this.soundBuffer, ptr1.ToPointer(), (uint)length1, ptr2.ToPointer(), (uint)length2);
        }
    }

    private void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            this.disposed = true;
            unsafe
            {
                this.soundBuffer->Vtbl->Release(this.soundBuffer);
            }
        }
    }

    private void GetPosition(out uint playPos, out uint safeWritePos)
    {
        unsafe
        {
            uint play = 0;
            uint write = 0;
            this.soundBuffer->Vtbl->GetCurrentPosition(this.soundBuffer, &play, &write);
            playPos = play;
            safeWritePos = write;
        }
    }
}

/// <summary>
/// Specifies sound buffer playback behavior.
/// </summary>
public enum PlaybackMode
{
    /// <summary>
    /// The buffer plays once and then stops.
    /// </summary>
    PlayOnce,
    /// <summary>
    /// The buffer plays repeatedly until it is explicitly stopped.
    /// </summary>
    LoopContinuously
}
