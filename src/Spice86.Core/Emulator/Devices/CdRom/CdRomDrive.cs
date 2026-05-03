using System;
using System.Collections.Generic;
using System.Linq;

using Spice86.Core.Emulator.Devices.CdRom.Image;

namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Engine layer for a CD-ROM drive, delegating raw I/O to an <see cref="ICdRomImage"/>.</summary>
public sealed class CdRomDrive : ICdRomDrive {
    private readonly List<ICdRomImage> _images = new();
    private int _currentIndex;
    private ICdRomImage _image;
    private CdAudioPlayback? _audioPlayback;
    private CdAudioPlayer? _audioPlayer;

    /// <inheritdoc/>
    public ICdRomImage Image => _image;

    /// <inheritdoc/>
    public CdRomMediaState MediaState { get; }

    /// <inheritdoc/>
    public bool IsAudioPlaying => _audioPlayback?.Status == CdAudioStatus.Playing;

    /// <inheritdoc/>
    public int ImageCount => _images.Count;

    /// <summary>Initialises a new <see cref="CdRomDrive"/> with the given image mounted.</summary>
    /// <param name="image">The CD-ROM image to mount initially.</param>
    public CdRomDrive(ICdRomImage image) {
        _image = image;
        _images.Add(image);
        _currentIndex = 0;
        MediaState = new CdRomMediaState();
    }

    /// <summary>Initialises a new <see cref="CdRomDrive"/> with a list of images.</summary>
    /// <param name="images">Ordered list of images; the first image is mounted initially.</param>
    public CdRomDrive(IReadOnlyList<ICdRomImage> images) {
        if (images.Count == 0) {
            throw new ArgumentException("At least one CD-ROM image is required.", nameof(images));
        }
        _images.AddRange(images);
        _currentIndex = 0;
        _image = _images[0];
        MediaState = new CdRomMediaState();
    }

    /// <inheritdoc/>
    public void AddImage(ICdRomImage image) {
        _images.Add(image);
    }

    /// <inheritdoc/>
    public void SwapToNextDisc() {
        if (_images.Count <= 1) {
            return;
        }
        _currentIndex = (_currentIndex + 1) % _images.Count;
        MediaState.IsDoorOpen = true;
        MediaState.NotifyMediaChanged();
        _image = _images[_currentIndex];
        MediaState.IsDoorOpen = false;
        MediaState.NotifyMediaChanged();
        StopAudio();
    }

    /// <inheritdoc/>
    public int Read(int lba, int sectorCount, Span<byte> destination, CdSectorMode mode) {
        int bytesRead = 0;
        for (int i = 0; i < sectorCount; i++) {
            Span<byte> slice = destination.Slice(bytesRead);
            bytesRead += _image.Read(lba + i, slice, mode);
        }
        return bytesRead;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TableOfContentsEntry> GetTableOfContents() {
        List<TableOfContentsEntry> entries = new();
        foreach (CdTrack track in _image.Tracks) {
            byte control = track.IsAudio ? (byte)0 : (byte)4;
            entries.Add(new TableOfContentsEntry(track.Number, track.StartLba, track.IsAudio, control, adr: 1));
        }
        entries.Add(new TableOfContentsEntry(trackNumber: 0xAA, lba: _image.TotalSectors, isAudio: false, control: 4, adr: 1));
        return entries;
    }

    /// <inheritdoc/>
    public TableOfContentsEntry? GetTrackInfo(int trackNumber) {
        IReadOnlyList<TableOfContentsEntry> toc = GetTableOfContents();
        return toc.FirstOrDefault(e => e.TrackNumber == trackNumber);
    }

    /// <inheritdoc/>
    public DiscInfo GetDiscInfo() {
        IReadOnlyList<CdTrack> tracks = _image.Tracks;
        int firstTrack = tracks[0].Number;
        int lastTrack = tracks[tracks.Count - 1].Number;
        int totalSectors = _image.TotalSectors;
        return new DiscInfo(firstTrack, lastTrack, totalSectors, leadOutLba: totalSectors);
    }

    /// <inheritdoc/>
    public void PlayAudio(int startLba, int sectorCount) {
        _audioPlayback = new CdAudioPlayback(startLba, startLba + sectorCount) {
            Status = CdAudioStatus.Playing,
        };
        _audioPlayer?.StartPlayback();
    }

    /// <inheritdoc/>
    public void StopAudio() {
        if (_audioPlayback != null) {
            _audioPlayback.Status = CdAudioStatus.Stopped;
        }
        _audioPlayer?.StopPlayback();
    }

    /// <inheritdoc/>
    public void ResumeAudio() {
        if (_audioPlayback == null) {
            _audioPlayback = new CdAudioPlayback(0, 0) {
                Status = CdAudioStatus.Stopped,
            };
            return;
        }
        if (_audioPlayback.Status == CdAudioStatus.Paused) {
            _audioPlayback.Status = CdAudioStatus.Playing;
            _audioPlayer?.ResumePlayback();
        }
    }

    /// <inheritdoc/>
    public void PauseAudio() {
        if (_audioPlayback != null && _audioPlayback.Status == CdAudioStatus.Playing) {
            _audioPlayback.Status = CdAudioStatus.Paused;
            _audioPlayer?.PausePlayback();
        }
    }

    /// <summary>Registers the <see cref="CdAudioPlayer"/> used to stream audio through the mixer.</summary>
    /// <param name="audioPlayer">The player to associate with this drive.</param>
    public void SetAudioPlayer(CdAudioPlayer audioPlayer) {
        _audioPlayer = audioPlayer;
    }

    /// <inheritdoc/>
    public CdAudioPlayback GetAudioStatus() {
        if (_audioPlayback != null) {
            return _audioPlayback;
        }
        return new CdAudioPlayback(0, 0) {
            Status = CdAudioStatus.Stopped,
        };
    }

    /// <inheritdoc/>
    public string? GetUpc() {
        return _image.UpcEan;
    }

    /// <inheritdoc/>
    public void Eject() {
        MediaState.IsDoorOpen = true;
        MediaState.NotifyMediaChanged();
    }

    /// <inheritdoc/>
    public void Insert(ICdRomImage image) {
        _images.Clear();
        _images.Add(image);
        _currentIndex = 0;
        _image = image;
        MediaState.IsDoorOpen = false;
        MediaState.NotifyMediaChanged();
    }
}
