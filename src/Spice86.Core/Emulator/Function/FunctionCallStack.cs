namespace Spice86.Core.Emulator.Function;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Tracks observed function calls and supports resynchronizing on non-local returns.
/// </summary>
public sealed class FunctionCallStack {
    private readonly List<FunctionCall> _calls = new();

    /// <summary>
    /// Gets the number of tracked calls.
    /// </summary>
    public int Count => _calls.Count;

    /// <summary>
    /// Gets the current tracked function call.
    /// </summary>
    public FunctionCall? Current => Count == 0 ? null : _calls[^1];

    /// <summary>
    /// Enumerates tracked calls from current caller to oldest caller.
    /// </summary>
    public IEnumerable<FunctionCall> FromTopToBottom {
        get {
            for (int index = _calls.Count - 1; index >= 0; index--) {
                yield return _calls[index];
            }
        }
    }

    /// <summary>
    /// Adds a tracked function call.
    /// </summary>
    /// <param name="functionCall">The function call to add.</param>
    public void Push(FunctionCall functionCall) {
        _calls.Add(functionCall);
    }

    /// <summary>
    /// Removes the current tracked function call.
    /// </summary>
    /// <param name="functionCall">The removed function call.</param>
    /// <returns><c>true</c> if a call was removed.</returns>
    public bool TryPop(out FunctionCall functionCall) {
        if (Count == 0) {
            functionCall = default;
            return false;
        }

        functionCall = _calls[^1];
        _calls.RemoveAt(Count - 1);
        return true;
    }

    /// <summary>
    /// Finds the first tracked call whose expected return address matches the observed return address.
    /// </summary>
    /// <param name="actualReturnAddress">The observed return address.</param>
    /// <returns>The matching call stack frame.</returns>
    public FunctionCallStackMatch? FindReturnMatch(SegmentedAddress? actualReturnAddress) {
        if (actualReturnAddress == null) {
            return null;
        }

        for (int index = _calls.Count - 1; index >= 0; index--) {
            FunctionCall functionCall = _calls[index];
            if (actualReturnAddress.Equals(functionCall.ExpectedReturnAddress)) {
                return new FunctionCallStackMatch(index, functionCall, index == Count - 1);
            }
        }

        return null;
    }

    /// <summary>
    /// Removes the matched function call and every newer tracked call.
    /// </summary>
    /// <param name="match">The call stack match to pop through.</param>
    public void PopThrough(FunctionCallStackMatch match) {
        _calls.RemoveRange(match.Index, Count - match.Index);
    }

    /// <summary>
    /// Removes all tracked function calls.
    /// </summary>
    public void Clear() {
        _calls.Clear();
    }
}