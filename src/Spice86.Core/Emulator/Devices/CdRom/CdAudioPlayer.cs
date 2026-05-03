namespace Spice86.Core.Emulator.Devices.CdRom;

using Spice86.Core.Emulator.Devices.CdRom.Image;
using Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;

/// <summary>Manages real-time CD audio streaming through the software mixer.</summary>
public sealed class CdAudioPlayer {
    private const int CdAudioSampleRateHz = 44100;
    private const int AudioSectorSizeBytes = 2352;
    private const int SamplesPerSector = 588;

    private readonly SoundChannel _soundChannel;
    private ICdRomDrive? _drive;

    /// <summary>Gets the underlying sound channel for test introspection.</summary>
    internal SoundChannel Channel => _soundChannel;

    /// <summary>Initialises a new <see cref="CdAudioPlayer"/> and registers a channel with the mixer.</summary>
    /// <param name="mixer">The software mixer to register the CD audio channel with.</param>
    public CdAudioPlayer(SoftwareMixer mixer) {
        _soundChannel = mixer.AddChannel(AudioCallback, CdAudioSampleRateHz, "CD Audio",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio, ChannelFeature.Stereo, ChannelFeature.ReverbSend });
        _soundChannel.Enable(false);
    }

    /// <summary>Sets the CD-ROM drive from which audio sectors will be read.</summary>
    /// <param name="drive">The drive to stream audio from.</param>
    public void SetDrive(ICdRomDrive drive) {
        _drive = drive;
    }

    /// <summary>Enables the sound channel to begin streaming audio.</summary>
    public void StartPlayback() {
        _soundChannel.Enable(true);
    }

    /// <summary>Disables the sound channel and halts audio streaming.</summary>
    public void StopPlayback() {
        _soundChannel.Enable(false);
    }

    /// <summary>Disables the sound channel without resetting the playback position.</summary>
    public void PausePlayback() {
        _soundChannel.Enable(false);
    }

    /// <summary>Re-enables the sound channel to continue streaming audio.</summary>
    public void ResumePlayback() {
        _soundChannel.Enable(true);
    }

    private void AudioCallback(int framesRequested) {
        if (_drive == null) {
            return;
        }
        CdAudioPlayback status = _drive.GetAudioStatus();
        if (status.Status != CdAudioStatus.Playing) {
            _soundChannel.Enable(false);
            return;
        }
        int sectorsNeeded = (framesRequested + SamplesPerSector - 1) / SamplesPerSector;
        byte[] rawAudio = new byte[sectorsNeeded * AudioSectorSizeBytes];
        int bytesRead = _drive.Read(status.CurrentLba, sectorsNeeded, rawAudio.AsSpan(), CdSectorMode.AudioRaw2352);
        if (bytesRead <= 0) {
            return;
        }
        int sampleCount = bytesRead / 2;
        float[] floatSamples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++) {
            floatSamples[i] = BitConverter.ToInt16(rawAudio, i * 2);
        }
        int numFrames = sampleCount / 2;
        _soundChannel.AddSamplesFloat(numFrames, floatSamples.AsSpan());
        status.CurrentLba += sectorsNeeded;
        if (status.CurrentLba >= status.EndLba) {
            status.Status = CdAudioStatus.Stopped;
            _soundChannel.Enable(false);
        }
    }
}
