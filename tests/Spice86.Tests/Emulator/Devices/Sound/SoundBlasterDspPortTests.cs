namespace Spice86.Tests.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;
using System.Reflection;
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
        private readonly object _irqState;
        private readonly PropertyInfo _pending8BitProperty;
        private readonly PropertyInfo _pending16BitProperty;
        private readonly PropertyInfo _speakerEnabledProperty;
        private readonly PropertyInfo _freqHzProperty;
        private readonly PropertyInfo _dspStateProperty;
        private readonly PropertyInfo _dspTestRegisterProperty;
        private readonly object _soundBlasterState;
        private readonly object _dspStateInstance;

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

            FieldInfo sbField = typeof(SoundBlaster).GetField("_sb", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("SoundBlaster _sb field not found");
            object sbInfo = sbField.GetValue(soundBlaster)
                ?? throw new InvalidOperationException("SoundBlaster _sb field is null");
            PropertyInfo irqProperty = sbInfo.GetType().GetProperty("Irq")
                ?? throw new InvalidOperationException("SoundBlaster Irq property not found");
            object irqState = irqProperty.GetValue(sbInfo)
                ?? throw new InvalidOperationException("SoundBlaster Irq state is null");

            _irqState = irqState;
            _pending8BitProperty = irqState.GetType().GetProperty("Pending8Bit")
                ?? throw new InvalidOperationException("Pending8Bit property not found");
            _pending16BitProperty = irqState.GetType().GetProperty("Pending16Bit")
                ?? throw new InvalidOperationException("Pending16Bit property not found");
            _speakerEnabledProperty = sbInfo.GetType().GetProperty("SpeakerEnabled")
                ?? throw new InvalidOperationException("SpeakerEnabled property not found");
            _freqHzProperty = sbInfo.GetType().GetProperty("FreqHz")
                ?? throw new InvalidOperationException("FreqHz property not found");
            _dspStateProperty = sbInfo.GetType().GetProperty("Dsp")
                ?? throw new InvalidOperationException("Dsp property not found");

            object dspState = _dspStateProperty.GetValue(sbInfo)
                ?? throw new InvalidOperationException("Dsp state is null");
            _dspTestRegisterProperty = dspState.GetType().GetProperty("TestRegister")
                ?? throw new InvalidOperationException("Dsp TestRegister property not found");
            _soundBlasterState = sbInfo;
            _dspStateInstance = dspState;
        }

        public SoundBlaster SoundBlaster { get; }

        public IOPortDispatcher Dispatcher { get; }

        public DualPic DualPic { get; }

        public Mixer Mixer { get; }

        public SoundBlasterHardwareConfig Config { get; }

        public void SetPending8BitIrq(bool value) {
            _pending8BitProperty.SetValue(_irqState, value);
        }

        public void SetPending16BitIrq(bool value) {
            _pending16BitProperty.SetValue(_irqState, value);
        }

        public bool GetPending8BitIrq() {
            object? value = _pending8BitProperty.GetValue(_irqState);
            return value is bool pending && pending;
        }

        public bool GetPending16BitIrq() {
            object? value = _pending16BitProperty.GetValue(_irqState);
            return value is bool pending && pending;
        }

        public bool SpeakerEnabled {
            get {
                object? value = _speakerEnabledProperty.GetValue(_soundBlasterState);
                return value is bool enabled && enabled;
            }
        }

        public uint FreqHz {
            get {
                object? value = _freqHzProperty.GetValue(_soundBlasterState);
                return value is uint freq ? freq : 0;
            }
        }

        public byte DspTestRegister {
            get {
                object? value = _dspTestRegisterProperty.GetValue(_dspStateInstance);
                return value is byte reg ? reg : (byte)0;
            }
        }


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
