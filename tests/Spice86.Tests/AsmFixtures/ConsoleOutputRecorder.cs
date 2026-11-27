namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;

using System.Text;

/// <summary>
/// Records every character written to the console during a run, in write order, through the
/// <see cref="IVgaFunctionality.OutputObserver"/> seam both DOS and BIOS character output reach.
/// The run restores the null observer when it ends.
/// </summary>
internal sealed class ConsoleOutputRecorder : IConsoleOutputObserver, IDisposable {
    private readonly WeakReference<Machine> _machine;
    private readonly StringBuilder _text = new();

    public ConsoleOutputRecorder(Machine machine) {
        _machine = new WeakReference<Machine>(machine);
        machine.VgaFunctions.OutputObserver = this;
    }

    /// <summary>Every character written to the console during the run, in write order.</summary>
    public string Text => _text.ToString();

    /// <inheritdoc />
    public void OnCharacter(char character) {
        _text.Append(character);
    }

    public void Dispose() {
        if (_machine.TryGetTarget(out Machine? machine)) {
            machine.VgaFunctions.OutputObserver = new NullConsoleOutputObserver();
        }
    }
}
