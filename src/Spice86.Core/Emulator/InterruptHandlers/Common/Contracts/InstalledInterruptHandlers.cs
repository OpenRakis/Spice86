namespace Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The interrupt handlers installed by the emulator, in install order, with the stub
/// address each was installed at. The hardware-triggered vectors are known at construction;
/// a handler installed on one of them also contributes its hardware address and its chained
/// vectors. <see cref="Register"/> is the only writer; everything else is a query over the
/// one record per install.
/// </summary>
public sealed class InstalledInterruptHandlers {
    private readonly HashSet<byte> _hardwareVectorNumbers;
    private readonly List<(IInterruptHandler Handler, SegmentedAddress Address)> _installed = new();
    private readonly Dictionary<byte, int> _installedIndexByVector = new();

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="hardwareVectorNumbers">The interrupt vectors that are hardware-triggered
    /// (external events).</param>
    public InstalledInterruptHandlers(IEnumerable<byte> hardwareVectorNumbers) {
        _hardwareVectorNumbers = new HashSet<byte>(hardwareVectorNumbers);
    }

    /// <summary>The installed handlers, indexed by interrupt vector.</summary>
    public IReadOnlyDictionary<byte, IInterruptHandler> Handlers =>
        _installed.ToDictionary(entry => entry.Handler.VectorNumber, entry => entry.Handler);

    /// <summary>Entry addresses of every emulator-installed handler, whatever its vector.
    /// Captured at install time, so every address here is by construction emulator-installed.</summary>
    public IReadOnlyList<SegmentedAddress> Addresses =>
        _installed.Select(entry => entry.Address).ToList();

    /// <summary>Entry addresses of the emulator-installed hardware handlers. These fire on
    /// external events with nondeterministic timing and may never be reached from the program's
    /// observed entry points, so they are seeded as known-safe CFG roots for speculative
    /// exploration.</summary>
    public IReadOnlyList<SegmentedAddress> HardwareAddresses =>
        _installed.Where(entry => _hardwareVectorNumbers.Contains(entry.Handler.VectorNumber))
            .Select(entry => entry.Address).ToList();

    /// <summary>The interrupt vectors that are hardware-triggered (external events).</summary>
    public IReadOnlySet<byte> HardwareVectorNumbers => _hardwareVectorNumbers;

    /// <summary>The vectors the installed hardware handlers raise themselves with an
    /// <c>int</c>, like the int 1Ch user hook of the int 8 stub: a program can hook them.</summary>
    public IReadOnlySet<byte> ChainedVectorNumbers =>
        _installed.Where(entry => _hardwareVectorNumbers.Contains(entry.Handler.VectorNumber))
            .SelectMany(entry => entry.Handler.ChainedVectorNumbers).ToHashSet();

    /// <summary>
    /// Records <paramref name="handler"/> installed on its vector at <paramref name="address"/>.
    /// </summary>
    /// <param name="handler">The handler installed on the vector.</param>
    /// <param name="address">The address the handler's entry stub was written to.</param>
    /// <exception cref="InvalidOperationException">A handler is already installed on the
    /// vector.</exception>
    public void Register(IInterruptHandler handler, SegmentedAddress address) {
        byte vector = handler.VectorNumber;
        if (!_installedIndexByVector.TryAdd(vector, _installed.Count)) {
            throw new InvalidOperationException(
                $"A handler is already installed on interrupt vector 0x{vector:X2}");
        }
        _installed.Add((handler, address));
    }

    /// <summary>
    /// Returns the address the entry stub of the handler installed on
    /// <paramref name="vector"/> was written to.
    /// </summary>
    /// <param name="vector">The interrupt vector number.</param>
    /// <param name="stubAddress">The installed stub address when found.</param>
    /// <returns><c>false</c> when the vector has no installed handler.</returns>
    public bool TryGetInstalledAddress(byte vector, out SegmentedAddress stubAddress) {
        if (_installedIndexByVector.TryGetValue(vector, out int index)) {
            stubAddress = _installed[index].Address;
            return true;
        }
        stubAddress = default;
        return false;
    }
}
