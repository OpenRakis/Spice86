namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;
using System.IO;

using NSubstitute;

using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

internal static class CdRomTestFixture {
    public static CdRomDrive CreateAudioCueBinDrive(
        TempFile tempFile,
        byte[] discBytes,
        out ICdRomImage image,
        out Action<int> audioCallback,
        out SoundChannel channel) {
        tempFile.CreateFile("disc.bin", discBytes);
        string cuePath = tempFile.CreateTextFile("disc.cue",
            "FILE \"disc.bin\" BINARY\r\n" +
            "TRACK 01 AUDIO\r\n" +
            "INDEX 01 00:00:00\r\n");

        image = new CueBinImage(cuePath);
        return CreateDrive(image, out audioCallback, out channel);
    }

    public static byte[] CreateAudioDiscBytes(int sectorCount) {
        byte[] binBytes = new byte[2352 * sectorCount];
        for (int i = 0; i < binBytes.Length; i++) {
            binBytes[i] = (byte)(i & 0xFF);
        }
        return binBytes;
    }

    public static VirtualIsoImage CreateVirtualIsoImage(TempFile tempFile) {
        string sourceDirectory = tempFile.CreateDirectory("source");
        File.WriteAllText(Path.Join(sourceDirectory, "README.TXT"), "Spice86");
        return new VirtualIsoImage(sourceDirectory, "SPICE86");
    }

    public static string CreateIsoFile(TempFile tempFile) {
        using VirtualIsoImage virtualIsoImage = CreateVirtualIsoImage(tempFile);
        byte[] isoBytes = new byte[virtualIsoImage.TotalSectors * 2048];
        for (int lba = 0; lba < virtualIsoImage.TotalSectors; lba++) {
            int offset = lba * 2048;
            int bytesRead = virtualIsoImage.Read(lba, isoBytes.AsSpan(offset, 2048), CdSectorMode.CookedData2048);
            if (bytesRead != 2048) {
                throw new InvalidOperationException($"Expected to materialize a full cooked sector at LBA {lba}.");
            }
        }

        return tempFile.CreateFile("disc.iso", isoBytes);
    }

    public static CdRomDrive CreateCookedOnlyCdRomDrive(TempFile tempFile, out ICdRomImage image) {
        image = CreateVirtualIsoImage(tempFile);
        return CreateDrive(image, out _, out _);
    }

    public static CdRomDrive CreateDrive(ICdRomImage image, out Action<int> audioCallback, out SoundChannel channel) {
        ISoundChannelCreator channelCreator = Substitute.For<ISoundChannelCreator>();
        Action<int>? capturedAudioCallback = null;
        SoundChannel? capturedChannel = null;
        channelCreator
            .AddChannel(Arg.Any<Action<int>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<HashSet<ChannelFeature>>())
            .Returns(callInfo => {
                Action<int> handler = (Action<int>)callInfo[0];
                SoundChannel createdChannel = new SoundChannel(handler, (string)callInfo[2], (HashSet<ChannelFeature>)callInfo[3]);
                capturedAudioCallback = handler;
                capturedChannel = createdChannel;
                return createdChannel;
            });
        IDriveActivityNotifier activityNotifier = Substitute.For<IDriveActivityNotifier>();
        CdRomDrive drive = new(image, channelCreator, activityNotifier, 'D');
        if (capturedAudioCallback == null || capturedChannel == null) {
            throw new InvalidOperationException("CD audio channel was not registered.");
        }

        audioCallback = capturedAudioCallback;
        channel = capturedChannel;
        return drive;
    }
}