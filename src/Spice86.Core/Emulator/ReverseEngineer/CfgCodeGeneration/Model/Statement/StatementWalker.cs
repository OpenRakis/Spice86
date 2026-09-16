namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;

using System.Linq;

/// <summary>Provides pre-order traversal of statement trees.</summary>
internal static class StatementWalker {
    /// <summary>Every item of <paramref name="items"/> and, recursively, of their nested bodies, in pre-order.</summary>
    public static IEnumerable<StatementItem> Descendants(IReadOnlyList<StatementItem> items) {
        Stack<StatementItem> stack = new();
        for (int i = items.Count - 1; i >= 0; i--) {
            stack.Push(items[i]);
        }
        while (stack.Count > 0) {
            StatementItem current = stack.Pop();
            yield return current;
            IReadOnlyList<StatementItem>[] bodies = current.NestedBodies.ToArray();
            for (int i = bodies.Length - 1; i >= 0; i--) {
                IReadOnlyList<StatementItem> body = bodies[i];
                for (int j = body.Count - 1; j >= 0; j--) {
                    stack.Push(body[j]);
                }
            }
        }
    }
}
