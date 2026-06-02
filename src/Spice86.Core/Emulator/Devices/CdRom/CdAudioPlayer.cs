namespace Spice86.Core.Emulator.Devices.CdRom;

using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>Manages real-time CD audio streaming through the software mixer.</summary>
public sealed class CdAudioPlayer {
    private const int CdAudioSampleRateHz = 44100;
    private const int AudioSectorSizeBytes = 2352;
    private const int SamplesPerSector = 588;

    private readonly SoundChannel _soundChannel;
    private readonly IDriveActivityNotifier? _activityNotifier;
    private readonly ICdRomDrive _drive;
    private char _driveLetter = '\0';
    private byte[] _rawAudioBuffer = [];
    private float[] _floatSampleBuffer = [];

    /// <summary>Initialises a new <see cref="CdAudioPlayer"/> with an activity notifier.</summary>
    /// <param name="cdRomDrive">The CD-ROM drive</param>
    /// <param name="channelCreator">The sound channel creator used to register the CD audio channel.</param>
    /// <param name="activityNotifier">Notifier that surfaces per-drive read activity for streamed audio sectors.</param>
    public CdAudioPlayer(ICdRomDrive cdRomDrive, ISoundChannelCreator channelCreator, IDriveActivityNotifier? activityNotifier) {
        _soundChannel = channelCreator.AddChannel(AudioCallback, CdAudioSampleRateHz, "CD Audio",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio, ChannelFeature.Stereo, ChannelFeature.ReverbSend });
        _soundChannel.Enable(false);
        _drive = cdRomDrive;
        _activityNotifier = activityNotifier;
    }

    /// <summary>Sets the drive letter associated with this player; used for activity notifications.</summary>
    /// <param name="letter">The DOS drive letter that owns the audio stream.</param>
    public void SetDriveLetter(char letter) {
        _driveLetter = char.ToUpperInvariant(letter);
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
        int rawBytesNeeded = sectorsNeeded * AudioSectorSizeBytes;
        if (_rawAudioBuffer.Length < rawBytesNeeded) {
            _rawAudioBuffer = new byte[rawBytesNeeded];
        }
        Span<byte> rawAudio = _rawAudioBuffer.AsSpan(0, rawBytesNeeded);
        int bytesRead = _drive.Read(status.CurrentLba, sectorsNeeded, rawAudio, CdSectorMode.AudioRaw2352);
        if (bytesRead <= 0) {
            return;
        }
        if (_driveLetter != '\0') {
            _activityNotifier?.NotifyRead(_driveLetter);
        }
        int sampleCount = bytesRead / 2;
        if (_floatSampleBuffer.Length < sampleCount) {
            _floatSampleBuffer = new float[sampleCount];
        }
        // CD-DA on a BIN image is signed 16-bit little-endian PCM (Red Book; mirrors
        // dosbox-staging cdrom_image.cpp BinaryFile::getEndian returning little-endian).
        ReadOnlySpan<byte> rawAudioRead = _rawAudioBuffer.AsSpan(0, bytesRead);
        for (int i = 0; i < sampleCount; i++) {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(rawAudioRead.Slice(i * 2, 2));
            _floatSampleBuffer[i] = sample;
        }
        int numFrames = sampleCount / 2;
        _soundChannel.AddSamplesFloat(numFrames, _floatSampleBuffer.AsSpan(0, sampleCount));
        status.CurrentLba += sectorsNeeded;
        if (status.CurrentLba >= status.EndLba) {
            status.Status = CdAudioStatus.Stopped;
            _soundChannel.Enable(false);
        }
    }
}
