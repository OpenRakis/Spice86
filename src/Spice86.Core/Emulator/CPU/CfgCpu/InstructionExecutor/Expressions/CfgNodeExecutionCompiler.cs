namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using FastExpressionCompiler;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Threading;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Spice86.Shared.Interfaces;

/// <summary>
/// Compiles the execution AST of a <see cref="ICfgNode"/> into a <see cref="CfgNodeExecutionAction{T}"/> delegate
/// and assigns it to <see cref="ICfgNode.CompiledExecution"/>. Must be called at init time, not on the hot path.
/// The compilation strategy is controlled by <see cref="JitMode"/>:
/// <list type="bullet">
///   <item><term><see cref="JitMode.InterpretedThenCompiled"/></term><description>An interpreted delegate is assigned immediately; background threads swap in an optimized compiled delegate.</description></item>
///   <item><term><see cref="JitMode.InterpretedOnly"/></term><description>Only interpreted delegates are used; no background threads are started.</description></item>
///   <item><term><see cref="JitMode.CompiledOnly"/></term><description>Each instruction is compiled synchronously on first encounter; no interpreted phase.</description></item>
/// </list>
/// Each AST enqueues its own compilation task; compiled delegates are not shared.
/// </summary>
public class CfgNodeExecutionCompiler : IDisposable {
    private readonly JitMode _jitMode;
    private readonly int _backgroundCompilerThreadCount;

    private readonly BlockingCollection<(Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> Expression,
        TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> Tcs)> _compilationQueue = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread[] _backgroundThreads;
    private readonly CfgNodeExecutionCompilerMonitor _monitor;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CfgNodeExecutionCompiler"/> and, when the
    /// <paramref name="jitMode"/> requires it, starts the background compiler threads.
    /// </summary>
    public CfgNodeExecutionCompiler(CfgNodeExecutionCompilerMonitor monitor, ILoggerService logger, JitMode jitMode) {
        _monitor = monitor;
        _logger = logger;
        _jitMode = jitMode;

        if (_jitMode == JitMode.InterpretedThenCompiled) {
            _backgroundCompilerThreadCount = Math.Max(1, Environment.ProcessorCount - 2);
            _logger.Information("CfgNodeExecutionCompiler: starting {ThreadCount} background compiler threads",
                _backgroundCompilerThreadCount);
            CancellationToken token = _cts.Token;
            _backgroundThreads = new Thread[_backgroundCompilerThreadCount];
            for (int i = 0; i < _backgroundCompilerThreadCount; i++) {
                Thread thread = new Thread(() => RunBackgroundCompiler(token)) {
                    IsBackground = true,
                    Name = $"CfgNodeBackgroundCompiler-{i}",
                    Priority = ThreadPriority.BelowNormal
                };
                _backgroundThreads[i] = thread;
                thread.Start();
            }
        } else {
            _backgroundCompilerThreadCount = 0;
            _backgroundThreads = Array.Empty<Thread>();
            _logger.Information("CfgNodeExecutionCompiler: JitMode={JitMode}; no background compiler threads started", _jitMode);
        }
    }

    private void RunBackgroundCompiler(CancellationToken cancellationToken) {
        try {
            foreach ((Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> Expression,
                TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> Tcs) item
                in _compilationQueue.GetConsumingEnumerable(cancellationToken)) {
                try {
                    CfgNodeExecutionAction<InstructionExecutionHelper> optimized = CompileExpression(item.Expression);
                    item.Tcs.TrySetResult(optimized);
                } catch (InvalidOperationException ex) {
                    item.Tcs.TrySetException(ex);
                } finally {
                    _monitor.RecordQueuePopped();
                }
            }
        } catch (OperationCanceledException) {
            // Shutdown requested; exit cleanly.
        }
    }

