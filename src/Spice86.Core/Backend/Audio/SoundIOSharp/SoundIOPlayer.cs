using SoundIOSharp;

namespace Spice86.Core.Backend.Audio.SoundIOSharp;

public sealed  class SoundIOPlayer : AudioPlayer {
    private readonly SoundIO _api;
    private readonly SoundIODevice _device;
    private readonly SoundIOOutStream _outStream;
    private bool _disposed;
    private SoundIOPlayer(AudioFormat format) : base(format) {
        _api = new();
        _api.ConnectBackend(GetBackend());
        _api.FlushEvents();
        _device = _api.GetOutputDevice(_api.DefaultOutputDeviceIndex);
        SoundIOOutStream outStream = _device.CreateOutStream();
        outStream.Format = SoundIOFormat.S16LE;
        _outStream = outStream;
    }

    private static SoundIOBackend GetBackend() {
        if (OperatingSystem.IsMacOS()) {
            return SoundIOBackend.CoreAudio;
        }
        else if (OperatingSystem.IsLinux()) {
            return SoundIOBackend.PulseAudio;
        } else {
            return SoundIOBackend.Wasapi;
        }
    }

    protected override void Start(bool useCallback) {
        _outStream.Open();
        _outStream.Start();
    }

    protected override void Stop() {
        _outStream.Pause(true);
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _outStream.Dispose();
                _device.RemoveReference();
                _api.Dispose();
                _disposed = true;
            }
        }
    }

    private static unsafe void WriteSample(IntPtr intPtr, byte sample) {
        void* ptr = intPtr.ToPointer();
        byte* bytePtr = (byte*)ptr;
        *bytePtr = sample;
    }

    protected override int WriteDataInternal(ReadOnlySpan<byte> data) {
        int frameCount = 0;
        SoundIOChannelAreas results = _outStream.BeginWrite(ref frameCount);
        for (int i = 0; i < Math.Min(frameCount, data.Length); i++) {
            SoundIOChannelLayout layout = _outStream.Layout;
            for (int channel = 0; channel < layout.ChannelCount; channel++) {
                SoundIOChannelArea area = results.GetArea (channel);
                WriteSample(area.Pointer, data[frameCount]);
                area.Pointer += area.Step;
            }
        }
        _outStream.EndWrite();
        return frameCount;
    }
    public static SoundIOPlayer Create() {
        return new SoundIOPlayer(new AudioFormat(SampleRate: 48000, Channels: 2,
            SampleFormat: SampleFormat.SignedPcm16));
    }
}