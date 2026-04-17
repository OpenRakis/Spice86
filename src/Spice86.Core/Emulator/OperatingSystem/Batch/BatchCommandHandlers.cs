namespace Spice86.Core.Emulator.OperatingSystem.Batch;

using System;

internal static class BatchCommandHandlers {
    internal static IBatchCommandHandler[] CreateKnownCommandHandlers() {
        return new IBatchCommandHandler[] {
            new RemCommandHandler(),
            new CallCommandHandler(),
            new GotoCommandHandler(),
            new ShiftCommandHandler(),
            new IfCommandHandler(),
            new ForCommandHandler(),
            new SetCommandHandler(),
            new EchoCommandHandler(),
            new PathCommandHandler(),
            new PauseCommandHandler(),
            new ClsCommandHandler(),
            new ChoiceCommandHandler(),
            new TypeCommandHandler(),
            new CdCommandHandler(),
            new ExitCommandHandler(),
            new MkdirCommandHandler(),
            new RmdirCommandHandler(),
            new DelCommandHandler(),
            new RenCommandHandler(),
            new DirCommandHandler(),
            new CopyCommandHandler(),
            new MoveCommandHandler(),
            new DateCommandHandler(),
            new TimeCommandHandler(),
            new VerCommandHandler(),
            new VolCommandHandler(),
            new LoadHighCommandHandler(),
            new EchoDotCommandHandler()
        };
    }

