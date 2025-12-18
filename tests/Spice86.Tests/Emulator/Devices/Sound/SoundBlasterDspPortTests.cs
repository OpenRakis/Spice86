namespace Spice86.Tests.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Backend.Audio;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Port-level Sound Blaster DSP tests mirroring DOSBox Staging semantics.
/// </summary>
    public class SoundBlasterDspPortTests {
    [Fact]
    public void DspResetHandshakeMatchesDosboxStaging() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();
        ushort basePort = context.Config.BaseAddress;

        context.Dispatcher.WriteByte((ushort)(basePort + 0x06), 0x01);
        context.Dispatcher.WriteByte((ushort)(basePort + 0x06), 0x00);

        byte statusAfterReset = context.Dispatcher.ReadByte((ushort)(basePort + 0x0E));
        statusAfterReset.Should().Be(0x80, "DOSBox Staging exposes bit 7 when the DSP has data after reset");

        byte resetData = context.Dispatcher.ReadByte((ushort)(basePort + 0x0A));
        resetData.Should().Be(0xAA, "DSP reset response should return 0xAA just like DOSBox Staging");

        byte statusAfterRead = context.Dispatcher.ReadByte((ushort)(basePort + 0x0E));
        statusAfterRead.Should().Be(0x00, "status bit should clear once the reset byte is consumed");

        byte emptyData = context.Dispatcher.ReadByte((ushort)(basePort + 0x0A));
        emptyData.Should().Be(0x00, "DOSBox returns zero when no DSP data is queued");
    }

    public class SoundBlasterDspAsmIntegrationTests {
        private const int MaxCycles = 200000;
        private const ushort ResultPort = 0x999;
        private static readonly string SbDspTestBinary = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "SoundBlasterTests", "sb_dsp_test.bin"));

        [Fact]
        public void SbDspTestBinaryPasses() {
            string programPath = Path.Combine(Path.GetTempPath(), $"sb_dsp_test_{Guid.NewGuid():N}.com");
            File.Copy(SbDspTestBinary, programPath, true);

            Spice86DependencyInjection di = new Spice86Creator(
                binName: programPath,
                enableCfgCpu: true,
                enablePit: true,
                recordData: false,
                maxCycles: MaxCycles,
                installInterruptVectors: true,
                failOnUnhandledPort: false).Create();

            DspResultPortHandler handler = new(di.Machine.CpuState, Substitute.For<ILoggerService>(), di.Machine.IoPortDispatcher);

            di.ProgramExecutor.Run();

            handler.Results.Should().ContainSingle();
            handler.Results[0].Should().Be(0x00);
        }

        private sealed class DspResultPortHandler : DefaultIOPortHandler {
            public List<byte> Results { get; } = new();

            public DspResultPortHandler(State state, ILoggerService logger, IOPortDispatcher dispatcher) : base(state, true, logger) {
                dispatcher.AddIOPortHandler(ResultPort, this);
            }

            public override void WriteByte(ushort port, byte value) {
                if (port == ResultPort) {
                    Results.Add(value);
                }
            }
        }
    }

    [Fact]
    public void DspWriteBufferStatusIsAlwaysReady() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();
        ushort basePort = context.Config.BaseAddress;

        byte writeStatus = context.Dispatcher.ReadByte((ushort)(basePort + 0x0C));
        writeStatus.Should().Be(0x7F, "DOSBox Staging reports the DSP write buffer as ready (bit 7 cleared)");
    }

    [Fact]
    public void StatusPortAcknowledgesPending8BitIrq() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();
        ushort basePort = context.Config.BaseAddress;

        context.SetPending8BitIrq(true);

        byte statusBeforeAck = context.Dispatcher.ReadByte((ushort)(basePort + 0x0E));
        statusBeforeAck.Should().Be(0x80, "bit 7 reflects pending 8-bit IRQ just like DOSBox Staging");

        context.GetPending8BitIrq().Should().BeFalse("reading the status port must acknowledge the pending 8-bit IRQ");

        byte statusAfterAck = context.Dispatcher.ReadByte((ushort)(basePort + 0x0E));
        statusAfterAck.Should().Be(0x00, "after acknowledgement, the status port should report no pending data or IRQ");
    }

    [Fact]
    public void Irq16AckPortClearsPendingFlag() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.Sb16);
        ushort basePort = context.Config.BaseAddress;

        context.SetPending16BitIrq(true);

        byte ackValue = context.Dispatcher.ReadByte((ushort)(basePort + 0x0F));
        ackValue.Should().Be(0xFF, "DOSBox Staging returns 0xFF on the 16-bit IRQ acknowledge port");
        context.GetPending16BitIrq().Should().BeFalse("reading the 16-bit IRQ acknowledge port clears the pending flag");
    }

    [Fact]
    public void DspIdentificationReturnsOnesComplement() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();
        ushort basePort = context.Config.BaseAddress;

        context.SendDspCommand(0xE0, 0xAA);
        List<byte> data = context.ReadAllDspData();

        data.Should().ContainSingle();
        data[0].Should().Be((byte)0x55);
    }

    [Fact]
    public void DspVersionMatchesSbPro2() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.SBPro2);
        List<byte> data = context.GetDspVersionBytes();

        data.Should().Equal(0x03, 0x02);
    }

    [Fact]
    public void DspVersionMatchesSb16() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.Sb16);
        List<byte> data = context.GetDspVersionBytes();

        data.Should().Equal(0x04, 0x05);
    }

    [Fact]
    public void SpeakerEnableDisableAffectsStatusCommand() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.SBPro2);
        ushort basePort = context.Config.BaseAddress;

        // Disable speaker then query status
        context.SendDspCommand(0xD3);
        context.SpeakerEnabled.Should().BeFalse();
        context.SendDspCommand(0xD8);
        List<byte> disabledStatus = context.ReadAllDspData();
        disabledStatus.Should().ContainSingle().Which.Should().Be(0x00);

        // Enable speaker then query status
        context.SendDspCommand(0xD1);
        context.SpeakerEnabled.Should().BeTrue();
        context.SendDspCommand(0xD8);
        List<byte> enabledStatus = context.ReadAllDspData();
        enabledStatus.Should().ContainSingle().Which.Should().Be(0xFF);
    }

    [Fact]
    public void DspCopyrightStringMatchesCreative() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();

        context.SendDspCommand(0xE3);
        List<byte> data = context.ReadAllDspData();

        data.Count.Should().BeGreaterThan(10);
        string copyright = System.Text.Encoding.ASCII.GetString(data.ToArray());
        copyright.Should().StartWith("COPYRIGHT (C) CREATIVE TECHNOLOGY LTD");
    }

    [Fact]
    public void TimeConstantSetsFrequency() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.SBPro2);

        context.SendDspCommand(0x40, 0xA5);

        uint expected = (uint)(1000000 / (256 - 0xA5));
        context.FreqHz.Should().Be(expected);
    }

    [Fact]
    public void Sb16SampleRateCommandSetsFrequency() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext(SbType.Sb16);

        context.SendDspCommand(0x41, 0x1F, 0x40); // 0x1F40 = 8000 Hz

        context.FreqHz.Should().Be(8000);
    }

    [Fact]
    public void TestRegisterStoresLastValue() {
        using SoundBlasterPortTestContext context = new SoundBlasterPortTestContext();

        context.SendDspCommand(0xE4, 0x5A);

        context.DspTestRegister.Should().Be(0x5A);
    }

    private sealed class SoundBlasterPortTestContext : IDisposable {
        public SoundBlasterPortTestContext(SbType sbType = SbType.SBPro2) {
            ILoggerService loggerService = Substitute.For<ILoggerService>();
            AddressReadWriteBreakpoints memoryBreakpoints = new();
            AddressReadWriteBreakpoints ioBreakpoints = new();
            State state = new(CpuModel.INTEL_80286);
            IOPortDispatcher dispatcher = new(ioBreakpoints, state, loggerService, failOnUnhandledPort: false);

            Ram ram = new(A20Gate.EndOfHighMemoryArea);
            A20Gate a20Gate = new();
            Memory memory = new(memoryBreakpoints, ram, a20Gate, initializeResetVector: false);

            DmaBus dmaBus = new(memory, state, dispatcher, failOnUnhandledPort: false, loggerService);
            DualPic dualPic = new(dispatcher, state, loggerService, failOnUnhandledPort: false);
            IEmulatedClock clock = new EmulatedClock();
            EmulationLoopScheduler scheduler = new(clock, loggerService);
            Mixer mixer = new(loggerService, AudioEngine.Dummy);

            HashSet<ChannelFeature> oplFeatures = new HashSet<ChannelFeature> {
                ChannelFeature.Synthesizer,
                ChannelFeature.Sleep,
                ChannelFeature.Stereo
            };
            MixerChannel oplChannel = mixer.AddChannel(_ => { }, 49716, "OplTestChannel", oplFeatures);

            SoundBlasterHardwareConfig config = new SoundBlasterHardwareConfig(7, 1, 5, sbType);
            SoundBlaster soundBlaster = new SoundBlaster(
                dispatcher,
                state,
                dmaBus,
                dualPic,
                mixer,
                oplChannel,
                loggerService,
                scheduler,
                clock,
                config);

            Dispatcher = dispatcher;
            DualPic = dualPic;
            Mixer = mixer;
            Config = config;
            SoundBlaster = soundBlaster;
        }

        public SoundBlaster SoundBlaster { get; }

        public IOPortDispatcher Dispatcher { get; }

        public DualPic DualPic { get; }

        public Mixer Mixer { get; }

        public SoundBlasterHardwareConfig Config { get; }

        public void SetPending8BitIrq(bool value) {
            SoundBlaster.PendingIrq8Bit = value;
        }

        public void SetPending16BitIrq(bool value) {
            SoundBlaster.PendingIrq16Bit = value;
        }

        public bool GetPending8BitIrq() {
            return SoundBlaster.PendingIrq8Bit;
        }

        public bool GetPending16BitIrq() {
            return SoundBlaster.PendingIrq16Bit;
        }

        public bool SpeakerEnabled => SoundBlaster.IsSpeakerEnabled;

        public uint FreqHz => SoundBlaster.DspFrequencyHz;

        public byte DspTestRegister => SoundBlaster.DspTestRegister;


        public void SendDspCommand(byte command, params byte[] parameters) {
            ushort writePort = (ushort)(Config.BaseAddress + 0x0C);
            Dispatcher.WriteByte(writePort, command);
            foreach (byte parameter in parameters) {
                Dispatcher.WriteByte(writePort, parameter);
            }
        }

        public List<byte> ReadAllDspData() {
            ushort readPort = (ushort)(Config.BaseAddress + 0x0A);
            List<byte> data = new List<byte>();
            byte value;
            while ((value = Dispatcher.ReadByte(readPort)) != 0x00 || data.Count == 0) {
                data.Add(value);
                if (!HasMoreData()) {
                    break;
                }
            }

            return data;
        }

        public List<byte> GetDspVersionBytes() {
            SendDspCommand(0xE1);
            List<byte> data = ReadAllDspData();
            return data;
        }

        private bool HasMoreData() {
            ushort statusPort = (ushort)(Config.BaseAddress + 0x0E);
            byte status = Dispatcher.ReadByte(statusPort);
            return (status & 0x80) != 0;
        }

        public void Dispose() {
            Mixer.Dispose();
        }
    }
}
