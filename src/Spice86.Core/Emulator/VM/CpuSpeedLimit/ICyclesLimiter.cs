namespace Spice86.Core.Emulator.VM.CpuSpeedLimit;

/// <summary>
/// This interface provides methods to increase or decrease CPU speed, for speed sensitive games.
/// </summary>
public interface ICyclesLimiter {
    /// <summary>
    /// The ideal number of CPU cycles for the vast majority of real mode games.
    /// </summary>
    public const int RealModeCpuCyclesPerMs = 3000;

    /// <summary>
    /// The current target of CPU cycles to achieve
    /// </summary>
    public int TargetCpuCyclesPerMs { get; set; }

    /// <summary>
    /// Limits the number of emulated CPU cycles per ms, for speed sensitive games.
    /// </summary>
    /// <remarks>
    /// Also, too many CPU cycles can make emulation performance worse,
    /// and sometimes even starves other threads (ie. sound/music gets cut off, UI freezes!)
    /// </remarks>
    public void RegulateCycles();

    /// <summary>
    /// Augments the number of target CPU cycles per ms
    /// </summary>
    public void IncreaseCycles();

    /// <summary>
    /// Decreases the number of target CPU cycles per ms
    /// </summary>
    public void DecreaseCycles();

    /// <summary>
    /// Gets the number of cycles not done yet (ND) within the current millisecond tick.
    /// Equivalent to DOSBox Staging's PIC_TickIndexND().
    /// </summary>
    /// <returns>The number of cycles not yet completed in the current millisecond tick.</returns>
    public long GetNumberOfCyclesNotDoneYet();

    /// <summary>
    /// Gets the percent of cycles completed within the current millisecond tick of the CPU.
    /// Equivalent to DOSBox Staging's PIC_TickIndex().
    /// </summary>
    /// <returns>A value between 0.0 and 1.0 representing the percentage of cycles completed.</returns>
    public double GetCycleProgressionPercentage();

    /// <summary>
    /// Gets the number of completed millisecond ticks.
    /// Equivalent to DOSBox Staging's PIC_Ticks.
    /// Reference: DOSBox src/hardware/pic.cpp PIC_Ticks, incremented by TIMER_AddTick()
    /// </summary>
    uint TickCount { get; }

    /// <summary>
    /// Called when the emulator is paused. Stops the internal throttle clock.
    /// </summary>
    void OnPause();

    /// <summary>
    /// Called when the emulator is resumed. Restarts the internal throttle clock.
    /// </summary>
    void OnResume();

    /// <summary>
    /// Consumes cycles from the current tick's budget to simulate I/O port access latency.
    /// Equivalent to DOSBox's <c>CPU_Cycles -= delaycyc</c> pattern used in OPL PortRead.
    /// This makes the next tick boundary arrive sooner, naturally pacing I/O-heavy loops.
    /// Reference: DOSBox src/hardware/audio/opl.cpp Opl::PortRead()
    /// </summary>
    /// <param name="cycles">Number of cycles to consume (clamped to remaining budget).</param>
    void ConsumeIoCycles(int cycles);

    /// <summary>
    /// Whether a tick boundary was crossed during the last <see cref="RegulateCycles"/> call.
    /// Used by the emulation loop to gate per-tick work such as input polling.
    /// Reference: DOSBox's <c>normal_loop()</c> only calls <c>GFX_PollAndHandleEvents()</c>
    /// between ticks, not every instruction.
    /// </summary>
    bool TickOccurred { get; }

    /// <summary>
    /// Thread-safe snapshot of FullIndex (TickCount + CycleProgressionPercentage).
    /// Updated atomically by the emulation thread in RegulateCycles.
    /// Equivalent to DOSBox's <c>atomic_pic_index</c> / <c>PIC_AtomicIndex()</c>.
    /// Cross-thread consumers (mixer, audio callbacks) should use this instead of
    /// computing FullIndex directly, which involves non-atomic reads of multiple fields.
    /// Reference: DOSBox src/hardware/pic.h PIC_AtomicIndex(), PIC_UpdateAtomicIndex()
    /// </summary>
    double AtomicFullIndex { get; }

    /// <summary>
    /// Gets the absolute cycle count at which the next tick boundary fires.
    /// Used by the scheduler to compute cycle thresholds for event gating.
    /// </summary>
    long NextTickBoundaryCycles { get; }

    /// <summary>
    /// Gets the cycle budget that was active when the current tick started.
    /// Snapshotted at each tick boundary so that FullIndex computations use a stable
    /// denominator within a tick, exactly like DOSBox's CPU_CycleMax.
    /// Reference: DOSBox src/cpu/cpu.h CPU_CycleMax
    /// </summary>
    int TickCycleMax { get; }

    /// <summary>
    /// Gets the total number of IO delay cycles removed during the current tick.
    /// This is used for auto-cycle adjustment to discount IO-heavy periods.
    /// Accumulated in <see cref="ConsumeIoCycles"/>, reset at each tick boundary.
    /// Reference: DOSBox src/cpu/cpu.h CPU_IODelayRemoved
    /// </summary>
    long IoDelayRemoved { get; }
}
