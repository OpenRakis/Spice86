namespace TinyAudio.Wasapi;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using TinyAudio.Wasapi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class AudioClient : IDisposable
{
    private static readonly Guid _SessionGuid = Guid.NewGuid();
    private const ushort WAVE_FORMAT_PCM = 1;
    private const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;
    private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    private readonly unsafe AudioClientInst* _inst;
    private unsafe AudioRenderClientInst* _renderInst;
    private bool _disposed;

    public unsafe AudioClient(AudioClientInst* inst)
    {
        this._inst = inst;

        WAVEFORMATEX* wfx = null;
        try
        {
            uint res = inst->Vtbl->GetMixFormat(inst, &wfx);
            if (res != 0)
                throw new InvalidOperationException();

            this.FrameSize = wfx->nBlockAlign;
            this.SampleSize = wfx->wBitsPerSample / 8u;
            this.MixFormat = GetAudioFormat(wfx) ?? throw new NotSupportedException("Mix format not supported");
        }
        finally
        {
            if (wfx != null)
                Marshal.FreeCoTaskMem(new IntPtr(wfx));
        }
    }
    ~AudioClient()
    {
        this.Dispose(false);
    }

    public AudioFormat MixFormat { get; }
    public uint FrameSize { get; }
    public uint SampleSize { get; }

    public bool IsFormatSupported(AudioFormat format, out AudioFormat? closestMatch)
    {
        if (format == null)
            throw new ArgumentNullException(nameof(format));
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        closestMatch = null;

        if (!TryGetWaveFormat(format, out WAVEFORMATEXTENSIBLE wfx))
            return false;

        unsafe
        {
            WAVEFORMATEX* match = null;
            try
            {
                uint res = this._inst->Vtbl->IsFormatSupported(this._inst, 0, (WAVEFORMATEX*)&wfx, &match);

                if (match != null)
                    closestMatch = GetAudioFormat(match);

                return res == 0;
            }
            finally
            {
                if (match != null)
                    Marshal.FreeCoTaskMem(new IntPtr(match));
            }
        }
    }

    public void Initialize(TimeSpan bufferDuration, AudioFormat? audioFormat = null, bool useCallback = false)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            if (!TryGetWaveFormat(audioFormat ?? this.MixFormat, out WAVEFORMATEXTENSIBLE wfx))
                throw new ArgumentException("Could not get a WaveFormat", nameof(audioFormat));

            Guid sessionId = _SessionGuid;
            uint res = this._inst->Vtbl->Initialize(this._inst, 0, useCallback ? AUDCLNT_STREAMFLAGS_EVENTCALLBACK : 0, bufferDuration.Ticks, 0, (WAVEFORMATEX*)&wfx, &sessionId);
            if (res != 0)
                throw new InvalidOperationException();

            Guid renderGuid = Guids.IID_IAudioRenderClient;
            void* service = null;
            res = this._inst->Vtbl->GetService(this._inst, &renderGuid, &service);
            if (res != 0)
                throw new InvalidOperationException();

            this._renderInst = (AudioRenderClientInst*)service;
        }
    }

    public void SetEventHandle(SafeHandle handle)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            uint res = this._inst->Vtbl->SetEventHandle(this._inst, handle?.DangerousGetHandle() ?? default);
            if (res != 0)
                throw new InvalidOperationException();
        }
    }

    public void Start()
    {
        unsafe
        {
            uint res = this._inst->Vtbl->Start(this._inst);
            if (res != 0)
                throw new InvalidOperationException();
        }
    }
    public void Stop()
    {
        unsafe
        {
            uint res = this._inst->Vtbl->Stop(this._inst);
            if (res != 0)
                throw new InvalidOperationException();
        }
    }

    public uint GetBufferSize()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            uint size = 0;
            this._inst->Vtbl->GetBufferSize(this._inst, &size);
            return size;
        }
    }
    public uint GetCurrentPadding()
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            uint padding = 0;
            this._inst->Vtbl->GetCurrentPadding(this._inst, &padding);
            return padding;
        }
    }

    public bool TryGetBuffer<TSample>(uint framesRequested, out Span<TSample> buffer) where TSample : unmanaged
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            byte* ptr = null;
            uint res = this._renderInst->Vtbl->GetBuffer(this._renderInst, framesRequested, &ptr);
            if (res != 0)
            {
                buffer = default;
                return false;
            }

            buffer = new(ptr, (int)(framesRequested * this.MixFormat.BytesPerFrame / sizeof(TSample)));
            return true;
        }
    }
    public unsafe void* GetBuffer(uint framesRequested)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        byte* ptr = null;
        uint res = this._renderInst->Vtbl->GetBuffer(this._renderInst, framesRequested, &ptr);
        if (res != 0)
            throw new InvalidOperationException();

        return ptr;
    }
    public void ReleaseBuffer(uint framesWritten)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(AudioClient));

        unsafe
        {
            uint res = this._renderInst->Vtbl->ReleaseBuffer(this._renderInst, framesWritten, 0);
            if (res != 0)
                throw new InvalidOperationException();
        }
    }

    private static bool TryGetWaveFormat(AudioFormat format, out WAVEFORMATEXTENSIBLE wfx)
    {
        unsafe
        {
            if (format.SampleFormat == SampleFormat.IeeeFloat32)
            {
                wfx = new WAVEFORMATEXTENSIBLE
                {
                    WaveFormatEx = new WAVEFORMATEX
                    {
                        cbSize = (ushort)sizeof(WAVEFORMATEXTENSIBLE),
                        wBitsPerSample = 32,
                        nChannels = (ushort)format.Channels,
                        nBlockAlign = (ushort)(format.Channels * 4u),
                        nSamplesPerSec = (uint)format.SampleRate,
                        nAvgBytesPerSec = (uint)format.Channels * 4u * (uint)format.SampleRate,
                        wFormatTag = WAVE_FORMAT_EXTENSIBLE
                    },
                    wValidBitsPerSample = 32,
                    dwChannelMask = 3,
                    SubFormat = Guids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
                };

                return true;
            }
            else if (format.SampleFormat is SampleFormat.UnsignedPcm8 or SampleFormat.SignedPcm16)
            {
                ushort bitsPerSample = format.SampleFormat == SampleFormat.SignedPcm16 ? (ushort)16 : (ushort)8;

                wfx = new WAVEFORMATEXTENSIBLE
                {
                    WaveFormatEx = new WAVEFORMATEX
                    {
                        cbSize = (ushort)sizeof(WAVEFORMATEX),
                        wBitsPerSample = bitsPerSample,
                        nChannels = (ushort)format.Channels,
                        nBlockAlign = (ushort)(format.Channels * (bitsPerSample / 8u)),
                        nSamplesPerSec = (uint)format.SampleRate,
                        nAvgBytesPerSec = (uint)format.Channels * (bitsPerSample / 8u) * (uint)format.SampleRate,
                        wFormatTag = WAVE_FORMAT_PCM
                    }
                };

                return true;
            }
            else
            {
                wfx = default;
                return false;
            }
        }
    }
    private static unsafe AudioFormat? GetAudioFormat(WAVEFORMATEX* wfx)
    {
        SampleFormat sampleFormat;
        if (wfx->wFormatTag == WAVE_FORMAT_PCM)
        {
            if (wfx->wBitsPerSample == 8)
                sampleFormat = SampleFormat.UnsignedPcm8;
            else if (wfx->wBitsPerSample == 16)
                sampleFormat = SampleFormat.SignedPcm16;
            else
                return null;
        }
        else if (wfx->wFormatTag == WAVE_FORMAT_EXTENSIBLE)
        {
            var wfx2 = (WAVEFORMATEXTENSIBLE*)wfx;
            if (wfx2->SubFormat == Guids.KSDATAFORMAT_SUBTYPE_PCM)
            {
                if (wfx->wBitsPerSample == 8)
                    sampleFormat = SampleFormat.UnsignedPcm8;
                else if (wfx->wBitsPerSample == 16)
                    sampleFormat = SampleFormat.SignedPcm16;
                else
                    return null;
            }
            else if (wfx2->SubFormat == Guids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT)
            {
                if (wfx->wBitsPerSample == 32)
                    sampleFormat = SampleFormat.IeeeFloat32;
                else
                    return null;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        return new((int)wfx->nSamplesPerSec, wfx->nChannels, sampleFormat);
    }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            unsafe
            {
                this._inst->Vtbl->Release(this._inst);
            }

            this._disposed = true;
        }
    }
}
