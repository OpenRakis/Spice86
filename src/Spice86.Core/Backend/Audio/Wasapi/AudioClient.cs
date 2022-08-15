namespace Spice86.Core.Backend.Audio.Wasapi;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Backend.Audio.Wasapi.Interop;

[SupportedOSPlatform("windows")]
internal sealed class AudioClient : IDisposable {
    private static readonly Guid SessionGuid = Guid.NewGuid();
    private const ushort WAVE_FORMAT_PCM = 1;
    private const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;
    private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    private readonly unsafe AudioClientInst* inst;
    private unsafe AudioRenderClientInst* renderInst;
    private bool disposed;

    public unsafe AudioClient(AudioClientInst* inst) {
        this.inst = inst;

        WAVEFORMATEX* wfx = null;
        try {
            uint res = inst->Vtbl->GetMixFormat(inst, &wfx);
            if (res != 0) {
                throw new InvalidOperationException();
            }

            FrameSize = wfx->nBlockAlign;
            SampleSize = wfx->wBitsPerSample / 8u;
            MixFormat = GetAudioFormat(wfx) ?? throw new NotSupportedException("Mix format not supported");
        } finally {
            if (wfx != null) {
                Marshal.FreeCoTaskMem(new IntPtr(wfx));
            }
        }
    }
    ~AudioClient() {
        Dispose(false);
    }

    public AudioFormat MixFormat { get; }
    public uint FrameSize { get; }
    public uint SampleSize { get; }

    public void Initialize(TimeSpan bufferDuration, AudioFormat? audioFormat = null, bool useCallback = false) {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            if (!TryGetWaveFormat(audioFormat ?? MixFormat, out WAVEFORMATEXTENSIBLE wfx)) {
                throw new ArgumentException();
            }

            Guid sessionId = SessionGuid;
            uint res = inst->Vtbl->Initialize(inst, 0, useCallback ? AUDCLNT_STREAMFLAGS_EVENTCALLBACK : 0, bufferDuration.Ticks, 0, (WAVEFORMATEX*)&wfx, &sessionId);
            if (res != 0) {
                throw new InvalidOperationException();
            }

            Guid renderGuid = Guids.IID_IAudioRenderClient;
            void* service = null;
            res = inst->Vtbl->GetService(inst, &renderGuid, &service);
            if (res != 0) {
                throw new InvalidOperationException();
            }

            renderInst = (AudioRenderClientInst*)service;
        }
    }

    public void SetEventHandle(SafeHandle handle) {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            uint res = inst->Vtbl->SetEventHandle(inst, handle?.DangerousGetHandle() ?? default);
            if (res != 0) {
                throw new InvalidOperationException();
            }
        }
    }

    public void Start() {
        unsafe {
            uint res = inst->Vtbl->Start(inst);
            if (res != 0) {
                throw new InvalidOperationException();
            }
        }
    }
    public void Stop() {
        unsafe {
            uint res = inst->Vtbl->Stop(inst);
            if (res != 0) {
                throw new InvalidOperationException();
            }
        }
    }

    public uint GetBufferSize() {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            uint size = 0;
            inst->Vtbl->GetBufferSize(inst, &size);
            return size;
        }
    }
    public uint GetCurrentPadding() {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            uint padding = 0;
            inst->Vtbl->GetCurrentPadding(inst, &padding);
            return padding;
        }
    }

    public bool TryGetBuffer<TSample>(uint framesRequested, out Span<TSample> buffer) where TSample : unmanaged {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            byte* ptr = null;
            uint res = renderInst->Vtbl->GetBuffer(renderInst, framesRequested, &ptr);
            if (res != 0) {
                buffer = default;
                return false;
            }

            buffer = new(ptr, (int)(framesRequested * MixFormat.BytesPerFrame / sizeof(TSample)));
            return true;
        }
    }

    public void ReleaseBuffer(uint framesWritten) {
        if (disposed) {
            throw new ObjectDisposedException(nameof(AudioClient));
        }

        unsafe {
            uint res = renderInst->Vtbl->ReleaseBuffer(renderInst, framesWritten, 0);
            if (res != 0) {
                throw new InvalidOperationException();
            }
        }
    }

    private static bool TryGetWaveFormat(AudioFormat format, out WAVEFORMATEXTENSIBLE wfx) {
        unsafe {
            if (format.SampleFormat == SampleFormat.IeeeFloat32) {
                wfx = new WAVEFORMATEXTENSIBLE {
                    WaveFormatEx = new WAVEFORMATEX {
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
            } else if (format.SampleFormat is SampleFormat.UnsignedPcm8 or SampleFormat.SignedPcm16) {
                ushort bitsPerSample = format.SampleFormat == SampleFormat.SignedPcm16 ? (ushort)16 : (ushort)8;

                wfx = new WAVEFORMATEXTENSIBLE {
                    WaveFormatEx = new WAVEFORMATEX {
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
            } else {
                wfx = default;
                return false;
            }
        }
    }
    private static unsafe AudioFormat? GetAudioFormat(WAVEFORMATEX* wfx) {
        SampleFormat sampleFormat;
        if (wfx->wFormatTag == WAVE_FORMAT_PCM) {
            if (wfx->wBitsPerSample == 8) {
                sampleFormat = SampleFormat.UnsignedPcm8;
            } else if (wfx->wBitsPerSample == 16) {
                sampleFormat = SampleFormat.SignedPcm16;
            } else {
                return null;
            }
        } else if (wfx->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
            WAVEFORMATEXTENSIBLE* wfx2 = (WAVEFORMATEXTENSIBLE*)wfx;
            if (wfx2->SubFormat == Guids.KSDATAFORMAT_SUBTYPE_PCM) {
                if (wfx->wBitsPerSample == 8) {
                    sampleFormat = SampleFormat.UnsignedPcm8;
                } else if (wfx->wBitsPerSample == 16) {
                    sampleFormat = SampleFormat.SignedPcm16;
                } else {
                    return null;
                }
            } else if (wfx2->SubFormat == Guids.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT) {
                if (wfx->wBitsPerSample == 32) {
                    sampleFormat = SampleFormat.IeeeFloat32;
                } else {
                    return null;
                }
            } else {
                return null;
            }
        } else {
            return null;
        }

        return new((int)wfx->nSamplesPerSec, wfx->nChannels, sampleFormat);
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if (!disposed) {
            if (disposing) {
                unsafe {
                    inst->Vtbl->Release(inst);
                }
            }
            disposed = true;
        }
    }
}
