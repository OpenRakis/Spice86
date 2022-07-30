namespace Spice86.Core.Emulator.ReverseEngineer;

/**
 * Out of function jumps can be reimplemented as call to function with the offset in parameter.
 * This class provides logic to avoid stack overflow when a() calls b() which calls a()
 * 
 */
public class JumpDispatcher {
    private static int InstanceCounter = 0;
    // Caller needs to return this when the Jump method returns false
    public Action? JumpAsmReturn { get; set; }
    // Caller needs to jump to its entry point with gotoAddress = NextEntryAddress when Jump returns true
    public int NextEntryAddress { get; private set; }
    private readonly int _instanceId = InstanceCounter++;
    private readonly Stack<Func<int, Action>> _jumpStack = new();
    private Func<int, Action>? _returnTo;

    public JumpDispatcher() {
    }

    public JumpDispatcher(Func<int, Action> initialTarget) {
        _jumpStack.Push(initialTarget);
    }

    // Emulates a jump by calling target and jumping inside it at entryAddress.
    // Maintains a stack of jumps so that if the same target is called twice without returning, it returns first to it and continues from there to avoid stack overflow
    public bool Jump(Func<int, Action> target, int entryAddress) {
        NextEntryAddress = entryAddress;
        if (!_jumpStack.Contains(target)) {
            _jumpStack.Push(target);
            JumpAsmReturn = target.Invoke(entryAddress);
        } else {
            _returnTo = target;
        }
        Func<int, Action> currentReturn = _jumpStack.Pop();
        if (_returnTo != null && _returnTo == currentReturn) {
            // Push it back, it is the place we are going to.
            _jumpStack.Push(currentReturn);
            _returnTo = null;
            return true;
        }
        return false;
    }
}