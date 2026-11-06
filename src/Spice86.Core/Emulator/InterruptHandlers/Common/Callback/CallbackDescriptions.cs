namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;

using System.Linq;
using Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

/// <summary>
/// Computes a one-line description of every registered callback for the generated code's
/// callback comments: an interrupt handler's <c>Run</c> callback is described by its vector
/// and service selector, any other callback by the declaring type and method of its delegate.
/// </summary>
public static class CallbackDescriptions {
    /// <summary>One description per callback of <paramref name="callbackHandler"/>, keyed by
    /// callback number.</summary>
    public static IReadOnlyDictionary<ushort, string> Describe(CallbackHandler callbackHandler) {
        Dictionary<ushort, string> descriptions = new();
        foreach (Callback callback in callbackHandler.AllRunnables.OfType<Callback>()) {
            descriptions[callback.Index] = Describe(callback);
        }
        return descriptions;
    }

    private static string Describe(Callback callback) {
        Action runnable = callback.Runnable;
        if (runnable.Target is IInterruptHandler handler && runnable.Method.Name == nameof(InterruptHandler.Run)) {
            return DescribeHandler(handler);
        }
        string declaringType = runnable.Method.DeclaringType?.Name ?? runnable.Method.Name;
        if (IsIdentifier(runnable.Method.Name)) {
            return $"Spice86 callback: {declaringType}.{runnable.Method.Name}";
        }
        return $"Spice86 callback: {declaringType}";
    }

    private static string DescribeHandler(IInterruptHandler handler) {
        string vector = $"INT {handler.VectorNumber:X2}h";
        string group = handler.GetType().Name;
        if (InterruptServiceGroups.TryGetPropertyName(handler.GetType(),
            out string? propertyName)) {
            group = propertyName;
        }
        switch (handler.Services) {
            case HandlerServices.SingleService single:
                if (!ReferenceEquals(single.Service.Contract, ServiceContract.Unknown)
                    && InterruptServiceGroups.TryGetPropertyName(handler.GetType(), out _)) {
                    return $"{vector}: {group}.{single.Service.Name}";
                }
                return $"{vector}: {handler.GetType().Name}";
            case HandlerServices.SelectedServices selected:
                return $"{vector}: {group} services selected by {SelectorName(selected.Selector)}";
            default:
                return $"{vector}: {handler.GetType().Name}";
        }
    }

    private static string SelectorName(ServiceSelector selector) => selector switch {
        ServiceSelector.Ah => "AH",
        ServiceSelector.Al => "AL",
        ServiceSelector.Ax => "AX",
        _ => throw new ArgumentOutOfRangeException(nameof(selector), selector, null)
    };

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsLetterOrDigit(character) || character == '_');
}
