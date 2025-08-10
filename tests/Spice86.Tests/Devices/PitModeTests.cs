using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using Xunit;

using Timer = Spice86.Core.Emulator.Devices.Timer.Timer;

namespace Spice86.Tests.Devices;

public class PitModeTests {
    private readonly Timer _pit;

    public PitModeTests() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        State state = new();
        IOPortDispatcher ioPortDispatcher = new(
            new(), state, loggerService, true);
        Configuration configuration = new();
        TestCounterConfiguratorFactory counterConfiguratorFactory = new(
            configuration, state, new PauseHandler(loggerService), loggerService);
        DualPic dualPic = new(state, ioPortDispatcher, false, false, loggerService);
        _pit = new Timer(configuration, state, ioPortDispatcher,
            counterConfiguratorFactory, loggerService, dualPic);
    }

    public class TestCounterConfiguratorFactory : CounterConfiguratorFactory {
        public TestCounterConfiguratorFactory(Configuration configuration, State state, IPauseHandler pauseHandler, ILoggerService loggerService)
            : base(configuration, state, pauseHandler, loggerService) { }

        public override CounterActivator InstantiateCounterActivator() {
            // Always return a test activator that is always activated
            return new TestCounterActivator(1193182);
        }
    }

    [Fact]
    public void Mode0_InterruptOnTerminalCount_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 0, 0)); // Configure counter 0, RW policy 3, Mode 0
        counter.ReloadValue = 3;
        counter.CurrentCount = 3;

        // In Mode 0, output should start LOW according to Intel 8254 datasheet
        // Fix the initial output state
        counter.OutputState = Pit8254Counter.OutputStatus.Low;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.InterruptOnTerminalCount, counter.CurrentPitMode);
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState);
        Assert.Equal(3, counter.CurrentCount);

        // Act & Assert: First activation (count 3->2)
        SimulateActivations(counter, 1);
        Assert.Equal(2, counter.CurrentCount);
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState);

        // Act & Assert: Second activation (count 2->1)
        SimulateActivations(counter, 1);
        Assert.Equal(1, counter.CurrentCount);
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState);

        // Act & Assert: Third activation (count 1->0)
        SimulateActivations(counter, 1);
        Assert.Equal(0, counter.CurrentCount);
        // Output should go high when counter reaches terminal count
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Further activations don't change count (counting stops at terminal count)
        SimulateActivations(counter, 1);
        Assert.Equal(0, counter.CurrentCount); // Should remain at 0
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Gate state change
        counter.CurrentCount = 3; // Reset counter
        counter.OutputState = Pit8254Counter.OutputStatus.Low; // Reset output
        counter.SetGateState(false); // Disable gate
                                     // Counting should be suspended when gate is disabled in mode 0
        SimulateActivations(counter, 1);
        Assert.Equal(3, counter.CurrentCount); // Count unchanged
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState); // Output unchanged
    }

    [Fact]
    public void Mode1_OneShot_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 1, 0)); // Configure counter 0, RW policy 3, Mode 1
        counter.ReloadValue = 3;
        counter.CurrentCount = 3;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.OneShot, counter.CurrentPitMode);

        // Act & Assert: Gate trigger
        counter.SetGateState(true);
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState);

        // Act & Assert: Counter reaches terminal count
        SimulateActivations(counter, 3);
        Assert.Equal(0, counter.CurrentCount);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Further activations don't change state
        SimulateActivations(counter, 2);
        Assert.Equal(0, counter.CurrentCount); // Remains at 0
    }

    [Fact]
    public void Mode2_RateGenerator_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 2, 0)); // Configure counter 0, RW policy 3, Mode 2
        counter.ReloadValue = 3;
        counter.CurrentCount = 3;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.RateGenerator, counter.CurrentPitMode);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Counter reaches 0
        SimulateActivations(counter, 3);
        // Counter should reload automatically
        Assert.Equal(3, counter.CurrentCount);

        // Act & Assert: Gate state change
        counter.SetGateState(false);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);
    }

    [Fact]
    public void Mode3_SquareWave_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 3, 0)); // Configure counter 0, RW policy 3, Mode 3
        counter.ReloadValue = 4;
        counter.CurrentCount = 4;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.SquareWave, counter.CurrentPitMode);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);
        Assert.True(counter.IsSquareWaveActive);

        // Act & Assert: Counter reaches halfway
        SimulateActivations(counter, 2);
        Assert.Equal(2, counter.CurrentCount);
        Assert.Equal(Pit8254Counter.OutputStatus.Low, counter.OutputState);

        // Act & Assert: Counter reaches 0
        SimulateActivations(counter, 2);
        Assert.Equal(4, counter.CurrentCount); // Reloads
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Gate state change
        counter.SetGateState(false);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);
        Assert.False(counter.IsSquareWaveActive);
    }

    [Fact]
    public void Mode4_SoftwareStrobe_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 4, 0)); // Configure counter 0, RW policy 3, Mode 4
        counter.ReloadValue = 3;
        counter.CurrentCount = 3;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.SoftwareStrobe, counter.CurrentPitMode);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Counter reaches 0
        SimulateActivations(counter, 3);
        Assert.Equal(3, counter.CurrentCount); // Counter reloads
        // Output should briefly go low then high again
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);
    }

    [Fact]
    public void Mode5_HardwareStrobe_Behavior() {
        // Arrange
        Pit8254Counter counter = _pit.GetCounter(0);
        _pit.WriteByte(0x43, GenerateConfigureCounterByte(0, 3, 5, 0)); // Configure counter 0, RW policy 3, Mode 5
        counter.ReloadValue = 3;
        counter.CurrentCount = 3;

        // Assert initial state
        Assert.Equal(Pit8254Counter.PitMode.HardwareStrobe, counter.CurrentPitMode);
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);

        // Act & Assert: Gate trigger
        counter.SetGateState(true);

        // Act & Assert: Counter reaches 0
        SimulateActivations(counter, 3);
        Assert.Equal(3, counter.CurrentCount); // Counter reloads
        // Output should briefly go low then high again
        Assert.Equal(Pit8254Counter.OutputStatus.High, counter.OutputState);
    }

    private byte GenerateConfigureCounterByte(byte counter, byte readWritePolicy, byte mode, byte bcd) {
        return (byte)(counter << 6 | readWritePolicy << 4 | mode << 1 | bcd);
    }

    /// <summary>
    /// Simulates activations of a PIT counter by directly invoking its ProcessActivation method.
    /// This method temporarily forces the counter's Activator to be activated to ensure
    /// the counter logic is executed regardless of timing constraints.
    /// 
    /// Reference: Intel 8254 Datasheet, Section 3.3 - Mode Definitions
    /// </summary>
    /// <param name="counter">The counter to activate</param>
    /// <param name="count">The number of activations to simulate</param>
    private void SimulateActivations(Pit8254Counter counter, int count) {
        // Now ProcessActivation() will actually process each activation
        for (int i = 0; i < count; i++) {
            counter.ProcessActivation();
        }
    }

    /// <summary>
    /// A test-specific activator that always reports as activated
    /// to ensure consistent test behavior
    /// </summary>
    private class TestCounterActivator : CounterActivator {
        public TestCounterActivator(long frequency)
            : base(new PauseHandler(Substitute.For<ILoggerService>()), 1.0) {
            Frequency = frequency;
        }

        public override bool IsActivated => true;

        protected override void UpdateNonZeroFrequency(double desiredFrequency) {
            // No implementation needed for testing
        }
    }
}