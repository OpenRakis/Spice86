namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Contains the DMA controller, the DMA transfer loop, and the DMA devices channel registrations.
/// </summary>
public sealed class DmaSubsystem : IDisposable {
    private readonly List<DmaChannel> _dmaDeviceChannels = new();
    private readonly Thread _dmaThread;
    private bool _exitDmaLoop;
    private bool _dmaThreadStarted;
    private readonly ManualResetEvent _dmaResetEvent = new(true);
    private bool _disposed;
    
    /// <summary>
    /// The DMA controller.
    /// </summary>
    public DmaController DmaController { get; }

    private readonly IMainWindowViewModel? _gui;

    private readonly Machine _machine;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="machine">The emulator machine.</param>
    /// <param name="configuration">The emulator configuration.</param>
    /// <param name="loggerService">The logging implementation</param>
    /// <param name="gui">The MainWindowViewModel. Can be null in headless mode.</param>
    public DmaSubsystem(Machine machine, Configuration configuration, ILoggerService loggerService, IMainWindowViewModel? gui) {
        DmaController = new DmaController(machine, configuration, loggerService);
        machine.RegisterIoPortHandler(DmaController);
        _gui = gui;
        _machine = machine;
        _dmaThread = new Thread(DmaLoop) {
            Name = "DMAThread"
        };
    }

    /// <summary>
    /// Registers a device for DMA transfers.
    /// </summary>
    /// <param name="dmaDevice">The device that uses a 8-bit DMA channel</param>
    /// <exception cref="ArgumentException">When the dma device has no DMA channel, or an equal or more amount than the DMA controller.</exception>
    public void RegisterDmaDevice(IDmaDevice8 dmaDevice) {
        if (dmaDevice.Channel < 0 || dmaDevice.Channel >= DmaController.Channels.Count) {
            throw new ArgumentException("Invalid DMA channel on DMA device.");
        }

        DmaController.Channels[dmaDevice.Channel].Device = dmaDevice;
        _dmaDeviceChannels.Add(DmaController.Channels[dmaDevice.Channel]);
    }

    /// <summary>
    /// https://techgenix.com/direct-memory-access/
    /// </summary>
    private void DmaLoop() {
        while (_machine.Cpu.IsRunning && !_exitDmaLoop && !_disposed) {
            for (int i = 0; i < _dmaDeviceChannels.Count; i++) {
                DmaChannel dmaChannel = _dmaDeviceChannels[i];
                if (_gui?.IsPaused == true || _machine.IsPaused) {
                    _gui?.WaitForContinue();
                }
                dmaChannel.Transfer(_machine.Memory);
                if (!_exitDmaLoop) {
                    _dmaResetEvent.WaitOne(1);
                }
            }
        }
    }

    /// <summary>
    /// Starts the DMA loop thread
    /// </summary>
    public void Run() {
        if (!_dmaThreadStarted) {
            _dmaThread.Start();
            _dmaThreadStarted = true;
        }
    }
    
    /// <summary>
    /// Performs DMA transfers when invoked.
    /// </summary>
    public void PerformDmaTransfers() {
        if (!_disposed && !_exitDmaLoop) {
            _dmaResetEvent.Set();
        }
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _dmaResetEvent.Set();
                _exitDmaLoop = true;
                if (_dmaThread.IsAlive && _dmaThreadStarted) {
                    _dmaThread.Join();
                }
                _dmaResetEvent.Dispose();
            }
            _disposed = true;
        }
    }
    
    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}