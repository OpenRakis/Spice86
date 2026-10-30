namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

/// <summary>
/// A pending child call of a trampolined routine: the routine that produced it resumes only after
/// <see cref="Routine"/> finished. The child's own result travels back through the
/// <see cref="AstResult{T}"/> box the caller passed to it.
/// </summary>
internal sealed class AstSubcall(IEnumerable<AstSubcall> routine) {
    /// <summary>The child routine to run before the caller resumes.</summary>
    public IEnumerable<AstSubcall> Routine { get; } = routine;
}

/// <summary>The slot a trampolined routine writes its return value into; read by the caller after the
/// call's <see cref="AstSubcall"/> completes.</summary>
internal sealed class AstResult<T> {
    public T Value { get; set; } = default!;
}

/// <summary>
/// Runs recursive routines written as iterators without consuming the thread's stack: each routine
/// yields one <see cref="AstSubcall"/> per child call, and this driver keeps the suspended enumerators
/// on a heap stack. Routines keep their recursive shape; the order of side effects and results is the
/// one the recursive form had.
/// </summary>
internal static class AstTrampoline {
    /// <summary>The child call <paramref name="routine"/> stands for inside a trampolined routine.</summary>
    public static AstSubcall Call(IEnumerable<AstSubcall> routine) => new(routine);

    /// <summary>Runs <paramref name="root"/> and every child call it yields, to completion.</summary>
    public static void Run(IEnumerable<AstSubcall> root) {
        Stack<IEnumerator<AstSubcall>> frames = new();
        frames.Push(root.GetEnumerator());
        try {
            while (frames.Count > 0) {
                IEnumerator<AstSubcall> top = frames.Peek();
                if (top.MoveNext()) {
                    frames.Push(top.Current.Routine.GetEnumerator());
                } else {
                    frames.Pop().Dispose();
                }
            }
        } finally {
            while (frames.Count > 0) {
                frames.Pop().Dispose();
            }
        }
    }
}
