namespace Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// Machine code that performs out of function jumps can be reimplemented as a call to function with the offset in parameter. <br/>
/// This class provides logic to avoid stack overflow when a() calls b() which calls a()
/// </summary>
public class JumpDispatcher {
    private static int _instanceCounter;
    
    /// <summary>
    /// Caller needs to return this when the Jump method returns false
    /// </summary>
    public Action? JumpAsmReturn { get; set; }
    
    /// <summary>
    /// Caller needs to jump to its entry point with gotoAddress = NextEntryAddress when Jump returns true
    /// </summary>
    public int NextEntryAddress { get; private set; }
    
    private readonly int _instanceId = _instanceCounter++;
    private readonly Stack<Func<int, Action>> _jumpStack = new();
    private Func<int, Action>? _returnTo;

    /// <summary>
    /// Initializes the JumpDispatcher without an initial target function.
    /// </summary>
    public JumpDispatcher() {
    }

    /// <summary>
    /// Initializes the JumpDispatcher with an initial target function.
    /// </summary>
    /// <param name="initialTarget">The initial target function.</param>
    public JumpDispatcher(Func<int, Action> initialTarget) {
        _jumpStack.Push(initialTarget);
    }

    /// <summary>
    /// Emulates a jump by calling target and jumping inside it at entryAddress.
    /// Maintains a stack of jumps so that if the same target is called twice without returning, it returns first to it and continues from there to avoid stack overflow.
    /// </summary>
    /// <param name="target">The target function to jump to.</param>
    /// <param name="entryAddress">The entry point address to jump to inside the target function.</param>
    /// <returns>True if the jump was already on the stack and a return point has been set; false otherwise.</returns>
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