namespace Spice86.Core.Emulator.Devices.CdRom;

using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Interfaces;
using Spice86.Audio.Common;
using Spice86.Audio.Filters;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>Manages real-time CD audio streaming through the software mixer.</summary>
public sealed class CdAudioPlayer {
    private const int CdAudioSampleRateHz = 44100;
    private const int AudioSectorSizeBytes = 2352;
    private const int SamplesPerSector = 588;

    private readonly SoundChannel _soundChannel;
    private readonly IDriveActivityNotifier _activityNotifier;
    private readonly ICdRomDrive _drive;
    private readonly char _driveLetter = '\0';
    private byte[] _rawAudioBuffer = [];
    private float[] _floatSampleBuffer = [];

    /// <summary>Initialises a new <see cref="CdAudioPlayer"/> with an activity notifier.</summary>
    /// <param name="cdRomDrive">The CD-ROM drive</param>
    /// <param name="channelCreator">The sound channel creator used to register the CD audio channel.</param>
    /// <param name="activityNotifier">Notifier that surfaces per-drive read activity for streamed audio sectors.</param>
    /// <param name="driveLetter">The DOS drive letter that owns the audio stream.</param>
    public CdAudioPlayer(ICdRomDrive cdRomDrive, ISoundChannelCreator channelCreator, IDriveActivityNotifier activityNotifier, char driveLetter) {
        _soundChannel = channelCreator.AddChannel(AudioCallback, CdAudioSampleRateHz, "CD Audio",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio, ChannelFeature.Stereo, ChannelFeature.ReverbSend });
        _soundChannel.Enable(false);
        _drive = cdRomDrive;
        _activityNotifier = activityNotifier;
        _driveLetter = char.ToUpperInvariant(driveLetter);
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

    /// <summary>Applies MSCDEX channel-control routing and gain to the live CD audio mixer channel.</summary>
    /// <param name="leftOutput">Mapped destination for the left CD channel.</param>
    /// <param name="leftVolume">Gain for the left CD channel in the 0-255 MSCDEX range.</param>
    /// <param name="rightOutput">Mapped destination for the right CD channel.</param>
    /// <param name="rightVolume">Gain for the right CD channel in the 0-255 MSCDEX range.</param>
    public void ApplyChannelControl(byte leftOutput, byte leftVolume, byte rightOutput, byte rightVolume) {
        _soundChannel.AppVolume = new AudioFrame(leftVolume / 255.0f, rightVolume / 255.0f);
        _soundChannel.SetChannelMap(new StereoLine {
            Left = leftOutput == 1 ? LineIndex.Right : LineIndex.Left,
            Right = rightOutput == 0 ? LineIndex.Left : LineIndex.Right,
        });
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
        int remainingSectors = status.EndLba - status.CurrentLba;
        if (remainingSectors <= 0) {
            status.Status = CdAudioStatus.Stopped;
            _soundChannel.Enable(false);
            return;
        }
        int sectorsNeeded = (framesRequested + SamplesPerSector - 1) / SamplesPerSector;
        int sectorsToRead = Math.Min(sectorsNeeded, remainingSectors);
        int rawBytesNeeded = sectorsToRead * AudioSectorSizeBytes;
        if (_rawAudioBuffer.Length < rawBytesNeeded) {
            _rawAudioBuffer = new byte[rawBytesNeeded];
        }
        Span<byte> rawAudio = _rawAudioBuffer.AsSpan(0, rawBytesNeeded);
        int bytesRead = _drive.Read(status.CurrentLba, sectorsToRead, rawAudio, CdSectorMode.AudioRaw2352);
        int completeSectorsRead = bytesRead / AudioSectorSizeBytes;
        if (completeSectorsRead <= 0) {
            status.Status = CdAudioStatus.Stopped;
            _soundChannel.Enable(false);
            return;
        }
        int completeBytesRead = completeSectorsRead * AudioSectorSizeBytes;
        if (_driveLetter != '\0') {
            _activityNotifier.NotifyRead(_driveLetter);
        }
        int sampleCount = completeBytesRead / 2;
        if (_floatSampleBuffer.Length < sampleCount) {
            _floatSampleBuffer = new float[sampleCount];
        }
        // CD-DA on a BIN image is signed 16-bit little-endian PCM (Red Book; mirrors
        // dosbox-staging cdrom_image.cpp BinaryFile::getEndian returning little-endian).
        ReadOnlySpan<byte> rawAudioRead = _rawAudioBuffer.AsSpan(0, completeBytesRead);
        for (int i = 0; i < sampleCount; i++) {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(rawAudioRead.Slice(i * 2, 2));
            _floatSampleBuffer[i] = sample;
        }
        int numFrames = sampleCount / 2;
        _soundChannel.AddSamplesFloat(numFrames, _floatSampleBuffer.AsSpan(0, sampleCount));
        status.CurrentLba += completeSectorsRead;
        if (status.CurrentLba >= status.EndLba || completeSectorsRead < sectorsToRead) {
            status.Status = CdAudioStatus.Stopped;
            _soundChannel.Enable(false);
        }
    }
}
