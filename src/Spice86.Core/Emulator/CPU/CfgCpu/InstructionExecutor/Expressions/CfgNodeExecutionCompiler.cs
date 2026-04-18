namespace Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using FastExpressionCompiler;

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
/// An interpreted delegate is assigned immediately to avoid first-execution jitter; a single background thread
/// then swaps in an optimized delegate compiled with <see cref="FastExpressionCompiler"/>.
/// Each AST enqueues its own compilation task; compiled delegates are not shared.
/// </summary>
public class CfgNodeExecutionCompiler : IDisposable {
    private readonly int _backgroundCompilerThreadCount;

    private readonly BlockingCollection<(Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> Expression,
        TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> Tcs)> _compilationQueue = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly Thread[] _backgroundThreads;
    private readonly CfgNodeExecutionCompilerMonitor _monitor;
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CfgNodeExecutionCompiler"/> and starts the background
    /// compiler threads.
    /// </summary>
    public CfgNodeExecutionCompiler(CfgNodeExecutionCompilerMonitor monitor, ILoggerService logger) {
        _monitor = monitor;
        _logger = logger;
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
    }

    private void RunBackgroundCompiler(CancellationToken cancellationToken) {
        try {
            foreach ((Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> Expression,
                TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> Tcs) item
                in _compilationQueue.GetConsumingEnumerable(cancellationToken)) {
                Stopwatch sw = Stopwatch.StartNew();
                try {
                    CfgNodeExecutionAction<InstructionExecutionHelper> optimized = item.Expression.CompileFast();
                    RuntimeHelpers.PrepareDelegate(optimized);
                    sw.Stop();
                    long micros = (long)((double)sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
                    _monitor.RecordCompileSuccess(micros);
                    item.Tcs.TrySetResult(optimized);
                } catch (InvalidOperationException ex) {
                    sw.Stop();
                    long micros = (long)((double)sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
                    _monitor.RecordCompileFailure(micros);
                    item.Tcs.TrySetException(ex);
                    _logger.Error(ex, "CfgNodeExecutionCompiler: compile error: {Message}", ex.Message);
                } finally {
                    _monitor.RecordQueuePopped();
                }
            }
        } catch (OperationCanceledException) {
            // Shutdown requested; exit cleanly.
        }
    }

    /// <summary>
    /// Assigns an interpreted delegate to <see cref="ICfgNode.CompiledExecution"/> immediately,
    /// then enqueues the expression for background compilation via <see cref="FastExpressionCompiler"/>.
    /// Compiled delegates are not shared; each AST enqueues its own compilation task.
    /// </summary>
    public void Compile(ICfgNode node) {
        AstBuilder astBuilder = new();
        IVisitableAstNode executionAst = node.GenerateExecutionAst(astBuilder);
        AstExpressionBuilder expressionBuilder = new();
        Expression expression = executionAst.Accept(expressionBuilder);
        Expression<CfgNodeExecutionAction<InstructionExecutionHelper>> exprWithHelper =
            expressionBuilder.ToActionWithHelper(expression);

        // Assign interpreted delegate immediately for low-latency first execution.
        CfgNodeExecutionAction<InstructionExecutionHelper> interpreted =
            exprWithHelper.Compile(preferInterpretation: true);
#if DEBUG
        node.CompiledExecution = WrapWithDebugContext(node, interpreted);
#else
        node.CompiledExecution = interpreted;
#endif
        _monitor.RecordInterpreted();

        _monitor.RecordQueuePushed();
        TaskCompletionSource<CfgNodeExecutionAction<InstructionExecutionHelper>> tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _compilationQueue.TryAdd((exprWithHelper, tcs));
        Task<CfgNodeExecutionAction<InstructionExecutionHelper>> task = tcs.Task;

        // When the compiled delegate is ready, swap it onto the node.
        task.ContinueWith(completedTask => {
            if (completedTask.IsCompletedSuccessfully) {
#if DEBUG
                node.CompiledExecution = WrapWithDebugContext(node, completedTask.Result);
#else
                node.CompiledExecution = completedTask.Result;
#endif
                _monitor.RecordSwapped();
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
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