    private CfgNodeExecutionAction<InstructionExecutionHelper> CompileExpression(
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper) {
        Stopwatch sw = Stopwatch.StartNew();
        try {
            CfgNodeExecutionAction<InstructionExecutionHelper> compiled = exprWithHelper.CompileFast();
            RuntimeHelpers.PrepareDelegate(compiled);
            sw.Stop();
            long micros = (long)((double)sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
            _monitor.RecordCompileSuccess(micros);
            return compiled;
        } catch (InvalidOperationException ex) {
            sw.Stop();
            long micros = (long)((double)sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
            _monitor.RecordCompileFailure(micros);
            _logger.Error(ex, "CfgNodeExecutionCompiler: compile error: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Assigns an execution delegate to <see cref="ICfgNode.CompiledExecution"/> according to the
    /// configured <see cref="JitMode"/>. Compiled delegates are not shared; each AST produces its own task.
    /// </summary>
    public void Compile(ICfgNode node) {
        AstBuilder astBuilder = new();
        IVisitableAstNode executionAst = node.GenerateExecutionAst(astBuilder);
        AstExpressionBuilder expressionBuilder = new();
        Expression expression = executionAst.Accept(expressionBuilder);
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper =
            expressionBuilder.ToActionWithHelper(expression);

        if (_jitMode == JitMode.InterpretedOnly) {
            CompileInterpreted(node, exprWithHelper);
        } else if (_jitMode == JitMode.CompiledOnly) {
            CompileNow(node, exprWithHelper);
        } else {
            CompileInterpretedThenBackground(node, exprWithHelper);
        }
    }

    private void CompileInterpreted(ICfgNode node,
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper) {
        CfgNodeExecutionAction<InstructionExecutionHelper> interpreted =
            exprWithHelper.Compile(preferInterpretation: true);
        AssignExecution(node, interpreted);
    }

    private void CompileNow(ICfgNode node,
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper) {
        CfgNodeExecutionAction<InstructionExecutionHelper> compiled = CompileExpression(exprWithHelper);
        AssignExecution(node, compiled);
    }

    private void CompileInterpretedThenBackground(ICfgNode node,
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper) {
        // Assign interpreted delegate immediately for low-latency first execution.
        CompileInterpreted(node, exprWithHelper);
        _monitor.RecordInterpreted();
        _monitor.RecordQueuePushed();
        TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _compilationQueue.TryAdd((exprWithHelper, tcs));
        Task<CfgNodeExecutionAction<InstructionExecutionHelper>> task = tcs.Task;

        // When the compiled delegate is ready, swap it onto the node.
        task.ContinueWith(completedTask => {
            if (completedTask.IsCompletedSuccessfully) {
                AssignExecution(node, completedTask.Result);
                _monitor.RecordSwapped();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void AssignExecution(ICfgNode node, CfgNodeExecutionAction<InstructionExecutionHelper> action) {
#if DEBUG
        node.CompiledExecution = WrapWithDebugContext(node, action);
#else
        node.CompiledExecution = action;
#endif
    }

    /// <inheritdoc />
    public void Dispose() {
        _cts.Cancel();
        _compilationQueue.CompleteAdding();
        foreach (Thread thread in _backgroundThreads) {
            thread.Join();
        }
        _monitor.Dispose();
        _cts.Dispose();
        _compilationQueue.Dispose();
    }

#if DEBUG
    /// <summary>
    /// Wraps a compiled delegate so that arithmetic exceptions carry the node's address and identity.
    /// The description string is captured once at compile time (init path), never on the hot path.
    /// </summary>
    private static CfgNodeExecutionAction<InstructionExecutionHelper> WrapWithDebugContext(
        ICfgNode node,
        CfgNodeExecutionAction<InstructionExecutionHelper> compiled) {
        string nodeDescription = $"node {node.Address} (Id={node.Id}, Type={node.GetType().Name})";
        return helper => {
            try {
                compiled(helper);
            } catch (DivideByZeroException ex) {
                throw new InvalidOperationException(
                    $"DivideByZeroException in compiled expression for {nodeDescription}", ex);
            } catch (OverflowException ex) {
                throw new InvalidOperationException(
                    $"OverflowException in compiled expression for {nodeDescription}", ex);
            }
        };
    }
#endif
}