    internal interface IBatchCommandHandler {
        bool TryExecute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out bool result, out LaunchRequest launchRequest);
    }

    private abstract class BatchCommandHandlerBase : IBatchCommandHandler {
        public bool TryExecute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out bool result, out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            if (!IsMatch(engine, context)) {
                result = false;
                return false;
            }

            result = Execute(engine, context, out launchRequest);
            return true;
        }

        protected abstract bool IsMatch(DosBatchExecutionEngine engine, CommandExecutionContext context);
        protected abstract bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest);
    }

    private abstract class ExactTokenBatchCommandHandler : BatchCommandHandlerBase {
        private readonly string[] _tokens;

        protected ExactTokenBatchCommandHandler(params string[] tokens) {
            _tokens = tokens;
        }

        protected override bool IsMatch(DosBatchExecutionEngine engine, CommandExecutionContext context) {
            for (int i = 0; i < _tokens.Length; i++) {
                if (string.Equals(context.ResolvedCommandToken, _tokens[i], StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class RemCommandHandler : ExactTokenBatchCommandHandler {
        internal RemCommandHandler() : base("REM") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return false;
        }
    }

    private sealed class CallCommandHandler : ExactTokenBatchCommandHandler {
        internal CallCommandHandler() : base("CALL") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            return engine.TryHandleCall(context.ArgumentPart, context.Redirection, out launchRequest);
        }
    }

    private sealed class GotoCommandHandler : ExactTokenBatchCommandHandler {
        internal GotoCommandHandler() : base("GOTO") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.TryHandleGoto(context.ArgumentPart);
        }
    }

    private sealed class ShiftCommandHandler : ExactTokenBatchCommandHandler {
        internal ShiftCommandHandler() : base("SHIFT") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.TryHandleShift();
        }
    }

    private sealed class IfCommandHandler : ExactTokenBatchCommandHandler {
        internal IfCommandHandler() : base("IF") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            return engine.TryHandleIf(context.ArgumentPart, context.Redirection, out launchRequest);
        }
    }

    private sealed class ForCommandHandler : ExactTokenBatchCommandHandler {
        internal ForCommandHandler() : base("FOR") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            return engine.TryHandleFor(context.ArgumentPart, context.Redirection, out launchRequest);
        }
    }

    private sealed class SetCommandHandler : ExactTokenBatchCommandHandler {
        internal SetCommandHandler() : base("SET") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleSet);
        }
    }

    private sealed class EchoCommandHandler : ExactTokenBatchCommandHandler {
        internal EchoCommandHandler() : base("ECHO") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleEcho);
        }
    }

    private sealed class PathCommandHandler : ExactTokenBatchCommandHandler {
        internal PathCommandHandler() : base("PATH") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandlePath);
        }
    }

    private sealed class PauseCommandHandler : BatchCommandHandlerBase {
        protected override bool IsMatch(DosBatchExecutionEngine engine, CommandExecutionContext context) {
            return DosBatchExecutionEngine.IsCommandToken(context.ResolvedCommandToken, "PAUSE");
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = InternalBatchProgramBuilder.BuildPauseLaunchRequest(context.Redirection);
            return true;
        }
    }

    private sealed class ClsCommandHandler : ExactTokenBatchCommandHandler {
        internal ClsCommandHandler() : base("CLS") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            engine.TryHandleCls();
            return false;
        }
    }

    private sealed class ChoiceCommandHandler : BatchCommandHandlerBase {
        protected override bool IsMatch(DosBatchExecutionEngine engine, CommandExecutionContext context) {
            return DosBatchExecutionEngine.IsCommandToken(context.ResolvedCommandToken, "CHOICE");
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = InternalBatchProgramBuilder.BuildChoiceLaunchRequest(context.ArgumentPart, context.Redirection);
            return true;
        }
    }

    private sealed class TypeCommandHandler : ExactTokenBatchCommandHandler {
        internal TypeCommandHandler() : base("TYPE") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleType);
        }
    }

    private sealed class CdCommandHandler : ExactTokenBatchCommandHandler {
        internal CdCommandHandler() : base("CD", "CHDIR") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleChdir);
        }
    }

    private sealed class ExitCommandHandler : ExactTokenBatchCommandHandler {
        internal ExitCommandHandler() : base("EXIT") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            engine.HandleExit();
            return false;
        }
    }

    private sealed class MkdirCommandHandler : ExactTokenBatchCommandHandler {
        internal MkdirCommandHandler() : base("MD", "MKDIR") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleMkdir);
        }
    }

    private sealed class RmdirCommandHandler : ExactTokenBatchCommandHandler {
        internal RmdirCommandHandler() : base("RD", "RMDIR") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleRmdir);
        }
    }

    private sealed class DelCommandHandler : ExactTokenBatchCommandHandler {
        internal DelCommandHandler() : base("DEL", "DELETE", "ERASE") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleDel);
        }
    }

    private sealed class RenCommandHandler : ExactTokenBatchCommandHandler {
        internal RenCommandHandler() : base("REN", "RENAME") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleRen);
        }
    }

    private sealed class DirCommandHandler : ExactTokenBatchCommandHandler {
        internal DirCommandHandler() : base("DIR") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleDir);
        }
    }

    private sealed class CopyCommandHandler : ExactTokenBatchCommandHandler {
        internal CopyCommandHandler() : base("COPY") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleCopy);
        }
    }

    private sealed class MoveCommandHandler : ExactTokenBatchCommandHandler {
        internal MoveCommandHandler() : base("MOVE") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleMove);
        }
    }

    private sealed class DateCommandHandler : ExactTokenBatchCommandHandler {
        internal DateCommandHandler() : base("DATE") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleDate);
        }
    }

    private sealed class TimeCommandHandler : ExactTokenBatchCommandHandler {
        internal TimeCommandHandler() : base("TIME") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleTime);
        }
    }

    private sealed class VerCommandHandler : ExactTokenBatchCommandHandler {
        internal VerCommandHandler() : base("VER") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandNoArgument(context, engine.TryHandleVer);
        }
    }

    private sealed class VolCommandHandler : ExactTokenBatchCommandHandler {
        internal VolCommandHandler() : base("VOL") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            return engine.ExecuteInternalCommandWithArgument(context, engine.TryHandleVol);
        }
    }

    private sealed class LoadHighCommandHandler : ExactTokenBatchCommandHandler {
        internal LoadHighCommandHandler() : base("LH", "LOADHIGH") {
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            string fullCommand = DosBatchExecutionEngine.AppendRedirection(context.ArgumentPart, context.Redirection);
            return engine.TryExecuteCommandLine(fullCommand, out launchRequest);
        }
    }

    private sealed class EchoDotCommandHandler : BatchCommandHandlerBase {
        protected override bool IsMatch(DosBatchExecutionEngine engine, CommandExecutionContext context) {
            return context.ResolvedCommandToken.StartsWith("ECHO.", StringComparison.OrdinalIgnoreCase);
        }

        protected override bool Execute(DosBatchExecutionEngine engine, CommandExecutionContext context,
            out LaunchRequest launchRequest) {
            launchRequest = ContinueBatchExecutionLaunchRequest.Instance;
            string echoArguments = context.PreprocessedLine.TrimStart()[4..];
            return engine.TryExecuteInternalCommandWithRedirection(context.Redirection,
                () => engine.TryHandleEcho(echoArguments));
        }
    }
}