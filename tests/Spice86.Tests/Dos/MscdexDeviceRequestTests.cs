namespace Spice86.Tests.Dos;

using System;
using System.Collections.Generic;
using System.IO;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.CdRom;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.InterruptHandlers.Mscdex;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Storage.CdRom;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;
using Spice86.Tests.Utility;

using Xunit;

public class MscdexDeviceRequestTests {
    [Fact]
    public void SendDeviceDriverRequest_IoctlMediaChanged_UsesDosBoxSwapRequestCodes() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-audio");
        CdRomDrive drive = CreateAudioCdRomDrive(tempFile, out ICdRomImage image, out _, out _);
        using (image) {
            MscdexTestHarness harness = new(drive);

            // Act
            harness.DispatchIoctlInput((byte)MscdexIoctlInputCode.MediaChanged);
            byte firstStatus = harness.Memory.UInt8[harness.BufferBaseAddress + 1];

            harness.DispatchIoctlInput((byte)MscdexIoctlInputCode.MediaChanged);
            byte secondStatus = harness.Memory.UInt8[harness.BufferBaseAddress + 1];

            // Assert
            firstStatus.Should().Be(0xFF,
                "MSCDEX IOCTL media-change should report swap requested on the first query after media insertion");
            secondStatus.Should().Be(0x01,
                "MSCDEX IOCTL media-change should report 0x01 once the pending swap notification has been consumed");
        }
    }

    [Fact]
    public void SendDeviceDriverRequest_Seek_UpdatesCurrentPositionWhenPlaybackIsIdle() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-audio");
        CdRomDrive drive = CreateAudioCdRomDrive(tempFile, out ICdRomImage image, out _, out _);
        using (image) {
            MscdexTestHarness harness = new(drive);

            // Act
            harness.DispatchSeek(321);
            harness.DispatchIoctlInput((byte)MscdexIoctlInputCode.CurrentPosition, 0);
            uint currentLba = harness.Memory.UInt32[harness.BufferBaseAddress + 2];

            // Assert
            currentLba.Should().Be(321u,
                "MSCDEX seek should update the idle current-position state used by IOCTL current-position queries");
        }
    }

    [Fact]
    public void SendDeviceDriverRequest_StopAndResume_PreservePausedAudioSessionAndDosBoxAudioStatus() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-audio");
        CdRomDrive drive = CreateAudioCdRomDrive(tempFile, out ICdRomImage image, out Action<int> audioCallback, out _);
        using (image) {
            MscdexTestHarness harness = new(drive);

            // Act
            harness.DispatchPlayAudio(100, 75);
            audioCallback(588 * 20);
            harness.DispatchStopAudio();
            harness.DispatchIoctlInput((byte)MscdexIoctlInputCode.AudioStatus);

            byte pauseFlag = harness.Memory.UInt8[harness.BufferBaseAddress + 1];
            byte startMinute = harness.Memory.UInt8[harness.BufferBaseAddress + 3];
            byte startSecond = harness.Memory.UInt8[harness.BufferBaseAddress + 4];
            byte startFrame = harness.Memory.UInt8[harness.BufferBaseAddress + 5];
            byte endMinute = harness.Memory.UInt8[harness.BufferBaseAddress + 7];
            byte endSecond = harness.Memory.UInt8[harness.BufferBaseAddress + 8];
            byte endFrame = harness.Memory.UInt8[harness.BufferBaseAddress + 9];

            harness.DispatchResumeAudio();
            CdAudioPlayback resumedPlayback = drive.GetAudioStatus();

            // Assert
            pauseFlag.Should().Be(1,
                "MSCDEX stop should preserve a resumable paused audio session instead of discarding playback state");
            startMinute.Should().Be(0);
            startSecond.Should().Be(3);
            startFrame.Should().Be(45,
                "MSCDEX audio status should report the paused playback position in Red Book MSF");
            endMinute.Should().Be(0);
            endSecond.Should().Be(3);
            endFrame.Should().Be(0,
                "MSCDEX audio status should report the requested playback length, not an absolute end LBA");
            resumedPlayback.Status.Should().Be(CdAudioStatus.Playing,
                "MSCDEX resume should restart a previously paused audio session");
        }
    }

    [Fact]
    public void SendDeviceDriverRequest_PlayAndResume_SetAudioPlayingBitInStatusWord() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-audio");
        CdRomDrive drive = CreateAudioCdRomDrive(tempFile, out ICdRomImage image, out Action<int> audioCallback, out _);
        using (image) {
            MscdexTestHarness harness = new(drive);

            // Act
            harness.DispatchPlayAudio(100, 75);
            ushort playStatus = harness.RequestStatusWord;

            audioCallback(588 * 20);
            harness.DispatchStopAudio();
            ushort stopStatus = harness.RequestStatusWord;

            harness.DispatchResumeAudio();
            ushort resumeStatus = harness.RequestStatusWord;

            // Assert
            playStatus.Should().Be(0x0300,
                "MSCDEX request status should set the audio-playing bit while playback is active");
            stopStatus.Should().Be(0x0100,
                "MSCDEX stop should clear the audio-playing bit once playback has been paused");
            resumeStatus.Should().Be(0x0300,
                "MSCDEX resume should restore the audio-playing bit when playback becomes active again");
        }
    }

    [Fact]
    public void SendDeviceDriverRequest_IoctlChannelControl_ClampsDosBoxRoutingAndAppliesLiveMixerState() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-audio");
        CdRomDrive drive = CreateAudioCdRomDrive(tempFile, out ICdRomImage image, out _, out SoundChannel channel);
        using (image) {
            MscdexTestHarness harness = new(drive);

            // Act
            harness.DispatchIoctlOutputChannelControl(3, 64, 9, 192, 2, 111, 3, 222);
            harness.DispatchIoctlInput((byte)MscdexIoctlInputCode.ChannelControl);

            // Assert
            channel.AppVolume.Left.Should().BeApproximately(64.0f / 255.0f, 0.0001f,
                "MSCDEX IOCTL channel-control should update the live left application gain on the CD audio mixer channel");
            channel.AppVolume.Right.Should().BeApproximately(192.0f / 255.0f, 0.0001f,
                "MSCDEX IOCTL channel-control should update the live right application gain on the CD audio mixer channel");
            channel.ChannelMap.Should().Be(StereoLine.StereoMap,
                "DOSBox clamps invalid channel-control output routes back to left/right before applying them");
            harness.Memory.UInt8[harness.BufferBaseAddress + 1].Should().Be(0,
                "invalid left output routes should round-trip as the DOSBox left default");
            harness.Memory.UInt8[harness.BufferBaseAddress + 2].Should().Be(64);
            harness.Memory.UInt8[harness.BufferBaseAddress + 3].Should().Be(1,
                "invalid right output routes should round-trip as the DOSBox right default");
            harness.Memory.UInt8[harness.BufferBaseAddress + 4].Should().Be(192);
            harness.Memory.UInt8[harness.BufferBaseAddress + 5].Should().Be(2);
            harness.Memory.UInt8[harness.BufferBaseAddress + 6].Should().Be(111);
            harness.Memory.UInt8[harness.BufferBaseAddress + 7].Should().Be(3);
            harness.Memory.UInt8[harness.BufferBaseAddress + 8].Should().Be(222);
        }
    }

    [Fact]
    public void SendDeviceDriverRequest_ReadLong_FailsWhenDriveReturnsShortRawSector() {
        // Arrange
        using TempFile tempFile = new("cdrom-mscdex-cooked");
        CdRomDrive drive = CreateCookedOnlyCdRomDrive(tempFile, out ICdRomImage image);
        using (image) {
            MscdexTestHarness harness = new(drive);
            harness.Memory.UInt8[harness.BufferBaseAddress] = 0x5A;

            // Act
            harness.DispatchReadLong(16, 1, rawFlag: 1);

            // Assert
            harness.RequestStatusWord.Should().Be(0x8000,
                "MSCDEX Read Long should fail when the drive cannot supply a full raw 2352-byte sector");
            harness.Memory.UInt8[harness.BufferBaseAddress].Should().Be(0x5A,
                "failed Read Long requests should not overwrite the caller buffer with truncated sector data");
        }
    }

    private static CdRomDrive CreateAudioCdRomDrive(TempFile tempFile, out ICdRomImage image,
        out Action<int> audioCallback,
        out SoundChannel channel) {
        tempFile.CreateFile("disc.bin", CreateAudioDiscBytes(200));
        string cuePath = tempFile.CreateTextFile("disc.cue",
            "FILE \"disc.bin\" BINARY\r\n" +
            "TRACK 01 AUDIO\r\n" +
            "INDEX 01 00:00:00\r\n");

        image = new CueBinImage(cuePath);
        return CreateDrive(image, out audioCallback, out channel);
    }

    private static byte[] CreateAudioDiscBytes(int sectorCount) {
        byte[] binBytes = new byte[2352 * sectorCount];
        for (int i = 0; i < binBytes.Length; i++) {
            binBytes[i] = (byte)(i & 0xFF);
        }
        return binBytes;
    }

    private static CdRomDrive CreateCookedOnlyCdRomDrive(TempFile tempFile, out ICdRomImage image) {
        string sourceDirectory = tempFile.CreateDirectory("source");
        File.WriteAllText(Path.Join(sourceDirectory, "README.TXT"), "Spice86");

        image = new VirtualIsoImage(sourceDirectory, "SPICE86");
        return CreateDrive(image, out _, out _);
    }

    private static CdRomDrive CreateDrive(ICdRomImage image, out Action<int> audioCallback, out SoundChannel channel) {
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

    private sealed class MscdexTestHarness {
        private const ushort RequestSegment = 0x2000;
        private const ushort BufferSegment = 0x2100;

        public State State { get; }
        public Memory Memory { get; }
        public ICdRomDrive Drive { get; }
        public Mscdex Mscdex { get; }
        public uint RequestBaseAddress { get; }
        public uint BufferBaseAddress { get; }
        public ushort RequestStatusWord => Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.RequestStatusOffset];

        public MscdexTestHarness(ICdRomDrive drive) {
            Drive = drive;
            State = new State(CpuModel.INTEL_80286);
            Memory = new Memory(new(), new Ram(0x200000), new A20Gate(), new RealModeMmu386(), false);
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            Mscdex = new Mscdex(State, Memory, loggerService);
            Mscdex.AddDrive(new MscdexDriveEntry('D', 3, drive));
            RequestBaseAddress = MemoryUtils.ToPhysicalAddress(RequestSegment, 0);
            BufferBaseAddress = MemoryUtils.ToPhysicalAddress(BufferSegment, 0);
        }

        public void DispatchIoctlInput(byte controlCode) {
            DispatchIoctlInput(controlCode, 0);
        }

        public void DispatchIoctlInput(byte controlCode, byte addressMode) {
            PrepareRequest((byte)MscdexDeviceDriverCommand.IoctlInput);
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt8[BufferBaseAddress] = controlCode;
            Memory.UInt8[BufferBaseAddress + 1] = addressMode;
            Mscdex.Dispatch();
        }

        public void DispatchSeek(uint lba) {
            PrepareRequest((byte)MscdexDeviceDriverCommand.Seek);
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.RequestAddressingModeOffset] = 0;
            Memory.UInt32[RequestBaseAddress + MscdexRequestOffsets.ReadLongStartSectorOffset] = lba;
            Mscdex.Dispatch();
        }

        public void DispatchPlayAudio(uint startLba, uint sectorCount) {
            PrepareRequest((byte)MscdexDeviceDriverCommand.PlayAudio);
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.RequestAddressingModeOffset] = 0;
            Memory.UInt32[RequestBaseAddress + MscdexRequestOffsets.PlayAudioStartLbaOffset] = startLba;
            Memory.UInt32[RequestBaseAddress + MscdexRequestOffsets.PlayAudioSectorCountOffset] = sectorCount;
            Mscdex.Dispatch();
        }

        public void DispatchIoctlOutputChannelControl(byte output0, byte volume0, byte output1, byte volume1,
            byte output2, byte volume2, byte output3, byte volume3) {
            PrepareRequest((byte)MscdexDeviceDriverCommand.IoctlOutput);
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt8[BufferBaseAddress] = (byte)MscdexIoctlOutputCode.ChannelControl;
            Memory.UInt8[BufferBaseAddress + 1] = output0;
            Memory.UInt8[BufferBaseAddress + 2] = volume0;
            Memory.UInt8[BufferBaseAddress + 3] = output1;
            Memory.UInt8[BufferBaseAddress + 4] = volume1;
            Memory.UInt8[BufferBaseAddress + 5] = output2;
            Memory.UInt8[BufferBaseAddress + 6] = volume2;
            Memory.UInt8[BufferBaseAddress + 7] = output3;
            Memory.UInt8[BufferBaseAddress + 8] = volume3;
            Mscdex.Dispatch();
        }

        public void DispatchReadLong(uint startLba, ushort sectorCount, byte rawFlag) {
            PrepareRequest((byte)MscdexDeviceDriverCommand.ReadLong);
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.RequestAddressingModeOffset] = 0;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset] = 0;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.IoctlBufferPtrOffset + 2] = BufferSegment;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.ReadLongSectorCountOffset] = sectorCount;
            Memory.UInt32[RequestBaseAddress + MscdexRequestOffsets.ReadLongStartSectorOffset] = startLba;
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.ReadLongRawFlagOffset] = rawFlag;
            Mscdex.Dispatch();
        }

        public void DispatchStopAudio() {
            PrepareRequest((byte)MscdexDeviceDriverCommand.StopAudio);
            Mscdex.Dispatch();
        }

        public void DispatchResumeAudio() {
            PrepareRequest((byte)MscdexDeviceDriverCommand.ResumeAudio);
            Mscdex.Dispatch();
        }

        private void PrepareRequest(byte command) {
            State.AL = 0x10;
            State.ES = RequestSegment;
            State.BX = 0;
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.RequestSubunitOffset] = 0;
            Memory.UInt8[RequestBaseAddress + MscdexRequestOffsets.RequestCommandOffset] = command;
            Memory.UInt16[RequestBaseAddress + MscdexRequestOffsets.RequestStatusOffset] = 0;
        }
    }
}
